using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace XStateNet.Semi.Transport
{
    /// <summary>
    /// Operation priority for circuit breaker execution
    /// </summary>
    public enum OperationPriority
    {
        Critical = 0,   // Health checks, circuit state tests
        High = 1,       // Important business operations
        Normal = 2,     // Regular operations
        Low = 3,        // Background tasks
        Bulk = 4        // Batch operations
    }

    /// <summary>
    /// Circuit breaker with priority-based execution support
    /// </summary>
    public class PriorityThreadSafeCircuitBreaker : ThreadSafeCircuitBreaker
    {
        private readonly Channel<PriorityOperation>[] _priorityChannels;
        private readonly SemaphoreSlim _halfOpenSemaphore;
        private readonly Task _priorityProcessor;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private int _concurrentOperations;
        private readonly int _maxConcurrentOperations;

        public PriorityThreadSafeCircuitBreaker(
            int failureThreshold,
            TimeSpan openDuration,
            TimeSpan? halfOpenTestDelay = null,
            int maxConcurrentOperations = 100,
            ILogger logger = null)
            : base(failureThreshold, openDuration, halfOpenTestDelay, logger)
        {
            _maxConcurrentOperations = maxConcurrentOperations;
            _halfOpenSemaphore = new SemaphoreSlim(1, 1);
            _cancellationTokenSource = new CancellationTokenSource();

            // Create priority channels
            var priorityCount = Enum.GetValues<OperationPriority>().Length;
            _priorityChannels = new Channel<PriorityOperation>[priorityCount];

            for (int i = 0; i < priorityCount; i++)
            {
                _priorityChannels[i] = Channel.CreateUnbounded<PriorityOperation>(
                    new UnboundedChannelOptions
                    {
                        SingleReader = false,
                        SingleWriter = false
                    });
            }

            _priorityProcessor = ProcessPriorityOperationsAsync(_cancellationTokenSource.Token);
        }

        public Task<T> ExecuteWithPriorityAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            OperationPriority priority = OperationPriority.Normal,
            CancellationToken cancellationToken = default)
        {
            // Quick check for circuit state
            var currentState = State;
            if (currentState == CircuitState.Open)
            {
                // Check if still within open duration
                var openedTicks = GetOpenedTimeTicks();
                if (openedTicks != 0)
                {
                    var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - openedTicks);
                    if (elapsed < GetOpenDuration())
                    {
                        throw new CircuitBreakerOpenException(
                            $"Circuit breaker is open. Retry after {(GetOpenDuration() - elapsed).TotalSeconds:F1} seconds");
                    }
                }
            }

            // For half-open state with critical priority, try immediate execution
            if (currentState == CircuitState.HalfOpen && priority == OperationPriority.Critical)
            {
                return ExecuteHalfOpenTestAsync(operation, cancellationToken);
            }

            // Queue the operation based on priority
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var priorityOp = new PriorityOperation(
                async ct =>
                {
                    try
                    {
                        var result = await base.ExecuteAsync(operation, ct);
                        tcs.SetResult(result);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                },
                priority,
                cancellationToken
            );

            var channel = _priorityChannels[(int)priority];
            if (!channel.Writer.TryWrite(priorityOp))
            {
                tcs.SetException(new InvalidOperationException("Failed to queue operation"));
            }

            return tcs.Task;
        }

        private async Task<T> ExecuteHalfOpenTestAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken)
        {
            // Try to acquire the half-open test semaphore immediately
            if (await _halfOpenSemaphore.WaitAsync(0, cancellationToken))
            {
                try
                {
                    return await base.ExecuteAsync(operation, cancellationToken);
                }
                finally
                {
                    _halfOpenSemaphore.Release();
                }
            }

            // If we can't get immediate access, fall back to regular execution
            throw new CircuitBreakerOpenException("Circuit is in half-open state and test slot is occupied");
        }

        private async Task ProcessPriorityOperationsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Check if we can process more operations
                if (_concurrentOperations >= _maxConcurrentOperations)
                {
                    await Task.Delay(10, cancellationToken);
                    continue;
                }

                PriorityOperation operation = null;

                // Try to get an operation from highest priority channel first
                for (int priority = 0; priority < _priorityChannels.Length; priority++)
                {
                    if (_priorityChannels[priority].Reader.TryRead(out operation))
                    {
                        break;
                    }
                }

                if (operation == null)
                {
                    await Task.Delay(1, cancellationToken);
                    continue;
                }

                // Process the operation
                Interlocked.Increment(ref _concurrentOperations);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await operation.ExecuteAsync();
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _concurrentOperations);
                    }
                }, cancellationToken);
            }
        }

        public int GetQueueDepth(OperationPriority priority)
        {
            return _priorityChannels[(int)priority].Reader.Count;
        }

        public int GetTotalQueueDepth()
        {
            int total = 0;
            foreach (var channel in _priorityChannels)
            {
                total += channel.Reader.Count;
            }
            return total;
        }

        public new void Dispose()
        {
            _cancellationTokenSource?.Cancel();

            foreach (var channel in _priorityChannels ?? Array.Empty<Channel<PriorityOperation>>())
            {
                channel.Writer.TryComplete();
            }

            try
            {
                _priorityProcessor?.Wait(TimeSpan.FromSeconds(5));
            }
            catch { }

            _halfOpenSemaphore?.Dispose();
            _cancellationTokenSource?.Dispose();

            base.Dispose();
        }

        private class PriorityOperation
        {
            private readonly Func<CancellationToken, Task> _operation;
            private readonly CancellationToken _cancellationToken;

            public OperationPriority Priority { get; }

            public PriorityOperation(
                Func<CancellationToken, Task> operation,
                OperationPriority priority,
                CancellationToken cancellationToken)
            {
                _operation = operation;
                Priority = priority;
                _cancellationToken = cancellationToken;
            }

            public Task ExecuteAsync()
            {
                if (_cancellationToken.IsCancellationRequested)
                    return Task.FromCanceled(_cancellationToken);

                return _operation(_cancellationToken);
            }
        }
    }

    /// <summary>
    /// Extension methods for priority-based circuit breaker operations
    /// </summary>
    public static class PriorityCircuitBreakerExtensions
    {
        public static Task<T> ExecuteCriticalAsync<T>(
            this PriorityThreadSafeCircuitBreaker breaker,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            return breaker.ExecuteWithPriorityAsync(operation, OperationPriority.Critical, cancellationToken);
        }

        public static Task<T> ExecuteHighPriorityAsync<T>(
            this PriorityThreadSafeCircuitBreaker breaker,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            return breaker.ExecuteWithPriorityAsync(operation, OperationPriority.High, cancellationToken);
        }

        public static Task<T> ExecuteLowPriorityAsync<T>(
            this PriorityThreadSafeCircuitBreaker breaker,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            return breaker.ExecuteWithPriorityAsync(operation, OperationPriority.Low, cancellationToken);
        }

        public static Task<T> ExecuteBulkAsync<T>(
            this PriorityThreadSafeCircuitBreaker breaker,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            return breaker.ExecuteWithPriorityAsync(operation, OperationPriority.Bulk, cancellationToken);
        }
    }
}