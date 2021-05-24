// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

#nullable disable // netstandard2.0
// ReSharper disable AssignNullToNotNullAttribute

namespace Microsoft.EntityFrameworkCore
{
    public class LinqExpressionToCSharpTranslator : ExpressionVisitor
    {
        protected SyntaxNode Result { get; set; }

        public virtual SyntaxNode Translate(Expression node)
            => Visit(node) is null ? null : Result;

        public virtual string TranslateAndSerialize(Expression node)
            => Translate(node) is { } translated ? translated.NormalizeWhitespace().ToFullString() : null;

        /// <inheritdoc />
        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (TranslationFailed(node.Left, out var left) || TranslationFailed(node.Right, out var right))
                return null;

            Result = node.NodeType switch
            {
                ExpressionType.Equal => BinaryExpression(SyntaxKind.EqualsExpression, left, right),
                ExpressionType.NotEqual => BinaryExpression(SyntaxKind.NotEqualsExpression, left, right),
                ExpressionType.GreaterThan => BinaryExpression(SyntaxKind.GreaterThanExpression, left, right),
                ExpressionType.LessThan => BinaryExpression(SyntaxKind.LessThanExpression, left, right),
                ExpressionType.GreaterThanOrEqual => BinaryExpression(SyntaxKind.GreaterThanOrEqualExpression, left, right),
                ExpressionType.LessThanOrEqual => BinaryExpression(SyntaxKind.LessThanOrEqualExpression, left, right),

                ExpressionType.AndAlso => BinaryExpression(SyntaxKind.LogicalAndExpression, left, right),
                ExpressionType.OrElse => BinaryExpression(SyntaxKind.LogicalOrExpression, left, right),

                ExpressionType.Assign => AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, left, right),

                ExpressionType.Add => throw new NotImplementedException(),
                ExpressionType.AddAssign => throw new NotImplementedException(),
                ExpressionType.AddAssignChecked => throw new NotImplementedException(),
                ExpressionType.AddChecked => throw new NotImplementedException(),
                ExpressionType.Subtract => throw new NotImplementedException(),
                ExpressionType.SubtractAssign => throw new NotImplementedException(),
                ExpressionType.SubtractAssignChecked => throw new NotImplementedException(),
                ExpressionType.SubtractChecked => throw new NotImplementedException(),
                ExpressionType.Divide => throw new NotImplementedException(),
                ExpressionType.DivideAssign => throw new NotImplementedException(),
                ExpressionType.Modulo => throw new NotImplementedException(),
                ExpressionType.ModuloAssign => throw new NotImplementedException(),
                ExpressionType.Multiply => throw new NotImplementedException(),
                ExpressionType.MultiplyAssign => throw new NotImplementedException(),
                ExpressionType.MultiplyAssignChecked => throw new NotImplementedException(),
                ExpressionType.MultiplyChecked => throw new NotImplementedException(),
                ExpressionType.LeftShift => throw new NotImplementedException(),
                ExpressionType.LeftShiftAssign => throw new NotImplementedException(),
                ExpressionType.RightShift => throw new NotImplementedException(),
                ExpressionType.RightShiftAssign => throw new NotImplementedException(),
                ExpressionType.And => throw new NotImplementedException(),
                ExpressionType.AndAssign => throw new NotImplementedException(),
                ExpressionType.Or => throw new NotImplementedException(),
                ExpressionType.OrAssign => throw new NotImplementedException(),
                ExpressionType.ExclusiveOr => throw new NotImplementedException(),
                ExpressionType.ExclusiveOrAssign => throw new NotImplementedException(),
                ExpressionType.Power => throw new NotImplementedException(),
                ExpressionType.PowerAssign => throw new NotImplementedException(),
                ExpressionType.Coalesce => throw new NotImplementedException(),

                _ => null
            };

