using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using XStateNet.Orchestration;
using Xunit;
using Xunit.Abstractions;

namespace XStateNet.Distributed.Tests.Resilience
{
    /// <summary>
    /// Network chaos tests simulating real-world network failures
    /// </summary>
    [Collection("TimingSensitive")]
    public class NetworkChaosTests : ResilienceTestBase
    {
        
        private readonly ILoggerFactory _loggerFactory;
        private EventBusOrchestrator? _orchestrator;

        public NetworkChaosTests(ITestOutputHelper output) : base(output)
        {

            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddDebug().SetMinimumLevel(LogLevel.Debug);
            });
        }

        [Fact]
        public async Task NetworkChaos_RandomDisconnects_SystemRecovers()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 4
            });

            var processedCount = 0;
            var failedCount = 0;
            var reconnectCount = 0;
            var random = new Random(42);

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["process"] = (ctx) =>
                {
                    // Simulate random network failure (20% chance)
                    if (random.Next(100) < 20)
                    {
                        Interlocked.Increment(ref failedCount);
                        throw new SocketException(10054); // Connection reset by peer
                    }
                    Interlocked.Increment(ref processedCount);
                }
            };

            var json = @"{
                id: 'network-node',
                initial: 'connected',
                states: {
                    connected: {
                        entry: ['process'],
                        on: {
                            PROCESS: 'connected',
                            DISCONNECT: 'disconnected'
                        }
                    },
                    disconnected: {
                        on: { RECONNECT: 'connected' }
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "network-node",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null,
                services: null,
                delays: null,
                activities: null,
                enableGuidIsolation: false);
            await _orchestrator.StartMachineAsync(machine.Id);

            // Act - Send events with random disconnects
            for (int i = 0; i < 500; i++)
            {
                try
                {
                    await _orchestrator.SendEventFireAndForgetAsync("test", "network-node", "PROCESS");

                    // Random disconnect simulation
                    if (random.Next(100) < 10) // 10% chance of disconnect
                    {
                        await _orchestrator.SendEventAsync("test", "network-node", "DISCONNECT");
                        await Task.Delay(random.Next(10, 50)); // Network recovery delay
                        await _orchestrator.SendEventAsync("test", "network-node", "RECONNECT");
                        Interlocked.Increment(ref reconnectCount);
                    }
                }
                catch
                {
                    // Expected network failures
                }
            }

            // Wait deterministically for all events to process
            await WaitForCountAsync(() => processedCount, targetValue: 400, timeoutSeconds: 5);

            // Assert
            _output.WriteLine($"Processed: {processedCount}, Failed: {failedCount}, Reconnects: {reconnectCount}");

            // With 500 events and 10% disconnect rate, expect most events to succeed
            Assert.True(processedCount >= 400,
                $"System should process most messages despite network chaos (processed {processedCount}/500)");
            Assert.True(reconnectCount > 0, "Should have experienced disconnects");
        }

        [Fact]
        public async Task NetworkChaos_LatencyInjection_MaintainsThroughput()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 8
            });

            var processedCount = 0;
            var latencies = new ConcurrentBag<long>();
            var random = new Random(42);

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["processWithLatency"] = (ctx) =>
                {
                    var sw = Stopwatch.StartNew();

                    // Inject random network latency (0-100ms)
                    var latencyMs = random.Next(0, 100);
                    if (latencyMs > 50)
                    {
                        // Simulate high latency
                        Thread.Sleep(latencyMs);
                    }

                    sw.Stop();
                    latencies.Add(sw.ElapsedMilliseconds);
                    Interlocked.Increment(ref processedCount);
                }
            };

            var json = @"{
                id: 'latency-node',
                initial: 'active',
                states: {
                    active: {
                        entry: ['processWithLatency'],
                        on: { TICK: 'active' }
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "latency-node",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null,
                services: null,
                delays: null,
                activities: null,
                enableGuidIsolation: false);
            await _orchestrator.StartMachineAsync("latency-node");

            var sw = Stopwatch.StartNew();

            // Act - Send events with latency
            var tasks = new List<Task>();
            for (int i = 0; i < 200; i++)
            {
                tasks.Add(_orchestrator.SendEventFireAndForgetAsync("test", "latency-node", "TICK"));
                if (tasks.Count >= 20)
                {
                    await Task.WhenAll(tasks);
                    tasks.Clear();
                }
            }
            await Task.WhenAll(tasks);
            await WaitUntilQuiescentAsync(() => processedCount, noProgressTimeoutMs: 2000, maxWaitSeconds: 5);

            sw.Stop();

            // Assert
            var avgLatency = latencies.Any() ? latencies.Average() : 0;
            var p95Latency = latencies.Any() ? latencies.OrderBy(l => l).ElementAt((int)(latencies.Count * 0.95)) : 0;
            var throughput = processedCount / (sw.ElapsedMilliseconds / 1000.0);

            _output.WriteLine($"Processed: {processedCount}, Avg Latency: {avgLatency:F2}ms, P95: {p95Latency}ms");
            _output.WriteLine($"Throughput: {throughput:F2} ops/sec in {sw.ElapsedMilliseconds}ms");

            Assert.True(processedCount >= 180, "Should maintain throughput despite latency");
            Assert.True(avgLatency < 60, "Average latency should be reasonable");
        }

        [Fact]
        public async Task NetworkChaos_PacketLoss_RetriesSucceed()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 4
            });

            var attemptCount = 0;
            var successCount = 0;
            var random = new Random(42);

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["sendWithPacketLoss"] = (ctx) =>
                {
                    Interlocked.Increment(ref attemptCount);

                    // Simulate 30% packet loss
                    if (random.Next(100) < 30)
                    {
                        throw new TimeoutException("Packet lost");
                    }

                    Interlocked.Increment(ref successCount);
                }
            };

            var json = @"{
                id: 'packet-node',
                initial: 'sending',
                states: {
                    sending: {
                        entry: ['sendWithPacketLoss'],
                        on: { RETRY: 'sending', SUCCESS: 'done' }
                    },
                    done: {}
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "packet-node",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null,
                services: null,
                delays: null,
                activities: null,
                enableGuidIsolation: false);
            await _orchestrator.StartMachineAsync("packet-node");

            // Act - Send with retries on packet loss
            var targetSuccesses = 100;
            var retries = 0;

            while (successCount < targetSuccesses && retries < 500)
            {
                try
                {
                    await _orchestrator.SendEventFireAndForgetAsync("test", "packet-node", "RETRY");
                    retries++;
                    await Task.Yield();
                }
                catch (TimeoutException)
                {
                    // Retry on packet loss
                }
            }

            await WaitUntilQuiescentAsync(() => successCount, noProgressTimeoutMs: 500, maxWaitSeconds: 2);

            // Assert
            var lossRate = (attemptCount - successCount) / (double)attemptCount;
            _output.WriteLine($"Attempts: {attemptCount}, Success: {successCount}, Loss Rate: {lossRate:P}");
            Assert.True(successCount >= 90, "Should achieve high success rate with retries");
            Assert.True(lossRate > 0.2 && lossRate < 0.4, "Should experience packet loss in expected range");
        }

        [Fact]
        public async Task NetworkChaos_PartitionedNetwork_IsolatesNodes()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 4
            });

            var partition1Count = 0;
            var partition2Count = 0;
            var crossPartitionFailed = 0;
            var isPartitioned = false;

            // Create two partitions
            var partition1Actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["process"] = (ctx) =>
                {
                    Interlocked.Increment(ref partition1Count);
                },
                ["sendToPartition2"] = (ctx) =>
                {
                    if (isPartitioned)
                    {
                        Interlocked.Increment(ref crossPartitionFailed);
                        throw new SocketException(10060); // Connection timeout
                    }
                    ctx.RequestSend("partition2", "RECEIVE");
                }
            };

            var partition2Actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["process"] = (ctx) =>
                {
                    Interlocked.Increment(ref partition2Count);
                }
            };

            var p1Json = @"{
                id: 'partition1',
                initial: 'active',
                states: {
                    active: {
                        entry: ['process'],
                        on: {
                            TICK: 'active',
                            SEND: { target: 'active', actions: ['sendToPartition2'] }
                        }
                    }
                }
            }";

            var p2Json = @"{
                id: 'partition2',
                initial: 'active',
                states: {
                    active: {
                        entry: ['process'],
                        on: { RECEIVE: 'active' }
                    }
                }
            }";

            var p1 = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "partition1",
                json: p1Json,
                orchestrator: _orchestrator,
                orchestratedActions: partition1Actions,
                guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);

            var p2 = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "partition2",
                json: p2Json,
                orchestrator: _orchestrator,
                orchestratedActions: partition2Actions,
                guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);

            await Task.WhenAll(
                _orchestrator.StartMachineAsync(p1.Id),
                _orchestrator.StartMachineAsync(p2.Id)
            );

            // Act - Normal communication (before partition)
            for (int i = 0; i < 50; i++)
            {
                await _orchestrator.SendEventFireAndForgetAsync("test", p1.Id, "SEND");
            }

            // Wait deterministically for pre-partition communication
            await WaitForCountAsync(() => partition2Count, targetValue: 45, timeoutSeconds: 3);

            var p2CountBeforePartition = partition2Count;
            _output.WriteLine($"Before partition - P2 received: {p2CountBeforePartition}/50");

            // Create network partition
            isPartitioned = true;
            var failureCountBefore = crossPartitionFailed;

            // Try to communicate across partition
            for (int i = 0; i < 50; i++)
            {
                try
                {
                    await _orchestrator.SendEventFireAndForgetAsync("test", p1.Id, "SEND");
                }
                catch
                {
                    // Expected failures
                }
            }

            // Wait for partition failure detection
            await WaitForCountAsync(() => crossPartitionFailed, targetValue: failureCountBefore + 45, timeoutSeconds: 3);

            var failuresDuringPartition = crossPartitionFailed - failureCountBefore;

            // Assert
            _output.WriteLine($"P1 Events: {partition1Count}, P2 Events: {partition2Count}");
            _output.WriteLine($"Cross-partition failures during partition: {failuresDuringPartition}");

            Assert.True(p2CountBeforePartition >= 45,
                $"Should communicate successfully before partition (got {p2CountBeforePartition}/50)");
            Assert.True(failuresDuringPartition >= 45,
                $"Should fail during partition (got {failuresDuringPartition}/50 failures)");
        }

        [Fact]
        public async Task NetworkChaos_ConcurrentConnectionFailures_GracefulDegradation()
        {
            // Arrange
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 8
            });

            var successCount = 0;
            var connectionErrors = 0;
            var timeoutErrors = 0;
            var random = new Random(42);

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["unreliableOperation"] = (ctx) =>
                {
                    var errorType = random.Next(100);

                    if (errorType < 15) // 15% connection errors
                    {
                        Interlocked.Increment(ref connectionErrors);
                        throw new SocketException(10054);
                    }
                    else if (errorType < 25) // 10% timeout errors
                    {
                        Interlocked.Increment(ref timeoutErrors);
                        throw new TimeoutException("Network timeout");
                    }

                    Interlocked.Increment(ref successCount);
                }
            };

            var json = @"{
                id: 'unreliable-node',
                initial: 'active',
                states: {
                    active: {
                        entry: ['unreliableOperation'],
                        on: { EXECUTE: 'active' }
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "unreliable-node",
                json: json,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
            await _orchestrator.StartMachineAsync("unreliable-node");

            // Act - Concurrent operations with network chaos
            var tasks = new List<Task>();
            for (int i = 0; i < 1000; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await _orchestrator.SendEventFireAndForgetAsync("test", "unreliable-node", "EXECUTE");
                    }
                    catch
                    {
                        // Expected network failures
                    }
                }));

                if (tasks.Count >= 100)
                {
                    await Task.WhenAll(tasks);
                    tasks.Clear();
                }
            }
            await Task.WhenAll(tasks);
            await WaitUntilQuiescentAsync(() => successCount + connectionErrors + timeoutErrors, noProgressTimeoutMs: 1000, maxWaitSeconds: 3);

            // Assert
            var totalAttempts = successCount + connectionErrors + timeoutErrors;
            var successRate = successCount / (double)totalAttempts;

            _output.WriteLine($"Success: {successCount}, Connection Errors: {connectionErrors}, Timeouts: {timeoutErrors}");
            _output.WriteLine($"Success Rate: {successRate:P}");

            Assert.True(successRate > 0.70, "Should maintain >70% success rate under network chaos");
            Assert.True(connectionErrors > 50, "Should experience connection errors");
            Assert.True(timeoutErrors > 30, "Should experience timeouts");
        }

        public override void Dispose()
        {
            _orchestrator?.Dispose();
            _loggerFactory?.Dispose();
        }
    }
}
