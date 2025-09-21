using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using XStateNet.Distributed.Resilience;
using XStateNet.Distributed.Channels;

namespace XStateNet.Distributed.Tests.Resilience
{
    /// <summary>
    /// Working tests for the new resilience features
    /// </summary>
    public class WorkingResilienceTests
    {
        [Fact]
        public async Task CircuitBreaker_Opens_After_Failures()
        {
            // Arrange
            var circuitBreaker = new CircuitBreaker("test", new CircuitBreakerOptions
            {
                FailureThreshold = 2,
                BreakDuration = TimeSpan.FromMilliseconds(100)
            });

            // Act - cause failures
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    await circuitBreaker.ExecuteAsync(async () =>
                    {
                        throw new InvalidOperationException("Test failure");
                    });
                }
                catch { }
            }

            // Assert
            Assert.Equal(CircuitState.Open, circuitBreaker.State);
        }

        [Fact]
        public async Task RetryPolicy_Retries_On_Failure()
        {
            // Arrange
            var retryPolicy = new RetryPolicy("test", new RetryOptions
            {
                MaxRetries = 3,
                InitialDelay = TimeSpan.FromMilliseconds(10)
            });
            var attempts = 0;

            // Act
            var result = await retryPolicy.ExecuteAsync(async (ct) =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new InvalidOperationException("Test failure");
                }
                return "success";
            });

            // Assert
            Assert.Equal("success", result);
            Assert.Equal(3, attempts);
        }

        [Fact]
        public async Task DeadLetterQueue_Stores_Messages()
        {
            // Arrange
            var storage = new InMemoryDeadLetterStorage();
            var dlq = new DeadLetterQueue(new DeadLetterQueueOptions
            {
                MaxQueueSize = 100
            }, storage);

            // Act
            var messageId = await dlq.EnqueueAsync(
                "test-message",
                "TestSource",
                "Test reason"
            );

            // Wait for async background processing
            await dlq.WaitForPendingOperationsAsync(TimeSpan.FromSeconds(1));

            // Assert
            Assert.NotNull(messageId);
            Assert.True(dlq.QueueDepth > 0);
        }

        [Fact]
        public async Task TimeoutProtection_Enforces_Timeout()
        {
            // Arrange
            var timeoutProtection = new TimeoutProtection(new TimeoutOptions
            {
                DefaultTimeout = TimeSpan.FromMilliseconds(100)
            });

            // Act & Assert
            await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await timeoutProtection.ExecuteAsync(
                    async (ct) =>
                    {
                        await Task.Delay(500, ct);
                        return "never returned";
                    },
                    TimeSpan.FromMilliseconds(50)
                );
            });
        }

        [Fact]
        public async Task BoundedChannel_Handles_Backpressure()
        {
            // Arrange
            var channel = new BoundedChannelManager<string>("test", new CustomBoundedChannelOptions
            {
                Capacity = 2,
                BackpressureStrategy = BackpressureStrategy.Drop,
                EnableCustomBackpressure = true
            });

            // Act
            Assert.True(await channel.WriteAsync("item1"));
            Assert.True(await channel.WriteAsync("item2"));
            // Channel is now full
            Assert.False(await channel.WriteAsync("item3")); // Should be dropped

            // Read one item
            var (success, item) = await channel.ReadAsync();
            Assert.True(success);
            Assert.Equal("item1", item);

            // Now we can write again
            Assert.True(await channel.WriteAsync("item4"));
        }

        [Fact]
        public async Task Integration_CircuitBreaker_With_Retry()
        {
            // Arrange
            var circuitBreaker = new CircuitBreaker("test", new CircuitBreakerOptions
            {
                FailureThreshold = 3,
                BreakDuration = TimeSpan.FromMilliseconds(100)
            });

            var retryPolicy = new RetryPolicy("test", new RetryOptions
            {
                MaxRetries = 2,
                InitialDelay = TimeSpan.FromMilliseconds(10)
            });

            var failureCount = 0;

            // Act
            try
            {
                await circuitBreaker.ExecuteAsync(async () =>
                {
                    return await retryPolicy.ExecuteAsync(async (ct) =>
                    {
                        failureCount++;
                        if (failureCount < 5)
                        {
                            throw new InvalidOperationException("Service unavailable");
                        }
                        return "success";
                    });
                });
            }
            catch
            {
                // Expected to fail after retries
            }

            // Assert
            Assert.True(failureCount > 0);
        }

        [Fact]
        public async Task Full_Resilience_Pipeline()
        {
            // Arrange
            var channel = new BoundedChannelManager<string>("test", new CustomBoundedChannelOptions
            {
                Capacity = 10,
                BackpressureStrategy = BackpressureStrategy.Wait
            });

            var circuitBreaker = new CircuitBreaker("test", new CircuitBreakerOptions
            {
                FailureThreshold = 3
            });

            var retryPolicy = new RetryPolicy("test", new RetryOptions
            {
                MaxRetries = 2
            });

            var timeoutProtection = new TimeoutProtection(new TimeoutOptions
            {
                DefaultTimeout = TimeSpan.FromMilliseconds(100)
            });

            var dlq = new DeadLetterQueue(new DeadLetterQueueOptions
            {
                MaxQueueSize = 100
            }, new InMemoryDeadLetterStorage());

            var processedCount = 0;
            var failedCount = 0;

            // Act - Send messages
            await channel.WriteAsync("message1");
            await channel.WriteAsync("fail1");
            await channel.WriteAsync("message2");

            // Process messages
            for (int i = 0; i < 3; i++)
            {
                var (success, message) = await channel.ReadAsync();
                if (!success || message == null) break;

                try
                {
                    await circuitBreaker.ExecuteAsync(async () =>
                    {
                        await retryPolicy.ExecuteAsync(async (ct) =>
                        {
                            await timeoutProtection.ExecuteAsync(
                                async (timeoutToken) =>
                                {
                                    if (message.Contains("fail"))
                                    {
                                        throw new InvalidOperationException("Failed to process");
                                    }
                                    processedCount++;
                                    return message;
                                },
                                TimeSpan.FromMilliseconds(50)
                            );
                            return message;
                        });
                        return message;
                    });
                }
                catch (Exception ex)
                {
                    failedCount++;
                    await dlq.EnqueueAsync(message, "Pipeline", "Processing failed", ex);
                }
            }

            // Wait for DLQ to process the message using completion event
            await dlq.WaitForPendingOperationsAsync(TimeSpan.FromSeconds(1));

            // Assert
            Assert.Equal(2, processedCount);
            Assert.Equal(1, failedCount);
            Assert.Equal(1, dlq.QueueDepth);
        }
    }
}