            return Result is null ? null : node;
        }

        /// <inheritdoc />
        protected override Expression VisitBlock(BlockExpression node)
        {
            var statements = new StatementSyntax[node.Variables.Count + node.Expressions.Count];

            for (var i = 0; i < node.Variables.Count; i++)
            {
                statements[i] = LocalDeclarationStatement(
                    VariableDeclaration(
                        type: IdentifierName(Identifier(TriviaList(), SyntaxKind.VarKeyword, "var", "var", TriviaList())),
                        variables: SingletonSeparatedList(VariableDeclarator(node.Variables[i].Name))));
            }

            for (var i = 0; i < node.Expressions.Count; i++)
            {
                if (TranslationFailed(node.Expressions[i], out var expression))
                    return null;
                statements[node.Variables.Count + i] = ExpressionStatement(expression);
            }

            Result = Block(statements);
            return node;
        }

        /// <inheritdoc />
        protected override Expression VisitConditional(ConditionalExpression node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Expression VisitConstant(ConstantExpression node)
        {
            Result = node.Value switch
            {
                int i => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(i)),
                string s => LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(s)),
                null => LiteralExpression(SyntaxKind.NullLiteralExpression),
                _ => null
            };

            return node;
        }

        /// <inheritdoc />
        protected override Expression VisitDebugInfo(DebugInfoExpression node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Expression VisitDefault(DefaultExpression node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Expression VisitGoto(GotoExpression node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Expression VisitInvocation(InvocationExpression node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override LabelTarget VisitLabelTarget(LabelTarget node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Expression VisitLabel(LabelExpression node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            if (node.Parameters.Count != 1)
                throw new NotImplementedException(); // ParenthesizedLambdaExpressionSyntax

            if (TranslationFailed(node.Body, out CSharpSyntaxNode body))
                return null;

            Result = SimpleLambdaExpression(Parameter(Identifier(node.Parameters[0].Name)), body);

            return node;
        }

        /// <inheritdoc />
        protected override Expression VisitLoop(LoopExpression node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Expression VisitMember(MemberExpression node)
        {
            if (TranslationFailed(node.Expression, out var expression))
                return null;

            Result = MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                expression,
                IdentifierName(node.Member.Name));

            return node;
        }

        /// <inheritdoc />
        protected override Expression VisitIndex(IndexExpression node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Expression VisitNewArray(NewArrayExpression node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Expression VisitNew(NewExpression node)
        {
            var arguments = new ArgumentSyntax[node.Arguments.Count];

            for (var i = 0; i < arguments.Length; i++)
            {
                if (TranslationFailed(node.Arguments[i], out var argument))
                    return null;
                arguments[i] = Argument(argument);
            }

            Result = ObjectCreationExpression(GetTypeSyntax(node.Type))
                .WithArgumentList(ArgumentList(SeparatedList(arguments)));

            return node;
        }

        /// <inheritdoc />
        protected override Expression VisitParameter(ParameterExpression node)
        {
            // In Roslyn, a parameter in a regular expression is just an identifier.
            // The parameter in the lambda declaration is handled separately in VisitLambda
            Result = IdentifierName(node.Name);
            return node;
        }

        /// <inheritdoc />
        protected override Expression VisitRuntimeVariables(RuntimeVariablesExpression node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override SwitchCase VisitSwitchCase(SwitchCase node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Expression VisitSwitch(SwitchExpression node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override CatchBlock VisitCatchBlock(CatchBlock node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Expression VisitTry(TryExpression node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Expression VisitTypeBinary(TypeBinaryExpression node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Expression VisitUnary(UnaryExpression node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Expression VisitMemberInit(MemberInitExpression node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Expression VisitListInit(ListInitExpression node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override ElementInit VisitElementInit(ElementInit node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override MemberBinding VisitMemberBinding(MemberBinding node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override MemberAssignment VisitMemberAssignment(MemberAssignment node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override MemberMemberBinding VisitMemberMemberBinding(MemberMemberBinding node)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override MemberListBinding VisitMemberListBinding(MemberListBinding node)
        {
            throw new NotImplementedException();
        }

        private bool TranslationFailed<T>(Expression node, out T translated)
            where T : class
        {
            if (Visit(node) is not null && Result is T result)
            {
                translated = result;
                return false;
            }

            translated = null;
            return true;
        }

        private bool TranslationFailed(Expression node, out ExpressionSyntax translated)
            => TranslationFailed<ExpressionSyntax>(node, out translated);

        private static TypeSyntax GetTypeSyntax(Type type)
            => type.IsGenericType
                ? GenericName(
                    Identifier(type.Name.Substring(0, type.Name.IndexOf('`'))),
                    TypeArgumentList(SeparatedList(type.GenericTypeArguments.Select(GetTypeSyntax))))
                : IdentifierName(type.Name);
    }
}
