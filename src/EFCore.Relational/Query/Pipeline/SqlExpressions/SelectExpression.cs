﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Query.Pipeline;

namespace Microsoft.EntityFrameworkCore.Relational.Query.Pipeline.SqlExpressions
{
    public class SelectExpression : TableExpressionBase
    {
        private IDictionary<ProjectionMember, Expression> _projectionMapping = new Dictionary<ProjectionMember, Expression>();
        private readonly List<ProjectionExpression> _projection = new List<ProjectionExpression>();
        private readonly IDictionary<EntityProjectionExpression, IDictionary<IProperty, int>> _entityProjectionCache
            = new Dictionary<EntityProjectionExpression, IDictionary<IProperty, int>>();

        private readonly List<TableExpressionBase> _tables = new List<TableExpressionBase>();
        private readonly List<OrderingExpression> _orderings = new List<OrderingExpression>();

        private readonly List<SqlExpression> _identifier = new List<SqlExpression>();
        private readonly List<SqlExpression> _childIdentifiers = new List<SqlExpression>();
        private readonly List<SelectExpression> _pendingCollections = new List<SelectExpression>();

        public IReadOnlyList<ProjectionExpression> Projection => _projection;
        public IReadOnlyList<TableExpressionBase> Tables => _tables;
        public IReadOnlyList<OrderingExpression> Orderings => _orderings;
        public SqlExpression Predicate { get; private set; }
        public SqlExpression Limit { get; private set; }
        public SqlExpression Offset { get; private set; }
        public bool IsDistinct { get; private set; }

        /// <summary>
        /// Marks this <see cref="SelectExpression"/> as representing an SQL set operation, such as a UNION.
        /// For regular SQL SELECT expressions, contains <c>None</c>.
        /// </summary>
        public SetOperationType SetOperationType { get; private set; }

        /// <summary>
        /// Returns whether this <see cref="SelectExpression"/> represents an SQL set operation, such as a UNION.
        /// </summary>
        public bool IsSetOperation => SetOperationType != SetOperationType.None;

        internal SelectExpression(
            string alias,
            List<ProjectionExpression> projections,
            List<TableExpressionBase> tables,
            List<OrderingExpression> orderings)
            : base(alias)
        {
            _projection = projections;
            _tables = tables;
            _orderings = orderings;
        }

        internal SelectExpression(IEntityType entityType)
            : base(null)
        {
            var tableExpression = new TableExpression(
                entityType.GetTableName(),
                entityType.GetSchema(),
                entityType.GetTableName().ToLower().Substring(0, 1));

            _tables.Add(tableExpression);

            var entityProjection = new EntityProjectionExpression(entityType, tableExpression, false);
            _projectionMapping[new ProjectionMember()] = entityProjection;

            if (entityType.FindPrimaryKey() != null)
            {
                foreach (var property in entityType.FindPrimaryKey().Properties)
                {
                    _identifier.Add(entityProjection.GetProperty(property));
                }
            }
        }

        public SelectExpression(IEntityType entityType, string sql, Expression arguments)
            : base(null)
        {
            var fromSqlExpression = new FromSqlExpression(
                sql,
                arguments,
                entityType.GetTableName().ToLower().Substring(0, 1));

            _tables.Add(fromSqlExpression);

            var entityProjection = new EntityProjectionExpression(entityType, fromSqlExpression, false);
            _projectionMapping[new ProjectionMember()] = entityProjection;

            if (entityType.FindPrimaryKey() != null)
            {
                foreach (var property in entityType.FindPrimaryKey().Properties)
                {
                    _identifier.Add(entityProjection.GetProperty(property));
                }
            }
        }

        public bool IsNonComposedFromSql()
        {
            return Limit == null
                && Offset == null
                && !IsDistinct
                && Predicate == null
                && Orderings.Count == 0
                && Tables.Count == 1
                && Tables[0] is FromSqlExpression fromSql
                && Projection.All(pe => pe.Expression is ColumnExpression column ? ReferenceEquals(column.Table, fromSql) : false);
        }

        public SqlExpression BindProperty(ProjectionBindingExpression projectionBindingExpression, IProperty property)
        {
            return ((EntityProjectionExpression)_projectionMapping[projectionBindingExpression.ProjectionMember])
                .GetProperty(property);
        }

        public void ApplyProjection()
        {
            if (Projection.Any())
            {
                return;
            }

            var result = new Dictionary<ProjectionMember, Expression>();
            foreach (var keyValuePair in _projectionMapping)
            {
                if (keyValuePair.Value is EntityProjectionExpression entityProjection)
                {
                    var map = new Dictionary<IProperty, int>();

                    foreach (var property in GetAllPropertiesInHierarchy(entityProjection.EntityType))
                    {
                        map[property] = AddToProjection(entityProjection.GetProperty(property));
                    }
                    result[keyValuePair.Key] = Constant(map);
                }
                else
                {
                    result[keyValuePair.Key] = Constant(AddToProjection(
                        (SqlExpression)keyValuePair.Value, keyValuePair.Key.LastMember?.Name));
                }
            }

            _projectionMapping = result;
        }

        private IEnumerable<IProperty> GetAllPropertiesInHierarchy(IEntityType entityType)
        {
            return entityType.GetTypesInHierarchy().SelectMany(e => e.GetDeclaredProperties());
        }

        public void ReplaceProjectionMapping(IDictionary<ProjectionMember, Expression> projectionMapping)
        {
            _projectionMapping.Clear();
            foreach (var kvp in projectionMapping)
            {
                _projectionMapping[kvp.Key] = kvp.Value;
            }
        }

