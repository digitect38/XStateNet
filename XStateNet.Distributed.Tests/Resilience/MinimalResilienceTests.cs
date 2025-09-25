using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using XStateNet.Distributed.Resilience;
using XStateNet.Distributed.Channels;

namespace XStateNet.Distributed.Tests.Resilience
{
    /// <summary>
    /// Minimal working tests that demonstrate the resilience features are functional
    /// </summary>
    public class MinimalResilienceTests
    {
        [Fact]
        public async Task CircuitBreaker_Works()
        {
            // Arrange
            var cb = new CircuitBreaker("test", new CircuitBreakerOptions
            {
                FailureThreshold = 2,
                BreakDuration = TimeSpan.FromMilliseconds(50) // Shorter duration for faster test
            });

            var stateChangedTcs = new TaskCompletionSource<CircuitState>();
            cb.StateChanged += (sender, args) =>
            {
                if (args.ToState == CircuitState.Open)
                    stateChangedTcs.TrySetResult(args.ToState);
            };

            // Act - Cause failures to open circuit
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    await cb.ExecuteAsync(async () =>
                    {
                        await Task.Yield();
                        throw new Exception("fail");
                        //return "never";
                    });
                }
                catch { }
            }

            // Wait for state change event with timeout
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            {
                await stateChangedTcs.Task.ConfigureAwait(false);
            }

            // Assert - Circuit should be open
            Assert.Equal(CircuitState.Open, cb.State);

            // Test that circuit rejects calls when open
            await Assert.ThrowsAsync<CircuitBreakerOpenException>(async () =>
            {
                await cb.ExecuteAsync(() => Task.FromResult("should fail"));
            });

            // Create a new task completion source for half-open transition
            var halfOpenTcs = new TaskCompletionSource<CircuitState>();
            cb.StateChanged += (sender, args) =>
            {
                if (args.ToState == CircuitState.HalfOpen || args.ToState == CircuitState.Closed)
                    halfOpenTcs.TrySetResult(args.ToState);
            };

            // Wait for break duration to elapse and retry
            // The circuit should transition to half-open after break duration
            // We need to wait for actual time to pass for the circuit breaker's timer
            var breakComplete = new TaskCompletionSource<bool>();
            using var timer = new Timer(_ => breakComplete.TrySetResult(true), null,
                TimeSpan.FromMilliseconds(100), Timeout.InfiniteTimeSpan);
            await breakComplete.Task;

