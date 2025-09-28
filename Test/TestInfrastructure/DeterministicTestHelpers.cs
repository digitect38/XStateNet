using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace XStateNet.Tests.TestInfrastructure
{
    public static class DeterministicTestHelpers
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DefaultPollingInterval = TimeSpan.FromMilliseconds(10);

        public static async Task WaitForConditionAsync(
            Func<bool> condition,
            string description = null,
            TimeSpan? timeout = null,
            TimeSpan? pollingInterval = null,
            ITestOutputHelper output = null)
        {
            var actualTimeout = timeout ?? DefaultTimeout;
            var actualPollingInterval = pollingInterval ?? DefaultPollingInterval;
            var stopwatch = Stopwatch.StartNew();

            output?.WriteLine($"Waiting for: {description ?? "condition"} (timeout: {actualTimeout})");

            while (!condition())
            {
                if (stopwatch.Elapsed > actualTimeout)
                {
                    throw new TimeoutException(
                        $"Condition '{description ?? "unknown"}' not met within {actualTimeout}");
                }

                await Task.Delay(actualPollingInterval);
            }

            output?.WriteLine($"Condition met after {stopwatch.ElapsedMilliseconds}ms: {description ?? "condition"}");
        }

        public static async Task<T> WaitForResultAsync<T>(
            Func<Task<T>> asyncFunc,
            Func<T, bool> validator,
            string description = null,
            TimeSpan? timeout = null,
            TimeSpan? pollingInterval = null,
            ITestOutputHelper output = null)
        {
            var actualTimeout = timeout ?? DefaultTimeout;
            var actualPollingInterval = pollingInterval ?? DefaultPollingInterval;
            var stopwatch = Stopwatch.StartNew();

            output?.WriteLine($"Waiting for result: {description ?? "async operation"} (timeout: {actualTimeout})");

            while (true)
            {
                if (stopwatch.Elapsed > actualTimeout)
                {
                    throw new TimeoutException(
                        $"Result '{description ?? "unknown"}' not valid within {actualTimeout}");
                }

                var result = await asyncFunc();
                if (validator(result))
                {
                    output?.WriteLine($"Got valid result after {stopwatch.ElapsedMilliseconds}ms: {description ?? "async operation"}");
                    return result;
                }

                await Task.Delay(actualPollingInterval);
            }
        }

        public static async Task RunWithRetriesAsync(
            Func<Task> action,
            int maxAttempts = 3,
            TimeSpan? delayBetweenAttempts = null,
            ITestOutputHelper output = null)
        {
            var actualDelay = delayBetweenAttempts ?? TimeSpan.FromMilliseconds(100);
            Exception lastException = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    output?.WriteLine($"Attempt {attempt}/{maxAttempts}");
                    await action();
                    output?.WriteLine($"Success on attempt {attempt}");
                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    output?.WriteLine($"Attempt {attempt} failed: {ex.Message}");

                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(actualDelay);
                    }
                }
            }

            throw new AggregateException(
                $"All {maxAttempts} attempts failed",
                lastException);
        }

        public static async Task RunConcurrentOperationsAsync(
            int concurrencyLevel,
            Func<int, Task> operation,
            ITestOutputHelper output = null)
        {
            output?.WriteLine($"Starting {concurrencyLevel} concurrent operations");
            var stopwatch = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, concurrencyLevel)
                .Select(i => operation(i))
                .ToArray();

            await Task.WhenAll(tasks);

            output?.WriteLine($"All {concurrencyLevel} operations completed in {stopwatch.ElapsedMilliseconds}ms");
        }

        public static async Task<List<T>> CollectConcurrentResultsAsync<T>(
            int operationCount,
            Func<int, Task<T>> operation,
            ITestOutputHelper output = null)
        {
            output?.WriteLine($"Collecting results from {operationCount} concurrent operations");
            var stopwatch = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, operationCount)
                .Select(i => operation(i))
                .ToArray();

            var results = await Task.WhenAll(tasks);

            output?.WriteLine($"Collected {results.Length} results in {stopwatch.ElapsedMilliseconds}ms");
            return results.ToList();
        }

        public static class Synchronization
        {
            public static async Task WaitForAllThreadsToReachBarrierAsync(
                CountdownEvent barrier,
                TimeSpan? timeout = null)
            {
                var actualTimeout = timeout ?? DefaultTimeout;
                var completedTask = Task.Run(() => barrier.Wait());
                var timeoutTask = Task.Delay(actualTimeout);

                var winner = await Task.WhenAny(completedTask, timeoutTask);
                if (winner == timeoutTask)
                {
                    throw new TimeoutException(
                        $"Not all threads reached barrier within {actualTimeout}. " +
                        $"Waiting for {barrier.InitialCount - barrier.CurrentCount}/{barrier.InitialCount} threads");
                }
            }

            public static async Task<T> WithTimeoutAsync<T>(
                Task<T> task,
                TimeSpan? timeout = null,
                string operationName = null)
            {
                var actualTimeout = timeout ?? DefaultTimeout;
                var timeoutTask = Task.Delay(actualTimeout);

                var winner = await Task.WhenAny(task, timeoutTask);
                if (winner == timeoutTask)
                {
                    throw new TimeoutException(
                        $"Operation '{operationName ?? "unknown"}' timed out after {actualTimeout}");
                }

                return await task;
            }

            public static async Task WithTimeoutAsync(
                Task task,
                TimeSpan? timeout = null,
                string operationName = null)
            {
                var actualTimeout = timeout ?? DefaultTimeout;
                var timeoutTask = Task.Delay(actualTimeout);

                var winner = await Task.WhenAny(task, timeoutTask);
                if (winner == timeoutTask)
                {
                    throw new TimeoutException(
                        $"Operation '{operationName ?? "unknown"}' timed out after {actualTimeout}");
                }

                await task;
            }
        }

        public static class StateValidation
        {
            public static void AssertEventualConsistency<T>(
                Func<T> getValue,
                T expectedValue,
                TimeSpan? timeout = null,
                ITestOutputHelper output = null)
            {
                var actualTimeout = timeout ?? DefaultTimeout;
                var stopwatch = Stopwatch.StartNew();

                while (stopwatch.Elapsed < actualTimeout)
                {
                    var currentValue = getValue();
                    if (EqualityComparer<T>.Default.Equals(currentValue, expectedValue))
                    {
                        output?.WriteLine($"Value became consistent after {stopwatch.ElapsedMilliseconds}ms");
                        return;
                    }

                    Thread.Sleep(10);
                }

                var finalValue = getValue();
                if (!EqualityComparer<T>.Default.Equals(finalValue, expectedValue))
                {
                    throw new InvalidOperationException(
                        $"Value did not reach expected state within {actualTimeout}. " +
                        $"Expected: {expectedValue}, Actual: {finalValue}");
                }
            }

            public static async Task AssertNoRaceConditionsAsync(
                Func<Task> operation,
                Func<bool> invariantCheck,
                int iterations = 100,
                int concurrencyLevel = 10,
                ITestOutputHelper output = null)
            {
                output?.WriteLine($"Testing for race conditions: {iterations} iterations with concurrency {concurrencyLevel}");

                for (int i = 0; i < iterations; i++)
                {
                    var tasks = Enumerable.Range(0, concurrencyLevel)
                        .Select(_ => operation())
                        .ToArray();

                    await Task.WhenAll(tasks);

                    if (!invariantCheck())
                    {
                        throw new InvalidOperationException(
                            $"Invariant violated on iteration {i + 1}/{iterations}");
                    }
                }

                output?.WriteLine($"No race conditions detected after {iterations} iterations");
            }
        }
    }
}