        public Expression GetMappedProjection(ProjectionMember projectionMember)
        {
            return _projectionMapping[projectionMember];
        }

        public int AddToProjection(SqlExpression sqlExpression)
        {
            return AddToProjection(sqlExpression, null);
        }

        private int AddToProjection(SqlExpression sqlExpression, string alias)
        {
            var existingIndex = _projection.FindIndex(pe => pe.Expression.Equals(sqlExpression));
            if (existingIndex != -1)
            {
                return existingIndex;
            }

            var baseAlias = alias ?? (sqlExpression as ColumnExpression)?.Name ?? (Alias != null ? "c" : null);
            var currentAlias = baseAlias ?? "";
            if (Alias != null && baseAlias != null)
            {
                var counter = 0;
                while (_projection.Any(pe => string.Equals(pe.Alias, currentAlias, StringComparison.OrdinalIgnoreCase)))
                {
                    currentAlias = $"{baseAlias}{counter++}";
                }
            }

            _projection.Add(new ProjectionExpression(sqlExpression, currentAlias));

            return _projection.Count - 1;
        }

        public IDictionary<IProperty, int> AddToProjection(EntityProjectionExpression entityProjection)
        {
            if (!_entityProjectionCache.TryGetValue(entityProjection, out var dictionary))
            {
                dictionary = new Dictionary<IProperty, int>();
                foreach (var property in GetAllPropertiesInHierarchy(entityProjection.EntityType))
                {
                    dictionary[property] = AddToProjection(entityProjection.GetProperty(property));
                }

                _entityProjectionCache[entityProjection] = dictionary;
            }

            return dictionary;
        }

        public void PrepareForAggregate()
        {
            if (IsDistinct || Limit != null || Offset != null || IsSetOperation)
            {
                PushdownIntoSubquery();
            }
        }

        public void ApplyPredicate(SqlExpression expression)
        {
            if (expression is SqlConstantExpression sqlConstant
                && (bool)sqlConstant.Value)
            {
                return;
            }

            if (Limit != null || Offset != null || IsSetOperation)
            {
                var mappings = PushdownIntoSubquery();
                expression = new SqlRemappingVisitor(mappings).Remap(expression);
            }

            if (Predicate == null)
            {
                Predicate = expression;
            }
            else
            {
                Predicate = new SqlBinaryExpression(
                    ExpressionType.AndAlso,
                    Predicate,
                    expression,
                    typeof(bool),
                    expression.TypeMapping);
            }
        }

        public override ExpressionType NodeType => ExpressionType.Extension;


        public void ApplyOrdering(OrderingExpression orderingExpression)
        {
            // TODO: We should not be pushing down set operations, see #16244
            if (IsDistinct || Limit != null || Offset != null || IsSetOperation)
            {
                orderingExpression = orderingExpression.Update(
                    new SqlRemappingVisitor(PushdownIntoSubquery())
                        .Remap(orderingExpression.Expression));
            }

            _orderings.Clear();
            _orderings.Add(orderingExpression);
        }

        public void AppendOrdering(OrderingExpression orderingExpression)
        {
            if (_orderings.FirstOrDefault(o => o.Expression.Equals(orderingExpression.Expression)) == null)
            {
                _orderings.Add(orderingExpression);
            }
        }

        public void ApplyLimit(SqlExpression sqlExpression)
        {
            // TODO: We should not be pushing down set operations, see #16244
            if (Limit != null || IsSetOperation)
            {
                PushdownIntoSubquery();
            }

            Limit = sqlExpression;
        }

        public void ApplyOffset(SqlExpression sqlExpression)
        {
            // TODO: We should not be pushing down set operations, see #16244
            if (Limit != null || Offset != null || IsSetOperation)
            {
                PushdownIntoSubquery();
            }

            Offset = sqlExpression;
        }

        public void ReverseOrderings()
        {
            if (Limit != null
                || Offset != null)
            {
                PushdownIntoSubquery();
            }

            var existingOrdering = _orderings.ToArray();

            _orderings.Clear();

            for (var i = 0; i < existingOrdering.Length; i++)
            {
                _orderings.Add(
                    new OrderingExpression(
                        existingOrdering[i].Expression,
                        !existingOrdering[i].Ascending));
            }
        }

        public void ApplyDistinct()
        {
            if (Limit != null || Offset != null || IsSetOperation)
            {
                PushdownIntoSubquery();
            }

            IsDistinct = true;
            ClearOrdering();
        }

        public void ClearOrdering()
        {
            _orderings.Clear();
        }


