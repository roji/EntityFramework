// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Microsoft.EntityFrameworkCore.Storage
{
    /// <summary>
    ///     An implementation of <see cref="IExecutionStrategy" /> that does no retries, but detects transient exceptions via
    ///     <see cref="DbException.IsTransient" /> and wraps them in an <see cref="InvalidOperationException" /> which informs the user
    ///     of the transience.
    /// </summary>
    public class RelationalNonRetryingExecutionStrategy : IExecutionStrategy
    {
        private ExecutionStrategyDependencies Dependencies { get; }

        /// <summary>
        ///     Always returns false, since the <see cref="Storage.NonRetryingExecutionStrategy" /> does not perform retries.
        /// </summary>
        public bool RetriesOnFailure
            => false;

        /// <summary>
        ///     Constructs a new <see cref="Storage.NonRetryingExecutionStrategy" /> with the given service dependencies.
        /// </summary>
        /// <param name="dependencies"> Dependencies for this execution strategy. </param>
        public RelationalNonRetryingExecutionStrategy([NotNull] ExecutionStrategyDependencies dependencies)
            => Dependencies = dependencies;

        /// <summary>
        ///     Executes the specified operation and returns the result.
        /// </summary>
        /// <param name="state"> The state that will be passed to the operation. </param>
        /// <param name="operation">
        ///     A delegate representing an executable operation that returns the result of type <typeparamref name="TResult" />.
        /// </param>
        /// <param name="verifySucceeded"> A delegate that tests whether the operation succeeded even though an exception was thrown. </param>
        /// <typeparam name="TState"> The type of the state. </typeparam>
        /// <typeparam name="TResult"> The return type of <paramref name="operation" />. </typeparam>
        /// <returns> The result from the operation. </returns>
        /// <exception cref="RetryLimitExceededException">
        ///     The operation has not succeeded after the configured number of retries.
        /// </exception>
        public TResult Execute<TState, TResult>(
            TState state,
            Func<DbContext, TState, TResult> operation,
            Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded)
        {
            try
            {
                return operation(Dependencies.CurrentContext.Context, state);
            }
            catch (Exception ex) when (ExecutionStrategy.CallOnWrappedException(ex, e => e is DbException { IsTransient: true }))
            {
                throw new InvalidOperationException(CoreStrings.TransientExceptionDetected, ex);
            }
        }

        /// <summary>
        ///     Executes the specified asynchronous operation and returns the result.
        /// </summary>
        /// <param name="state"> The state that will be passed to the operation. </param>
        /// <param name="operation">
        ///     A function that returns a started task of type <typeparamref name="TResult" />.
        /// </param>
        /// <param name="verifySucceeded"> A delegate that tests whether the operation succeeded even though an exception was thrown. </param>
        /// <param name="cancellationToken">
        ///     A cancellation token used to cancel the retry operation, but not operations that are already in flight
        ///     or that already completed successfully.
        /// </param>
        /// <typeparam name="TState"> The type of the state. </typeparam>
        /// <typeparam name="TResult"> The result type of the <see cref="Task{TResult}" /> returned by <paramref name="operation" />. </typeparam>
        /// <returns>
        ///     A task that will run to completion if the original task completes successfully (either the
        ///     first time or after retrying transient failures). If the task fails with a non-transient error or
        ///     the retry limit is reached, the returned task will become faulted and the exception must be observed.
        /// </returns>
        /// <exception cref="RetryLimitExceededException">
        ///     The operation has not succeeded after the configured number of retries.
        /// </exception>
        /// <exception cref="OperationCanceledException"> If the <see cref="CancellationToken"/> is canceled. </exception>
        public async Task<TResult> ExecuteAsync<TState, TResult>(
            TState state,
            Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
            Func<DbContext, TState,
                CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await operation(Dependencies.CurrentContext.Context, state, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ExecutionStrategy.CallOnWrappedException(ex, e => e is DbException { IsTransient: true }))
            {
                throw new InvalidOperationException(CoreStrings.TransientExceptionDetected, ex);
            }
        }
    }
}
