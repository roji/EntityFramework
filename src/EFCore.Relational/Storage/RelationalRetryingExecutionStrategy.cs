// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.Common;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

#nullable enable

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore
{
    /// <summary>
    ///     <para>
    ///         An <see cref="IExecutionStrategy" /> implementation for retrying failed executions based on
    ///         <see cref="DbException.IsTransient" />.
    ///     </para>
    ///     <para>
    ///         Not all ADO.NET drivers implement <see cref="DbException.IsTransient" />; consult your driver documentation before using
    ///         this execution strategy.
    ///     </para>
    /// </summary>
    public class RelationalRetryingExecutionStrategy : ExecutionStrategy
    {
        private readonly Func<Exception, bool>? _additionalTransienceDetector;

        /// <summary>
        ///     <para>
        ///         Creates a new instance of <see cref="RelationalRetryingExecutionStrategy" />.
        ///     </para>
        ///     <para>
        ///         Default values of 6 for the maximum retry count and 30 seconds for the maximum default delay are used.
        ///     </para>
        /// </summary>
        /// <param name="context"> The context on which the operations will be invoked. </param>
        public RelationalRetryingExecutionStrategy(
            [NotNull] DbContext context)
            : this(context, DefaultMaxRetryCount)
        {
        }

        /// <summary>
        ///     <para>
        ///         Creates a new instance of <see cref="RelationalRetryingExecutionStrategy" />.
        ///     </para>
        ///     <para>
        ///         Default values of 6 for the maximum retry count and 30 seconds for the maximum default delay are used.
        ///     </para>
        /// </summary>
        /// <param name="dependencies"> Parameter object containing service dependencies. </param>
        public RelationalRetryingExecutionStrategy(
            [NotNull] ExecutionStrategyDependencies dependencies)
            : this(dependencies, DefaultMaxRetryCount)
        {
        }

        /// <summary>
        ///     <para>
        ///         Creates a new instance of <see cref="RelationalRetryingExecutionStrategy" />.
        ///     </para>
        ///     <para>
        ///         A default value 30 seconds for the maximum default delay is used.
        ///     </para>
        /// </summary>
        /// <param name="context"> The context on which the operations will be invoked. </param>
        /// <param name="maxRetryCount"> The maximum number of retry attempts. </param>
        public RelationalRetryingExecutionStrategy(
            [NotNull] DbContext context,
            int maxRetryCount)
            : this(context, maxRetryCount, DefaultMaxDelay, additionalTransienceDetector: null)
        {
        }

        /// <summary>
        ///     <para>
        ///         Creates a new instance of <see cref="RelationalRetryingExecutionStrategy" />.
        ///     </para>
        ///     <para>
        ///         A default value 30 seconds for the maximum default delay is used.
        ///     </para>
        /// </summary>
        /// <param name="dependencies"> Parameter object containing service dependencies. </param>
        /// <param name="maxRetryCount"> The maximum number of retry attempts. </param>
        public RelationalRetryingExecutionStrategy(
            [NotNull] ExecutionStrategyDependencies dependencies,
            int maxRetryCount)
            : this(dependencies, maxRetryCount, DefaultMaxDelay, additionalTransienceDetector: null)
        {
        }

        /// <summary>
        ///     Creates a new instance of <see cref="RelationalRetryingExecutionStrategy" />.
        /// </summary>
        /// <param name="context"> The context on which the operations will be invoked. </param>
        /// <param name="maxRetryCount"> The maximum number of retry attempts. </param>
        /// <param name="maxRetryDelay"> The maximum delay between retries. </param>
        /// <param name="additionalTransienceDetector">
        ///     An optional function to identify additional exceptions as transient, beyond what is exposed by
        ///     <see cref="DbException.IsTransient "/>.
        /// </param>
        public RelationalRetryingExecutionStrategy(
            [NotNull] DbContext context,
            int maxRetryCount,
            TimeSpan maxRetryDelay,
            [CanBeNull] Func<Exception, bool>? additionalTransienceDetector)
            : base(
                context,
                maxRetryCount,
                maxRetryDelay)
            => _additionalTransienceDetector = additionalTransienceDetector;

        /// <summary>
        ///     Creates a new instance of <see cref="RelationalRetryingExecutionStrategy" />.
        /// </summary>
        /// <param name="dependencies"> Parameter object containing service dependencies. </param>
        /// <param name="maxRetryCount"> The maximum number of retry attempts. </param>
        /// <param name="maxRetryDelay"> The maximum delay between retries. </param>
        /// <param name="additionalTransienceDetector">
        ///     An optional function to identify additional exceptions as transient, beyond what is exposed by
        ///     <see cref="DbException.IsTransient "/>.
        /// </param>
        public RelationalRetryingExecutionStrategy(
            [NotNull] ExecutionStrategyDependencies dependencies,
            int maxRetryCount,
            TimeSpan maxRetryDelay,
            Func<Exception, bool>? additionalTransienceDetector)
            : base(dependencies, maxRetryCount, maxRetryDelay)
            => _additionalTransienceDetector = additionalTransienceDetector;

        /// <summary>
        ///     Determines whether the specified exception represents a transient failure that can be
        ///     compensated by a retry. Additional exceptions to retry on can be passed to the constructor.
        /// </summary>
        /// <param name="exception"> The exception object to be verified. </param>
        /// <returns>
        ///     <see langword="true" /> if the specified exception is considered as transient, otherwise <see langword="false" />.
        /// </returns>
        protected override bool ShouldRetryOn(Exception? exception)
            => exception is not null
                && (
                    exception is DbException { IsTransient: true }
                    || exception is TimeoutException
                    || _additionalTransienceDetector?.Invoke(exception) == true);
    }
}
