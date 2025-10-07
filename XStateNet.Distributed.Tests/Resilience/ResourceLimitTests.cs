using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using XStateNet.Orchestration;
using Xunit;
using Xunit.Abstractions;

namespace XStateNet.Distributed.Tests.Resilience
{
    /// <summary>
    /// Tests for resource exhaustion scenarios (thread pools, connections, memory)
    /// </summary>
    [Collection("TimingSensitive")]
    public class ResourceLimitTests : ResilienceTestBase
    {
        
        private readonly ILoggerFactory _loggerFactory;
        private EventBusOrchestrator? _orchestrator;

        public ResourceLimitTests(ITestOutputHelper output) : base(output)
        {

            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddDebug().SetMinimumLevel(LogLevel.Warning);
            });
        }

        [Fact]
        public async Task ResourceLimit_ThreadPoolExhaustion_GracefulDegradation()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 4 // Limited pool
            });

            var completedCount = 0;
            var queuedCount = 0;
            var rejectedCount = 0;

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["blockingOperation"] = (ctx) =>
                {
                    // Simulate blocking I/O that consumes thread pool
                    Thread.Sleep(50); // 50ms blocking operation
                    Interlocked.Increment(ref completedCount);
                }
            };

            var json = @"{
                id: 'blocking-machine',
                initial: 'active',
                states: {
                    active: {
                        entry: ['blockingOperation'],
                        on: { WORK: 'active' }
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "blocking-machine",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
            await _orchestrator.StartMachineAsync("blocking-machine");

            // Act - Flood with blocking operations
            var sw = Stopwatch.StartNew();
            var tasks = new List<Task>();

            // Send 100 blocking operations (each takes 100ms)
            // With 4 pool threads, this creates realistic thread pool pressure
            for (int i = 0; i < 100; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        Interlocked.Increment(ref queuedCount);
                        await _orchestrator.SendEventFireAndForgetAsync("test", "blocking-machine", "WORK");
                    }
                    catch
                    {
                        Interlocked.Increment(ref rejectedCount);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Wait deterministically for all operations to complete
            await WaitForCountAsync(() => completedCount, targetValue: queuedCount, timeoutSeconds: 15);

            sw.Stop();

            // Assert
            ThreadPool.GetAvailableThreads(out var workerThreads, out var completionPortThreads);
            ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxCompletionPortThreads);

            _output.WriteLine($"Completed: {completedCount}");
            _output.WriteLine($"Queued: {queuedCount}");
            _output.WriteLine($"Rejected: {rejectedCount}");
            _output.WriteLine($"Time: {sw.Elapsed.TotalSeconds:F1}s");
            _output.WriteLine($"Available Threads: {workerThreads}/{maxWorkerThreads}");

            // With deterministic wait, all tasks should complete
            Assert.Equal(100, queuedCount);
            Assert.Equal(100, completedCount);
            Assert.Equal(0, rejectedCount);

            _output.WriteLine($"\n✅ System handled {completedCount} blocking operations gracefully");
            _output.WriteLine($"   Queue depth: {queuedCount}, Rejected: {rejectedCount}");
        }

        [Fact]
        public async Task ResourceLimit_ConnectionPoolExhaustion_ReusesConnections()
        {
            // Arrange - Simulate limited connection pool
            var maxConnections = 10;
            var connectionPool = new SemaphoreSlim(maxConnections, maxConnections);
            var activeConnections = 0;
            var maxActiveConnections = 0;
            var successCount = 0;
            var timeoutCount = 0;

            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 8
            });

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["useConnection"] = (ctx) =>
                {
                    var acquired = connectionPool.Wait(TimeSpan.FromMilliseconds(100));
                    if (!acquired)
                    {
                        Interlocked.Increment(ref timeoutCount);
                        throw new TimeoutException("Connection pool exhausted");
                    }

                    try
                    {
                        var current = Interlocked.Increment(ref activeConnections);
                        if (current > maxActiveConnections)
                            maxActiveConnections = current;

                        // Simulate connection usage
                        Thread.Sleep(50);
                        Interlocked.Increment(ref successCount);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref activeConnections);
                        connectionPool.Release();
                    }
                }
            };

            var json = @"{
                id: 'connection-user',
                initial: 'active',
                states: {
                    active: {
                        entry: ['useConnection'],
                        on: { CONNECT: 'active' }
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "connection-user",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
            await _orchestrator.StartMachineAsync("connection-user");

            // Act - Attempt many concurrent connections
            var tasks = new List<Task>();
            for (int i = 0; i < 100; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await _orchestrator.SendEventFireAndForgetAsync("test", "connection-user", "CONNECT");
                    }
                    catch
                    {
                        // Expected timeouts
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Wait deterministically for all operations to complete
            await WaitForCountAsync(() => successCount + timeoutCount, targetValue: 100, timeoutSeconds: 10);

            // Assert
            var totalProcessed = successCount + timeoutCount;

            _output.WriteLine($"Max Connections: {maxConnections}");
            _output.WriteLine($"Max Active: {maxActiveConnections}");
            _output.WriteLine($"Success: {successCount}");
            _output.WriteLine($"Timeouts: {timeoutCount}");
            _output.WriteLine($"Total Processed: {totalProcessed}");

            Assert.True(maxActiveConnections <= maxConnections,
                $"Should not exceed connection pool limit (max active: {maxActiveConnections}/{maxConnections})");

            // With 100ms timeout per connection attempt, some will timeout but most should succeed
            // The semaphore allows 10 concurrent connections, so operations should succeed
            Assert.True(totalProcessed >= 90,
                $"Should process most requests (processed {totalProcessed}/100)");
            Assert.True(successCount >= 80,
                $"Should successfully process most requests despite connection limits (success {successCount}, timeout {timeoutCount})");
        }

        [Fact]
        public async Task ResourceLimit_MemoryPressure_TriggersGC()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 4
            });

            var allocations = new ConcurrentBag<byte[]>();
            var processedCount = 0;
            var gcCount = GC.CollectionCount(0);

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["allocateMemory"] = (ctx) =>
                {
                    // Allocate 10MB per operation
                    var largeArray = new byte[10 * 1024 * 1024];
                    Random.Shared.NextBytes(largeArray);

                    // Keep reference temporarily
                    allocations.Add(largeArray);

                    // Release some to avoid OOM
                    if (allocations.Count > 20)
                    {
                        allocations.TryTake(out _);
                    }

                    Interlocked.Increment(ref processedCount);
                }
            };

            var json = @"{
                id: 'memory-intensive',
                initial: 'active',
                states: {
                    active: {
                        entry: ['allocateMemory'],
                        on: { ALLOCATE: 'active' }
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "memory-intensive",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
            await _orchestrator.StartMachineAsync("memory-intensive");

            // Record initial state
            var initialMemory = GC.GetTotalMemory(false);

            // Act - Allocate lots of memory
            for (int i = 0; i < 50; i++)
            {
                await _orchestrator.SendEventFireAndForgetAsync("test", "memory-intensive", "ALLOCATE");
                await Task.Yield();
            }

            await WaitUntilQuiescentAsync(() => processedCount, noProgressTimeoutMs: 1000, maxWaitSeconds: 3);

            var currentMemory = GC.GetTotalMemory(false);
            var gcCountAfter = GC.CollectionCount(0);
            var gcTriggered = gcCountAfter - gcCount;

            // Assert
            var memoryUsedMB = (currentMemory - initialMemory) / (1024.0 * 1024.0);
            _output.WriteLine($"Processed: {processedCount}");
            _output.WriteLine($"Memory Used: {memoryUsedMB:F2} MB");
            _output.WriteLine($"GC Collections: {gcTriggered}");
            _output.WriteLine($"Allocations Held: {allocations.Count}");

            Assert.True(processedCount > 40, "Should process most allocations");
            Assert.True(gcTriggered > 0, "GC should have been triggered under memory pressure");
        }

        [Fact]
        public async Task ResourceLimit_BoundedChannel_Backpressure()
        {
            // Arrange
            var channelCapacity = 100;
            var channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

            var producedCount = 0;
            var consumedCount = 0;
            var backpressureHits = 0;

            // Consumer (slow)
            var consumer = Task.Run(async () =>
            {
                await foreach (var item in channel.Reader.ReadAllAsync())
                {
                    await Task.Delay(20); // Slow consumer
                    Interlocked.Increment(ref consumedCount);
                }
            });

            // Act - Fast producer
            var sw = Stopwatch.StartNew();
            var producer = Task.Run(async () =>
            {
                for (int i = 0; i < 500; i++)
                {
                    var writeTask = channel.Writer.WriteAsync(new WorkItem { Id = i });

                    if (!writeTask.IsCompleted)
                    {
                        Interlocked.Increment(ref backpressureHits);
                    }

                    await writeTask;
                    Interlocked.Increment(ref producedCount);
                }
                channel.Writer.Complete();
            });

            await producer;
            await consumer;
            sw.Stop();

            // Assert
            _output.WriteLine($"Produced: {producedCount}");
            _output.WriteLine($"Consumed: {consumedCount}");
            _output.WriteLine($"Backpressure Hits: {backpressureHits}");
            _output.WriteLine($"Time: {sw.Elapsed.TotalSeconds:F1}s");

            Assert.Equal(500, producedCount);
            Assert.Equal(500, consumedCount);
            Assert.True(backpressureHits > 300, "Should experience significant backpressure");
        }

        [Fact]
        public async Task ResourceLimit_FileHandleExhaustion_GracefulHandling()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), $"xstatenet-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var fileHandles = new ConcurrentBag<FileStream>();
                var successCount = 0;
                var failureCount = 0;
                var maxHandles = 0;

                // Act - Try to open many file handles
                var tasks = new List<Task>();
                for (int i = 0; i < 1000; i++)
                {
                    var fileIndex = i;
                    tasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            var filePath = Path.Combine(tempDir, $"file-{fileIndex}.tmp");
                            var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                            fileHandles.Add(fs);

                            var current = fileHandles.Count;
                            if (current > maxHandles)
                                maxHandles = current;

                            Interlocked.Increment(ref successCount);

                            // Write some data
                            var data = new byte[1024];
                            fs.Write(data, 0, data.Length);
                        }
                        catch (IOException)
                        {
                            Interlocked.Increment(ref failureCount);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            Interlocked.Increment(ref failureCount);
                        }
                    }));

                    if (tasks.Count >= 50)
                    {
                        await Task.WhenAll(tasks);
                        tasks.Clear();
                    }
                }

                await Task.WhenAll(tasks);

                // Cleanup
                while (fileHandles.TryTake(out var fs))
                {
                    try { fs.Dispose(); } catch { }
                }

                // Assert
                _output.WriteLine($"File Handles Created: {successCount}");
                _output.WriteLine($"Failures: {failureCount}");
                _output.WriteLine($"Max Concurrent: {maxHandles}");

                Assert.True(successCount > 100, "Should create many file handles");
            }
            finally
            {
                // Cleanup
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public async Task ResourceLimit_SemaphoreSlim_MaxConcurrency()
        {
            // Arrange
            var maxConcurrency = 5;
            var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var currentConcurrency = 0;
            var maxObservedConcurrency = 0;
            var completedCount = 0;

            // Act - Run many concurrent operations with semaphore limiting
            var tasks = new List<Task>();
            for (int i = 0; i < 100; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var current = Interlocked.Increment(ref currentConcurrency);

                        // Track max concurrency
                        lock (semaphore)
                        {
                            if (current > maxObservedConcurrency)
                                maxObservedConcurrency = current;
                        }

                        // Simulate work
                        await Task.Delay(50);

                        Interlocked.Increment(ref completedCount);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref currentConcurrency);
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            _output.WriteLine($"Max Concurrency Limit: {maxConcurrency}");
            _output.WriteLine($"Max Observed: {maxObservedConcurrency}");
            _output.WriteLine($"Completed: {completedCount}");

            Assert.Equal(100, completedCount);
            Assert.True(maxObservedConcurrency <= maxConcurrency,
                $"Observed concurrency ({maxObservedConcurrency}) exceeded limit ({maxConcurrency})");
        }

        [Fact]
        public async Task ResourceLimit_ConcurrentDictionary_HighContention()
        {
            // Arrange
            var dict = new ConcurrentDictionary<string, int>();
            var operationCount = 0;

            // Act - High contention on same keys
            var tasks = new List<Task>();
            for (int thread = 0; thread < 50; thread++)
            {
                tasks.Add(Task.Run(() =>
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        var key = $"key-{i % 10}"; // Only 10 keys, high contention

                        // AddOrUpdate
                        dict.AddOrUpdate(key, 1, (k, v) => v + 1);

                        // TryGetValue
                        dict.TryGetValue(key, out var _);

                        // TryUpdate
                        if (dict.TryGetValue(key, out var value))
                        {
                            dict.TryUpdate(key, value + 1, value);
                        }

                        Interlocked.Increment(ref operationCount);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            var totalValue = dict.Values.Sum();
            _output.WriteLine($"Operations: {operationCount:N0}");
            _output.WriteLine($"Unique Keys: {dict.Count}");
            _output.WriteLine($"Total Value: {totalValue:N0}");

            Assert.Equal(50 * 1000, operationCount);
            Assert.True(dict.Count <= 10, "Should have at most 10 keys");
            Assert.True(totalValue > 50000, "All increments should have been recorded");
        }

        public override void Dispose()
        {
            _orchestrator?.Dispose();
            _loggerFactory?.Dispose();
        }

        private class WorkItem
        {
            public int Id { get; set; }
        }
    }
}
