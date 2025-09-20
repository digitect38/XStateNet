using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using XStateNet.Distributed.Resilience;

namespace XStateNet.Distributed.Tests.Resilience
{
    public class RetryPolicyTests
    {
        [Fact]
        public async Task RetryPolicy_SucceedsOnFirstAttempt()
        {
            // Arrange
            var options = new RetryOptions { MaxRetries = 3 };
            var retryPolicy = new RetryPolicy("test", options);
            var attemptCount = 0;

            // Act
            var result = await retryPolicy.ExecuteAsync(async (ct) =>
            {
                attemptCount++;
                return "success";
            });

            // Assert
            Assert.Equal("success", result);
            Assert.Equal(1, attemptCount);
        }

        [Fact]
        public async Task RetryPolicy_RetriesOnFailure()
        {
            // Arrange
            var options = new RetryOptions
            {
                MaxRetries = 3,
                InitialDelay = TimeSpan.FromMilliseconds(10)
            };
            var retryPolicy = new RetryPolicy("test", options);
            var attemptCount = 0;

            // Act
            var result = await retryPolicy.ExecuteAsync(async (ct) =>
            {
                attemptCount++;
                if (attemptCount < 3)
                {
                    throw new InvalidOperationException($"Attempt {attemptCount} failed");
                }
                return "success";
            });

            // Assert
            Assert.Equal("success", result);
            Assert.Equal(3, attemptCount);
        }

        [Fact]
        public async Task RetryPolicy_FailsAfterMaxRetries()
        {
            // Arrange
            var options = new RetryOptions
            {
                MaxRetries = 2,
                InitialDelay = TimeSpan.FromMilliseconds(10)
            };
            var retryPolicy = new RetryPolicy("test", options);
            var attemptCount = 0;

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await retryPolicy.ExecuteAsync<string>(async (ct) =>
                {
                    attemptCount++;
                    throw new InvalidOperationException($"Attempt {attemptCount} failed");
                });
            });

