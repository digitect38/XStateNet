using System;
using System.Threading;
using System.Threading.Tasks;
using XStateNet.Distributed.Resilience;
using XStateNet.Distributed.Channels;
using Microsoft.Extensions.Logging;

namespace XStateNet.Distributed.Examples
{
    /// <summary>
    /// Example demonstrating the new resilience features
    /// </summary>
    public class ResilienceExample
    {
        public static async Task RunExample()
        {
            Console.WriteLine("=== XStateNet Resilience Features Demo ===\n");

            // Create logger factory
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole().SetMinimumLevel(LogLevel.Information);
            });

            // 1. Circuit Breaker Demo
            await DemoCircuitBreaker();

            // 2. Retry Policy Demo
            await DemoRetryPolicy();

            // 3. Dead Letter Queue Demo
            await DemoDeadLetterQueue();

            // 4. Timeout Protection Demo
            await DemoTimeoutProtection();

            // 5. Bounded Channel Demo
            await DemoBoundedChannel();

            // 6. Full Integration Demo
            await DemoFullIntegration();

            Console.WriteLine("\n=== Demo Complete ===");
        }

        static async Task DemoCircuitBreaker()
        {
            Console.WriteLine("\n--- Circuit Breaker Demo ---");

            var circuitBreaker = new CircuitBreaker("demo-cb", new CircuitBreakerOptions
            {
                FailureThreshold = 2,
                BreakDuration = TimeSpan.FromSeconds(2)
            });

            // Simulate failures
            for (int i = 1; i <= 3; i++)
            {
                try
                {
                    await circuitBreaker.ExecuteAsync(async () =>
                    {
                        Console.WriteLine($"  Attempt {i}: Processing...");
                        if (i <= 2)
                        {
                            throw new InvalidOperationException("Service unavailable");
                        }
                        return "Success";
                    });
                    Console.WriteLine($"  Attempt {i}: Success");
                }
                catch (CircuitBreakerOpenException)
                {
                    Console.WriteLine($"  Attempt {i}: Circuit breaker is OPEN - request rejected");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Attempt {i}: Failed - {ex.Message}");
                }

                if (i == 2)
                {
                    Console.WriteLine("  Waiting for circuit to close...");
                    await Task.Delay(2500);
                }
            }
        }

        static async Task DemoRetryPolicy()
        {
            Console.WriteLine("\n--- Retry Policy Demo ---");

            var retryPolicy = new RetryPolicy("demo-retry", new RetryOptions
            {
                MaxRetries = 3,
                InitialDelay = TimeSpan.FromMilliseconds(100),
                BackoffStrategy = BackoffStrategy.Exponential
            });

            var attemptCount = 0;
            try
            {
                var result = await retryPolicy.ExecuteAsync(async (ct) =>
                {
                    attemptCount++;
                    Console.WriteLine($"  Attempt {attemptCount}");

                    if (attemptCount < 3)
                    {
                        throw new InvalidOperationException("Temporary failure");
                    }

                    return "Success after retries";
                });

                Console.WriteLine($"  Result: {result}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Failed after all retries: {ex.Message}");
            }
        }

        static async Task DemoDeadLetterQueue()
        {
            Console.WriteLine("\n--- Dead Letter Queue Demo ---");

            var storage = new InMemoryDeadLetterStorage();
            var dlq = new DeadLetterQueue(new DeadLetterQueueOptions
            {
                MaxQueueSize = 100,
                MaxRetries = 3
            }, storage);

            // Enqueue failed messages
            Console.WriteLine("  Enqueueing failed messages...");
            for (int i = 1; i <= 3; i++)
            {
                var messageId = await dlq.EnqueueAsync(
                    $"message-{i}",
                    "DemoSource",
                    $"Processing failed for message {i}"
                );
                Console.WriteLine($"  Enqueued: {messageId}");
            }

            Console.WriteLine($"  Dead Letter Queue depth: {dlq.QueueDepth}");

            // Show queue statistics
            Console.WriteLine($"  Messages in DLQ: {dlq.QueueDepth}");
        }

        static async Task DemoTimeoutProtection()
        {
            Console.WriteLine("\n--- Timeout Protection Demo ---");

            var timeoutProtection = new TimeoutProtection(new TimeoutOptions
            {
                DefaultTimeout = TimeSpan.FromSeconds(1)
            });

            // Fast operation
            try
            {
                var result = await timeoutProtection.ExecuteAsync(
                    async (ct) =>
                    {
                        Console.WriteLine("  Executing fast operation...");
                        await Task.Delay(100, ct);
                        return "Fast operation completed";
                    },
                    TimeSpan.FromMilliseconds(500)
                );
                Console.WriteLine($"  Success: {result}");
            }
            catch (TimeoutException)
            {
                Console.WriteLine("  Fast operation timed out");
            }

            // Slow operation
            try
            {
                var result = await timeoutProtection.ExecuteAsync(
                    async (ct) =>
                    {
                        Console.WriteLine("  Executing slow operation...");
                        await Task.Delay(2000, ct);
                        return "Slow operation completed";
                    },
                    TimeSpan.FromMilliseconds(500)
                );
                Console.WriteLine($"  Success: {result}");
            }
            catch (TimeoutException)
            {
                Console.WriteLine("  Slow operation timed out (expected)");
            }
        }

        static async Task DemoBoundedChannel()
        {
            Console.WriteLine("\n--- Bounded Channel Demo ---");

            var channel = new BoundedChannelManager<string>("demo-channel", new CustomBoundedChannelOptions
            {
                Capacity = 3,
                BackpressureStrategy = BackpressureStrategy.Drop,
                EnableMonitoring = true
            });

            // Fill the channel
            Console.WriteLine("  Writing to channel...");
            for (int i = 1; i <= 5; i++)
            {
                var success = await channel.WriteAsync($"Message-{i}");
                Console.WriteLine($"  Write Message-{i}: {(success ? "Success" : "Dropped (channel full)")}");
            }

            // Read from channel
            Console.WriteLine("  Reading from channel...");
            for (int i = 0; i < 3; i++)
            {
                var (success, item) = await channel.ReadAsync();
                if (!success) break;
                Console.WriteLine($"  Read: {item}");
            }

            // Show statistics
            var stats = channel.GetStatistics();
            Console.WriteLine($"  Statistics - Written: {stats.TotalItemsWritten}, Read: {stats.TotalItemsRead}");
        }

        static async Task DemoFullIntegration()
        {
            Console.WriteLine("\n--- Full Integration Demo ---");
            Console.WriteLine("  Processing messages with complete resilience pipeline...");

            // Create components
            var channel = new BoundedChannelManager<string>("pipeline", new CustomBoundedChannelOptions
            {
                Capacity = 10,
                BackpressureStrategy = BackpressureStrategy.Wait
            });

            var circuitBreaker = new CircuitBreaker("pipeline-cb", new CircuitBreakerOptions
            {
                FailureThreshold = 3,
                BreakDuration = TimeSpan.FromSeconds(1)
            });

            var retryPolicy = new RetryPolicy("pipeline-retry", new RetryOptions
            {
                MaxRetries = 2,
                InitialDelay = TimeSpan.FromMilliseconds(50)
            });

            var timeoutProtection = new TimeoutProtection(new TimeoutOptions
            {
                DefaultTimeout = TimeSpan.FromMilliseconds(200)
            });

            var dlq = new DeadLetterQueue(new DeadLetterQueueOptions
            {
                MaxQueueSize = 100
            }, new InMemoryDeadLetterStorage());

            // Send test messages
            await channel.WriteAsync("good-message-1");
            await channel.WriteAsync("fail-message-1");
            await channel.WriteAsync("good-message-2");
            await channel.WriteAsync("timeout-message");
            await channel.WriteAsync("good-message-3");

            // Process messages
            var processedCount = 0;
            var failedCount = 0;

            for (int i = 0; i < 5; i++)
            {
                var (success, message) = await channel.ReadAsync();
                if (!success || message == null) break;

                Console.WriteLine($"  Processing: {message}");

                try
                {
                    await circuitBreaker.ExecuteAsync(async () =>
                    {
                        return await retryPolicy.ExecuteAsync(async (ct) =>
                        {
                            return await timeoutProtection.ExecuteAsync(
                                async (timeoutToken) =>
                                {
                                    // Simulate different scenarios
                                    if (message.Contains("fail"))
                                    {
                                        throw new InvalidOperationException("Processing failed");
                                    }
                                    if (message.Contains("timeout"))
                                    {
                                        await Task.Delay(500, timeoutToken);
                                    }

                                    await Task.Delay(10, timeoutToken);
                                    processedCount++;
                                    Console.WriteLine($"    ✓ Processed successfully");
                                    return message;
                                },
                                TimeSpan.FromMilliseconds(100)
                            );
                        });
                    });
                }
                catch (Exception ex)
                {
                    failedCount++;
                    Console.WriteLine($"    ✗ Failed: {ex.GetType().Name} - {ex.Message}");
                    await dlq.EnqueueAsync(message, "Pipeline", "Processing failed", ex);
                }
            }

            Console.WriteLine($"\n  Summary:");
            Console.WriteLine($"    Processed: {processedCount}");
            Console.WriteLine($"    Failed: {failedCount}");
            Console.WriteLine($"    In DLQ: {dlq.QueueDepth}");
        }

    }
}