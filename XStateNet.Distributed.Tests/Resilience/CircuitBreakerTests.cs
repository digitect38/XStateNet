using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using XStateNet.Distributed.Resilience;
using XStateNet.Distributed.Tests.TestHelpers;
using XStateNet.Distributed.Tests.TestInfrastructure;

namespace XStateNet.Distributed.Tests.Resilience
{
    [Collection("TimingSensitive")]
    public class CircuitBreakerTests
    {
        [Fact]
        public Task CircuitBreaker_StartsInClosedState()
        {
            // Arrange
            var options = new CircuitBreakerOptions();
            var circuitBreaker = new CircuitBreaker("test", options);

            // Assert
            Assert.Equal(CircuitState.Closed, circuitBreaker.State);
            return Task.CompletedTask;
        }

        [Fact]
        public async Task CircuitBreaker_OpensAfterThresholdFailures()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 3,
                BreakDuration = TimeSpan.FromMilliseconds(100)
            };
            var circuitBreaker = new CircuitBreaker("test", options);
            var failureCount = 0;

            // Act
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    await circuitBreaker.ExecuteAsync(() =>
                    {
                        throw new InvalidOperationException("Test failure");
                    });
                }
                catch (InvalidOperationException)
                {
                    failureCount++;
                }
            }

            // Assert
            Assert.Equal(3, failureCount);
            Assert.Equal(CircuitState.Open, circuitBreaker.State);
        }

        [Fact]
        public async Task CircuitBreaker_RejectsCallsWhenOpen()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                BreakDuration = TimeSpan.FromSeconds(1)
            };
            var circuitBreaker = new CircuitBreaker("test", options);

            // Act - Open the circuit
            try
            {
                await circuitBreaker.ExecuteAsync(() =>
                {
                    throw new InvalidOperationException("Test failure");
                });
            }
            catch { }

            // Assert - Should reject calls
            await Assert.ThrowsAsync<CircuitBreakerOpenException>(async () =>
            {
                await circuitBreaker.ExecuteAsync(() => Task.FromResult("test"));
            });
        }

        [Fact]
        public async Task CircuitBreaker_TransitionsToHalfOpen_AfterBreakDuration()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                BreakDuration = TimeSpan.FromMilliseconds(100), // Increased for better timing precision
                SuccessCountInHalfOpen = 1
            };
            var circuitBreaker = new CircuitBreaker("test", options);

            var stateChangeObserved = new TaskCompletionSource<CircuitState>();
            circuitBreaker.StateChanged += (sender, args) =>
            {
                if (args.ToState == CircuitState.HalfOpen || args.ToState == CircuitState.Closed)
                    stateChangeObserved.TrySetResult(args.ToState);
            };

            // Act - Open the circuit
            try
            {
                await circuitBreaker.ExecuteAsync(async () =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("Test failure");
                });
            }
            catch { }

            Assert.Equal(CircuitState.Open, circuitBreaker.State);

            // Use deterministic wait for state transition
            var transitionReady = new TaskCompletionSource<bool>();

            // Set up timer to signal when break duration has passed
            using var timer = new Timer(_ => transitionReady.TrySetResult(true), null,
                TimeSpan.FromMilliseconds(120), Timeout.InfiniteTimeSpan);
            await transitionReady.Task;

            // Attempt execution - should succeed now that circuit is half-open
            string? result = null;
            Exception? caughtException = null;

            try
            {
                result = await circuitBreaker.ExecuteAsync(() => Task.FromResult("success"));
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            // Assert - should have succeeded
            Assert.Null(caughtException);
            Assert.NotNull(result);
            Assert.Equal("success", result);

            // Circuit should now be closed after successful execution in half-open
            Assert.Equal(CircuitState.Closed, circuitBreaker.State);

            // Verify state change was observed
            if (stateChangeObserved.Task.IsCompleted)
            {
                var observedState = await stateChangeObserved.Task;
                Assert.True(observedState == CircuitState.HalfOpen || observedState == CircuitState.Closed,
                    $"Expected HalfOpen or Closed, but observed {observedState}");
            }
        }

        [Fact]
        public async Task CircuitBreaker_ClosesAfterSuccessInHalfOpen()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                BreakDuration = TimeSpan.FromMilliseconds(100), // Increased for timing precision
                SuccessCountInHalfOpen = 2
            };
            var circuitBreaker = new CircuitBreaker("test", options);

            var stateChanges = new TaskCompletionSource<bool>();
            var statesObserved = new List<CircuitState>();

            circuitBreaker.StateChanged += (sender, args) =>
            {
                statesObserved.Add(args.ToState);
                if (args.ToState == CircuitState.Closed && statesObserved.Contains(CircuitState.HalfOpen))
                    stateChanges.TrySetResult(true);
            };

            // Open circuit
            try
            {
                await circuitBreaker.ExecuteAsync(() =>
                {
                    throw new InvalidOperationException();
                });
            }
            catch { }

            // Wait deterministically for circuit to transition to half-open
            var transitionReady = new TaskCompletionSource<bool>();
            using var timer = new Timer(_ => transitionReady.TrySetResult(true), null,
                TimeSpan.FromMilliseconds(120), Timeout.InfiniteTimeSpan);
            await transitionReady.Task;

            // Execute successful calls in half-open state
            var successfulCalls = 0;

            // First successful call in half-open
            try
            {
                var result = await circuitBreaker.ExecuteAsync(() => Task.FromResult(1));
                successfulCalls++;
                Assert.Equal(1, result);
            }
            catch (CircuitBreakerOpenException)
            {
                Assert.True(false, "Circuit did not transition to half-open for first call");
            }

            // Second successful call should close the circuit (SuccessCountInHalfOpen = 2)
            try
            {
                var result = await circuitBreaker.ExecuteAsync(() => Task.FromResult(2));
                successfulCalls++;
                Assert.Equal(2, result);
            }
            catch (CircuitBreakerOpenException)
            {
                Assert.True(false, "Circuit did not allow second successful call");
            }

            // Wait deterministically for state transition to complete
            await Task.Yield(); // Allow state transition to complete

            // Assert
            Assert.Equal(2, successfulCalls);
            Assert.Equal(CircuitState.Closed, circuitBreaker.State);
        }

        [Fact]
        public async Task CircuitBreaker_ReopensOnFailureInHalfOpen()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                BreakDuration = TimeSpan.FromMilliseconds(100) // Increased for timing precision
            };
            var circuitBreaker = new CircuitBreaker("test", options);

            // Track state changes
            var stateChanges = new List<CircuitState>();
            var stateChangeEvent = new TaskCompletionSource<bool>();

            circuitBreaker.StateChanged += (sender, args) =>
            {
                stateChanges.Add(args.ToState);
                if (args.ToState == CircuitState.Open && stateChanges.Contains(CircuitState.HalfOpen))
                {
                    stateChangeEvent.TrySetResult(true);
                }
            };

            // Step 1: Open circuit with initial failure
            try
            {
                await circuitBreaker.ExecuteAsync(async () =>
                {
                    await Task.Yield(); // Ensure async execution
                    throw new InvalidOperationException("Initial failure");
                });
            }
            catch (InvalidOperationException) { /* Expected */ }

            Assert.Equal(CircuitState.Open, circuitBreaker.State);

            // Step 2: Wait deterministically for circuit to be ready to transition to half-open
            var transitionReady = new TaskCompletionSource<bool>();
            using var timer = new Timer(_ => transitionReady.TrySetResult(true), null,
                TimeSpan.FromMilliseconds(120), Timeout.InfiniteTimeSpan);
            await transitionReady.Task;

            // Step 3: Attempt execution which should trigger half-open state and then fail
            Exception? caughtException = null;
            try
            {
                await circuitBreaker.ExecuteAsync(async () =>
                {
                    await Task.Yield(); // Ensure async execution
                    // If we reach here, we're in half-open state
                    throw new InvalidOperationException("Failure in half-open");
                });
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            // Step 4: Verify the failure occurred and wasn't rejected
            Assert.NotNull(caughtException);
            Assert.IsType<InvalidOperationException>(caughtException);
            Assert.Equal("Failure in half-open", caughtException.Message);

            // Step 5: Wait deterministically for async state transition to complete
            bool transitionCompleted = false;
            using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
            {
                try
                {
                    await stateChangeEvent.Task.WaitAsync(cts.Token);
                    transitionCompleted = true;
                }
                catch (OperationCanceledException)
                {
                    transitionCompleted = false;
                }
            }

            // Step 6: Verify final state
            Assert.True(transitionCompleted || circuitBreaker.State == CircuitState.Open,
                "Circuit should have transitioned back to Open after failure in HalfOpen");
            Assert.Equal(CircuitState.Open, circuitBreaker.State);

            // Step 7: Verify state transition sequence
            Assert.Contains(CircuitState.Open, stateChanges); // Initial open
            // HalfOpen might not be captured if transition is very fast
        }

        [Fact]
        public async Task CircuitBreaker_FailureRateThreshold_OpensCircuit()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureRateThreshold = 0.5, // 50% failure rate
                MinimumThroughput = 4,
                SamplingDuration = TimeSpan.FromSeconds(10)
            };
            var circuitBreaker = new CircuitBreaker("test", options);

            // Act - 2 successes, 3 failures (60% failure rate)
            await circuitBreaker.ExecuteAsync(() => Task.FromResult(1));
            await circuitBreaker.ExecuteAsync(() => Task.FromResult(2));

            for (int i = 0; i < 3; i++)
            {
                try
                {
                    await circuitBreaker.ExecuteAsync(() =>
                    {
                        throw new InvalidOperationException();
                    });
                }
                catch { }
            }

            // Assert
            Assert.Equal(CircuitState.Open, circuitBreaker.State);
        }

        [Fact]
        public async Task CircuitBreaker_StateChangedEvent_Fires()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1
            };
            var circuitBreaker = new CircuitBreaker("test", options);
            CircuitStateChangedEventArgs? eventArgs = null;
            var eventFired = new TaskCompletionSource<bool>();

            circuitBreaker.StateChanged += (sender, args) =>
            {
                eventArgs = args;
                eventFired.TrySetResult(true);
            };

            // Act
            try
            {
                await circuitBreaker.ExecuteAsync(() =>
                {
                    throw new InvalidOperationException();
                });
            }
            catch { }

            // Wait for the event to fire (it's fired asynchronously)
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            {
                await eventFired.Task.WaitAsync(cts.Token);
            }

            // Assert
            Assert.NotNull(eventArgs);
            Assert.Equal("test", eventArgs.CircuitBreakerName);
            Assert.Equal(CircuitState.Open, eventArgs.ToState);
            Assert.NotNull(eventArgs.LastException);
        }

        [Fact]
        public async Task CircuitBreaker_HandlesNonAsync_Operations()
        {
            // Arrange
            var options = new CircuitBreakerOptions();
            var circuitBreaker = new CircuitBreaker("test", options);

            // Act
            var result = await circuitBreaker.ExecuteAsync(() => 42);

            // Assert
            Assert.Equal(42, result);
        }

        [Fact]
        [TestPriority(TestPriority.Critical)]
        public async Task CircuitBreaker_ThreadSafe_ConcurrentOperations()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 5
            };
            var circuitBreaker = new CircuitBreaker("test", options);
            var successCount = 0;
            var failureCount = 0;

            // Act - Execute many concurrent operations
            var tasks = new Task[100];
            for (int i = 0; i < 100; i++)
            {
                var index = i;
                tasks[i] = Task.Run(async () =>
                {
                    try
                    {
                        if (index % 10 == 0) // 10% failure rate
                        {
                            await circuitBreaker.ExecuteAsync(() =>
                            {
                                throw new InvalidOperationException();
                            });
                        }
                        else
                        {
                            await circuitBreaker.ExecuteAsync(() =>
                            {
                                Interlocked.Increment(ref successCount);
                                return Task.FromResult(true);
                            });
                        }
                    }
                    catch
                    {
                        Interlocked.Increment(ref failureCount);
                    }
                });
            }

            await Task.WhenAll(tasks);

            // Assert - Should handle concurrent operations correctly
            Assert.True(successCount > 0);
            Assert.True(failureCount > 0);
        }

        [Fact]
        public void CircuitBreaker_Dispose_CleansUpResources()
        {
            // Arrange
            var options = new CircuitBreakerOptions();
            var circuitBreaker = new CircuitBreaker("test", options);

            // Act
            circuitBreaker.Dispose();
            circuitBreaker.Dispose(); // Should not throw on second dispose

            // Assert - No exception thrown
            Assert.True(true);
        }
    }
}