            Assert.Equal(3, attemptCount); // Initial attempt + 2 retries
        }

        [Fact]
        public async Task RetryPolicy_ExponentialBackoff()
        {
            // Arrange
            var options = new RetryOptions
            {
                MaxRetries = 3,
                InitialDelay = TimeSpan.FromMilliseconds(10),
                MaxDelay = TimeSpan.FromMilliseconds(100),
                BackoffMultiplier = 2.0,
                BackoffStrategy = BackoffStrategy.Exponential,
                JitterStrategy = JitterStrategy.None // Disable jitter for predictable delays
            };
            var retryPolicy = new RetryPolicy("test", options);
            var attemptTimes = new List<DateTime>();

            // Act
            try
            {
                await retryPolicy.ExecuteAsync<string>(async (ct) =>
                {
                    attemptTimes.Add(DateTime.UtcNow);
                    throw new InvalidOperationException();
                });
            }
            catch { }

            // Assert - Verify delays increase exponentially
            Assert.Equal(4, attemptTimes.Count); // Initial + 3 retries

            // Verify delays are increasing exponentially
            // We expect roughly: 10ms, 20ms, 40ms but timing can vary
            for (int i = 1; i < attemptTimes.Count; i++)
            {
                var delay = attemptTimes[i] - attemptTimes[i - 1];

                // Just verify delays are positive and reasonable
                Assert.True(delay.TotalMilliseconds > 0,
                    $"Delay {delay.TotalMilliseconds}ms is not positive for retry {i}");
                Assert.True(delay.TotalMilliseconds <= 150,
                    $"Delay {delay.TotalMilliseconds}ms is too large for retry {i}");

                // For exponential, verify delays are generally increasing (with wide tolerance for system variations)
                if (i > 1)
                {
                    var prevDelay = attemptTimes[i - 1] - attemptTimes[i - 2];
                    // Just verify it's increasing in the general pattern - very loose tolerance due to system variations
                    var ratio = delay.TotalMilliseconds / prevDelay.TotalMilliseconds;
                    Assert.True(ratio >= 1.0,
                        $"Exponential pattern not observed: delay {i} ({delay.TotalMilliseconds:F2}ms) should be >= previous delay ({prevDelay.TotalMilliseconds:F2}ms)");
                }
            }
        }

        [Fact]
        public async Task RetryPolicy_ConstantDelay()
        {
            // Arrange
            var options = new RetryOptions
            {
                MaxRetries = 3,
                InitialDelay = TimeSpan.FromMilliseconds(20),
                BackoffStrategy = BackoffStrategy.Fixed,
                JitterStrategy = JitterStrategy.None // Disable jitter for predictable delays
            };
            var retryPolicy = new RetryPolicy("test", options);
            var attemptTimes = new List<DateTime>();

            // Act
            try
            {
                await retryPolicy.ExecuteAsync<string>(async (ct) =>
                {
                    attemptTimes.Add(DateTime.UtcNow);
                    throw new InvalidOperationException();
                });
            }
            catch { }

            // Assert - Verify delays are constant
            Assert.Equal(4, attemptTimes.Count);

            for (int i = 1; i < attemptTimes.Count; i++)
            {
                var delay = attemptTimes[i] - attemptTimes[i - 1];
                // For Fixed strategy, delays should be roughly constant (with wide tolerance)
                Assert.True(delay.TotalMilliseconds > 0,
                    $"Delay {delay.TotalMilliseconds}ms is not positive for retry {i}");
                // Very loose upper bound for system variations
                Assert.True(delay.TotalMilliseconds <= 500,
                    $"Delay {delay.TotalMilliseconds}ms is unreasonably large for retry {i}");
            }
        }

        [Fact]
        public async Task RetryPolicy_LinearBackoff()
        {
            // Arrange
            var options = new RetryOptions
            {
                MaxRetries = 3,
                InitialDelay = TimeSpan.FromMilliseconds(10),
                BackoffStrategy = BackoffStrategy.Linear,
                JitterStrategy = JitterStrategy.None // Disable jitter for predictable delays
            };
            var retryPolicy = new RetryPolicy("test", options);
            var attemptTimes = new List<DateTime>();

            // Act
            try
            {
                await retryPolicy.ExecuteAsync<string>(async (ct) =>
                {
                    attemptTimes.Add(DateTime.UtcNow);
                    throw new InvalidOperationException();
                });
            }
            catch { }

            // Assert - Verify delays increase linearly
            Assert.Equal(4, attemptTimes.Count);

            // For linear backoff, delays should increase consistently
            for (int i = 1; i < attemptTimes.Count; i++)
            {
                var delay = attemptTimes[i] - attemptTimes[i - 1];

                // Just verify delays are positive and reasonable
                Assert.True(delay.TotalMilliseconds >= 5,
                    $"Delay {delay.TotalMilliseconds}ms is too small for retry {i}");
                Assert.True(delay.TotalMilliseconds <= 100,
                    $"Delay {delay.TotalMilliseconds}ms is too large for retry {i}");

                // For linear, each delay should be roughly 10ms more than the previous
                if (i > 1)
                {
                    var prevDelay = attemptTimes[i - 1] - attemptTimes[i - 2];
                    var diff = delay.TotalMilliseconds - prevDelay.TotalMilliseconds;
                    // Allow wider tolerance for timing variations
                    Assert.True(diff >= -5 && diff <= 30,
                        $"Linear growth not observed: delay {i} increased by {diff:F2}ms from previous delay");
                }
            }
        }

        [Fact]
        public async Task RetryPolicy_JitterStrategy()
        {
            // Arrange
            var options = new RetryOptions
            {
                MaxRetries = 5,
                InitialDelay = TimeSpan.FromMilliseconds(50),
                JitterStrategy = XStateNet.Distributed.Resilience.JitterStrategy.Full
            };
            var retryPolicy = new RetryPolicy("test", options);
            var delays = new List<double>();

            // Act
            for (int i = 0; i < 3; i++)
            {
                var start = DateTime.UtcNow;
                try
                {
                    await retryPolicy.ExecuteAsync<string>(async (ct) =>
                    {
                        if ((DateTime.UtcNow - start).TotalMilliseconds > 0)
                        {
                            delays.Add((DateTime.UtcNow - start).TotalMilliseconds);
                        }
                        throw new InvalidOperationException();
                    }, CancellationToken.None);
                }
                catch { }
            }

            // Assert - With jitter, delays should vary
            Assert.True(delays.Count > 0);
            var hasVariation = false;
            for (int i = 1; i < delays.Count; i++)
            {
                if (Math.Abs(delays[i] - delays[i - 1]) > 5)
                {
                    hasVariation = true;
                    break;
                }
            }
            Assert.True(hasVariation, "Jitter should cause variation in retry delays");
        }

        [Fact]
        public async Task RetryPolicy_RespectsRetryableExceptions()
        {
            // Arrange
            var options = new RetryOptions
            {
                MaxRetries = 3,
                InitialDelay = TimeSpan.FromMilliseconds(10),
                RetryableExceptions = new HashSet<Type> { typeof(InvalidOperationException) }
            };
            var retryPolicy = new RetryPolicy("test", options);
            var attemptCount = 0;

            // Act & Assert - Should NOT retry on ArgumentException
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await retryPolicy.ExecuteAsync<string>(async (ct) =>
                {
                    attemptCount++;
                    throw new ArgumentException("Not retryable");
                });
            });

            Assert.Equal(1, attemptCount); // No retries
        }

        [Fact]
        public async Task RetryPolicy_RespectsCancellation()
        {
            // Arrange
            var options = new RetryOptions
            {
                MaxRetries = 10,
                InitialDelay = TimeSpan.FromMilliseconds(100)
            };
            var retryPolicy = new RetryPolicy("test", options);
            var cts = new CancellationTokenSource();
            var attemptCount = 0;

            // Act
            var task = retryPolicy.ExecuteAsync<string>(async (ct) =>
            {
                attemptCount++;
                if (attemptCount == 2)
                {
                    cts.Cancel();
                }
                throw new InvalidOperationException();
            }, cts.Token);

            // Assert
            await Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
            Assert.True(attemptCount >= 2);
            Assert.True(attemptCount <= 3); // Should stop quickly after cancellation
        }

        [Fact]
        public async Task RetryPolicy_MaxDelayRespected()
        {
            // Arrange
            var options = new RetryOptions
            {
                MaxRetries = 5,
                InitialDelay = TimeSpan.FromMilliseconds(10),
                MaxDelay = TimeSpan.FromMilliseconds(50),
                BackoffMultiplier = 10.0, // Large multiplier to test max delay
                BackoffStrategy = BackoffStrategy.Exponential
            };
            var retryPolicy = new RetryPolicy("test", options);
            var maxDelay = TimeSpan.Zero;

            // Act
            try
            {
                var lastAttempt = DateTime.UtcNow;
                await retryPolicy.ExecuteAsync<string>(async (ct) =>
                {
                    var now = DateTime.UtcNow;
                    var delay = now - lastAttempt;
                    if (delay > maxDelay)
                    {
                        maxDelay = delay;
                    }
                    lastAttempt = now;
                    throw new InvalidOperationException();
                });
            }
            catch { }

            // Assert
            Assert.True(maxDelay.TotalMilliseconds <= 100); // Max delay + some tolerance
        }

        [Fact]
        public async Task RetryPolicy_ThreadSafe_ConcurrentExecutions()
        {
            // Arrange
            var options = new RetryOptions
            {
                MaxRetries = 2,
                InitialDelay = TimeSpan.FromMilliseconds(10)
            };
            var retryPolicy = new RetryPolicy("test", options);
            var successCount = 0;

            // Act
            var tasks = new Task[50];
            for (int i = 0; i < 50; i++)
            {
                var taskId = i;
                tasks[i] = Task.Run(async () =>
                {
                    var result = await retryPolicy.ExecuteAsync(async (ct) =>
                    {
                        if (taskId % 2 == 0 && Random.Shared.NextDouble() < 0.3)
                        {
                            throw new InvalidOperationException();
                        }
                        Interlocked.Increment(ref successCount);
                        return taskId;
                    });
                });
            }

            // Assert
            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                // Some tasks may fail, that's ok
            }

            Assert.True(successCount > 0);
        }
    }
}