        /// <summary>
        ///     Applies a set operation (e.g. Union, Intersect) on this query, pushing it down and <paramref name="otherSelectExpression"/>
        ///     down to be the set operands.
        /// </summary>
        /// <param name="setOperationType"> The type of set operation to be applied. </param>
        /// <param name="otherSelectExpression"> The other expression to participate as an operate in the operation (along with this one). </param>
        /// <param name="shaperExpression"> The shaper expression currently in use. </param>
        /// <returns>
        ///     A shaper expression to be used. This will be the same as <paramref name="shaperExpression"/>, unless the set operation
        ///     modified the return type (i.e. upcast to common ancestor).
        /// </returns>
        public Expression ApplySetOperation(
            SetOperationType setOperationType,
            SelectExpression otherSelectExpression,
            Expression shaperExpression)
        {
            // TODO: throw if there are pending collection joins
            var select1 = new SelectExpression(null, new List<ProjectionExpression>(), _tables.ToList(), _orderings.ToList())
            {
                IsDistinct = IsDistinct,
                Predicate = Predicate,
                Offset = Offset,
                Limit = Limit,
                SetOperationType = SetOperationType
            };

            select1._projectionMapping = new Dictionary<ProjectionMember, Expression>(_projectionMapping);
            _projectionMapping.Clear();

            select1._identifier.AddRange(_identifier);
            _identifier.Clear();

            var select2 = otherSelectExpression;

            if (_projection.Any())
            {
                throw new InvalidOperationException("Can't process set operations after client evaluation, consider moving the operation before the last Select() call (see issue #16243)");
            }
            else
            {
                if (select1._projectionMapping.Count != select2._projectionMapping.Count)
                {
                    // Should not be possible after compiler checks
                    throw new Exception("Different projection mapping count in set operation");
                }

                foreach (var joinedMapping in select1._projectionMapping.Join(
                    select2._projectionMapping,
                    kv => kv.Key,
                    kv => kv.Key,
                    (kv1, kv2) => (kv1.Key, Value1: kv1.Value, Value2: kv2.Value)))
                {
                    if (joinedMapping.Value1 is EntityProjectionExpression entityProjection1
                        && joinedMapping.Value2 is EntityProjectionExpression entityProjection2)
                    {
                        HandleEntityMapping(joinedMapping.Key, select1, entityProjection1, select2, entityProjection2);
                        continue;
                    }

                    if (joinedMapping.Value1 is ColumnExpression && joinedMapping.Value2 is ColumnExpression
                        || joinedMapping.Value1 is SubSelectExpression && joinedMapping.Value2 is SubSelectExpression)
                    {
                        HandleColumnMapping(
                            joinedMapping.Key,
                            select1, (SqlExpression)joinedMapping.Value1,
                            select2, (SqlExpression)joinedMapping.Value2);
                        continue;
                    }

                    throw new InvalidOperationException("Non-matching or unknown projection mapping type in set operation");
                }
            }

            Offset = null;
            Limit = null;
            IsDistinct = false;
            Predicate = null;
            _orderings.Clear();
            _tables.Clear();
            _tables.Add(select1);
            _tables.Add(otherSelectExpression);
            SetOperationType = setOperationType;

            return shaperExpression;

            void HandleEntityMapping(
                ProjectionMember projectionMember,
                SelectExpression select1, EntityProjectionExpression projection1,
                SelectExpression select2, EntityProjectionExpression projection2)
            {
                 var propertyExpressions = new Dictionary<IProperty, ColumnExpression>();

                 if (projection1.EntityType == projection2.EntityType)
                 {
                     foreach (var property in GetAllPropertiesInHierarchy(projection1.EntityType))
                     {
                         propertyExpressions[property] = AddSetOperationColumnProjections(
                             property,
                             select1, projection1.GetProperty(property),
                             select2, projection2.GetProperty(property));
                     }

                     _projectionMapping[projectionMember] = new EntityProjectionExpression(projection1.EntityType, propertyExpressions);
                     return;
                 }

                 throw new InvalidOperationException("Set operations over different entity types are currently unsupported (see #16298)");
            }

            ColumnExpression AddSetOperationColumnProjections(
                IProperty property,
                SelectExpression select1, ColumnExpression column1,
                SelectExpression select2, ColumnExpression column2)
            {
                var columnName = column1.Name;

                select1._projection.Add(new ProjectionExpression(column1, columnName));
                select2._projection.Add(new ProjectionExpression(column2, columnName));

                if (select1._identifier.Contains(column1))
                {
                    _identifier.Add(column1);
                }

                return column1;
            }

            void HandleColumnMapping(
                ProjectionMember projectionMember,
                SelectExpression select1, SqlExpression innerColumn1,
                SelectExpression select2, SqlExpression innerColumn2)
            {
                // The actual columns may actually be different, but we don't care as long as the type and alias
                // coming out of the two operands are the same
                var alias = projectionMember.LastMember?.Name;
                select1.AddToProjection(innerColumn1, alias);
                select2.AddToProjection(innerColumn2, alias);
                _projectionMapping[projectionMember] = innerColumn1;
            }
        }

