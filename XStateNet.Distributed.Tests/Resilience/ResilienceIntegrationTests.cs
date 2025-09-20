using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using MessagePack;
using XStateNet.Distributed.Resilience;
using XStateNet.Distributed.Channels;
using XStateNet.Distributed.StateMachines;
using System.Threading.Channels;

namespace XStateNet.Distributed.Tests.Resilience
{
    /// <summary>
    /// Integration tests for resilience components working together
    /// </summary>
    public class ResilienceIntegrationTests : IDisposable
    {
        private readonly CircuitBreaker _circuitBreaker;
        private readonly RetryPolicy _retryPolicy;
        private readonly TimeoutProtection _timeoutProtection;
        private readonly DeadLetterQueue _dlq;
        private readonly InMemoryDeadLetterStorage _storage;
        private readonly ILogger<ResilienceIntegrationTests> _logger;

        public ResilienceIntegrationTests()
        {
            // Setup logger
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
            });
            _logger = loggerFactory.CreateLogger<ResilienceIntegrationTests>();

            // Setup components
            _circuitBreaker = new CircuitBreaker("test-cb", new CircuitBreakerOptions
            {
                FailureThreshold = 3,
                BreakDuration = TimeSpan.FromMilliseconds(500),
                SuccessCountInHalfOpen = 2
            });

            _retryPolicy = new RetryPolicy("test-retry", new RetryOptions
            {
                MaxRetries = 3,
                InitialDelay = TimeSpan.FromMilliseconds(50),
                BackoffStrategy = BackoffStrategy.Exponential
            });

            _timeoutProtection = new TimeoutProtection(new TimeoutOptions
            {
                DefaultTimeout = TimeSpan.FromSeconds(1)
            });

