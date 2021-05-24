#if NOT_YET
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Microsoft.EntityFrameworkCore
{
    public class RoslynToExpressionTreeTranslatorTests
    {
        [Fact]
        public void Single()
            => Test(
                @"
var blogs = new DbSet<Blog>();
_ = blogs.Single();",
                expression => {});

        [Fact]
        public void Where_Single()
            => Test(
                @"
var blogs = new DbSet<Blog>();
_ = blogs.Where(b => b.Rank == 3).Single();",
                expression => {});

        void Test(string sourceFragment, Action<IReadOnlyList<string>> assertAction)
        {
            var source = $@"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Linq.Expressions;

{sourceFragment}

class BloggingContext
{{
    public DbSet<Blog> Blogs {{ get; set; }}
}}

class DbSet<T> : IQueryable<T>, IAsyncEnumerable<T>
{{
    public IEnumerator<T> GetEnumerator() => throw new NotSupportedException();
    IEnumerator IEnumerable.GetEnumerator() => throw new NotSupportedException();
    public Type ElementType => throw new NotSupportedException();
    public Expression Expression => throw new NotSupportedException();
    public IQueryProvider Provider => throw new NotSupportedException();
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = new CancellationToken())
        => throw new NotSupportedException();
}}

class Blog
{{
    public int Rank {{ get; set; }}
}}";

            // https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md#unit-testing-of-generators

            // TODO: Clean this up
            var dotNetCoreDir = Path.GetDirectoryName(typeof(object).GetTypeInfo().Assembly.Location);

            var inputCompilation = CSharpCompilation.Create("compilation",
                new[] {CSharpSyntaxTree.ParseText(source)},
                new[]
                {
                    MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Type).GetTypeInfo().Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(IEnumerable<>).GetTypeInfo().Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).GetTypeInfo().Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(IQueryable<>).GetTypeInfo().Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Queryable).GetTypeInfo().Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(List<>).GetTypeInfo().Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Binder).GetTypeInfo().Assembly.Location),
                    MetadataReference.CreateFromFile(Path.Combine(dotNetCoreDir!, "System.Runtime.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(dotNetCoreDir!, "System.Collections.dll"))
                },
                new CSharpCompilationOptions(OutputKind.ConsoleApplication));

            var syntaxReceiver = new QueryableExpressionIdentifyingSyntaxReceiver();

            var wrappingGenerator = new DummyWrappingGenerator(syntaxReceiver);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(wrappingGenerator);
            driver = driver.RunGeneratorsAndUpdateCompilation(inputCompilation, out var outputCompilation, out var diagnostics);

            Assert.True(
                diagnostics.IsEmpty,
                "SyntaxReceiver failed: " + string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString())));

            // GeneratorDriverRunResult runResult = driver.GetRunResult();
            // Assert.Single(runResult.GeneratedTrees);
            // Assert.True(runResult.Diagnostics.IsEmpty);

            // We need to reference Types (real .NET reflection Type, not Roslyn type symbols), so produce an Assembly from the compilation
            Assembly compilationAssembly;
            using (var memoryStream = new MemoryStream())
            {
                Assert.True(inputCompilation.Emit(memoryStream).Success, "Could not emit compilation");
                memoryStream.Seek(0, SeekOrigin.Begin);
                compilationAssembly = Assembly.Load(memoryStream.ToArray());
            }

            var translator = new RoslynToExpressionTreeTranslator();

            foreach (var (syntaxTree, expressions) in syntaxReceiver.QueryableExpressions)
            {
                var semanticModel = inputCompilation.GetSemanticModel(syntaxTree);

                foreach (var expression in expressions)
                {
                    translator.Translate(expression, semanticModel, compilationAssembly);
                }
            }
        }

        [Generator]
        class DummyWrappingGenerator : ISourceGenerator
        {
            readonly ISyntaxContextReceiver _syntaxReceiver;

            public DummyWrappingGenerator(ISyntaxContextReceiver syntaxReceiver)
                => _syntaxReceiver = syntaxReceiver;

            public void Initialize(GeneratorInitializationContext context)
                => context.RegisterForSyntaxNotifications(() => _syntaxReceiver);

            public void Execute(GeneratorExecutionContext context)
            {
            }
        }
    }
}
#endif