        public IDictionary<SqlExpression, ColumnExpression> PushdownIntoSubquery()
        {
            var subquery = new SelectExpression("t", new List<ProjectionExpression>(), _tables.ToList(), _orderings.ToList())
            {
                IsDistinct = IsDistinct,
                Predicate = Predicate,
                Offset = Offset,
                Limit = Limit,
                SetOperationType = SetOperationType
            };

            if (subquery.Limit == null && subquery.Offset == null)
            {
                subquery.ClearOrdering();
            }

            var projectionMap = new Dictionary<SqlExpression, ColumnExpression>();
            if (_projection.Any())
            {
                var projections = _projection.Select(pe => pe.Expression).ToList();
                _projection.Clear();
                foreach (var projection in projections)
                {
                    var index = subquery.AddToProjection(projection);
                    var projectionExpression = subquery._projection[index];
                    var outerColumn = new ColumnExpression(projectionExpression, subquery, IsNullableProjection(projectionExpression));
                    AddToProjection(outerColumn);
                    projectionMap[projection] = outerColumn;
                }
            }
            else
            {
                foreach (var mapping in _projectionMapping.ToList())
                {
                    if (mapping.Value is EntityProjectionExpression entityProjection)
                    {
                        var propertyExpressions = new Dictionary<IProperty, ColumnExpression>();
                        foreach (var property in GetAllPropertiesInHierarchy(entityProjection.EntityType))
                        {
                            var innerColumn = entityProjection.GetProperty(property);
                            var index = subquery.AddToProjection(innerColumn);
                            var projectionExpression = subquery._projection[index];
                            var outerColumn = new ColumnExpression(projectionExpression, subquery, IsNullableProjection(projectionExpression));
                            projectionMap[innerColumn] = outerColumn;
                            propertyExpressions[property] = outerColumn;
                        }

                        _projectionMapping[mapping.Key] = new EntityProjectionExpression(entityProjection.EntityType, propertyExpressions);
                    }
                    else
                    {
                        var innerColumn = (SqlExpression)mapping.Value;
                        var index = subquery.AddToProjection(innerColumn);
                        var projectionExpression = subquery._projection[index];
                        var outerColumn = new ColumnExpression(projectionExpression, subquery, IsNullableProjection(projectionExpression));
                        projectionMap[innerColumn] = outerColumn;
                        _projectionMapping[mapping.Key] = outerColumn;
                    }
                }
            }

            var sqlRemappingVisitor = new SqlRemappingVisitor(projectionMap);

            var identifier = _identifier.ToList();
            _identifier.Clear();
            // TODO: See issue#15873
            _identifier.AddRange(identifier.Select(sqlRemappingVisitor.Remap));

            var childIdentifiers = _childIdentifiers.ToList();
            _childIdentifiers.Clear();
            // TODO: See issue#15873
            _childIdentifiers.AddRange(childIdentifiers.Select(sqlRemappingVisitor.Remap));

            var pendingCollections = _pendingCollections.ToList();
            _pendingCollections.Clear();
            _pendingCollections.AddRange(pendingCollections.Select(sqlRemappingVisitor.Remap));

            _orderings.Clear();
            foreach (var ordering in subquery._orderings)
            {
                var orderingExpression = ordering.Expression;
                if (projectionMap.TryGetValue(orderingExpression, out var outerColumn))
                {
                    _orderings.Add(new OrderingExpression(outerColumn, ordering.Ascending));
                }
                else
                {
                    var index = subquery.AddToProjection(orderingExpression);
                    var projectionExpression = subquery._projection[index];
                    outerColumn = new ColumnExpression(projectionExpression, subquery, IsNullableProjection(projectionExpression));
                    _orderings.Add(new OrderingExpression(outerColumn, ordering.Ascending));
                }
            }

            Offset = null;
            Limit = null;
            IsDistinct = false;
            Predicate = null;
            SetOperationType = SetOperationType.None;
            _tables.Clear();
            _tables.Add(subquery);

            return projectionMap;
        }

        private static bool IsNullableProjection(ProjectionExpression projection)
        {
            return projection.Expression is ColumnExpression column ? column.Nullable : true;
        }

        public CollectionShaperExpression AddCollectionProjection(ShapedQueryExpression shapedQueryExpression, INavigation navigation)
        {
            var innerSelectExpression = (SelectExpression)shapedQueryExpression.QueryExpression;
            _pendingCollections.Add(innerSelectExpression);

            return new CollectionShaperExpression(
                new ProjectionBindingExpression(this, _pendingCollections.Count - 1, typeof(object)),
                shapedQueryExpression.ShaperExpression,
                navigation);
        }

        public Expression ApplyCollectionJoin(int collectionIndex, int collectionId, Expression innerShaper, INavigation navigation)
        {
            var innerSelectExpression = _pendingCollections[collectionIndex];
            _pendingCollections[collectionIndex] = null;
            var parentIdentifier = GetIdentifierAccessor(_identifier);
            var outerIdentifier = GetIdentifierAccessor(_identifier.Concat(_childIdentifiers));
            innerSelectExpression.ApplyProjection();
            var selfIdentifier = innerSelectExpression.GetIdentifierAccessor(innerSelectExpression._identifier);

            if (collectionIndex == 0)
            {
                foreach (var column in _identifier)
                {
                    AppendOrdering(new OrderingExpression(column, ascending: true));
                }
            }

            var joinPredicate = ExtractJoinKey(innerSelectExpression);
            if (joinPredicate != null)
            {
                if (innerSelectExpression.Offset != null
                    || innerSelectExpression.Limit != null
                    || innerSelectExpression.IsDistinct
                    || innerSelectExpression.Predicate != null
                    || innerSelectExpression.Tables.Count > 1)
                {
                    joinPredicate = new SqlRemappingVisitor(innerSelectExpression.PushdownIntoSubquery())
                        .Remap(joinPredicate);
                }

                var leftJoinExpression = new LeftJoinExpression(innerSelectExpression.Tables.Single(), joinPredicate);
                _tables.Add(leftJoinExpression);
                var indexOffset = _projection.Count;
                foreach (var projection in innerSelectExpression.Projection)
                {
                    AddToProjection(projection.Expression is ColumnExpression column ? column.MakeNullable() : projection.Expression);
                }

                foreach (var column in innerSelectExpression._identifier.Concat(innerSelectExpression._childIdentifiers))
                {
                    var updatedColumn = column is ColumnExpression column1 ? column1.MakeNullable() : column;
                    _childIdentifiers.Add(updatedColumn);
                    AppendOrdering(new OrderingExpression(updatedColumn, ascending: true));
                }

                var shaperRemapper = new ShaperRemappingExpressionVisitor(this, innerSelectExpression, indexOffset);
                innerShaper = shaperRemapper.Visit(innerShaper);
                selfIdentifier = shaperRemapper.Visit(selfIdentifier);

                return new RelationalCollectionShaperExpression(
                    collectionId, parentIdentifier, outerIdentifier, selfIdentifier, innerShaper, navigation);
            }

            throw new NotImplementedException();
        }

