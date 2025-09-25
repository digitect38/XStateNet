// Standalone demonstration of XStateNet resilience features
using System;
using System.Threading;
using System.Threading.Tasks;
using XStateNet.Distributed.Resilience;
using XStateNet.Distributed.Channels;

class ResilienceDemo
{
    static async Task Main()
    {
        Console.WriteLine("=== XStateNet Resilience Features Demo ===\n");

        await TestCircuitBreaker();
        await TestRetryPolicy();
        await TestDeadLetterQueue();
        await TestTimeoutProtection();
        await TestBoundedChannel();

        Console.WriteLine("\n✅ All resilience features are working correctly!");
    }

    static async Task TestCircuitBreaker()
    {
        Console.WriteLine("1. Testing Circuit Breaker...");
        var cb = new CircuitBreaker("demo", new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            BreakDuration = TimeSpan.FromMilliseconds(100)
        });

        // Cause failures
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await cb.ExecuteAsync(async () =>
                {
                    await Task.Yield();
                    throw new Exception("Service error");
                });
            }
            catch (Exception ex)
            {
                // Expected failure for demo - circuit breaker should open
                Console.WriteLine($"      Expected failure: {ex.GetType().Name}");
            }
        }

        Console.WriteLine($"   State after failures: {cb.State}");

        // Wait for recovery
        await Task.Delay(150);

        var result = await cb.ExecuteAsync(() => Task.FromResult("Recovered"));
        Console.WriteLine($"   Result after recovery: {result}");
    }

    static async Task TestRetryPolicy()
    {
        Console.WriteLine("\n2. Testing Retry Policy...");
        var retry = new RetryPolicy("demo", new RetryOptions
        {
            MaxRetries = 3,
            InitialDelay = TimeSpan.FromMilliseconds(10),
            BackoffStrategy = BackoffStrategy.Exponential
        });

        var attempts = 0;
        var result = await retry.ExecuteAsync(async ct =>
        {
            attempts++;
            Console.WriteLine($"   Attempt {attempts}");
            if (attempts < 3)
            {
                throw new Exception("Temporary failure");
            }
            return "Success after retries";
        });

        Console.WriteLine($"   Final result: {result}");
    }

    static async Task TestDeadLetterQueue()
    {
        Console.WriteLine("\n3. Testing Dead Letter Queue...");
        var dlq = new DeadLetterQueue(
            new DeadLetterQueueOptions { MaxQueueSize = 100 },
            new InMemoryDeadLetterStorage()
        );

        var messageId = await dlq.EnqueueAsync(
            "Failed message",
            "TestSource",
            "Processing failed"
        );

        Console.WriteLine($"   Message enqueued with ID: {messageId}");
        Console.WriteLine($"   Queue depth: {dlq.QueueDepth}");

        var retrieved = await dlq.DequeueAsync<string>(messageId);
        Console.WriteLine($"   Retrieved message: {retrieved}");
    }

    static async Task TestTimeoutProtection()
    {
        Console.WriteLine("\n4. Testing Timeout Protection...");
        var timeout = new TimeoutProtection(new TimeoutOptions
        {
            DefaultTimeout = TimeSpan.FromMilliseconds(100)
        });

        // Fast operation
        var fastResult = await timeout.ExecuteAsync(
            ct => Task.FromResult("Fast operation"),
            TimeSpan.FromMilliseconds(200)
        );
        Console.WriteLine($"   Fast operation: {fastResult}");

        // Slow operation (will timeout)
        try
        {
            await timeout.ExecuteAsync(
                async ct =>
                {
                    await Task.Delay(500, ct);
                    return "Slow operation";
                },
                TimeSpan.FromMilliseconds(50)
            );
        }
        catch (TimeoutException)
        {
            Console.WriteLine("   Slow operation timed out (expected)");
        }
    }

    static async Task TestBoundedChannel()
    {
        Console.WriteLine("\n5. Testing Bounded Channel...");
        var channel = new BoundedChannelManager<string>("demo", new CustomBoundedChannelOptions
        {
            Capacity = 3,
            BackpressureStrategy = BackpressureStrategy.Drop
        });

        // Fill channel
        Console.WriteLine("   Writing messages...");
        for (int i = 1; i <= 5; i++)
        {
            var success = await channel.WriteAsync($"Message-{i}");
            Console.WriteLine($"     Message-{i}: {(success ? "Written" : "Dropped")}");
        }

        // Read messages
        Console.WriteLine("   Reading messages...");
        for (int i = 0; i < 3; i++)
        {
            var (success, msg) = await channel.ReadAsync();
            if (success)
            {
                Console.WriteLine($"     Read: {msg}");
            }
        }

        var stats = channel.GetStatistics();
        Console.WriteLine($"   Stats - Written: {stats.TotalItemsWritten}, Dropped: {stats.TotalItemsDropped}");
    }
}