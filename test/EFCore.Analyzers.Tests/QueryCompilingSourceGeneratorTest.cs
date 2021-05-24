// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;


namespace Microsoft.EntityFrameworkCore
{
    public class QueryCompilingSourceGeneratorTest
    {
        [Fact]
        public void Foo()
            => Test(@"_ = EF.CompileQuery((BloggingContext context) => context.Blogs);");

        private void Test(string sourceFragment)
        {
                        var source = $@"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

{sourceFragment}

class BloggingContext : DbContext
{{
    public DbSet<Blog> Blogs {{ get; set; }}

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer(@""Server=(localdb)\mssqllocaldb;Database=Blogging;Integrated Security=True"");
}}

class Blog
{{
    public int Id {{ get; set; }}
    public int Rank {{ get; set; }}
}}";

            // https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md#unit-testing-of-generators

            // TODO: Clean all this up
            var dotNetCoreDir = Path.GetDirectoryName(typeof(object).GetTypeInfo().Assembly.Location);

            var inputCompilation = CSharpCompilation.Create("compilation",
                new[] { CSharpSyntaxTree.ParseText(source) },
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
                    MetadataReference.CreateFromFile(typeof(DbConnection).GetTypeInfo().Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(IListSource).GetTypeInfo().Assembly.Location),

                    MetadataReference.CreateFromFile(typeof(DbContext).GetTypeInfo().Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(SqlServerDbContextOptionsExtensions).GetTypeInfo().Assembly.Location),

                    MetadataReference.CreateFromFile(Path.Combine(dotNetCoreDir!, "System.Runtime.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(dotNetCoreDir!, "System.Collections.dll"))
                },
                new CSharpCompilationOptions(OutputKind.ConsoleApplication));

            var generator = new QueryCompilingSourceGenerator(skipOptInCheck: true);

            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

            driver.RunGeneratorsAndUpdateCompilation(inputCompilation, out var outputCompilation, out var diagnostics);

            if (!diagnostics.IsEmpty)
            {
                var stringBuilder = new StringBuilder()
                    .AppendLine("Compilation failed:")
                    .AppendLine();

                foreach (var diagnostic in diagnostics)
                {
                    stringBuilder.AppendLine(diagnostic.ToString());
                }

                Assert.True(false, "Compilation failed: " + stringBuilder);
            }

            Assert.Equal(2, outputCompilation.SyntaxTrees.Count());
            // var diagnostics2 = outputCompilation.GetDiagnostics();
            // Assert.True(outputCompilation.GetDiagnostics().IsEmpty);

            var outputTree = outputCompilation.SyntaxTrees.Single(t => t.FilePath.EndsWith("EF.PrecompiledQueries.Generated.cs", StringComparison.Ordinal));

            throw new Exception("Code: " + outputTree);
        }
    }
}
