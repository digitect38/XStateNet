using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using XStateNet.Distributed.Resilience;

namespace XStateNet.Distributed.Tests.Resilience
{
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
                BreakDuration = TimeSpan.FromMilliseconds(100),
                SuccessCountInHalfOpen = 1
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

            Assert.Equal(CircuitState.Open, circuitBreaker.State);

            // Wait for break duration
            await Task.Delay(150);

            // Execute successful call - should transition to half-open then closed
            var result = await circuitBreaker.ExecuteAsync(() => Task.FromResult("success"));

            // Assert
            Assert.Equal("success", result);
            Assert.Equal(CircuitState.Closed, circuitBreaker.State);
        }

        [Fact]
        public async Task CircuitBreaker_ClosesAfterSuccessInHalfOpen()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                BreakDuration = TimeSpan.FromMilliseconds(50),
                SuccessCountInHalfOpen = 2
            };
            var circuitBreaker = new CircuitBreaker("test", options);

            // Open circuit
            try
            {
                await circuitBreaker.ExecuteAsync(() =>
                {
                    throw new InvalidOperationException();
                });
            }
            catch { }

            // Wait for half-open
            await Task.Delay(100);

            // Execute successful calls in half-open
            await circuitBreaker.ExecuteAsync(() => Task.FromResult(1));
            await circuitBreaker.ExecuteAsync(() => Task.FromResult(2));

            // Assert
            Assert.Equal(CircuitState.Closed, circuitBreaker.State);
        }

        [Fact]
        public async Task CircuitBreaker_ReopensOnFailureInHalfOpen()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                BreakDuration = TimeSpan.FromMilliseconds(50)
            };
            var circuitBreaker = new CircuitBreaker("test", options);

            // Open circuit
            try
            {
                await circuitBreaker.ExecuteAsync(() =>
                {
                    throw new InvalidOperationException();
                });
            }
            catch { }

            // Wait for half-open
            await Task.Delay(100);

            // Fail in half-open
            try
            {
                await circuitBreaker.ExecuteAsync(async () =>
                {
                    throw new InvalidOperationException("Fail in half-open");
                });
            }
            catch { }

            // Assert - should be open again
            Assert.Equal(CircuitState.Open, circuitBreaker.State);
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

            circuitBreaker.StateChanged += (sender, args) =>
            {
                eventArgs = args;
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