            // Now the circuit should be in half-open state
            var result = await cb.ExecuteAsync(() => Task.FromResult("success"));
            Assert.Equal("success", result);
        }

        [Fact]
        public async Task RetryPolicy_Works()
        {
            // Arrange
            var retry = new XStateNetRetryPolicy("test", new RetryOptions
            {
                MaxRetries = 2,
                InitialDelay = TimeSpan.FromMilliseconds(10)
            });

            var attempts = 0;

            // Act
            var result = await retry.ExecuteAsync(ct =>
            {
                attempts++;
                if (attempts < 2) throw new Exception("retry");
                return Task.FromResult("success");
            });

            // Assert
            Assert.Equal("success", result);
            Assert.Equal(2, attempts);
        }

        [Fact]
        public async Task DeadLetterQueue_Works()
        {
            // Arrange
            var dlq = new DeadLetterQueue(
                new DeadLetterQueueOptions { MaxQueueSize = 100 },
                new InMemoryDeadLetterStorage()
            );

            // Act
            var id = await dlq.EnqueueAsync("test-message", "source", "reason");
            await dlq.WaitForPendingOperationsAsync(TimeSpan.FromSeconds(1)); // Wait for async processing

            // Assert
            Assert.NotNull(id);
            Assert.Equal(1, dlq.QueueDepth);

            // Can retrieve the message
            var message = await dlq.DequeueAsync<string>(id);
            Assert.Equal("test-message", message);
        }

        [Fact]
        public async Task TimeoutProtection_Works()
        {
            // Arrange
            var timeout = new TimeoutProtection(new TimeoutOptions
            {
                DefaultTimeout = TimeSpan.FromMilliseconds(50)
            });

            // Act & Assert - Fast operation succeeds
            var result = await timeout.ExecuteAsync(
                ct => Task.FromResult("fast"),
                TimeSpan.FromMilliseconds(100)
            );
            Assert.Equal("fast", result);

            // Test cancellation token propagation
            var tcs = new TaskCompletionSource<string>();
            var timeoutTask = timeout.ExecuteAsync(
                async ct =>
                {
                    ct.Register(() => tcs.TrySetCanceled());
                    return await tcs.Task;
                },
                TimeSpan.FromMilliseconds(50)
            );

            // Verify timeout occurs
            await Assert.ThrowsAsync<TimeoutException>(async () => await timeoutTask);

            // Verify cancellation was triggered
            Assert.True(tcs.Task.IsCanceled);

            // Test with manual completion before timeout
            var quickTcs = new TaskCompletionSource<string>();
            var quickTask = timeout.ExecuteAsync(
                ct => quickTcs.Task,
                TimeSpan.FromSeconds(1)
            );

            // Complete task before timeout
            quickTcs.SetResult("completed");
            var quickResult = await quickTask;
            Assert.Equal("completed", quickResult);
        }

        [Fact]
        public async Task BoundedChannel_Works()
        {
            // Arrange
            var channel = new BoundedChannelManager<int>("test", new CustomBoundedChannelOptions
            {
                Capacity = 2,
                FullMode = ChannelFullMode.DropNewest  // Use built-in drop behavior instead of custom backpressure
            });

            // Act - Fill channel
            Assert.True(await channel.WriteAsync(1));
            Assert.True(await channel.WriteAsync(2));
            Assert.True(await channel.WriteAsync(3)); // DropNewest allows writes to succeed

            // Read from channel
            // With DropNewest, when full and writing 3, it drops 3 (the newest)
            // So channel still has [1, 2]
            var (success1, item1) = await channel.ReadAsync();
            Assert.True(success1);
            Assert.Equal(1, item1);

            // Can write again after reading
            Assert.True(await channel.WriteAsync(4));
        }

        [Fact]
        public async Task Integration_AllComponentsWork()
        {
            // Arrange
            var cb = new CircuitBreaker("integration", new CircuitBreakerOptions
            {
                FailureThreshold = 5
            });

            var retry = new RetryPolicy("integration", new RetryOptions
            {
                MaxRetries = 2
            });

            var timeout = new TimeoutProtection(new TimeoutOptions
            {
                DefaultTimeout = TimeSpan.FromMilliseconds(100)
            });

            var dlq = new DeadLetterQueue(
                new DeadLetterQueueOptions { MaxQueueSize = 100 },
                new InMemoryDeadLetterStorage()
            );

            var channel = new BoundedChannelManager<string>("integration",
                new CustomBoundedChannelOptions
                {
                    Capacity = 10
                });

            // Act - Send messages through pipeline
            await channel.WriteAsync("message1");
            await channel.WriteAsync("fail");
            await channel.WriteAsync("message2");

            var processed = 0;
            var failed = 0;

            for (int i = 0; i < 3; i++)
            {
                var (hasMessage, message) = await channel.ReadAsync();
                if (!hasMessage) break;

                try
                {
                    await cb.ExecuteAsync(async () =>
                    {
                        await retry.ExecuteAsync(async ct =>
                        {
                            await timeout.ExecuteAsync(async timeoutCt =>
                            {
                                if (message.Contains("fail"))
                                {
                                    throw new Exception("Processing failed");
                                }
                                processed++;
                                return message;
                            }, TimeSpan.FromMilliseconds(50));
                            return message;
                        });
                        return message;
                    });
                }
                catch
                {
                    failed++;
                    await dlq.EnqueueAsync(message, "Pipeline", "Failed processing");
                }
            }

            // Wait for any async operations like DLQ to complete
            await dlq.WaitForPendingOperationsAsync(TimeSpan.FromSeconds(1));

            // Assert
            Assert.Equal(2, processed);
            Assert.Equal(1, failed);
            Assert.Equal(1, dlq.QueueDepth);
        }
    }
}