        private Expression GetIdentifierAccessor(IEnumerable<SqlExpression> identifyingProjection)
        {
            var updatedExpressions = new List<Expression>();
            foreach (var keyExpression in identifyingProjection)
            {
                var index = AddToProjection(keyExpression);
                var projectionBindingExpression = new ProjectionBindingExpression(this, index, keyExpression.Type.MakeNullable());

                updatedExpressions.Add(
                    projectionBindingExpression.Type.IsValueType
                    ? Convert(projectionBindingExpression, typeof(object))
                    : (Expression)projectionBindingExpression);
            }

            return NewArrayInit(
                typeof(object),
                updatedExpressions);
        }

        private class ShaperRemappingExpressionVisitor : ExpressionVisitor
        {
            private readonly SelectExpression _queryExpression;
            private readonly SelectExpression _innerSelectExpression;
            private readonly int _offset;

            public ShaperRemappingExpressionVisitor(SelectExpression queryExpression, SelectExpression innerSelectExpression, int offset)
            {
                _queryExpression = queryExpression;
                _innerSelectExpression = innerSelectExpression;
                _offset = offset;
            }

            protected override Expression VisitExtension(Expression extensionExpression)
            {
                if (extensionExpression is ProjectionBindingExpression projectionBindingExpression)
                {
                    var oldIndex = (int)GetProjectionIndex(projectionBindingExpression);

                    return new ProjectionBindingExpression(_queryExpression, oldIndex + _offset, projectionBindingExpression.Type);
                }

                if (extensionExpression is EntityShaperExpression entityShaper)
                {
                    var oldIndexMap = (IDictionary<IProperty, int>)GetProjectionIndex(
                        (ProjectionBindingExpression)entityShaper.ValueBufferExpression);
                    var indexMap = new Dictionary<IProperty, int>();
                    foreach (var keyValuePair in oldIndexMap)
                    {
                        indexMap[keyValuePair.Key] = keyValuePair.Value + _offset;
                    }

                    return new EntityShaperExpression(
                        entityShaper.EntityType,
                        new ProjectionBindingExpression(_queryExpression, indexMap),
                        nullable: true,
                        entityShaper.ParentNavigation,
                        entityShaper.NestedEntities);
                }

                return base.VisitExtension(extensionExpression);
            }

            private object GetProjectionIndex(ProjectionBindingExpression projectionBindingExpression)
            {
                return projectionBindingExpression.ProjectionMember != null
                    ? ((ConstantExpression)_innerSelectExpression.GetMappedProjection(projectionBindingExpression.ProjectionMember)).Value
                    : (projectionBindingExpression.Index != null
                        ? (object)projectionBindingExpression.Index
                        : projectionBindingExpression.IndexMap);
            }
        }

        private SqlExpression ExtractJoinKey(SelectExpression selectExpression)
        {
            if (selectExpression.Predicate != null)
            {
                var joinPredicate = ExtractJoinKey(selectExpression, selectExpression.Predicate, out var predicate);
                selectExpression.Predicate = predicate;

                return joinPredicate;
            }

            return null;
        }

        private SqlExpression ExtractJoinKey(SelectExpression selectExpression, SqlExpression predicate, out SqlExpression updatedPredicate)
        {
            if (predicate is SqlBinaryExpression sqlBinaryExpression)
            {
                var joinPredicate = ValidateKeyComparison(selectExpression, sqlBinaryExpression);
                if (joinPredicate != null)
                {
                    updatedPredicate = null;

                    return joinPredicate;
                }

                if (sqlBinaryExpression.OperatorType == ExpressionType.AndAlso)
                {
                    static SqlExpression combineNonNullExpressions(SqlExpression left, SqlExpression right)
                    {
                        return left != null
                            ? right != null
                                ? new SqlBinaryExpression(ExpressionType.AndAlso, left, right, left.Type, left.TypeMapping)
                                : left
                            : right;
                    }

                    var leftJoinKey = ExtractJoinKey(selectExpression, sqlBinaryExpression.Left, out var leftPredicate);
                    var rightJoinKey = ExtractJoinKey(selectExpression, sqlBinaryExpression.Right, out var rightPredicate);

                    updatedPredicate = combineNonNullExpressions(leftPredicate, rightPredicate);

                    return combineNonNullExpressions(leftJoinKey, rightJoinKey);
                }
            }

            updatedPredicate = predicate;
            return null;
        }

        private SqlBinaryExpression ValidateKeyComparison(SelectExpression inner, SqlBinaryExpression sqlBinaryExpression)
        {
            if (sqlBinaryExpression.OperatorType == ExpressionType.Equal)
            {
                if (sqlBinaryExpression.Left is ColumnExpression leftColumn
                    && sqlBinaryExpression.Right is ColumnExpression rightColumn)
                {
                    if (ContainsTableReference(leftColumn.Table)
                        && inner.ContainsTableReference(rightColumn.Table))
                    {
                        inner.AddToProjection(rightColumn);
                        return sqlBinaryExpression;
                    }

                    if (ContainsTableReference(rightColumn.Table)
                        && inner.ContainsTableReference(leftColumn.Table))
                    {
                        inner.AddToProjection(leftColumn);
                        return sqlBinaryExpression.Update(
                            sqlBinaryExpression.Right,
                            sqlBinaryExpression.Left);
                    }
                }
            }

            return null;
        }

