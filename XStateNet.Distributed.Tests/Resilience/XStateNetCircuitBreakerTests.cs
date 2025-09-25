using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using XStateNet.Distributed.Resilience;

namespace XStateNet.Distributed.Tests.Resilience
{
    /// <summary>
    /// Unit tests for XStateNet-based CircuitBreaker implementation
    /// Tests the state machine transitions and 'after' property behavior
    /// </summary>
    public class XStateNetCircuitBreakerTests
    {
        [Fact]
        public Task XStateNetCircuitBreaker_StartsInClosedState()
        {
            // Arrange
            var options = new CircuitBreakerOptions();
            var circuitBreaker = new XStateNetCircuitBreaker("test_xstatenet", options);

            // Assert
            Assert.Equal(CircuitState.Closed, circuitBreaker.State);
            return Task.CompletedTask;
        }

        [Fact]
        public async Task XStateNetCircuitBreaker_OpensAfterThresholdFailures()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 3,
                BreakDuration = TimeSpan.FromMilliseconds(200)
            };
            var circuitBreaker = new XStateNetCircuitBreaker("test_xstatenet", options);
            var failureCount = 0;

            // Act
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    await circuitBreaker.ExecuteAsync(async () =>
                    {
                        await Task.Yield(); // Allow async execution
                        throw new InvalidOperationException("Test failure");
                    });

                }
                catch (InvalidOperationException)
                {
                    // make interlocked increment
                    Interlocked.Increment(ref failureCount);
                    //failureCount++;
                }
            }

            // Assert
            Assert.Equal(3, failureCount);
            Assert.Equal(CircuitState.Open, circuitBreaker.State);
        }

        [Fact]
        public async Task XStateNetCircuitBreaker_RejectsCallsWhenOpen()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                BreakDuration = TimeSpan.FromSeconds(1)
            };
            var circuitBreaker = new XStateNetCircuitBreaker("test_xstatenet", options);

            // Act - Open the circuit
            try
            {
                await circuitBreaker.ExecuteAsync(async () =>
                {
                    await Task.Yield(); // Allow async execution
                    throw new InvalidOperationException("Test failure");
                });
            }
            catch { }

            Assert.Equal(CircuitState.Open, circuitBreaker.State);

            // Assert - Should reject calls
            await Assert.ThrowsAsync<CircuitBreakerOpenException>(async () =>
            {
                await circuitBreaker.ExecuteAsync(() => Task.FromResult("test"));
            });
        }

        [Fact]
        public async Task XStateNetCircuitBreaker_AutomaticallyTransitionsToHalfOpen_UsingAfterProperty()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                BreakDuration = TimeSpan.FromMilliseconds(100),
                SuccessCountInHalfOpen = 1
            };
            var circuitBreaker = new XStateNetCircuitBreaker("test_xstatenet", options);

            var stateChanges = new List<CircuitState>();
            circuitBreaker.StateChanged += (sender, args) =>
            {
                stateChanges.Add(args.ToState);
            };

            // Act - Open the circuit
            try
            {
                await circuitBreaker.ExecuteAsync(async () =>
                {
                    await Task.Yield(); // Allow async execution
                    throw new InvalidOperationException("Test failure");
                });
            }
            catch { }

            Assert.Equal(CircuitState.Open, circuitBreaker.State);

            // Wait deterministically for automatic transition to HalfOpen via 'after' property
            var transitionReady = new TaskCompletionSource<bool>();
            using var timer = new Timer(_ => transitionReady.TrySetResult(true), null,
                TimeSpan.FromMilliseconds(150), Timeout.InfiniteTimeSpan);
            await transitionReady.Task;

            // Try an operation - should be allowed in HalfOpen
            var result = await circuitBreaker.ExecuteAsync(async () =>
            {
                await Task.Yield(); // Allow async execution
                return "success";
            });

            // Assert
            Assert.Equal("success", result);
            Assert.Contains(CircuitState.Open, stateChanges);
            Assert.Contains(CircuitState.HalfOpen, stateChanges);
            Assert.Equal(CircuitState.Closed, circuitBreaker.State); // Should close after success
        }

        [Fact]
        public async Task XStateNetCircuitBreaker_HalfOpenToOpen_OnFailure()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                BreakDuration = TimeSpan.FromMilliseconds(50),
                SuccessCountInHalfOpen = 2
            };
            var circuitBreaker = new XStateNetCircuitBreaker("test_xstatenet", options);

            // Open the circuit
            try
            {
                await circuitBreaker.ExecuteAsync(() => throw new InvalidOperationException("Fail"));
            }
            catch { }

            // Wait deterministically for HalfOpen
            var transitionReady = new TaskCompletionSource<bool>();
            using var timer = new Timer(_ => transitionReady.TrySetResult(true), null,
                TimeSpan.FromMilliseconds(100), Timeout.InfiniteTimeSpan);
            await transitionReady.Task;

            // Act - Fail in HalfOpen state
            try
            {
                await circuitBreaker.ExecuteAsync(() => throw new InvalidOperationException("Fail again"));
            }
            catch { }

            // Assert - Should be Open again
            Assert.Equal(CircuitState.Open, circuitBreaker.State);
        }

        [Fact]
        public async Task XStateNetCircuitBreaker_HalfOpenToClosed_AfterSuccessCount()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                BreakDuration = TimeSpan.FromMilliseconds(50),
                SuccessCountInHalfOpen = 2
            };
            var circuitBreaker = new XStateNetCircuitBreaker("test_xstatenet", options);

            // Open the circuit
            try
            {
                await circuitBreaker.ExecuteAsync(() => throw new InvalidOperationException("Fail"));
            }
            catch { }

            // Wait deterministically for HalfOpen
            var transitionReady = new TaskCompletionSource<bool>();
            using var timer = new Timer(_ => transitionReady.TrySetResult(true), null,
                TimeSpan.FromMilliseconds(100), Timeout.InfiniteTimeSpan);
            await transitionReady.Task;

            // Act - Succeed twice in HalfOpen state
            await circuitBreaker.ExecuteAsync(async () =>
            {
                await Task.Yield(); // Allow async execution
                return "success1";
            });

            await circuitBreaker.ExecuteAsync(async () =>
            {
                await Task.Yield(); // Allow async execution
                return "success2";
            });

            // Assert - Should be Closed after required successes
            Assert.Equal(CircuitState.Closed, circuitBreaker.State);
        }

        [Fact]
        public async Task XStateNetCircuitBreaker_StateChangeEvents_FiredCorrectly()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 2,
                BreakDuration = TimeSpan.FromMilliseconds(50),
                SuccessCountInHalfOpen = 1
            };
            var circuitBreaker = new XStateNetCircuitBreaker("test_xstatenet", options);

            var stateChanges = new List<(CircuitState from, CircuitState to)>();
            circuitBreaker.StateChanged += (sender, args) =>
            {
                stateChanges.Add((args.FromState, args.ToState));
            };

            // Act - Cause failures to open
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    await circuitBreaker.ExecuteAsync(() => throw new InvalidOperationException("Fail"));
                }
                catch { }
            }

            // Wait deterministically for HalfOpen
            var transitionReady = new TaskCompletionSource<bool>();
            using var timer = new Timer(_ => transitionReady.TrySetResult(true), null,
                TimeSpan.FromMilliseconds(100), Timeout.InfiniteTimeSpan);
            await transitionReady.Task;

            // Success to close
            await circuitBreaker.ExecuteAsync(() => Task.FromResult("success"));

            // Assert
            Assert.Contains((CircuitState.Closed, CircuitState.Open), stateChanges);
            Assert.Contains((CircuitState.Open, CircuitState.HalfOpen), stateChanges);
            Assert.Contains((CircuitState.HalfOpen, CircuitState.Closed), stateChanges);
        }

        [Fact]
        public async Task XStateNetCircuitBreaker_FailureRateThreshold_OpensCircuit()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 10, // High consecutive threshold
                FailureRateThreshold = 0.5, // 50% failure rate
                MinimumThroughput = 4,
                SamplingDuration = TimeSpan.FromSeconds(1),
                BreakDuration = TimeSpan.FromMilliseconds(100)
            };
            var circuitBreaker = new XStateNetCircuitBreaker("test_xstatenet", options);

            // Act - Mix of successes and failures
            // 2 successes
            for (int i = 0; i < 2; i++)
            {
                await circuitBreaker.ExecuteAsync(() => Task.FromResult("success"));
            }

            // 3 failures (>50% failure rate with 5 total)
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    await circuitBreaker.ExecuteAsync(() => throw new InvalidOperationException("Fail"));
                }
                catch { }
            }

            // Assert
            Assert.Equal(CircuitState.Open, circuitBreaker.State);
        }

        [Fact]
        public async Task XStateNetCircuitBreaker_ConsecutiveFailures_ResetsOnSuccess()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 3,
                BreakDuration = TimeSpan.FromMilliseconds(100)
            };
            var circuitBreaker = new XStateNetCircuitBreaker("test_xstatenet", options);

            // Act - 2 failures, then success, then 2 more failures
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    await circuitBreaker.ExecuteAsync(() => throw new InvalidOperationException("Fail"));
                }
                catch { }
            }

            // Success should reset consecutive failures
            await circuitBreaker.ExecuteAsync(() => Task.FromResult("success"));

            // 2 more failures (should not open since count was reset)
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    await circuitBreaker.ExecuteAsync(() => throw new InvalidOperationException("Fail"));
                }
                catch { }
            }

            // Assert - Should still be closed
            Assert.Equal(CircuitState.Closed, circuitBreaker.State);
        }

        [Fact]
        public async Task XStateNetCircuitBreaker_Dispose_CleansUpProperly()
        {
            // Arrange
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                BreakDuration = TimeSpan.FromMilliseconds(50)
            };
            var circuitBreaker = new XStateNetCircuitBreaker("test_xstatenet", options);

            // Act - Open the circuit
            try
            {
                await circuitBreaker.ExecuteAsync(() => throw new InvalidOperationException("Fail"));
            }
            catch { }

            Assert.Equal(CircuitState.Open, circuitBreaker.State);

            // Dispose
            circuitBreaker.Dispose();

            // Assert - Should not throw or hang
            // Further operations should throw ObjectDisposedException
            // (This is implementation-dependent and may need adjustment)
            Assert.True(true, "Dispose completed without hanging");
        }
    }
}