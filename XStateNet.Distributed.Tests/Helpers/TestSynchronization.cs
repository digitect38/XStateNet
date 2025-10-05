namespace XStateNet.Distributed.Tests.Helpers
{
    /// <summary>
    /// Helper class for deterministic test synchronization
    /// Replaces arbitrary Task.Delay with event-based waiting
    /// </summary>
    public static class TestSynchronization
    {
        /// <summary>
        /// Wait for a condition to become true with polling
        /// </summary>
        public static async Task WaitForConditionAsync(
            Func<bool> condition,
            TimeSpan? timeout = null,
            int pollingIntervalMs = 10)
        {
            var actualTimeout = timeout ?? TimeSpan.FromSeconds(5);
            var cts = new CancellationTokenSource(actualTimeout);

            while (!cts.Token.IsCancellationRequested)
            {
                if (condition())
                {
                    return;
                }
                await Task.Delay(pollingIntervalMs, cts.Token);
            }

            throw new TimeoutException($"Condition not met within {actualTimeout.TotalSeconds} seconds");
        }

        /// <summary>
        /// Wait for an async condition to become true
        /// </summary>
        public static async Task WaitForConditionAsync(
            Func<Task<bool>> asyncCondition,
            TimeSpan? timeout = null,
            int pollingIntervalMs = 10)
        {
            var actualTimeout = timeout ?? TimeSpan.FromSeconds(5);
            var cts = new CancellationTokenSource(actualTimeout);

            while (!cts.Token.IsCancellationRequested)
            {
                if (await asyncCondition())
                {
                    return;
                }
                await Task.Delay(pollingIntervalMs, cts.Token);
            }

            throw new TimeoutException($"Condition not met within {actualTimeout.TotalSeconds} seconds");
        }

        /// <summary>
        /// Wait for a specific number of events to be collected
        /// </summary>
        public static async Task WaitForEventCountAsync<T>(
            ICollection<T> collection,
            int expectedCount,
            TimeSpan? timeout = null)
        {
            await WaitForConditionAsync(
                () => collection.Count >= expectedCount,
                timeout);
        }

        // State-waiting methods removed as they depend on StateChanged event details not available in this assembly
        // These methods should be implemented directly in test files that have access to the proper event types

        // EventBus-related methods removed as they depend on types not in this assembly
        // These would need to be implemented in the actual test files that have access to IEventBus

        /// <summary>
        /// Wait for message processing to complete
        /// </summary>
        public static async Task WaitForMessageProcessingAsync(
            int expectedProcessedCount,
            Func<int> getCurrentCount,
            TimeSpan? timeout = null)
        {
            await WaitForConditionAsync(
                () => getCurrentCount() >= expectedProcessedCount,
                timeout);
        }

        /// <summary>
        /// Create a completion trigger for async operations
        /// </summary>
        public class CompletionTrigger<T>
        {
            private readonly TaskCompletionSource<T> _tcs = new();
            private readonly CancellationTokenSource _cts;

            public CompletionTrigger(TimeSpan timeout)
            {
                _cts = new CancellationTokenSource(timeout);
                _cts.Token.Register(() => _tcs.TrySetCanceled());
            }

            public void Complete(T result) => _tcs.TrySetResult(result);
            public void Fail(Exception ex) => _tcs.TrySetException(ex);
            public Task<T> Task => _tcs.Task;

            public async Task<T> WaitAsync()
            {
                try
                {
                    return await _tcs.Task;
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("Operation timed out");
                }
            }
        }

        /// <summary>
        /// Helper for circuit breaker recovery waiting
        /// Since circuit breakers have time-based recovery, we need to wait for the break duration
        /// This is a legitimate use of delay but we wrap it for clarity
        /// </summary>
        public static async Task WaitForCircuitBreakerRecovery(TimeSpan breakDuration)
        {
            // Add small buffer to ensure we're past the break duration
            await Task.Delay(breakDuration.Add(TimeSpan.FromMilliseconds(50)));
        }

        /// <summary>
        /// Helper for simulating work in performance tests
        /// This is a legitimate use of delay for simulation
        /// </summary>
        public static async Task SimulateWork(int milliseconds)
        {
            await Task.Delay(milliseconds);
        }

        /// <summary>
        /// Helper for timeout test scenarios
        /// This is a legitimate use of delay to test timeout behavior
        /// </summary>
        public static async Task SimulateSlowOperation(int milliseconds, CancellationToken ct)
        {
            await Task.Delay(milliseconds, ct);
        }
    }
}