        // We treat a set operation as a transparent wrapper over its left operand (the ColumnExpression projection mappings
        // found on a set operation SelectExpression are actually those of its left operand).
        private bool ContainsTableReference(TableExpressionBase table)
            => IsSetOperation
                ? ((SelectExpression)Tables[0]).ContainsTableReference(table)
                : Tables.Any(te => ReferenceEquals(te is JoinExpressionBase jeb ? jeb.Table : te, table));

        public void AddInnerJoin(SelectExpression innerSelectExpression, SqlExpression joinPredicate, Type transparentIdentifierType)
        {
            // TODO: write a test which has distinct on outer so that we can verify pushdown
            if (innerSelectExpression.Orderings.Any()
                || innerSelectExpression.Limit != null
                || innerSelectExpression.Offset != null
                || innerSelectExpression.IsDistinct
                // TODO: Predicate can be lifted in inner join
                || innerSelectExpression.Predicate != null
                || innerSelectExpression.Tables.Count > 1)
            {
                joinPredicate = new SqlRemappingVisitor(innerSelectExpression.PushdownIntoSubquery())
                    .Remap(joinPredicate);
            }

            _identifier.AddRange(innerSelectExpression._identifier);
            var joinTable = new InnerJoinExpression(innerSelectExpression.Tables.Single(), joinPredicate);
            _tables.Add(joinTable);

            var outerMemberInfo = transparentIdentifierType.GetTypeInfo().GetDeclaredField("Outer");
            var projectionMapping = new Dictionary<ProjectionMember, Expression>();
            foreach (var projection in _projectionMapping)
            {
                projectionMapping[projection.Key.ShiftMember(outerMemberInfo)] = projection.Value;
            }

            var innerMemberInfo = transparentIdentifierType.GetTypeInfo().GetDeclaredField("Inner");
            foreach (var projection in innerSelectExpression._projectionMapping)
            {
                projectionMapping[projection.Key.ShiftMember(innerMemberInfo)] = projection.Value;
            }

            _projectionMapping = projectionMapping;
        }

        public void AddLeftJoin(SelectExpression innerSelectExpression, SqlExpression joinPredicate, Type transparentIdentifierType)
        {
            if (Limit != null || Offset != null || IsDistinct || IsSetOperation)
            {
                joinPredicate = new SqlRemappingVisitor(PushdownIntoSubquery())
                    .Remap(joinPredicate);
            }

            if (innerSelectExpression.Orderings.Any()
                || innerSelectExpression.Limit != null
                || innerSelectExpression.Offset != null
                || innerSelectExpression.IsDistinct
                || innerSelectExpression.Predicate != null
                || innerSelectExpression.Tables.Count > 1)
            {
                joinPredicate = new SqlRemappingVisitor(innerSelectExpression.PushdownIntoSubquery())
                    .Remap(joinPredicate);
            }

            var joinTable = new LeftJoinExpression(innerSelectExpression.Tables.Single(), joinPredicate);
            _tables.Add(joinTable);

            var outerMemberInfo = transparentIdentifierType.GetTypeInfo().GetDeclaredField("Outer");
            var projectionMapping = new Dictionary<ProjectionMember, Expression>();
            foreach (var projection in _projectionMapping)
            {
                projectionMapping[projection.Key.ShiftMember(outerMemberInfo)] = projection.Value;
            }

            var innerMemberInfo = transparentIdentifierType.GetTypeInfo().GetDeclaredField("Inner");
            foreach (var projection in innerSelectExpression._projectionMapping)
            {
                var projectionToAdd = projection.Value;
                if (projectionToAdd is EntityProjectionExpression entityProjection)
                {
                    projectionToAdd = entityProjection.MakeNullable();
                }
                else if (projectionToAdd is ColumnExpression column)
                {
                    projectionToAdd = column.MakeNullable();
                }

                projectionMapping[projection.Key.ShiftMember(innerMemberInfo)] = projectionToAdd;
            }

            _projectionMapping = projectionMapping;
        }

        public void AddCrossJoin(SelectExpression innerSelectExpression, Type transparentIdentifierType)
        {
            if (Limit != null || Offset != null || IsDistinct || Predicate != null || IsSetOperation)
            {
                PushdownIntoSubquery();
            }

            if (innerSelectExpression.Orderings.Any()
                || innerSelectExpression.Limit != null
                || innerSelectExpression.Offset != null
                || innerSelectExpression.IsDistinct
                || innerSelectExpression.Predicate != null)
            {
                innerSelectExpression.PushdownIntoSubquery();
            }

            _identifier.AddRange(innerSelectExpression._identifier);
            var joinTable = new CrossJoinExpression(innerSelectExpression.Tables.Single());
            _tables.Add(joinTable);

            var outerMemberInfo = transparentIdentifierType.GetTypeInfo().GetDeclaredField("Outer");
            var projectionMapping = new Dictionary<ProjectionMember, Expression>();
            foreach (var projection in _projectionMapping)
            {
                projectionMapping[projection.Key.ShiftMember(outerMemberInfo)] = projection.Value;
            }

            var innerMemberInfo = transparentIdentifierType.GetTypeInfo().GetDeclaredField("Inner");
            foreach (var projection in innerSelectExpression._projectionMapping)
            {
                projectionMapping[projection.Key.ShiftMember(innerMemberInfo)] = projection.Value;
            }

            _projectionMapping = projectionMapping;
        }

