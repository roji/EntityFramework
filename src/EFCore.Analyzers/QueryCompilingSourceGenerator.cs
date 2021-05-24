// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.EntityFrameworkCore.Util;

#pragma warning disable 162
#pragma warning disable RS2000

namespace Microsoft.EntityFrameworkCore
{
    [Generator]
    public class QueryCompilingSourceGenerator : ISourceGenerator
    {
        private static readonly DiagnosticDescriptor PrecompilationFailed = new(
            id: "EF2001",
            title: "Couldn't pre-compile query, see generated source file for more details.",
            messageFormat: "Entity Framework query pre-compilation failed.",
            category: "Usage",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly SymbolDisplayFormat QualifiedTypeNameSymbolDisplayFormat = new(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

        private bool _skipOptInCheck = true; // TODO: Needs to be false (and work)
        private readonly Dictionary<string, Assembly> _referencedAssemblies = new();
        private IndentedStringBuilder? _stringBuilder;

        public QueryCompilingSourceGenerator()
        {
            // Don't collapse this with the other overload
        }

        public QueryCompilingSourceGenerator(bool skipOptInCheck)
        {
            // TODO: This is only used for tests - do this by properly passing in AnalyzerConfigOptions (.editorconfig)
            _skipOptInCheck = skipOptInCheck;
        }

        /// <inheritdoc />
        public void Initialize(GeneratorInitializationContext context)
            => context.RegisterForSyntaxNotifications(() => new CompiledQueryFinder());

        public void Execute(GeneratorExecutionContext context)
        {
            try
            {
                AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;

                ExecuteCore(context);
            }
            finally
            {
                AssemblyLoadContext.Default.Resolving -= OnAssemblyResolving;
            }
        }

        private void ExecuteCore(GeneratorExecutionContext context)
        {
            if (!_skipOptInCheck && (!context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.EfPrecompileQueries", out var enablePrecompilation)
                || enablePrecompilation != "enabled"))
            {
                return;
            }

            // We need to reference Types (real .NET reflection Type, not Roslyn type symbols), so produce a .NET Assembly from the
            // compilation
            // TODO: Do this lazily, only after we know we need it
            var compilation = context.Compilation;
            InitializeAssemblyResolution(compilation);

            // We need a regular .NET Assembly from the compilation we've just performed
            using var memoryStream = new MemoryStream();
            var emitResult = compilation.Emit(memoryStream);

            if (!emitResult.Success)
            {
                // TODO: Error handling here...
                throw new InvalidOperationException("Could not emit compilation");
            }

            memoryStream.Seek(0, SeekOrigin.Begin);
            var compilationAssembly = AssemblyLoadContext.Default.LoadFromStream(memoryStream);

            _stringBuilder = new IndentedStringBuilder();

            _stringBuilder.AppendLine(@"
#pragma warning disable CS1591

public class Foo
{
    public void PrecompileQueries()
    {
");

            _stringBuilder.IncrementIndent().IncrementIndent();

            var expressionTranslator = new RoslynToExpressionTreeTranslator(typeSymbol => ResolveTypeSymbol(typeSymbol, compilation, compilationAssembly));
            var expressionPrinter = new ExpressionPrinter();

            // TODO: Do these lazily, only if we found candidate invocations etc.

            // TODO: Qualify by assembly?
            if (compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EF") is not INamedTypeSymbol efTypeSymbol)
            {
                // TODO: Emit warning
                return;
            }

            if (compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Query.Internal.IQueryCompiler") is not INamedTypeSymbol queryCompilerTypeSymbol ||
                ResolveTypeSymbol(queryCompilerTypeSymbol, compilation, compilationAssembly) is not { } queryCompilerType ||
                queryCompilerType.GetMethod("CreateCompiledQueryExpression") is not { } genericCreateCompiledQueryExpressionMethod) // TODO: specify parameters in GetMethod
            {
                return;
            }

            if (compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions") is not INamedTypeSymbol accessorExtensionsTypeSymbol ||
                ResolveTypeSymbol(accessorExtensionsTypeSymbol, compilation, compilationAssembly) is not { } accessorExtensionsType ||
                accessorExtensionsType.GetMethod("GetService") is not { } getServiceMethod) // TODO: specify parameters in GetMethod
            {
                return;
            }

            var getQueryCompilerMethod = getServiceMethod.MakeGenericMethod(queryCompilerType);
            // var getQueryCompilerMethodInfo = getServiceMethodInfo.MakeGenericMethod()

            // var getServiceMethodInfo = null;

            // Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService

            // TODO: Check whether invocations in the same syntax tree are actually grouped. If not, need to group for perf.
            SyntaxTree? syntaxTree = null;
            SemanticModel? semanticModel = null;

            foreach (var invocation in ((CompiledQueryFinder)context.SyntaxReceiver!).QueryCompilationInvocations)
            {
                _stringBuilder
                    .AppendLine("{")
                    .IncrementIndent()
                    .AppendLine($"// Compiled query from: {invocation.SyntaxTree.FilePath}, line {invocation.SyntaxTree.GetLineSpan(invocation.Span).StartLinePosition.Line}")
                    .AppendLine();

                if (invocation.SyntaxTree != syntaxTree)
                {
                    syntaxTree = invocation.SyntaxTree;
                    semanticModel = compilation.GetSemanticModel(syntaxTree);
                }

                if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol
                    || !methodSymbol.ContainingType.Equals(efTypeSymbol, SymbolEqualityComparer.Default))
                {
                    goto InvocationEnd;
                }

                // Extract the result type of the query (TResult) from the lambda that EF.CompileQuery returns
                // if (ResolveTypeSymbol(methodSymbol.TypeArguments[1], compilation, compilationAssembly) is not { } queryReturnType)
                if (methodSymbol.ReturnType is not INamedTypeSymbol returnedLambdaTypeSymbol
                    || ResolveTypeSymbol(returnedLambdaTypeSymbol.TypeArguments[returnedLambdaTypeSymbol.TypeArguments.Length-1], compilation, compilationAssembly) is not { } queryReturnType)
                {
                    goto InvocationEnd;
                }

                // Found a confirmed invocation of EF.CompileQuery. Extract the lambda parameter.
                // TODO: Hard-coding to a particular overload for now
                // TODO: Only ParenthesizedLambdaExpressionSyntax, no simple?
                if (invocation.ArgumentList.Arguments.Count != 1 ||
                    invocation.ArgumentList.Arguments[0].Expression is not ParenthesizedLambdaExpressionSyntax lambda ||
                    lambda.ExpressionBody is null ||
                    lambda.Modifiers.Any() ||
                    !lambda.AsyncKeyword.IsKind(SyntaxKind.None) ||
                    lambda.ParameterList.Parameters.Count == 0)
                {
                    goto InvocationEnd;
                }

                // We have a query lambda, as a Roslyn syntax tree. Translate to LINQ expression tree.
                var translatedLambda = (LambdaExpression)expressionTranslator.Translate(lambda, semanticModel!)!;

                // We have the query as a LINQ expression tree. To proceed to compilation, we instantiate a DbContext (parameterless
                // constructor) and extract IQueryCompiler from it.

                // The first parameter to the lambda is the DbContext; get the CLR Type for that
                // TODO: think about where ResolveTypeSymbol should live, factoring
                if (semanticModel.GetDeclaredSymbol(lambda.ParameterList.Parameters[0]) is not { } contextParameterSymbol
                    || ResolveTypeSymbol(contextParameterSymbol.Type, compilation, compilationAssembly) is not { } contextParameterType)
                {
                    goto InvocationEnd;
                }

                // TODO: Reusing the same DbContext instance (and QueryCompilationContextFactory) to compile multiple queries
                // (also don't need to resolve the type symbol again and again above, once is enough)

                try
                {
                    var dbContext = Activator.CreateInstance(contextParameterType);

                    var expression = new QueryExpressionRewriter(dbContext, translatedLambda.Parameters).Visit(translatedLambda.Body);

                    // TODO: Async
                    var queryCompiler = getQueryCompilerMethod.Invoke(null, new[] { dbContext });
                    var createCompiledQueryExpressionMethod = genericCreateCompiledQueryExpressionMethod.MakeGenericMethod(queryReturnType);
                    var compiledQuery = (Expression)createCompiledQueryExpressionMethod.Invoke(queryCompiler, new[] { expression });

                    // We have the generated code ready, serialize it.
                    _stringBuilder
                        .AppendLine("/*")
                        .AppendLine(expressionPrinter.Print(compiledQuery))
                        .AppendLine("*/");
                }
                catch (Exception e)
                {
                    // If the DbContext's parameterless constructor doesn't exist or mis-configures the context, we get here. Need
                    // to warn appropriately.
                    _stringBuilder
                        .AppendLine("/* Exception occured while translating:")
                        .AppendLine(e.ToString())
                        .AppendLine("*/");

                    context.ReportDiagnostic(Diagnostic.Create(PrecompilationFailed, invocation.GetLocation()));
                }

                InvocationEnd:
                _stringBuilder
                    .DecrementIndent()
                    .AppendLine("}");
            }

            _stringBuilder
                .DecrementIndent()
                .AppendLine("}")
                .DecrementIndent()
                .AppendLine("}");

            context.AddSource("EF.PrecompiledQueries.Generated.cs", SourceText.From(_stringBuilder.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// Finds invocations of <c>EF.CompileQuery</c> and related methods.
        /// </summary>
        private class CompiledQueryFinder : ISyntaxReceiver
        {
            public List<InvocationExpressionSyntax> QueryCompilationInvocations { get; } = new();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (syntaxNode is InvocationExpressionSyntax
                    {
                        Expression : MemberAccessExpressionSyntax
                        {
                            Expression: IdentifierNameSyntax { Identifier : { Text : "EF" } }
                        } memberAccess
                    } invocation)
                {
                    // TODO: Go over the overloads, parameter numbers...
                    switch (memberAccess.Name.Identifier.Text)
                    {
                        case "CompileQuery":
                        case "CompileAsyncQuery":
                            QueryCompilationInvocations.Add(invocation);
                            break;
                    }
                }
            }
        }

        public Type? ResolveTypeSymbol(ITypeSymbol typeSymbol, Compilation compilation, Assembly compilationAssembly)
        {
            Assembly assembly;

            if (typeSymbol.ContainingAssembly.Equals(compilation.Assembly, SymbolEqualityComparer.Default))
            {
                assembly = compilationAssembly;
            }
            else if (typeSymbol.ContainingAssembly.Name == "System.Private.CoreLib"
                || typeSymbol.ContainingAssembly.Name == "System.Runtime")
            {
                assembly = typeof(object).Assembly;
            }
            else
            {
                if (compilation.GetMetadataReference(typeSymbol.ContainingAssembly) is not PortableExecutableReference portableExecutableReference
                    || portableExecutableReference.FilePath is not string assemblyPath)
                {
                    // The symbol resides in a non-portable assembly, so we can't get a path from it and therefore load it.
                    return null;
                }

                // TODO: Awful hack, but probably only needed since my test project references EF Core via <ProjectReference> - the
                // metadata reference points to the ref DLL, which cannot be loaded.

                if (assemblyPath.Contains("/ref/"))
                {
                    assemblyPath = assemblyPath.Replace("/ref/", "/");
                }

                if (assemblyPath.Contains(@"\ref\"))
                {
                    assemblyPath = assemblyPath.Replace(@"\ref\", "/");
                }

                try
                {
                    // assembly = _assemblyLoadContext!.LoadFromAssemblyPath(assemblyPath);
                    // assembly = Assembly.LoadFile(assemblyPath);

                    assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
                }
                catch (Exception e)
                {
                    throw e;
                }
            }

            switch (typeSymbol)
            {
                case INamedTypeSymbol { IsGenericType: true } genericTypeSymbol
                    // TODO: Hacky... Detect open type, to avoid trying MakeGenericType on it
                    when genericTypeSymbol.TypeArguments.Any(a => a is ITypeParameterSymbol):
                {
                    var genericTypeName = genericTypeSymbol.ToDisplayString(QualifiedTypeNameSymbolDisplayFormat)
                                          + '`' + genericTypeSymbol.TypeParameters.Length;

                    // var type = assembly.GetType(FormatTypeName(genericTypeSymbol));

                    var type = assembly.GetType(genericTypeName);

                    return type;
                }

                case INamedTypeSymbol { IsGenericType: true } genericTypeSymbol:
                {
                    var genericTypeName = genericTypeSymbol.OriginalDefinition.ToDisplayString(QualifiedTypeNameSymbolDisplayFormat)
                                          + '`' + genericTypeSymbol.TypeParameters.Length;

                    var definition = assembly.GetType(genericTypeName);
                    var typeArguments = genericTypeSymbol.TypeArguments.Select(a => ResolveTypeSymbol(a, compilation, compilationAssembly)).ToArray();
                    if (typeArguments.Contains(null))
                        return null;

                    return definition?.MakeGenericType(typeArguments!);
                }

                default:
                    return assembly.GetType(typeSymbol.ToDisplayString(QualifiedTypeNameSymbolDisplayFormat));
            }

            // if (typeSymbol is INamedTypeSymbol { IsGenericType: true } genericTypeSymbol)
            // {
            //     var genericTypeName = genericTypeSymbol.OriginalDefinition.ToDisplayString(QualifiedTypeNameSymbolDisplayFormat)
            //                           + '`' + genericTypeSymbol.TypeParameters.Length;
            //
            //     var definition = assembly.GetType(genericTypeName);
            //     var typeArguments = genericTypeSymbol.TypeArguments.Select(ResolveTypeSymbol).ToArray();
            //     if (typeArguments.Contains(null))
            //         return null;
            //
            //     return definition?.MakeGenericType(typeArguments!);
            // }
            //
            // return assembly.GetType(typeSymbol.ToDisplayString(QualifiedTypeNameSymbolDisplayFormat));
        }

        private void InitializeAssemblyResolution(Compilation compilation)
        {
            _referencedAssemblies.Clear();

            foreach (var metadataReference in compilation.References.OfType<PortableExecutableReference>())
            {
                if (metadataReference.FilePath is null)
                    continue;

                // TODO: Awful hack, but probably only needed since my test project references EF Core via <ProjectReference> - the
                // metadata reference points to the ref DLL, which cannot be loaded.
                var assemblyPath = metadataReference.FilePath;

                if (assemblyPath.Contains("/ref/"))
                {
                    assemblyPath = assemblyPath.Replace("/ref/", "/");
                }

                if (assemblyPath.Contains(@"\ref\"))
                {
                    assemblyPath = assemblyPath.Replace(@"\ref\", "/");
                }

                try
                {
                    // TODO: The following is... fuzzy...
                    // var assembly = LoadFromAssemblyPath(assemblyPath);

                    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
                    _referencedAssemblies[assembly.GetName().Name] = assembly;
                } catch {}
            }
        }

        private Assembly? OnAssemblyResolving(AssemblyLoadContext loadContext, AssemblyName assemblyName)
            => _referencedAssemblies.TryGetValue(assemblyName.Name, out var assembly)
                ? assembly
                : null;

        // private class MyAssemblyLoadContext : AssemblyLoadContext
        // {
        //     private readonly IndentedStringBuilder _stringBuilder;
        //     private Dictionary<string, Assembly> _referencedAssemblies = new();
        //
        //     public MyAssemblyLoadContext(Compilation compilation, IndentedStringBuilder stringBuilder)
        //     {
        //         _stringBuilder = stringBuilder;
        //
        //         foreach (var metadataReference in compilation.References.OfType<PortableExecutableReference>())
        //         {
        //             if (metadataReference.FilePath is null)
        //                 continue;
        //
        //             // TODO: Awful hack, but probably only needed since my test project references EF Core via <ProjectReference> - the
        //             // metadata reference points to the ref DLL, which cannot be loaded.
        //             var assemblyPath = metadataReference.FilePath.Contains("/ref/")
        //                 ? metadataReference.FilePath.Replace("/ref/", "/")
        //                 : metadataReference.FilePath;
        //
        //             try
        //             {
        //                 // TODO: The following is... fuzzy...
        //                 var assembly = LoadFromAssemblyPath(assemblyPath);
        //                 _referencedAssemblies[assembly.GetName().Name] = assembly;
        //             } catch {}
        //         }
        //     }
        //
        //     protected override Assembly? Load(AssemblyName assemblyName)
        //     {
        //         // _stringBuilder.AppendLine($"// Loading assembly: {assemblyName.Name}");
        //         //
        //         // var dump = string.Join(", ", _referencedAssemblies.Keys.Where(k => k.Contains("DependencyInjection")));
        //         // _stringBuilder.AppendLine($"// Assembly dump: {dump}");
        //         //
        //         return _referencedAssemblies.TryGetValue(assemblyName.Name, out var assembly)
        //             ? assembly
        //             : null;
        //     }
        // }

        // TODO: Adapted from CompiledQueryBase
        private sealed class QueryExpressionRewriter : ExpressionVisitor
        {
            public const string QueryParameterPrefix = "__";

            private readonly object _context;
            private readonly IReadOnlyCollection<ParameterExpression> _parameters;

            public QueryExpressionRewriter(
                object context,
                IReadOnlyCollection<ParameterExpression> parameters)
            {
                _context = context;
                _parameters = parameters;
            }

            protected override Expression VisitParameter(ParameterExpression parameterExpression)
            {
                Check.NotNull(parameterExpression, nameof(parameterExpression));

                if (_context.GetType().IsAssignableFrom(parameterExpression.Type))
                {
                    return Expression.Constant(_context);
                }

                return _parameters.Contains(parameterExpression)
                    ? Expression.Parameter(
                        parameterExpression.Type,
                        QueryParameterPrefix + parameterExpression.Name)
                    : parameterExpression;
            }
        }
    }
}
