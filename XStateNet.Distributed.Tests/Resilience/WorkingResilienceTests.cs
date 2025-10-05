using XStateNet.Distributed.Channels;
using XStateNet.Distributed.Resilience;
using XStateNet.Orchestration;
using Xunit;
using CircuitBreakerOpenException = XStateNet.Orchestration.CircuitBreakerOpenException;

namespace XStateNet.Distributed.Tests.Resilience
{
    /// <summary>
    /// Working tests for the new resilience features
    /// </summary>
    public class WorkingResilienceTests : IDisposable
    {
        private EventBusOrchestrator? _orchestrator;

        public void Dispose()
        {
            _orchestrator?.Dispose();
        }

        [Fact]
        public async Task CircuitBreaker_Opens_After_Failures()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var circuitBreaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 2,
                openDuration: TimeSpan.FromMilliseconds(100));
            await circuitBreaker.StartAsync();

            // Act - cause failures
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    await circuitBreaker.ExecuteAsync<bool>(async ct =>
                    {
                        await Task.Yield();
                        throw new InvalidOperationException("Test failure");
                    }, CancellationToken.None);
                }
                catch { }
            }

            await Task.Delay(50);

            // Assert
            Assert.Contains("open", circuitBreaker.CurrentState);
        }

        [Fact]
        public async Task RetryPolicy_Retries_On_Failure()
        {
            // Arrange
            var retryPolicy = new XStateNetRetryPolicy("test", new RetryOptions
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
                        // Simulate long operation that will timeout
                        var tcs = new TaskCompletionSource<string>();
                        using (ct.Register(() => tcs.TrySetCanceled()))
                        {
                            // This will never complete, causing a timeout
                            return await tcs.Task;
                        }
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
                FullMode = ChannelFullMode.DropOldest
            });

            // Act
            Assert.True(await channel.WriteAsync("item1"));
            Assert.True(await channel.WriteAsync("item2"));
            Assert.True(await channel.WriteAsync("item3"));

            // Verify write statistics
            var stats = channel.GetStatistics();
            Assert.Equal(3, stats.TotalItemsWritten);

            // Read remaining items
            var (success1, item1) = await channel.ReadAsync();
            Assert.True(success1);
            Assert.Equal("item2", item1);

            var (success2, item2) = await channel.ReadAsync();
            Assert.True(success2);
            Assert.Equal("item3", item2);
        }

        [Fact]
        public async Task CircuitBreaker_With_Retry_Integration()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var circuitBreaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 3,
                openDuration: TimeSpan.FromMilliseconds(200));
            await circuitBreaker.StartAsync();

            var retryPolicy = new XStateNetRetryPolicy("test", new RetryOptions
            {
                MaxRetries = 2,
                InitialDelay = TimeSpan.FromMilliseconds(10)
            });

            var attemptCount = 0;

            // Act - Execute with retry inside circuit breaker
            var result = await circuitBreaker.ExecuteAsync(async ct =>
            {
                return await retryPolicy.ExecuteAsync(async (retryToken) =>
                {
                    attemptCount++;
                    if (attemptCount < 2)
                    {
                        throw new InvalidOperationException("Temporary failure");
                    }
                    return "success";
                });
            }, CancellationToken.None);

            // Assert
            Assert.Equal("success", result);
            Assert.Equal(2, attemptCount);
            Assert.Contains("closed", circuitBreaker.CurrentState);
        }

        [Fact]
        public async Task CircuitBreaker_Transitions_To_HalfOpen()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var circuitBreaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 1,
                openDuration: TimeSpan.FromMilliseconds(100));
            await circuitBreaker.StartAsync();

            // Open circuit
            try
            {
                await circuitBreaker.ExecuteAsync<bool>(async ct =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("Failure");
                }, CancellationToken.None);
            }
            catch { }

            await Task.Delay(50);
            Assert.Contains("open", circuitBreaker.CurrentState);

            // Wait for timeout
            await Task.Delay(150);

            // Should now accept a test request (half-open)
            var result = await circuitBreaker.ExecuteAsync(ct => Task.FromResult("test"), CancellationToken.None);
            Assert.Equal("test", result);

            await Task.Delay(50);
            Assert.Contains("closed", circuitBreaker.CurrentState);
        }

        [Fact]
        public async Task CircuitBreaker_Rejects_When_Open()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            var circuitBreaker = new OrchestratedCircuitBreaker(
                "test",
                _orchestrator,
                failureThreshold: 1,
                openDuration: TimeSpan.FromSeconds(10));
            await circuitBreaker.StartAsync();

            // Open circuit
            try
            {
                await circuitBreaker.ExecuteAsync<bool>(async ct =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("Failure");
                }, CancellationToken.None);
            }
            catch { }

            await Task.Delay(50);

            // Assert - Should reject
            await Assert.ThrowsAsync<CircuitBreakerOpenException>(async () =>
            {
                await circuitBreaker.ExecuteAsync(ct => Task.FromResult("test"), CancellationToken.None);
            });
        }
    }
}