        private class SqlRemappingVisitor : ExpressionVisitor
        {
            private readonly IDictionary<SqlExpression, ColumnExpression> _mappings;

            public SqlRemappingVisitor(IDictionary<SqlExpression, ColumnExpression> mappings)
            {
                _mappings = mappings;
            }

            public SqlExpression Remap(SqlExpression sqlExpression) => (SqlExpression)Visit(sqlExpression);
            public SelectExpression Remap(SelectExpression sqlExpression) => (SelectExpression)Visit(sqlExpression);

            public override Expression Visit(Expression expression)
            {
                if (expression is SqlExpression sqlExpression
                    && _mappings.TryGetValue(sqlExpression, out var outer))
                {
                    return outer;
                }

                return base.Visit(expression);
            }
        }

        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            // We have to do in-place mutation till we have applied pending collections because of shaper references
            // This is pseudo finalization phase for select expression.
            if (_pendingCollections.Any(e => e != null))
            {
                if (Projection.Any())
                {
                    var projections = _projection.ToList();
                    _projection.Clear();
                    _projection.AddRange(projections.Select(e => (ProjectionExpression)visitor.Visit(e)));
                }
                else
                {
                    var projectionMapping = new Dictionary<ProjectionMember, Expression>();
                    foreach (var mapping in _projectionMapping)
                    {
                        var newProjection = visitor.Visit(mapping.Value);

                        projectionMapping[mapping.Key] = newProjection;
                    }

                    _projectionMapping = projectionMapping;
                }

                var tables = _tables.ToList();
                _tables.Clear();
                _tables.AddRange(tables.Select(e => (TableExpressionBase)visitor.Visit(e)));

                Predicate = (SqlExpression)visitor.Visit(Predicate);

                var orderings = _orderings.ToList();
                _orderings.Clear();
                _orderings.AddRange(orderings.Select(e => e.Update((SqlExpression)visitor.Visit(e.Expression))));

                Offset = (SqlExpression)visitor.Visit(Offset);
                Limit = (SqlExpression)visitor.Visit(Limit);

                return this;
            }
            else
            {
                var changed = false;

                var projections = new List<ProjectionExpression>();
                IDictionary<ProjectionMember, Expression> projectionMapping;
                if (Projection.Any())
                {
                    projectionMapping = _projectionMapping;
                    foreach (var item in Projection)
                    {
                        var projection = (ProjectionExpression)visitor.Visit(item);
                        projections.Add(projection);

                        changed |= projection != item;
                    }
                }
                else
                {
                    projectionMapping = new Dictionary<ProjectionMember, Expression>();
                    foreach (var mapping in _projectionMapping)
                    {
                        var newProjection = visitor.Visit(mapping.Value);
                        changed |= newProjection != mapping.Value;

                        projectionMapping[mapping.Key] = newProjection;
                    }
                }

                var tables = new List<TableExpressionBase>();
                foreach (var table in _tables)
                {
                    var newTable = (TableExpressionBase)visitor.Visit(table);
                    changed |= newTable != table;
                    tables.Add(newTable);
                }

                var predicate = (SqlExpression)visitor.Visit(Predicate);
                changed |= predicate != Predicate;

                var orderings = new List<OrderingExpression>();
                foreach (var ordering in _orderings)
                {
                    var orderingExpression = (SqlExpression)visitor.Visit(ordering.Expression);
                    changed |= orderingExpression != ordering.Expression;
                    orderings.Add(ordering.Update(orderingExpression));
                }

                var offset = (SqlExpression)visitor.Visit(Offset);
                changed |= offset != Offset;

                var limit = (SqlExpression)visitor.Visit(Limit);
                changed |= limit != Limit;

                if (changed)
                {
                    var newSelectExpression = new SelectExpression(Alias, projections, tables, orderings)
                    {
                        _projectionMapping = projectionMapping,
                        Predicate = predicate,
                        Offset = offset,
                        Limit = limit,
                        IsDistinct = IsDistinct,
                        SetOperationType = SetOperationType
                    };

                    newSelectExpression._identifier.AddRange(_identifier);
                    newSelectExpression._identifier.AddRange(_childIdentifiers);

                    return newSelectExpression;
                }

                return this;
            }
        }

        public override bool Equals(object obj)
            => obj != null
            && (ReferenceEquals(this, obj)
                || obj is SelectExpression selectExpression
                    && Equals(selectExpression));

        private bool Equals(SelectExpression selectExpression)
        {
            if (!base.Equals(selectExpression))
            {
                return false;
            }

            if (_projectionMapping.Count != selectExpression._projectionMapping.Count)
            {
                foreach (var projectionMapping in _projectionMapping)
                {
                    if (!selectExpression._projectionMapping.TryGetValue(projectionMapping.Key, out var projection))
                    {
                        return false;
                    }

                    if (!projectionMapping.Value.Equals(projection))
                    {
                        return false;
                    }
                }
            }

            if (!_tables.SequenceEqual(selectExpression._tables))
            {
                return false;
            }

            if (!(Predicate == null && selectExpression.Predicate == null
                || Predicate != null && Predicate.Equals(selectExpression.Predicate)))
            {
                return false;
            }

            if (!_pendingCollections.SequenceEqual(selectExpression._pendingCollections))
            {
                return false;
            }

            if (!_orderings.SequenceEqual(selectExpression._orderings))
            {
                return false;
            }

            if (!(Offset == null && selectExpression.Offset == null
                || Offset != null && Offset.Equals(selectExpression.Offset)))
            {
                return false;
            }

            if (!(Limit == null && selectExpression.Limit == null
                || Limit != null && Limit.Equals(selectExpression.Limit)))
            {
                return false;
            }

            return IsDistinct == selectExpression.IsDistinct;
        }

