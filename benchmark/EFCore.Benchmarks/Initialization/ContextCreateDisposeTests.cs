// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore.Benchmarks.Models.AdventureWorks;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore.Benchmarks.Initialization
{
    [MemoryDiagnoser]
    public abstract class ContextCreateDisposeTests
    {
        protected abstract void ConfigureProvider(DbContextOptionsBuilder builder);
        private DbContextOptions<MyDbContext> _options;

        private PooledDbContextFactory<MyDbContext> _pooledDbContextFactory;

        [Params(true, false)]
        public bool WithInternalServiceProvider { get; set; }

        [GlobalSetup]
        public void GlobalSetup()
        {
            var builder = new DbContextOptionsBuilder<MyDbContext>();
            ConfigureProvider(builder);
            _options = builder.Options;
            _pooledDbContextFactory = new PooledDbContextFactory<MyDbContext>(
                new DbContextPool<MyDbContext>(_options));
        }

        [Benchmark(Baseline = true)]
        public void Unpooled()
        {
            using var ctx = new MyDbContext(_options);
            if (WithInternalServiceProvider)
            {
                _ = ((IInfrastructure<IServiceProvider>)ctx).Instance;
            }
        }

        [Benchmark]
        public void Pooled()
        {
            using var ctx = _pooledDbContextFactory.CreateDbContext();
            if (WithInternalServiceProvider)
            {
                _ = ((IInfrastructure<IServiceProvider>)ctx).Instance;
            }
        }
    }

    class MyDbContext : DbContext
    {
        public MyDbContext(DbContextOptions options) : base(options) {}
    }
}
