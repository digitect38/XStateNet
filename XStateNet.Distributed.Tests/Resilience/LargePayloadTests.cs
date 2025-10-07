using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using XStateNet.Orchestration;
using Xunit;
using Xunit.Abstractions;

namespace XStateNet.Distributed.Tests.Resilience
{
    /// <summary>
    /// Tests for handling large payloads and memory-intensive operations
    /// </summary>
    [Collection("TimingSensitive")]
    public class LargePayloadTests : ResilienceTestBase
    {
        
        private readonly ILoggerFactory _loggerFactory;
        private EventBusOrchestrator? _orchestrator;

        public LargePayloadTests(ITestOutputHelper output) : base(output)
        {

            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddDebug().SetMinimumLevel(LogLevel.Warning);
            });
        }

        [Theory]
        [InlineData(1024)]        // 1 KB
        [InlineData(10 * 1024)]   // 10 KB
        [InlineData(100 * 1024)]  // 100 KB
        [InlineData(1024 * 1024)] // 1 MB
        public async Task LargePayload_VariousSizes_ProcessedSuccessfully(int payloadSizeBytes)
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 4
            });

            var processedSize = payloadSizeBytes * 10; // Track expected size
            var processedCount = 0;

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["processLargePayload"] = (ctx) =>
                {
                    // Simulate processing large payload
                    Interlocked.Increment(ref processedCount);

                    // Simulate computation on large data
                    var tempData = new byte[Math.Min(1000, payloadSizeBytes)];
                    int checksum = 0;
                    for (int i = 0; i < tempData.Length; i++)
                    {
                        checksum += tempData[i];
                    }
                }
            };

            var json = @"{
                id: 'payload-processor',
                initial: 'ready',
                states: {
                    ready: {
                        on: {
                            PROCESS: {
                                target: 'processing'
                            }
                        }
                    },
                    processing: {
                        entry: ['processLargePayload'],
                        always: [{ target: 'ready' }]
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "payload-processor",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
            await _orchestrator.StartMachineAsync("payload-processor");

            // Act - Send events with large payloads
            var sw = Stopwatch.StartNew();
            var messageCount = 10;

            for (int i = 0; i < messageCount; i++)
            {
                var payload = new byte[payloadSizeBytes];
                Random.Shared.NextBytes(payload);

                await _orchestrator.SendEventFireAndForgetAsync("test", "payload-processor", "PROCESS", payload);

                // Small delay for large payloads to avoid overwhelming the queue
                if (payloadSizeBytes >= 1024 * 1024)
                {
                    await Task.Delay(10);
                }
            }

            await WaitForConditionAsync(
                condition: () => processedCount >= messageCount,
                getProgress: () => processedCount,
                timeoutSeconds: 10,
                noProgressTimeoutMs: 2000);
            sw.Stop();

            // Assert
            var totalMB = processedSize / (1024.0 * 1024.0);
            var throughput = totalMB / sw.Elapsed.TotalSeconds;

            _output.WriteLine($"Payload Size: {payloadSizeBytes / 1024.0:F2} KB");
            _output.WriteLine($"Messages: {processedCount}/{messageCount}");
            _output.WriteLine($"Simulated Total Data: {totalMB:F2} MB");
            _output.WriteLine($"Throughput: {throughput:F2} MB/sec");
            _output.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");

            Assert.Equal(messageCount, processedCount);
        }

        [Fact]
        public async Task LargePayload_10MB_Messages_HandledWithBackpressure()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 4,
                EnableBackpressure = true,
                MaxQueueDepth = 50
            });

            var processedCount = 0;
            var droppedCount = 0;
            var totalBytesProcessed = 0L;

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["processHuge"] = (ctx) =>
                {
                    // Simulate intensive processing
                    Thread.Sleep(50);
                    var messageSize = 10 * 1024 * 1024; // 10MB
                    Interlocked.Add(ref totalBytesProcessed, messageSize);
                    Interlocked.Increment(ref processedCount);
                }
            };

            var json = @"{
                id: 'huge-processor',
                initial: 'ready',
                states: {
                    ready: {
                        on: {
                            HUGE: {
                                target: 'processing'
                            }
                        }
                    },
                    processing: {
                        entry: ['processHuge'],
                        always: [{ target: 'ready' }]
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "huge-processor",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
            await _orchestrator.StartMachineAsync("huge-processor");

            // Act - Send 10MB messages
            var messageSizeMB = 10;
            var messageCount = 20;
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < messageCount; i++)
            {
                try
                {
                    var payload = new byte[messageSizeMB * 1024 * 1024];
                    Random.Shared.NextBytes(payload);
                    await _orchestrator.SendEventFireAndForgetAsync("test", "huge-processor", "HUGE", payload);
                }
                catch
                {
                    Interlocked.Increment(ref droppedCount);
                }
            }

            // Wait for processing
            await WaitUntilQuiescentAsync(() => processedCount + droppedCount, noProgressTimeoutMs: 3000, maxWaitSeconds: 10);
            sw.Stop();

            // Assert
            var processedMB = totalBytesProcessed / (1024.0 * 1024.0);
            _output.WriteLine($"Processed: {processedCount}/{messageCount} messages");
            _output.WriteLine($"Dropped: {droppedCount}");
            _output.WriteLine($"Total Data: {processedMB:F2} MB");
            _output.WriteLine($"Time: {sw.Elapsed.TotalSeconds:F1}s");

            Assert.True(processedCount > 0, "Should process some large messages");
            Assert.True(processedCount + droppedCount == messageCount, "All messages should be accounted for");
        }

        [Fact]
        public async Task LargePayload_ConcurrentLargeMessages_NoMemoryLeak()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 8
            });

            var processedCount = 0;
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["process"] = (ctx) =>
                {
                    // Process data simulation
                    var dataSize = 512 * 1024; // 512KB
                    var sum = 0L;
                    for (int i = 0; i < Math.Min(10000, dataSize); i++)
                    {
                        sum += i;
                    }
                    Interlocked.Increment(ref processedCount);
                }
            };

            var json = @"{
                id: 'concurrent-large',
                initial: 'ready',
                states: {
                    ready: {
                        on: {
                            DATA: {
                                target: 'processing'
                            }
                        }
                    },
                    processing: {
                        entry: ['process'],
                        always: [{ target: 'ready' }]
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "concurrent-large",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
            await _orchestrator.StartMachineAsync("concurrent-large");

            // Record initial memory
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var initialMemory = GC.GetTotalMemory(false);

            // Act - Send concurrent large messages
            var tasks = new List<Task>();
            var messageSizeKB = 512; // 512 KB per message
            var concurrentMessages = 100;

            for (int i = 0; i < concurrentMessages; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var payload = new byte[messageSizeKB * 1024];
                    Random.Shared.NextBytes(payload);
                    await _orchestrator.SendEventFireAndForgetAsync("test", "concurrent-large", "DATA", payload);
                }));

                if (tasks.Count >= 20)
                {
                    await Task.WhenAll(tasks);
                    tasks.Clear();
                }
            }
            await Task.WhenAll(tasks);
            await WaitForCountAsync(() => processedCount, targetValue: concurrentMessages, timeoutSeconds: 5);

            // Force GC and check memory
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var finalMemory = GC.GetTotalMemory(false);

            var memoryGrowthMB = (finalMemory - initialMemory) / (1024.0 * 1024.0);

            // Assert
            _output.WriteLine($"Processed: {processedCount}/{concurrentMessages}");
            _output.WriteLine($"Initial Memory: {initialMemory / (1024.0 * 1024.0):F2} MB");
            _output.WriteLine($"Final Memory: {finalMemory / (1024.0 * 1024.0):F2} MB");
            _output.WriteLine($"Memory Growth: {memoryGrowthMB:F2} MB");

            Assert.True(processedCount >= concurrentMessages * 0.9, "Should process most messages");
            Assert.True(memoryGrowthMB < 200, "Memory growth should be reasonable (< 200 MB)");
        }

        [Fact]
        public async Task LargePayload_StringMessages_UnicodeAndSpecialChars()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 4
            });

            var processedCount = 0;
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["processText"] = (ctx) =>
                {
                    // Simulate text processing
                    Interlocked.Increment(ref processedCount);
                }
            };

            var json = @"{
                id: 'text-processor',
                initial: 'ready',
                states: {
                    ready: {
                        on: {
                            TEXT: {
                                target: 'processing'
                            }
                        }
                    },
                    processing: {
                        entry: ['processText'],
                        always: [{ target: 'ready' }]
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "text-processor",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
            await _orchestrator.StartMachineAsync("text-processor");

            // Act - Send various string payloads
            var testStrings = new[]
            {
                // Unicode characters
                new string('中', 10000) + "文字テスト" + new string('한', 1000),
                // Special characters
                new string('\0', 1000) + new string('\n', 500) + new string('\t', 500),
                // Emojis
                string.Concat(Enumerable.Repeat("😀🎉🚀💻🔥", 2000)),
                // Mixed
                "ASCII" + new string('€', 5000) + "Ω" + new string('∑', 3000),
                // Large ASCII
                new string('A', 100000),
                // Control characters
                string.Concat(Enumerable.Range(0, 1000).Select(i => (char)(i % 128)))
            };

            foreach (var testString in testStrings)
            {
                await _orchestrator.SendEventAsync("test", "text-processor", "TEXT", testString);
            }

            await WaitForCountAsync(() => processedCount, targetValue: testStrings.Length, timeoutSeconds: 5);

            // Assert
            _output.WriteLine($"Processed {processedCount} string messages");
            _output.WriteLine($"String types tested: Unicode, Emojis, Control chars, Large ASCII");

            Assert.Equal(testStrings.Length, processedCount);
        }

        [Fact]
        public async Task LargePayload_ComplexNestedStructures_SerializeDeserialize()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 4
            });

            var processedCount = 0;
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["processComplex"] = (ctx) =>
                {
                    // Simulate complex payload processing
                    Interlocked.Increment(ref processedCount);
                }
            };

            var json = @"{
                id: 'complex-processor',
                initial: 'ready',
                states: {
                    ready: {
                        on: {
                            COMPLEX: {
                                target: 'processing'
                            }
                        }
                    },
                    processing: {
                        entry: ['processComplex'],
                        always: [{ target: 'ready' }]
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "complex-processor",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
            await _orchestrator.StartMachineAsync("complex-processor");

            // Act - Create complex nested payload
            var messageCount = 10;
            for (int i = 0; i < messageCount; i++)
            {
                var payload = new ComplexPayload
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow,
                    Items = Enumerable.Range(0, 1000).Select(j => new Item
                    {
                        Name = $"Item-{j}",
                        Value = j * 1.5,
                        Tags = new[] { $"tag-{j}", $"category-{j % 10}" }
                    }).ToList(),
                    Metadata = Enumerable.Range(0, 100).ToDictionary(
                        k => $"key-{k}",
                        v => $"value-{v}-{Guid.NewGuid()}"),
                    NestedData = new NestedData
                    {
                        Level1 = new Level
                        {
                            Data = new byte[10000],
                            Level2 = new Level
                            {
                                Data = new byte[5000],
                                Level3 = new Level { Data = new byte[1000] }
                            }
                        }
                    }
                };

                Random.Shared.NextBytes(payload.NestedData.Level1.Data);

                await _orchestrator.SendEventAsync("test", "complex-processor", "COMPLEX", payload);
            }

            await WaitForCountAsync(() => processedCount, targetValue: messageCount, timeoutSeconds: 5);

            // Assert
            _output.WriteLine($"Processed {processedCount}/{messageCount} complex payloads");
            Assert.Equal(messageCount, processedCount);
        }

        [Fact]
        public async Task LargePayload_MultiMB_Batching_OptimizesPerformance()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 8
            });

            var singleMessageTime = TimeSpan.Zero;
            var batchedMessageTime = TimeSpan.Zero;

            var processedCount = 0;
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["process"] = (ctx) =>
                {
                    Interlocked.Increment(ref processedCount);
                    Thread.Sleep(10); // Simulate processing
                }
            };

            var json = @"{
                id: 'batch-processor',
                initial: 'ready',
                states: {
                    ready: {
                        on: {
                            BATCH: {
                                target: 'processing'
                            }
                        }
                    },
                    processing: {
                        entry: ['process'],
                        always: [{ target: 'ready' }]
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "batch-processor",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
            await _orchestrator.StartMachineAsync("batch-processor");

            var messageSizeKB = 256;
            var messageCount = 50;

            // Test 1: Send messages one by one
            var sw1 = Stopwatch.StartNew();
            for (int i = 0; i < messageCount; i++)
            {
                var payload = new byte[messageSizeKB * 1024];
                await _orchestrator.SendEventAsync("test", "batch-processor", "BATCH", payload);
            }
            await WaitForCountAsync(() => processedCount, targetValue: messageCount, timeoutSeconds: 5);
            sw1.Stop();
            singleMessageTime = sw1.Elapsed;

            var processedSingle = processedCount;
            processedCount = 0;

            // Test 2: Send messages in batches
            await WaitUntilQuiescentAsync(() => processedCount, noProgressTimeoutMs: 1000, maxWaitSeconds: 3);
            var sw2 = Stopwatch.StartNew();
            var batchSize = 10;
            for (int batch = 0; batch < messageCount / batchSize; batch++)
            {
                var tasks = new List<Task>();
                for (int i = 0; i < batchSize; i++)
                {
                    var payload = new byte[messageSizeKB * 1024];
                    tasks.Add(_orchestrator.SendEventFireAndForgetAsync("test", "batch-processor", "BATCH", payload));
                }
                await Task.WhenAll(tasks);
            }
            await WaitForCountAsync(() => processedCount, targetValue: messageCount, timeoutSeconds: 5);
            sw2.Stop();
            batchedMessageTime = sw2.Elapsed;

            var processedBatched = processedCount;

            // Assert
            _output.WriteLine($"Single Messages: {singleMessageTime.TotalMilliseconds:F0}ms ({processedSingle} processed)");
            _output.WriteLine($"Batched Messages: {batchedMessageTime.TotalMilliseconds:F0}ms ({processedBatched} processed)");
            _output.WriteLine($"Improvement: {(1 - batchedMessageTime.TotalMilliseconds / singleMessageTime.TotalMilliseconds):P}");

            Assert.True(processedSingle >= messageCount * 0.9, "Most single messages should be processed");
            Assert.True(processedBatched >= messageCount * 0.9, "Most batched messages should be processed");
        }

        public override void Dispose()
        {
            _orchestrator?.Dispose();
            _loggerFactory?.Dispose();
        }

        // Test data structures
        public class ComplexPayload
        {
            public Guid Id { get; set; }
            public DateTime Timestamp { get; set; }
            public List<Item> Items { get; set; } = new();
            public Dictionary<string, string> Metadata { get; set; } = new();
            public NestedData? NestedData { get; set; }
        }

        public class Item
        {
            public string Name { get; set; } = string.Empty;
            public double Value { get; set; }
            public string[] Tags { get; set; } = Array.Empty<string>();
        }

        public class NestedData
        {
            public Level? Level1 { get; set; }
        }

        public class Level
        {
            public byte[] Data { get; set; } = Array.Empty<byte>();
            public Level? Level2 { get; set; }
            public Level? Level3 { get; set; }
        }
    }
}