        // This does not take internal states since when using this method SelectExpression should be finalized
        public SelectExpression Update(
            List<ProjectionExpression> projections,
            List<TableExpressionBase> tables,
            SqlExpression predicate,
            List<OrderingExpression> orderings,
            SqlExpression limit,
            SqlExpression offset,
            bool distinct,
            string alias)
        {
            var projectionMapping = new Dictionary<ProjectionMember, Expression>();
            foreach (var kvp in _projectionMapping)
            {
                projectionMapping[kvp.Key] = kvp.Value;
            }

            return new SelectExpression(alias, projections, tables, orderings)
            {
                _projectionMapping = projectionMapping,
                Predicate = predicate,
                Offset = offset,
                Limit = limit,
                IsDistinct = distinct,
                SetOperationType = SetOperationType
            };
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.GetHashCode());
            foreach (var projectionMapping in _projectionMapping)
            {
                hash.Add(projectionMapping.Key);
                hash.Add(projectionMapping.Value);
            }

            foreach (var table in _tables)
            {
                hash.Add(table);
            }

            hash.Add(Predicate);

            foreach (var ordering in _orderings)
            {
                hash.Add(ordering);
            }

            hash.Add(Offset);
            hash.Add(Limit);
            hash.Add(IsDistinct);

            return hash.ToHashCode();
        }

        public override void Print(ExpressionPrinter expressionPrinter)
        {
            expressionPrinter.StringBuilder.AppendLine("Projection Mapping:");
            using (expressionPrinter.StringBuilder.Indent())
            {
                foreach (var projectionMappingEntry in _projectionMapping)
                {
                    expressionPrinter.StringBuilder.AppendLine();
                    expressionPrinter.StringBuilder.Append(projectionMappingEntry.Key + " -> ");
                    expressionPrinter.Visit(projectionMappingEntry.Value);
                }
            }

            expressionPrinter.StringBuilder.AppendLine();
            if (Alias != null)
            {
                expressionPrinter.StringBuilder.AppendLine("(");
                expressionPrinter.StringBuilder.IncrementIndent();
            }

            expressionPrinter.StringBuilder.Append("SELECT ");

            if (IsDistinct)
            {
                expressionPrinter.StringBuilder.Append("DISTINCT ");
            }

            if (Limit != null
                && Offset == null)
            {
                expressionPrinter.StringBuilder.Append("TOP(");
                expressionPrinter.Visit(Limit);
                expressionPrinter.StringBuilder.Append(") ");
            }

            if (Projection.Any())
            {
                expressionPrinter.VisitList(Projection);
            }
            else
            {
                expressionPrinter.StringBuilder.Append("1");
            }

            if (Tables.Any())
            {
                expressionPrinter.StringBuilder.AppendLine().Append("FROM ");

                expressionPrinter.VisitList(Tables, p => p.StringBuilder.AppendLine());
            }

            if (Predicate != null)
            {
                expressionPrinter.StringBuilder.AppendLine().Append("WHERE ");
                expressionPrinter.Visit(Predicate);
            }

            if (Orderings.Any())
            {
                var orderings = Orderings.ToList();
                if (orderings.Count > 0)
                {
                    expressionPrinter.StringBuilder.AppendLine().Append("ORDER BY ");
                    expressionPrinter.VisitList(orderings);
                }
            }
            else if (Offset != null)
            {
                expressionPrinter.StringBuilder.AppendLine().Append("ORDER BY (SELECT 1)");
            }

            if (Offset != null)
            {
                expressionPrinter.StringBuilder.AppendLine().Append("OFFSET ");
                expressionPrinter.Visit(Offset);
                expressionPrinter.StringBuilder.Append(" ROWS");

                if (Limit != null)
                {
                    expressionPrinter.StringBuilder.Append(" FETCH NEXT ");
                    expressionPrinter.Visit(Limit);
                    expressionPrinter.StringBuilder.Append(" ROWS ONLY");
                }
            }

            if (Alias != null)
            {
                expressionPrinter.StringBuilder.DecrementIndent();
                expressionPrinter.StringBuilder.AppendLine().Append(") AS " + Alias);
            }
        }
    }

    /// <summary>
    /// Marks a <see cref="SelectExpression"/> as representing an SQL set operation, such as a UNION.
    /// </summary>
    public enum SetOperationType
    {
        /// <summary>
        /// Represents a regular SQL SELECT expression that isn't a set operation.
        /// </summary>
        None = 0,

        /// <summary>
        /// Represents an SQL UNION set operation.
        /// </summary>
        Union = 1,

        /// <summary>
        /// Represents an SQL UNION ALL set operation.
        /// </summary>
        UnionAll = 2,

        /// <summary>
        /// Represents an SQL INTERSECT set operation.
        /// </summary>
        Intersect = 3,

        /// <summary>
        /// Represents an SQL EXCEPT set operation.
        /// </summary>
        Except = 4
    }
}

