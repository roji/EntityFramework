// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.EntityFrameworkCore
{
    public class RoslynToExpressionTreeTranslator : CSharpSyntaxVisitor<Expression>
    {
        private readonly Func<ITypeSymbol, Type?> _typeResolver;
        private SemanticModel _semanticModel = null!;

        private readonly Stack<ImmutableDictionary<string, ParameterExpression>> _stack
            = new(new[] { ImmutableDictionary<string, ParameterExpression>.Empty });

        public RoslynToExpressionTreeTranslator(Func<ITypeSymbol, Type?> typeResolver)
            => _typeResolver = typeResolver;

        public Expression? Translate(SyntaxNode node, SemanticModel semanticModel)
        {
            _semanticModel = semanticModel;

            var result = Visit(node);

            Debug.Assert(_stack.Count == 1);
            return result;
        }

        public override Expression? VisitInvocationExpression(InvocationExpressionSyntax invocation)
        {
            if (!(_semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol methodSymbol))
                return null;

            // TODO: Potential mismatch between the assembly being referenced by the project being compiled and the assembly we load here
            var declaringType = _typeResolver(methodSymbol.ContainingType);

            if (declaringType is null)
                return null;

            // At the *syntactic* level, an extension method invocation looks like a normal instance's (because it is)
            // TODO: Test invoking extension without extension syntax (as static)
            var argCount = invocation.ArgumentList.Arguments.Count;
            Expression[] arguments;
            var destIndex = 0;

            if (methodSymbol.IsExtensionMethod && methodSymbol.ReceiverType is not null)
            {
                argCount++;
                arguments = new Expression[argCount];

                switch (invocation.Expression)
                {
                    case MemberAccessExpressionSyntax memberAccess:
                        if (Visit(memberAccess.Expression) is { } result)
                        {
                            arguments[destIndex++] = result;
                            break;
                        }
                        else return null;
                    default:
                        return null;
                }
            }
            else
            {
                arguments = new Expression[argCount];
            }

            for (var srcIndex = 0; srcIndex < invocation.ArgumentList.Arguments.Count; srcIndex++, destIndex++)
            {
                var arg = invocation.ArgumentList.Arguments[srcIndex];
                if (!arg.RefKindKeyword.IsKind(SyntaxKind.None))
                    return null;

                if (Visit(arg.Expression) is { } result)
                    arguments[destIndex] = result;
                else return null;
            }

            if (methodSymbol.IsGenericMethod)
            {
                var originalDefinition = methodSymbol.OriginalDefinition;
                var argTypes = originalDefinition.IsExtensionMethod && originalDefinition.ReceiverType is not null
                    ? new[] { _typeResolver(originalDefinition.ReceiverType) }.Concat(originalDefinition.Parameters.Select(p => _typeResolver(p.Type))).ToArray()
                    : throw new NotSupportedException();

                if (argTypes.Contains(null))
                    return null;

                // TODO: Match generic constraints?
                var definitionMethodInfos = declaringType.GetMethods()
                    .Where(m =>
                    {
                        if (m.Name != methodSymbol.Name ||
                            !m.IsGenericMethodDefinition ||
                            m.GetGenericArguments().Length != methodSymbol.TypeArguments.Length)
                        {
                            return false;
                        }

                        var candidateArgs = m.GetParameters();
                        if (candidateArgs.Length != argTypes.Length)
                            return false;
                        for (var i = 0; i < candidateArgs.Length; i++)
                        {
                            var candidateType = candidateArgs[i].ParameterType;
                            // TODO: The following is probably wrong.
                            if (candidateType.IsGenericType)
                                candidateType = candidateType.GetGenericTypeDefinition();
                            if (candidateType != (argTypes[i]!.IsGenericType ? argTypes[i]!.GetGenericTypeDefinition() : argTypes[i]!))
                                return false;
                        }
                        return true;
                    }).ToList();

                var definitionMethodInfo = definitionMethodInfos[0];

                if (definitionMethodInfo is null)
                    return null;

                var typeParams = methodSymbol.TypeArguments.Select(_typeResolver).ToArray();
                if (typeParams.Contains(null))
                    return null;

                var method = definitionMethodInfo.MakeGenericMethod(typeParams!);

                // TODO: Instance method
                return Expression.Call(null, method, arguments);
            }
            else
            {
                // TODO: Instance method
                // _result = Expression.Call(null, declaringType.GetMethod(methodSymbol.Name, argTypes!)!, arguments);
                throw new NotImplementedException();
            }
        }

        public override Expression? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax lambda)
        {
            if (lambda.ExpressionBody is null ||
                lambda.Modifiers.Any() ||
                !lambda.AsyncKeyword.IsKind(SyntaxKind.None))
            {
                return null;
            }

            var paramName = lambda.Parameter.Identifier.Text;
            if (_semanticModel.GetDeclaredSymbol(lambda.Parameter) is not { } parameterSymbol ||
                _typeResolver(parameterSymbol.Type) is not { } parameterType)
            {
                return null;
            }

            var parameter = Expression.Parameter(parameterType, paramName);
            _stack.Push(_stack.Peek().SetItem(paramName, parameter));

            try
            {
                return Visit(lambda.ExpressionBody) is { } body
                    ? Expression.Lambda(body, parameter)
                    : null;
            }
            finally
            {
                _stack.Pop();
            }
        }

        public override Expression? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax lambda)
        {
            if (lambda.ExpressionBody is null ||
                lambda.Modifiers.Any() ||
                !lambda.AsyncKeyword.IsKind(SyntaxKind.None))
            {
                return null;
            }

            var translatedParameters = new List<ParameterExpression>();
            foreach (var parameter in lambda.ParameterList.Parameters)
            {
                if (_semanticModel.GetDeclaredSymbol(parameter) is not { } parameterSymbol ||
                    _typeResolver(parameterSymbol.Type) is not { } parameterType)
                {
                    return null;
                }

                translatedParameters.Add(Expression.Parameter(parameterType, parameter.Identifier.Text));
            }

            _stack.Push(_stack.Peek().AddRange(translatedParameters.Select(p => new KeyValuePair<string, ParameterExpression>(p.Name, p))));

            try
            {
                return Visit(lambda.ExpressionBody) is { } body
                    ? Expression.Lambda(body, translatedParameters)
                    : null;
            }
            finally
            {
                _stack.Pop();
            }
        }

        public override Expression? VisitBinaryExpression(BinaryExpressionSyntax binary)
        {
            if (Visit(binary.Left) is not { } left)
                return null;

            if (Visit(binary.Right) is not { } right)
                return null;

            var expressionType = binary.OperatorToken.Kind() switch
            {
                SyntaxKind.EqualsEqualsToken => ExpressionType.Equal,
                _ => (ExpressionType?)null
            };

            if (expressionType is null)
                return null;

            return Expression.MakeBinary(expressionType.Value, left, right);
        }

        public override Expression? VisitMemberAccessExpression(MemberAccessExpressionSyntax memberAccess)
        {
            if (Visit(memberAccess.Expression) is not { } expression)
                return null;

            var member = expression.Type.GetMember(memberAccess.Name.Identifier.Text).SingleOrDefault();
            if (member is null)
                return null;

            return Expression.MakeMemberAccess(expression, member);
        }

        public override Expression? VisitIdentifierName(IdentifierNameSyntax identifierName)
        {
            if (_stack.Peek().TryGetValue(identifierName.Identifier.Text, out var parameter))
                return parameter;

            // TODO: Support closure parameter

            if (!(_semanticModel.GetSymbolInfo(identifierName).Symbol is ILocalSymbol localSymbol))
                return null;

            // TODO: Separate out EF Core-specific logic (EF Core would extend this visitor)
            if (localSymbol.Type.Name.Contains("DbSet"))
            {
                var queryRootType = _typeResolver(localSymbol.Type)!;
                // TODO: Decide what to actually return for query root
                return Expression.Constant(null, queryRootType);
            }

            return null;
        }

        public override Expression VisitLiteralExpression(LiteralExpressionSyntax literal)
        {
            // TODO: Set the type too?
            return Expression.Constant(literal.Token.Value);
        }

        public override Expression? DefaultVisit(SyntaxNode _) => null;
    }
}
