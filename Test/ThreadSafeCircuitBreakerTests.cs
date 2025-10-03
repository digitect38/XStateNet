using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using XStateNet.Semi.Transport;
using XStateNet.Tests.TestInfrastructure;
using Microsoft.Extensions.Logging;

namespace XStateNet.Tests
{
    [TestCaseOrderer("XStateNet.Tests.TestInfrastructure.PriorityOrderer", "XStateNet.Tests")]
    [Collection("TimingSensitive")]
    public class ThreadSafeCircuitBreakerTests
    {
        private readonly ITestOutputHelper _output;
        private readonly ILogger _logger;

        public ThreadSafeCircuitBreakerTests(ITestOutputHelper output)
        {
            _output = output;
            _logger = new XunitLogger(output);
        }

        [Fact]
        [TestPriority(TestPriority.Critical)]
        public async Task CircuitBreaker_OpensAfterThreshold_NoRaceCondition()
        {
            // Arrange
            var breaker = new ThreadSafeCircuitBreaker(
                failureThreshold: 3,
                openDuration: TimeSpan.FromMilliseconds(100),
                logger: _logger);

            var stateChanges = new ConcurrentBag<ThreadSafeCircuitBreaker.CircuitState>();
            breaker.StateChanged += (s, state) => stateChanges.Add(state);

            // Act - Concurrent failures from multiple threads
            var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        await breaker.ExecuteAsync<bool>(async ct =>
                        {
                            await Task.Delay(1, ct);
                            throw new InvalidOperationException("Test failure");
                        });
                    }
                    catch { /* Expected */ }
                }
            }));

            await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(ThreadSafeCircuitBreaker.CircuitState.Open, breaker.State);
            Assert.Contains(ThreadSafeCircuitBreaker.CircuitState.Open, stateChanges);

            // Circuit should open exactly once despite concurrent failures
            var openTransitions = stateChanges.Count(s => s == ThreadSafeCircuitBreaker.CircuitState.Open);
            Assert.Equal(1, openTransitions);

            _output.WriteLine($"Circuit opened after {breaker.FailureCount} failures");
        }

        [Fact]
        [TestPriority(TestPriority.Critical)]
        public async Task CircuitBreaker_HalfOpenToClosedTransition_ThreadSafe()
        {
            // Arrange
            var breaker = new ThreadSafeCircuitBreaker(
                failureThreshold: 2,
                openDuration: TimeSpan.FromMilliseconds(50),
                halfOpenTestDelay: TimeSpan.FromMilliseconds(5),
                logger: _logger);

            // Open the circuit
            breaker.RecordFailure();
            breaker.RecordFailure();
            Assert.Equal(ThreadSafeCircuitBreaker.CircuitState.Open, breaker.State);

            // Wait for timeout - allow extra time for system scheduling variability
            await Task.Delay(100);

            var successCount = 0;
            var halfOpenCount = 0;
            var closedCount = 0;

            // Act - Multiple threads try to use circuit after timeout
            var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
            {
                await Task.Delay(Random.Shared.Next(0, 10)); // Random start delay

                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        var result = await breaker.ExecuteAsync(async ct =>
                        {
                            if (breaker.State == ThreadSafeCircuitBreaker.CircuitState.HalfOpen)
                                Interlocked.Increment(ref halfOpenCount);

                            await Task.Delay(1, ct);
                            return true;
                        });

                        if (result)
                        {
                            Interlocked.Increment(ref successCount);
                            if (breaker.State == ThreadSafeCircuitBreaker.CircuitState.Closed)
                                Interlocked.Increment(ref closedCount);
                        }
                    }
                    catch (CircuitBreakerOpenException)
                    {
                        // Expected when circuit is open
                    }

                    await Task.Delay(5);
                }
            }));

            await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(ThreadSafeCircuitBreaker.CircuitState.Closed, breaker.State);
            Assert.True(halfOpenCount > 0, "Should have entered half-open state");
            Assert.True(successCount > 0, "Should have successful operations");

            _output.WriteLine($"Half-open executions: {halfOpenCount}, Success count: {successCount}");
        }

        [Fact]
        [TestPriority(TestPriority.Critical)]
        public async Task CircuitBreaker_HalfOpenFailure_ReOpensImmediately()
        {
            // Arrange
            var breaker = new ThreadSafeCircuitBreaker(
                failureThreshold: 2,
                openDuration: TimeSpan.FromMilliseconds(50),
                logger: _logger);

            var stateTransitions = new ConcurrentBag<(ThreadSafeCircuitBreaker.CircuitState oldState,
                ThreadSafeCircuitBreaker.CircuitState newState, string reason)>();
            breaker.StateTransitioned += (s, e) => stateTransitions.Add(e);

            // Open the circuit
            breaker.RecordFailure();
            breaker.RecordFailure();
            Assert.Equal(ThreadSafeCircuitBreaker.CircuitState.Open, breaker.State);

            // Wait for circuit to timeout and allow half-open - allow extra time for system scheduling
            await Task.Delay(100);

            // Act - Test half-open failure
            var halfOpenExecuted = false;
            var exceptionThrown = false;

            try
            {
                await breaker.ExecuteAsync<bool>(async ct =>
                {
                    halfOpenExecuted = true;
                    await Task.Delay(1, ct);
                    throw new InvalidOperationException("Test failure in half-open");
                });
            }
            catch (CircuitBreakerOpenException)
            {
                // Circuit might still be open if timing is off
                _output.WriteLine("Circuit still open, retrying after delay");
                await Task.Delay(50);

                try
                {
                    await breaker.ExecuteAsync<bool>(async ct =>
                    {
                        halfOpenExecuted = true;
                        await Task.Delay(1, ct);
                        throw new InvalidOperationException("Test failure in half-open");
                    });
                }
                catch (InvalidOperationException)
                {
                    exceptionThrown = true;
                }
            }
            catch (InvalidOperationException)
            {
                exceptionThrown = true;
            }

            // Now run concurrent tasks that should all be rejected
            var rejectedCount = 0;
            var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
            {
                try
                {
                    await breaker.ExecuteAsync<bool>(async ct =>
                    {
                        await Task.Delay(1, ct);
                        return true;
                    });
                }
                catch (CircuitBreakerOpenException)
                {
                    Interlocked.Increment(ref rejectedCount);
                }
            }));

            await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(ThreadSafeCircuitBreaker.CircuitState.Open, breaker.State);
            Assert.True(halfOpenExecuted, "Half-open test should have been executed");
            Assert.True(exceptionThrown, "Exception should have been thrown in half-open");
            Assert.True(rejectedCount > 0, $"At least some operations should have been rejected, got {rejectedCount}");

            // Should have transitioned: Closed -> Open -> HalfOpen -> Open
            var transitions = stateTransitions.ToList();
            Assert.Contains(transitions, t =>
                t.oldState == ThreadSafeCircuitBreaker.CircuitState.HalfOpen &&
                t.newState == ThreadSafeCircuitBreaker.CircuitState.Open &&
                t.reason.Contains("half-open", StringComparison.OrdinalIgnoreCase));

            _output.WriteLine($"Recorded {transitions.Count} state transitions, {rejectedCount} operations rejected");
        }

        [Fact]
        [TestPriority(TestPriority.High)]
        public async Task CircuitBreaker_ConcurrentSuccessAndFailure_MaintainsConsistentState()
        {
            // Arrange
            var breaker = new ThreadSafeCircuitBreaker(
                failureThreshold: 5,
                openDuration: TimeSpan.FromMilliseconds(100),
                logger: _logger);

            var operations = new ConcurrentBag<(bool success, ThreadSafeCircuitBreaker.CircuitState state, long failures)>();

            // Act - Mix of successes and failures from multiple threads
            var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
            {
                for (int j = 0; j < 10; j++)
                {
                    var shouldFail = (i + j) % 3 == 0; // Some operations fail

                    try
                    {
                        var result = await breaker.ExecuteAsync(async ct =>
                        {
                            await Task.Delay(1, ct);
                            if (shouldFail)
                                throw new InvalidOperationException("Test failure");
                            return true;
                        });

                        operations.Add((true, breaker.State, breaker.FailureCount));
                    }
                    catch (CircuitBreakerOpenException)
                    {
                        // Circuit is open, wait a bit
                        await Task.Delay(20);
                    }
                    catch (InvalidOperationException)
                    {
                        operations.Add((false, breaker.State, breaker.FailureCount));
                    }
                }
            }));

            await Task.WhenAll(tasks);

            // Assert - Check consistency of recorded states
            var operationsList = operations.ToList();
            var maxFailuresBeforeOpen = operationsList
                .Where(op => op.state == ThreadSafeCircuitBreaker.CircuitState.Closed)
                .Select(op => op.failures)
                .DefaultIfEmpty(0)
                .Max();

            // Failure count should not exceed threshold + some concurrent operations
            Assert.True(maxFailuresBeforeOpen <= 5 + 5, // threshold + buffer for concurrent ops
                $"Max failures in closed state: {maxFailuresBeforeOpen}");

            _output.WriteLine($"Total operations: {operationsList.Count}, " +
                $"Max failures before open: {maxFailuresBeforeOpen}");
        }

        [Fact]
        [TestPriority(TestPriority.High)]
        public async Task CircuitBreaker_Reset_ClearsAllState()
        {
            // Arrange
            var breaker = new ThreadSafeCircuitBreaker(
                failureThreshold: 2,
                openDuration: TimeSpan.FromSeconds(10), // Long duration to ensure it doesn't auto-recover
                logger: _logger);

            // Open the circuit deterministically
            breaker.RecordFailure();
            breaker.RecordFailure();
            Assert.Equal(ThreadSafeCircuitBreaker.CircuitState.Open, breaker.State);
            Assert.Equal(2, breaker.FailureCount);

            // Verify circuit is open - operations should be rejected
            var beforeResetRejected = false;
            try
            {
                await breaker.ExecuteAsync(async ct =>
                {
                    await Task.Yield(); // Minimal async operation
                    return true;
                });
            }
            catch (CircuitBreakerOpenException)
            {
                beforeResetRejected = true;
            }
            Assert.True(beforeResetRejected, "Circuit should reject operations when open");

            // Act - Reset the circuit
            breaker.Reset();

            // Verify reset state
            Assert.Equal(ThreadSafeCircuitBreaker.CircuitState.Closed, breaker.State);
            Assert.Equal(0, breaker.FailureCount);
            Assert.Equal(0, breaker.SuccessCount);

            // Verify circuit is now closed - operations should succeed
            var afterResetSucceeded = false;
            try
            {
                var result = await breaker.ExecuteAsync(async ct =>
                {
                    await Task.Yield(); // Minimal async operation
                    return true;
                });
                afterResetSucceeded = result;
            }
            catch (CircuitBreakerOpenException)
            {
                afterResetSucceeded = false;
            }
            Assert.True(afterResetSucceeded, "Circuit should allow operations after reset");

            // Verify stats are cleared
            var stats = breaker.GetStats();
            Assert.Null(stats.OpenedTime);
            Assert.Equal(0, stats.RemainingOpenTime);
            Assert.Equal(ThreadSafeCircuitBreaker.CircuitState.Closed, stats.State);

            // Test concurrent operations after reset to ensure thread-safety
            var concurrentTasks = new Task<bool>[10];
            for (int i = 0; i < concurrentTasks.Length; i++)
            {
                concurrentTasks[i] = breaker.ExecuteAsync(async ct =>
                {
                    await Task.Yield();
                    return true;
                });
            }

            var results = await Task.WhenAll(concurrentTasks);
            Assert.All(results, r => Assert.True(r, "All operations should succeed after reset"));

            _output.WriteLine($"Reset test completed: Circuit state={breaker.State}, FailureCount={breaker.FailureCount}, SuccessCount={breaker.SuccessCount}");
        }

        [Fact]
        [TestPriority(TestPriority.High)]
        public async Task CircuitBreaker_Statistics_ThreadSafeReads()
        {
            // Arrange
            var breaker = new ThreadSafeCircuitBreaker(
                failureThreshold: 3,
                openDuration: TimeSpan.FromMilliseconds(100),
                logger: _logger);

            var allStats = new ConcurrentBag<CircuitBreakerStats>();

            // Act - Concurrent reads and writes
            var tasks = new List<Task>();

            // Writer tasks
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < 20; j++)
                    {
                        if (j % 3 == 0)
                            breaker.RecordFailure();
                        else
                            breaker.RecordSuccess();

                        await Task.Delay(1);
                    }
                }));
            }

            // Reader tasks
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < 30; j++)
                    {
                        var stats = breaker.GetStats();
                        allStats.Add(stats);
                        await Task.Delay(1);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - All stats should be internally consistent
            foreach (var stats in allStats)
            {
                // Stats should be a consistent snapshot
                Assert.True(stats.FailureCount >= 0, "Failure count should never be negative");
                Assert.True(stats.SuccessCount >= 0, "Success count should never be negative");

                if (stats.State == ThreadSafeCircuitBreaker.CircuitState.Open)
                {
                    Assert.NotNull(stats.OpenedTime);
                }

                if (stats.OpenedTime != null)
                {
                    Assert.True(stats.OpenedTime <= DateTime.UtcNow, "Opened time should not be in future");
                }
            }

            _output.WriteLine($"Collected {allStats.Count} statistics snapshots, all consistent");
        }

        [Fact]
        [TestPriority(TestPriority.Critical)]
        public async Task CircuitBreaker_NoThunderingHerd_OnHalfOpenTransition()
        {
            // Arrange
            var breaker = new ThreadSafeCircuitBreaker(
                failureThreshold: 2,
                openDuration: TimeSpan.FromMilliseconds(50),
                halfOpenTestDelay: TimeSpan.FromMilliseconds(20),
                logger: _logger);

            // Open the circuit
            breaker.RecordFailure();
            breaker.RecordFailure();

            // Wait for open duration - allow extra time for system scheduling
            await Task.Delay(100);

            var halfOpenExecutions = 0;
            var rejectedDueToOpen = 0;

            // Act - Many threads try to execute after timeout
            var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
            {
                try
                {
                    await breaker.ExecuteAsync(async ct =>
                    {
                        if (breaker.State == ThreadSafeCircuitBreaker.CircuitState.HalfOpen)
                        {
                            Interlocked.Increment(ref halfOpenExecutions);
                            await Task.Delay(10, ct); // Simulate work
                        }
                        return true;
                    });
                }
                catch (CircuitBreakerOpenException)
                {
                    Interlocked.Increment(ref rejectedDueToOpen);
                }
            }));

            await Task.WhenAll(tasks);

            // Assert - Only one or very few should execute in half-open
            Assert.True(halfOpenExecutions <= 3,
                $"Too many half-open executions: {halfOpenExecutions}. Should prevent thundering herd.");
            Assert.True(rejectedDueToOpen > 0,
                "Some requests should be rejected to prevent thundering herd");

            _output.WriteLine($"Half-open executions: {halfOpenExecutions}, Rejected: {rejectedDueToOpen}");
        }

        private class XunitLogger : ILogger
        {
            private readonly ITestOutputHelper _output;

            public XunitLogger(ITestOutputHelper output)
            {
                _output = output;
            }

            public IDisposable BeginScope<TState>(TState state) => new NullScope();
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [{logLevel}] {formatter(state, exception)}");
                if (exception != null)
                    _output.WriteLine($"  Exception: {exception.Message}");
            }

            private class NullScope : IDisposable
            {
                public void Dispose() { }
            }
        }
    }
}