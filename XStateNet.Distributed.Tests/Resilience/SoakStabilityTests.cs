using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using XStateNet.Orchestration;
using Xunit;
using Xunit.Abstractions;
#if false
namespace XStateNet.Distributed.Tests.Resilience
{
    /// <summary>
    /// Long-running soak and stability tests simulating production workloads
    /// </summary>
    [Collection("TimingSensitive")]
    public class SoakStabilityTests : ResilienceTestBase
    {
        private readonly ILoggerFactory _loggerFactory;
        private EventBusOrchestrator? _orchestrator;

        public SoakStabilityTests(ITestOutputHelper output) : base(output)
        {
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddDebug().SetMinimumLevel(LogLevel.Warning);
            });
        }

        [Fact]
        public async Task SoakTest_ContinuousLoad_30Seconds_NoMemoryLeak()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 8
            });

            var processedCount = 0;
            var errorCount = 0;
            var memorySnapshots = new ConcurrentBag<long>();

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["process"] = (ctx) =>
                {
                    // Simulate work with some allocations
                    var temp = new byte[1024]; // 1KB allocation per event
                    Array.Fill(temp, (byte)Random.Shared.Next(256));
                    Interlocked.Increment(ref processedCount);
                }
            };

            var json = @"{
                id: 'soak-machine',
                initial: 'idle',
                states: {
                    idle: {
                        on: { TICK: 'processing' }
                    },
                    processing: {
                        entry: ['process'],
                        on: { TICK: 'idle' }
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "soak-machine",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null, services: null, delays: null, activities: null,
                enableGuidIsolation: false);
            await _orchestrator.StartMachineAsync("soak-machine");

            // Record initial memory
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var initialMemory = GC.GetTotalMemory(false);
            memorySnapshots.Add(initialMemory);

            // Act - Run continuous load for 30 seconds
            var sw = Stopwatch.StartNew();
            var duration = TimeSpan.FromSeconds(30);
            var memoryCheckInterval = TimeSpan.FromSeconds(5);
            var lastMemoryCheck = DateTime.UtcNow;

            _output.WriteLine($"Starting 30-second soak test...");

            while (sw.Elapsed < duration)
            {
                try
                {
                    // Send events in bursts
                    var tasks = new List<Task>();
                    for (int i = 0; i < 100; i++)
                    {
                        tasks.Add(_orchestrator.SendEventFireAndForgetAsync("test", "soak-machine", "TICK"));
                    }
                    await Task.WhenAll(tasks);

                    // Check memory periodically
                    if (DateTime.UtcNow - lastMemoryCheck > memoryCheckInterval)
                    {
                        var currentMemory = GC.GetTotalMemory(false);
                        memorySnapshots.Add(currentMemory);
                        _output.WriteLine($"[{sw.Elapsed.TotalSeconds:F1}s] Processed: {processedCount:N0}, Memory: {currentMemory / (1024.0 * 1024.0):F2} MB");
                        lastMemoryCheck = DateTime.UtcNow;
                    }

                    await WaitWithProgressAsync(() => processedCount, minimumWaitMs: 50, additionalQuiescentMs: 100);
                }
                catch
                {
                    Interlocked.Increment(ref errorCount);
                }
            }

            sw.Stop();

            // Final memory check
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var finalMemory = GC.GetTotalMemory(false);
            memorySnapshots.Add(finalMemory);

            // Assert
            var throughput = processedCount / sw.Elapsed.TotalSeconds;
            var memoryGrowth = (finalMemory - initialMemory) / (1024.0 * 1024.0);
            var errorRate = errorCount / (double)processedCount;

            _output.WriteLine($"\n=== Soak Test Results ===");
            _output.WriteLine($"Duration: {sw.Elapsed.TotalSeconds:F1}s");
            _output.WriteLine($"Events Processed: {processedCount:N0}");
            _output.WriteLine($"Throughput: {throughput:F0} events/sec");
            _output.WriteLine($"Errors: {errorCount} ({errorRate:P})");
            _output.WriteLine($"Initial Memory: {initialMemory / (1024.0 * 1024.0):F2} MB");
            _output.WriteLine($"Final Memory: {finalMemory / (1024.0 * 1024.0):F2} MB");
            _output.WriteLine($"Memory Growth: {memoryGrowth:F2} MB");

            Assert.True(processedCount > 10000, "Should process significant events in 30 seconds");
            Assert.True(errorRate < 0.01, "Error rate should be < 1%");
            Assert.True(memoryGrowth < 100, "Memory growth should be < 100 MB (no major leak)");
        }

        [Fact]
        public async Task SoakTest_VaryingLoad_BurstTraffic_StaysStable()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 8,
                EnableBackpressure = true,
                MaxQueueDepth = 10000
            });

            var processedCount = 0;
            var droppedCount = 0;
            var loadLevels = new ConcurrentBag<(DateTime time, int load)>();

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["process"] = (ctx) => Interlocked.Increment(ref processedCount)
            };

            var json = @"{
                id: 'burst-machine',
                initial: 'idle',
                states: {
                    idle: {
                        on: { EVENT: 'active' }
                    },
                    active: {
                        entry: ['process'],
                        on: { EVENT: 'idle' }
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "burst-machine",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null, services: null, delays: null, activities: null,
                enableGuidIsolation: false);
            await _orchestrator.StartMachineAsync("burst-machine");

            // Act - Simulate varying load patterns
            var sw = Stopwatch.StartNew();
            var phases = new[]
            {
                (name: "Warmup", duration: 2, eventsPerSec: 100),
                (name: "Low Load", duration: 3, eventsPerSec: 200),
                (name: "Spike 1", duration: 2, eventsPerSec: 2000),
                (name: "Normal", duration: 3, eventsPerSec: 500),
                (name: "Spike 2", duration: 2, eventsPerSec: 3000),
                (name: "Cooldown", duration: 3, eventsPerSec: 100)
            };

            foreach (var phase in phases)
            {
                _output.WriteLine($"Phase: {phase.name} ({phase.eventsPerSec} events/sec for {phase.duration}s)");
                var phaseStart = DateTime.UtcNow;
                var phaseEnd = phaseStart.AddSeconds(phase.duration);
                var delayMs = 1000 / phase.eventsPerSec;

                while (DateTime.UtcNow < phaseEnd)
                {
                    try
                    {
                        await _orchestrator.SendEventFireAndForgetAsync("test", "burst-machine", "EVENT");
                        loadLevels.Add((DateTime.UtcNow, phase.eventsPerSec));
                    }
                    catch
                    {
                        Interlocked.Increment(ref droppedCount);
                    }

                    if (delayMs > 0)
                        await Task.Delay(delayMs);
                }
            }

            sw.Stop();
            await WaitUntilQuiescentAsync(() => processedCount, noProgressTimeoutMs: 2000, maxWaitSeconds: 5);

            // Assert
            var avgThroughput = processedCount / sw.Elapsed.TotalSeconds;
            var dropRate = droppedCount / (double)(processedCount + droppedCount);

            _output.WriteLine($"\n=== Varying Load Test Results ===");
            _output.WriteLine($"Total Processed: {processedCount:N0}");
            _output.WriteLine($"Dropped: {droppedCount:N0}");
            _output.WriteLine($"Drop Rate: {dropRate:P}");
            _output.WriteLine($"Avg Throughput: {avgThroughput:F0} events/sec");

            Assert.True(processedCount > 5000, "Should process many events across phases");
            Assert.True(dropRate < 0.15, "Drop rate should be < 15% with backpressure");
        }

        [Fact]
        public async Task StabilityTest_MultipleMachines_ContinuousInteraction_15Seconds()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 8
            });

            var machineCount = 50;
            var interactions = 0;
            var errors = new ConcurrentBag<Exception>();

            // Create multiple interacting machines
            for (int i = 0; i < machineCount; i++)
            {
                var machineId = $"machine-{i}";
                var nextId = $"machine-{(i + 1) % machineCount}";

                var actions = new Dictionary<string, Action<OrchestratedContext>>
                {
                    ["interact"] = (ctx) =>
                    {
                        Interlocked.Increment(ref interactions);
                        // Occasionally forward to next machine
                        if (Random.Shared.Next(100) < 20)
                        {
                            ctx.RequestSend(nextId, "FORWARD");
                        }
                    }
                };

                var json = $@"{{
                    id: '{machineId}',
                    initial: 'idle',
                    states: {{
                        idle: {{
                            on: {{
                                TICK: 'active',
                                FORWARD: 'active'
                            }}
                        }},
                        active: {{
                            entry: ['interact'],
                            on: {{
                                TICK: 'idle',
                                FORWARD: 'idle'
                            }}
                        }}
                    }}
                }}";

                var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                    id: machineId,
                    json: json,
                    orchestrator: _orchestrator,
                    orchestratedActions: actions,
                    guards: null, services: null, delays: null, activities: null,
                    enableGuidIsolation: false);
                await _orchestrator.StartMachineAsync(machineId);
            }

            // Act - Run continuous interactions for 15 seconds
            var sw = Stopwatch.StartNew();
            var duration = TimeSpan.FromSeconds(15);
            var random = new Random(42);

            _output.WriteLine($"Starting 15-second stability test with {machineCount} machines...");

            while (sw.Elapsed < duration)
            {
                try
                {
                    // Send events to random machines
                    var tasks = new List<Task>();
                    for (int i = 0; i < 20; i++)
                    {
                        var targetId = $"machine-{random.Next(machineCount)}";
                        tasks.Add(_orchestrator.SendEventFireAndForgetAsync("test", targetId, "TICK"));
                    }
                    await Task.WhenAll(tasks);

                    await WaitWithProgressAsync(() => interactions, minimumWaitMs: 50, additionalQuiescentMs: 100);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }

            sw.Stop();
            await WaitUntilQuiescentAsync(() => interactions, noProgressTimeoutMs: 1000, maxWaitSeconds: 3);

            // Assert
            var interactionsPerSecond = interactions / sw.Elapsed.TotalSeconds;

            _output.WriteLine($"\n=== Stability Test Results ===");
            _output.WriteLine($"Duration: {sw.Elapsed.TotalSeconds:F1}s");
            _output.WriteLine($"Machines: {machineCount}");
            _output.WriteLine($"Total Interactions: {interactions:N0}");
            _output.WriteLine($"Interactions/sec: {interactionsPerSecond:F0}");
            _output.WriteLine($"Errors: {errors.Count}");

            Assert.True(interactions > 2000, "Should have many interactions");
            Assert.True(errors.Count < 50, "Should have minimal errors");
        }

        [Fact]
        public async Task StabilityTest_GradualLoadIncrease_FindsBreakingPoint()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 8,
                EnableBackpressure = true,
                MaxQueueDepth = 5000
            });

            var processedCount = 0;
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["process"] = (ctx) => Interlocked.Increment(ref processedCount)
            };

            var json = @"{
                id: 'load-test',
                initial: 'idle',
                states: {
                    idle: {
                        on: { EVENT: 'active' }
                    },
                    active: {
                        entry: ['process'],
                        on: { EVENT: 'idle' }
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "load-test",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null, services: null, delays: null, activities: null,
                enableGuidIsolation: false);
            await _orchestrator.StartMachineAsync("load-test");

            // Act - Gradually increase load
            var loadResults = new List<(int load, int processed, double latencyMs)>();
            var loadLevels = new[] { 100, 500, 1000, 2000, 3000, 4000, 5000 };

            foreach (var load in loadLevels)
            {
                var phaseStart = processedCount;
                var sw = Stopwatch.StartNew();
                var sent = 0;

                // Run for 2 seconds at this load level
                var endTime = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < endTime)
                {
                    var tasks = new List<Task>();
                    for (int i = 0; i < load / 10; i++) // Send in batches
                    {
                        tasks.Add(_orchestrator.SendEventFireAndForgetAsync("test", "load-test", "EVENT"));
                        sent++;
                    }
                    await Task.WhenAll(tasks);
                    await WaitWithProgressAsync(() => processedCount, minimumWaitMs: 100, additionalQuiescentMs: 200);
                }

                sw.Stop();
                var processed = processedCount - phaseStart;
                var latency = sw.ElapsedMilliseconds / (double)Math.Max(processed, 1);
                loadResults.Add((load, processed, latency));

                _output.WriteLine($"Load: {load} events/sec -> Processed: {processed}, Latency: {latency:F2}ms");

                await WaitUntilQuiescentAsync(() => processedCount, noProgressTimeoutMs: 500, maxWaitSeconds: 2);
            }

            // Assert
            _output.WriteLine($"\n=== Load Increase Results ===");
            foreach (var result in loadResults)
            {
                _output.WriteLine($"Target: {result.load}/s, Actual: {result.processed / 2.5:F0}/s, Latency: {result.latencyMs:F2}ms");
            }

            Assert.True(loadResults.Last().processed > 0, "Should handle even highest load");

            // Check that throughput scales reasonably with load
            // At minimum, highest load should process more than lowest load
            Assert.True(loadResults.Last().processed > loadResults[0].processed,
                "Higher load should result in higher throughput");

            // Check that latency doesn't catastrophically explode (allow up to 100x degradation)
            // Note: Due to warmup/batching effects, latency may actually improve with load
            var maxLatency = loadResults.Max(r => r.latencyMs);
            var minLatency = loadResults.Min(r => r.latencyMs);
            Assert.True(maxLatency < minLatency * 100,
                $"Latency degradation too severe: {maxLatency:F2}ms vs {minLatency:F2}ms");
        }

        [Fact]
        public async Task StabilityTest_LongRunning_WithRecovery_20Seconds()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 4
            });

            var successCount = 0;
            var recoveryCount = 0;
            var failureCount = 0;

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["process"] = (ctx) =>
                {
                    // Simulate intermittent failures (5%)
                    if (Random.Shared.Next(100) < 5)
                    {
                        Interlocked.Increment(ref failureCount);
                        throw new InvalidOperationException("Transient failure");
                    }
                    Interlocked.Increment(ref successCount);
                },
                ["recover"] = (ctx) => Interlocked.Increment(ref recoveryCount)
            };

            var json = @"{
                id: 'resilient-machine',
                initial: 'idle',
                states: {
                    idle: {
                        on: {
                            WORK: 'working',
                            ERROR: 'recovering'
                        }
                    },
                    working: {
                        entry: ['process'],
                        on: {
                            WORK: 'idle',
                            ERROR: 'recovering'
                        }
                    },
                    recovering: {
                        entry: ['recover'],
                        on: { RETRY: 'idle' }
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "resilient-machine",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null, services: null, delays: null, activities: null,
                enableGuidIsolation: false);
            await _orchestrator.StartMachineAsync("resilient-machine");

            // Act - Run for 20 seconds with recovery
            var sw = Stopwatch.StartNew();
            var duration = TimeSpan.FromSeconds(20);

            _output.WriteLine($"Starting 20-second stability test with recovery...");

            while (sw.Elapsed < duration)
            {
                try
                {
                    await _orchestrator.SendEventFireAndForgetAsync("test", "resilient-machine", "WORK");
                }
                catch
                {
                    // Trigger recovery
                    await _orchestrator.SendEventAsync("test", "resilient-machine", "ERROR");
                    await Task.Yield();
                    await _orchestrator.SendEventAsync("test", "resilient-machine", "RETRY");
                }

                await Task.Yield();
            }

            sw.Stop();

            // Assert
            var totalOps = successCount + failureCount;
            var successRate = successCount / (double)totalOps;

            _output.WriteLine($"\n=== Long-Running Stability Results ===");
            _output.WriteLine($"Duration: {sw.Elapsed.TotalSeconds:F1}s");
            _output.WriteLine($"Success: {successCount}");
            _output.WriteLine($"Failures: {failureCount}");
            _output.WriteLine($"Recoveries: {recoveryCount}");
            _output.WriteLine($"Success Rate: {successRate:P}");

            Assert.True(successCount > 1000, "Should process many successful operations");
            Assert.True(successRate > 0.90, "Should maintain >90% success rate with recovery");
        }

        public override void Dispose()
        {
            _orchestrator?.Dispose();
            _loggerFactory?.Dispose();
            base.Dispose();
        }
    }
}
#endif