            _storage = new InMemoryDeadLetterStorage();
            _dlq = new DeadLetterQueue(new DeadLetterQueueOptions
            {
                MaxRetries = 3
            }, _storage);
        }

        [Fact]
        public async Task CircuitBreaker_With_Retry_Pattern()
        {
            // Arrange
            var failureCount = 0;
            var successCount = 0;

            // Act - Execute with retry inside circuit breaker
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    await _circuitBreaker.ExecuteAsync(async () =>
                    {
                        return await _retryPolicy.ExecuteAsync(async (ct) =>
                        {
                            // Fail first 3 attempts
                            if (failureCount < 3)
                            {
                                failureCount++;
                                throw new InvalidOperationException("Service unavailable");
                            }
                            successCount++;
                            return $"Success {successCount}";
                        });
                    });
                }
                catch (CircuitBreakerOpenException)
                {
                    _logger.LogWarning("Circuit breaker is open");
                    await Task.Delay(600); // Wait for circuit to potentially close
                }
                catch (InvalidOperationException)
                {
                    _logger.LogWarning("Operation failed after retries");
                }
            }

            // Assert
            Assert.True(failureCount >= 3);
            Assert.True(successCount > 0);
        }

        [Fact]
        public async Task Timeout_With_Retry_SendsTo_DLQ()
        {
            // Arrange
            var message = new TestMessage { Id = "timeout-test", Data = "Important data" };
            var attemptCount = 0;

            // Using InMemoryDeadLetterStorage which doesn't need setup

            // Act
            try
            {
                await _retryPolicy.ExecuteAsync(async (ct) =>
                {
                    attemptCount++;
                    return await _timeoutProtection.ExecuteAsync(
                        async (timeoutToken) =>
                        {
                            // Always timeout
                            await Task.Delay(2000, timeoutToken);
                            return message;
                        },
                        TimeSpan.FromMilliseconds(100)
                    );
                });
            }
            catch (TimeoutException ex)
            {
                // Send to DLQ after retries exhausted
                await _dlq.EnqueueAsync(message, "TimeoutTest", "Timeout after retries", ex);
            }

            // Assert
            Assert.Equal(4, attemptCount); // 1 initial + 3 retries
        }

        [Fact]
        public async Task Full_Resilience_Pipeline()
        {
            // Arrange
            var channel = new BoundedChannelManager<WorkItem>("work-queue",
                new CustomBoundedChannelOptions
                {
                    Capacity = 10,
                    FullMode = ChannelFullMode.Wait
                });

            var processedItems = new List<string>();

            // Using InMemoryDeadLetterStorage which handles storage internally

            // Processor with full resilience
            var processor = Task.Run(async () =>
            {
                while (!channel.Reader.Completion.IsCompleted)
                {
                    try
                    {
                        var (success, item) = await channel.ReadAsync();
                        if (!success) continue;

                        // Process with resilience pipeline
                        await _circuitBreaker.ExecuteAsync(async () =>
                        {
                            await _retryPolicy.ExecuteAsync(async (ct) =>
                            {
                                await _timeoutProtection.ExecuteAsync(
                                    async (timeoutToken) =>
                                    {
                                        await ProcessWorkItemAsync(item, processedItems, timeoutToken);
                                        return item;
                                    },
                                    TimeSpan.FromMilliseconds(500)
                                );
                                return item;
                            });
                            return item;
                        });
                    }
                    catch (Exception ex) when (!(ex is ChannelClosedException))
                    {
                        _logger.LogError(ex, "Failed to process item");
                    }
                }
            });

            // Act - Send work items
            for (int i = 1; i <= 15; i++)
            {
                var item = new WorkItem
                {
                    Id = $"item-{i}",
                    ShouldFail = i % 5 == 0, // Every 5th item fails
                    ProcessingTime = i % 3 == 0 ? 600 : 50 // Every 3rd item is slow
                };

                if (!await channel.WriteAsync(item))
                {
                    // Channel full - send to DLQ
                    await _dlq.EnqueueAsync(item, "Producer", "Channel full");
                }
            }

            channel.Complete();
            await processor;

            // Assert
            Assert.True(processedItems.Count > 0);
            Assert.True(processedItems.Count < 15); // Some items should have failed
        }

        [Fact]
        public async Task CircuitBreaker_Protects_Downstream_Service()
        {
            // Arrange - Create a simple deterministic circuit breaker test
            var requestCount = 0;
            var successCount = 0;
            var rejectedCount = 0;

            // Simple operation that fails first 3 times
            Func<Task<string>> failingOperation = async () =>
            {
                var currentRequest = Interlocked.Increment(ref requestCount);
                await Task.Delay(10);

                if (currentRequest <= 3)
                {
                    throw new InvalidOperationException($"Request {currentRequest} failed");
                }

                return $"Success {currentRequest}";
            };

            // Act - Execute 10 requests sequentially
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var result = await _circuitBreaker.ExecuteAsync(failingOperation);
                    successCount++;
                    _logger.LogInformation($"Request {i} succeeded: {result}");
                }
                catch (CircuitBreakerOpenException ex)
                {
                    rejectedCount++;
                    _logger.LogInformation($"Request {i} rejected: {ex.Message}");
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning($"Request {i} failed: {ex.Message}");
                }
            }

            _logger.LogInformation($"Results: Success={successCount}, Rejected={rejectedCount}, Total Requests Sent={requestCount}");

            // Assert
            // After 3 failures, circuit should open and reject subsequent requests
            Assert.True(rejectedCount > 0, $"Circuit breaker should have rejected some requests. Success={successCount}, Rejected={rejectedCount}");
            Assert.True(requestCount <= 5, $"Circuit breaker should have prevented some requests from reaching the service. Actual requests sent: {requestCount}");
        }

        public void Dispose()
        {
        }

        private async Task ProcessWorkItemAsync(WorkItem item, List<string> processedItems, CancellationToken cancellationToken)
        {
            await Task.Delay(item.ProcessingTime, cancellationToken);

            if (item.ShouldFail)
            {
                throw new InvalidOperationException($"Failed to process {item.Id}");
            }

            lock (processedItems)
            {
                processedItems.Add(item.Id);
            }
            await Task.CompletedTask;
        }

        [MessagePackObject]
        public class WorkItem
        {
            [Key(0)]
            public string Id { get; set; } = string.Empty;
            [Key(1)]
            public bool ShouldFail { get; set; }
            [Key(2)]
            public int ProcessingTime { get; set; } = 50;
        }

        [MessagePackObject]
        public class TestMessage
        {
            [Key(0)]
            public string Id { get; set; } = string.Empty;
            [Key(1)]
            public string Data { get; set; } = string.Empty;
        }

        private class UnreliableService
        {
            private int _callCount;
            public int CallCount => _callCount;

            public async Task<string> ProcessRequestAsync(int requestId)
            {
                var currentCount = Interlocked.Increment(ref _callCount);

                // Fail the first 3 consecutive requests to trigger circuit breaker
                // (FailureThreshold = 3)
                if (currentCount <= 3)
                {
                    throw new InvalidOperationException($"Service error for request {requestId}");
                }

                // After circuit opens and recovers, fail some more requests
                if (currentCount >= 10 && currentCount <= 12)
                {
                    throw new InvalidOperationException($"Service error for request {requestId}");
                }

                await Task.Delay(Random.Shared.Next(10, 100));
                return $"Processed {requestId}";
            }
        }

    }
}