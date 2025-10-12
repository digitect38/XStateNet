using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using XStateNet.Orchestration;
using Xunit;
using Xunit.Abstractions;
using CircuitBreakerOpenException = XStateNet.Orchestration.CircuitBreakerOpenException;
#if false
namespace XStateNet.Distributed.Tests.Resilience
{
    /// <summary>
    /// Extreme continuous tests - pushing the system to absolute limits 1000 times
    /// </summary>
    [Collection("TimingSensitive")]
    public class ExtremeContinuousTests : ResilienceTestBase
    {
        
        private readonly ILoggerFactory _loggerFactory;
        private readonly List<EventBusOrchestrator> _orchestrators = new();

        public ExtremeContinuousTests(ITestOutputHelper output) : base(output)
        {

            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddDebug().SetMinimumLevel(LogLevel.Error);
            });
        }

        [Fact]
        public async Task Extreme_ConcurrentMachines_1000Iterations()
        {
            var tracker = new ContinuousTestTracker("ConcurrentMachines", _output);

            for (int iteration = 0; iteration < 1000; iteration++)
            {
                var iterationSw = Stopwatch.StartNew();
                try
                {
                    using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
                    {
                        EnableLogging = false,
                        PoolSize = 8
                    });

                    var machineCount = 100;
                    var processedCount = 0;

                    // Create many concurrent machines
                    var createTasks = new List<Task>();
                    for (int i = 0; i < machineCount; i++)
                    {
                        var machineId = $"m{i}";
                        var actions = new Dictionary<string, Action<OrchestratedContext>>
                        {
                            ["work"] = (ctx) => Interlocked.Increment(ref processedCount)
                        };

                        var json = $@"{{
                            id: '{machineId}',
                            initial: 'ready',
                            states: {{
                                ready: {{
                                    on: {{ WORK: {{ target: 'working' }} }}
                                }},
                                working: {{
                                    entry: ['work'],
                                    always: [{{ target: 'ready' }}]
                                }}
                            }}
                        }}";

                        var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                            id: machineId, json: json, orchestrator: orchestrator,
                            orchestratedActions: actions, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
                        createTasks.Add(orchestrator.StartMachineAsync(machineId));
                    }
                    await Task.WhenAll(createTasks);

                    // Bombard all machines with events
                    var eventTasks = new List<Task>();
                    for (int i = 0; i < machineCount; i++)
                    {
                        for (int j = 0; j < 10; j++)
                        {
                            eventTasks.Add(orchestrator.SendEventFireAndForgetAsync("test", $"m{i}", "WORK"));
                        }
                    }
                    await Task.WhenAll(eventTasks);
                    await WaitForConditionAsync(
                        condition: () => processedCount >= machineCount * 8,
                        getProgress: () => processedCount,
                        timeoutSeconds: 3,
                        noProgressTimeoutMs: 200);

                    iterationSw.Stop();
                    tracker.RecordSuccess(iterationSw.ElapsedMilliseconds);

                    // Validate
                    Assert.True(processedCount >= machineCount * 8, // 80% success rate
                        $"Iteration {iteration}: Only {processedCount}/{machineCount * 10} events processed");
                }
                catch (Exception ex)
                {
                    iterationSw.Stop();
                    tracker.RecordFailure(ex, iterationSw.ElapsedMilliseconds);
                }

                tracker.ReportProgress(iteration + 1, 1000);
            }

            tracker.PrintFinalReport();
            Assert.True(tracker.SuccessRate >= 0.95, tracker.GetFailureMessage());
        }

        [Fact]
        public async Task Extreme_ChainedCircuitBreakers_1000Iterations()
        {
            var tracker = new ContinuousTestTracker("ChainedCircuitBreakers", _output);

            for (int iteration = 0; iteration < 1000; iteration++)
            {
                var iterationSw = Stopwatch.StartNew();
                try
                {
                    using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
                    {
                        EnableLogging = false
                    });

                    // Create chain of 10 circuit breakers
                    var cbCount = 10;
                    var circuitBreakers = new List<OrchestratedCircuitBreaker>();

                    for (int i = 0; i < cbCount; i++)
                    {
                        var cb = new OrchestratedCircuitBreaker(
                            $"cb{i}",
                            orchestrator,
                            failureThreshold: 3,
                            openDuration: TimeSpan.FromMilliseconds(50));
                        await cb.StartAsync();
                        circuitBreakers.Add(cb);
                    }

                    // Call through the chain
                    var callCount = 50;
                    for (int call = 0; call < callCount; call++)
                    {
                        try
                        {
                            await circuitBreakers[0].ExecuteAsync<bool>(async ct0 =>
                            {
                                return await circuitBreakers[1].ExecuteAsync<bool>(async ct1 =>
                                {
                                    return await circuitBreakers[2].ExecuteAsync<bool>(async ct2 =>
                                    {
                                        return await circuitBreakers[3].ExecuteAsync<bool>(async ct3 =>
                                        {
                                            return await circuitBreakers[4].ExecuteAsync<bool>(async ct4 =>
                                            {
                                                return await circuitBreakers[5].ExecuteAsync<bool>(async ct5 =>
                                                {
                                                    return await circuitBreakers[6].ExecuteAsync<bool>(async ct6 =>
                                                    {
                                                        return await circuitBreakers[7].ExecuteAsync<bool>(async ct7 =>
                                                        {
                                                            return await circuitBreakers[8].ExecuteAsync<bool>(async ct8 =>
                                                            {
                                                                return await circuitBreakers[9].ExecuteAsync<bool>(async ct9 =>
                                                                {
                                                                    // Last one fails
                                                                    if (call < 5)
                                                                        throw new InvalidOperationException("Fail");
                                                                    return true;
                                                                }, ct8);
                                                            }, ct7);
                                                        }, ct6);
                                                    }, ct5);
                                                }, ct4);
                                            }, ct3);
                                        }, ct2);
                                    }, ct1);
                                }, ct0);
                            }, CancellationToken.None);
                        }
                        catch { }
                    }

                    await Task.Yield();

                    // Cleanup
                    foreach (var cb in circuitBreakers)
                        cb.Dispose();

                    iterationSw.Stop();
                    tracker.RecordSuccess(iterationSw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    iterationSw.Stop();
                    tracker.RecordFailure(ex, iterationSw.ElapsedMilliseconds);
                }

                tracker.ReportProgress(iteration + 1, 1000);
            }

            tracker.PrintFinalReport();
            Assert.True(tracker.SuccessRate >= 0.95, tracker.GetFailureMessage());
        }

        [Fact]
        public async Task Extreme_RandomChaos_1000Iterations()
        {
            var tracker = new ContinuousTestTracker("RandomChaos", _output);
            var random = new Random(42);

            for (int iteration = 0; iteration < 1000; iteration++)
            {
                var iterationSw = Stopwatch.StartNew();
                try
                {
                    using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
                    {
                        EnableLogging = false,
                        PoolSize = 4
                    });

                    var successCount = 0;
                    var actions = new Dictionary<string, Action<OrchestratedContext>>
                    {
                        ["chaos"] = (ctx) =>
                        {
                            // Random chaos operations
                            var chaos = random.Next(100);

                            if (chaos < 10) // 10% - throw exception
                                throw new InvalidOperationException("Random chaos");
                            else if (chaos < 20) // 10% - timeout
                                throw new TimeoutException("Chaos timeout");
                            else if (chaos < 25) // 5% - network error
                                throw new SocketException(10054);
                            else if (chaos < 30) // 5% - slow operation
                                Thread.Sleep(random.Next(50, 150));
                            else // 70% - success
                                Interlocked.Increment(ref successCount);
                        }
                    };

                    var json = @"{
                        id: 'chaos',
                        initial: 'ready',
                        states: {
                            ready: {
                                on: { TICK: { target: 'processing' } }
                            },
                            processing: {
                                entry: ['chaos'],
                                always: [{ target: 'ready' }]
                            }
                        }
                    }";

                    var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                        id: "chaos", json: json, orchestrator: orchestrator,
                        orchestratedActions: actions, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
                    await orchestrator.StartMachineAsync("chaos");

                    // Send events
                    var tasks = new List<Task>();
                    for (int i = 0; i < 100; i++)
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                await orchestrator.SendEventFireAndForgetAsync("test", "chaos", "TICK");
                            }
                            catch { }
                        }));
                    }
                    await Task.WhenAll(tasks);
                    await WaitUntilQuiescentAsync(() => successCount, noProgressTimeoutMs: 500, maxWaitSeconds: 3);

                    iterationSw.Stop();
                    tracker.RecordSuccess(iterationSw.ElapsedMilliseconds);

                    Assert.True(successCount >= 50, $"Iteration {iteration}: Only {successCount}/100 succeeded");
                }
                catch (Exception ex)
                {
                    iterationSw.Stop();
                    tracker.RecordFailure(ex, iterationSw.ElapsedMilliseconds);
                }

                tracker.ReportProgress(iteration + 1, 1000);
            }

            tracker.PrintFinalReport();
            Assert.True(tracker.SuccessRate >= 0.95, tracker.GetFailureMessage());
        }

        [Fact]
        public async Task Extreme_MemoryChurn_1000Iterations()
        {
            var tracker = new ContinuousTestTracker("MemoryChurn", _output);
            var initialMemory = GC.GetTotalMemory(false);

            for (int iteration = 0; iteration < 1000; iteration++)
            {
                var iterationSw = Stopwatch.StartNew();
                try
                {
                    using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
                    {
                        EnableLogging = false,
                        PoolSize = 4
                    });

                    var allocations = new ConcurrentBag<byte[]>();
                    var churnCount = 0;
                    var actions = new Dictionary<string, Action<OrchestratedContext>>
                    {
                        ["churn"] = (ctx) =>
                        {
                            // Allocate 1MB
                            var data = new byte[1024 * 1024];
                            Random.Shared.NextBytes(data);
                            allocations.Add(data);

                            // Release old allocations
                            if (allocations.Count > 10)
                            {
                                for (int i = 0; i < 5; i++)
                                    allocations.TryTake(out _);
                            }
                            Interlocked.Increment(ref churnCount);
                        }
                    };

                    var json = @"{
                        id: 'churn',
                        initial: 'ready',
                        states: {
                            ready: {
                                on: { CHURN: { target: 'processing' } }
                            },
                            processing: {
                                entry: ['churn'],
                                always: [{ target: 'ready' }]
                            }
                        }
                    }";

                    var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                        id: "churn", json: json, orchestrator: orchestrator,
                        orchestratedActions: actions, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
                    await orchestrator.StartMachineAsync("churn");

                    // Churn memory

                    for (int i = 0; i < 20; i++)
                    {
                        await orchestrator.SendEventFireAndForgetAsync("test", "churn", "CHURN");
                    }
                    await WaitForCountAsync(() => churnCount, targetValue: 20, timeoutSeconds: 3);

                    allocations.Clear();

                    iterationSw.Stop();
                    tracker.RecordSuccess(iterationSw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    iterationSw.Stop();
                    tracker.RecordFailure(ex, iterationSw.ElapsedMilliseconds);
                }

                // Force GC every 50 iterations
                if ((iteration + 1) % 50 == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }

                tracker.ReportProgress(iteration + 1, 1000);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            var finalMemory = GC.GetTotalMemory(false);
            var memoryGrowthMB = (finalMemory - initialMemory) / (1024.0 * 1024.0);

            tracker.PrintFinalReport();
            _output.WriteLine($"Memory Growth: {memoryGrowthMB:F2} MB");

            Assert.True(tracker.SuccessRate >= 0.95, tracker.GetFailureMessage());
            Assert.True(memoryGrowthMB < 300, $"Memory leak: {memoryGrowthMB:F2} MB");
        }

        [Fact]
        public async Task Extreme_DeadlockPrevention_1000Iterations()
        {
            var tracker = new ContinuousTestTracker("DeadlockPrevention", _output);

            for (int iteration = 0; iteration < 1000; iteration++)
            {
                var iterationSw = Stopwatch.StartNew();
                try
                {
                    using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
                    {
                        EnableLogging = false,
                        PoolSize = 4
                    });

                    var machine1ToMachine2Count = 0;
                    var machine2ToMachine1Count = 0;

                    var machine1Actions = new Dictionary<string, Action<OrchestratedContext>>
                    {
                        ["sendToM2"] = (ctx) =>
                        {
                            Interlocked.Increment(ref machine1ToMachine2Count);
                            ctx.RequestSend("m2", "PING");
                        }
                    };

                    var machine2Actions = new Dictionary<string, Action<OrchestratedContext>>
                    {
                        ["sendToM1"] = (ctx) =>
                        {
                            Interlocked.Increment(ref machine2ToMachine1Count);
                            ctx.RequestSend("m1", "PONG");
                        }
                    };

                    var m1Json = @"{
                        id: 'm1',
                        initial: 'idle',
                        states: {
                            idle: { on: { START: 'sending' } },
                            sending: { entry: ['sendToM2'], on: { PONG: 'idle' } }
                        }
                    }";

                    var m2Json = @"{
                        id: 'm2',
                        initial: 'idle',
                        states: {
                            idle: { on: { PING: 'sending' } },
                            sending: { entry: ['sendToM1'], on: { DONE: 'idle' } }
                        }
                    }";

                    var m1 = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                        id: "m1", json: m1Json, orchestrator: orchestrator,
                        orchestratedActions: machine1Actions, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
                    var m2 = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                        id: "m2", json: m2Json, orchestrator: orchestrator,
                        orchestratedActions: machine2Actions, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);

                    await Task.WhenAll(
                        orchestrator.StartMachineAsync("m1"),
                        orchestrator.StartMachineAsync("m2")
                    );

                    // Trigger bidirectional communication
                    using var cts = new CancellationTokenSource(2000); // Timeout to detect deadlock
                    var tasks = new List<Task>();

                    for (int i = 0; i < 50; i++)
                    {
                        tasks.Add(orchestrator.SendEventFireAndForgetAsync("test", "m1", "START"));
                        tasks.Add(orchestrator.SendEventFireAndForgetAsync("test", "m2", "PING"));
                    }

                    await Task.WhenAll(tasks).WaitAsync(cts.Token);
                    await WaitForConditionAsync(
                        condition: () => machine1ToMachine2Count > 40,
                        getProgress: () => machine1ToMachine2Count,
                        timeoutSeconds: 2,
                        noProgressTimeoutMs: 200);

                    iterationSw.Stop();
                    tracker.RecordSuccess(iterationSw.ElapsedMilliseconds);

                    Assert.True(machine1ToMachine2Count > 40, "Should not deadlock");
                }
                catch (TimeoutException)
                {
                    iterationSw.Stop();
                    tracker.RecordFailure(new Exception("Deadlock detected"), iterationSw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    iterationSw.Stop();
                    tracker.RecordFailure(ex, iterationSw.ElapsedMilliseconds);
                }

                tracker.ReportProgress(iteration + 1, 1000);
            }

            tracker.PrintFinalReport();
            Assert.True(tracker.SuccessRate >= 0.95, tracker.GetFailureMessage());
        }

        [Fact]
        public async Task Extreme_BurstTraffic_1000Iterations()
        {
            var tracker = new ContinuousTestTracker("BurstTraffic", _output);

            for (int iteration = 0; iteration < 1000; iteration++)
            {
                var iterationSw = Stopwatch.StartNew();
                try
                {
                    using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
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
                        id: 'burst',
                        initial: 'ready',
                        states: {
                            ready: {
                                on: { BURST: { target: 'processing' } }
                            },
                            processing: {
                                entry: ['process'],
                                always: [{ target: 'ready' }]
                            }
                        }
                    }";

                    var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                        id: "burst", json: json, orchestrator: orchestrator,
                        orchestratedActions: actions, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
                    await orchestrator.StartMachineAsync("burst");

                    // Simulate burst traffic - 1000 events at once
                    var tasks = new List<Task>();
                    for (int i = 0; i < 1000; i++)
                    {
                        tasks.Add(orchestrator.SendEventFireAndForgetAsync("test", "burst", "BURST"));
                    }
                    await Task.WhenAll(tasks);
                    await WaitForConditionAsync(
                        condition: () => processedCount >= 800,
                        getProgress: () => processedCount,
                        timeoutSeconds: 3,
                        noProgressTimeoutMs: 500);

                    iterationSw.Stop();
                    tracker.RecordSuccess(iterationSw.ElapsedMilliseconds);

                    Assert.True(processedCount >= 850, $"Iteration {iteration}: Only {processedCount}/1000 processed");
                }
                catch (Exception ex)
                {
                    iterationSw.Stop();
                    tracker.RecordFailure(ex, iterationSw.ElapsedMilliseconds);
                }

                tracker.ReportProgress(iteration + 1, 1000);
            }

            tracker.PrintFinalReport();
            Assert.True(tracker.SuccessRate >= 0.95, tracker.GetFailureMessage());
        }

        [Fact]
        public async Task Extreme_StateTransitionStorm_1000Iterations()
        {
            var tracker = new ContinuousTestTracker("StateTransitionStorm", _output);

            for (int iteration = 0; iteration < 1000; iteration++)
            {
                var iterationSw = Stopwatch.StartNew();
                try
                {
                    using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
                    {
                        EnableLogging = false,
                        PoolSize = 4
                    });

                    var transitionCount = 0;
                    var actions = new Dictionary<string, Action<OrchestratedContext>>
                    {
                        ["count"] = (ctx) => Interlocked.Increment(ref transitionCount)
                    };

                    // Machine with many states
                    var json = @"{
                        id: 'storm',
                        initial: 's1',
                        states: {
                            s1: { entry: ['count'], on: { E1: 's2', E2: 's3', E3: 's4' } },
                            s2: { entry: ['count'], on: { E1: 's3', E2: 's4', E3: 's5' } },
                            s3: { entry: ['count'], on: { E1: 's4', E2: 's5', E3: 's1' } },
                            s4: { entry: ['count'], on: { E1: 's5', E2: 's1', E3: 's2' } },
                            s5: { entry: ['count'], on: { E1: 's1', E2: 's2', E3: 's3' } }
                        }
                    }";

                    var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                        id: "storm", json: json, orchestrator: orchestrator,
                        orchestratedActions: actions, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
                    await orchestrator.StartMachineAsync("storm");

                    // Rapid state transitions
                    var events = new[] { "E1", "E2", "E3" };
                    var tasks = new List<Task>();
                    for (int i = 0; i < 500; i++)
                    {
                        var evt = events[i % 3];
                        tasks.Add(orchestrator.SendEventFireAndForgetAsync("test", "storm", evt));
                    }
                    await Task.WhenAll(tasks);
                    await WaitForConditionAsync(
                        condition: () => transitionCount >= 450,
                        getProgress: () => transitionCount,
                        timeoutSeconds: 3,
                        noProgressTimeoutMs: 300);

                    iterationSw.Stop();
                    tracker.RecordSuccess(iterationSw.ElapsedMilliseconds);

                    Assert.True(transitionCount >= 450, $"Iteration {iteration}: Only {transitionCount}/500 transitions");
                }
                catch (Exception ex)
                {
                    iterationSw.Stop();
                    tracker.RecordFailure(ex, iterationSw.ElapsedMilliseconds);
                }

                tracker.ReportProgress(iteration + 1, 1000);
            }

            tracker.PrintFinalReport();
            Assert.True(tracker.SuccessRate >= 0.95, tracker.GetFailureMessage());
        }

        public override void Dispose()
        {
            foreach (var orch in _orchestrators)
                orch?.Dispose();
            _loggerFactory?.Dispose();
        }

        private class ContinuousTestTracker
        {
            private readonly string _testName;
            
            private readonly Stopwatch _totalStopwatch = Stopwatch.StartNew();
            private int _passCount;
            private int _failCount;
            private readonly ConcurrentBag<long> _successTimes = new();
            private readonly ConcurrentBag<long> _failureTimes = new();
            private readonly ConcurrentDictionary<string, int> _errorsByType = new();
            private DateTime _lastProgressReport = DateTime.UtcNow;
            private readonly ITestOutputHelper _output;

            public ContinuousTestTracker(string testName, ITestOutputHelper output)
            {
                _testName = testName;
                _output = output;
                _output.WriteLine($"\n{'═',80}");
                _output.WriteLine($"  EXTREME CONTINUOUS TEST: {testName}");
                _output.WriteLine($"  Starting 1000 iterations...");
                _output.WriteLine($"{'═',80}\n");
            }

            public int PassCount => _passCount;
            public int FailCount => _failCount;
            public double SuccessRate => (_passCount + _failCount) > 0 ? _passCount / (double)(_passCount + _failCount) : 0;

            public void RecordSuccess(long durationMs)
            {
                Interlocked.Increment(ref _passCount);
                _successTimes.Add(durationMs);
            }

            public void RecordFailure(Exception ex, long durationMs)
            {
                Interlocked.Increment(ref _failCount);
                _failureTimes.Add(durationMs);
                var errorType = ex.GetType().Name;
                _errorsByType.AddOrUpdate(errorType, 1, (k, v) => v + 1);
            }

            public void ReportProgress(int current, int total)
            {
                if (current % 100 == 0 || (DateTime.UtcNow - _lastProgressReport).TotalSeconds >= 10)
                {
                    var progress = current / (double)total;
                    var avgTime = _successTimes.Any() ? _successTimes.Average() : 0;
                    var currentMemoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

                    _output.WriteLine($"[{current:D4}/{total}] " +
                        $"{progress:P0} | " +
                        $"✅ {_passCount} | " +
                        $"❌ {_failCount} | " +
                        $"Avg: {avgTime:F0}ms | " +
                        $"Mem: {currentMemoryMB:F1}MB");

                    _lastProgressReport = DateTime.UtcNow;
                }
            }

            public void PrintFinalReport()
            {
                _totalStopwatch.Stop();

                var allTimes = _successTimes.Concat(_failureTimes).OrderBy(t => t).ToList();
                var avgTime = allTimes.Any() ? allTimes.Average() : 0;
                var p50 = allTimes.Any() ? allTimes[allTimes.Count / 2] : 0;
                var p95 = allTimes.Any() ? allTimes[(int)(allTimes.Count * 0.95)] : 0;
                var p99 = allTimes.Any() ? allTimes[(int)(allTimes.Count * 0.99)] : 0;

                _output.WriteLine($"\n{'═',80}");
                _output.WriteLine($"  FINAL REPORT: {_testName}");
                _output.WriteLine($"{'═',80}");
                _output.WriteLine($"Total Duration: {_totalStopwatch.Elapsed.TotalMinutes:F1} min");
                _output.WriteLine($"Passed: {_passCount:N0} ✅");
                _output.WriteLine($"Failed: {_failCount:N0} ❌");
                _output.WriteLine($"Success Rate: {SuccessRate:P2}");
                _output.WriteLine($"Throughput: {1000 / _totalStopwatch.Elapsed.TotalSeconds:F2} iter/sec");
                _output.WriteLine($"\nTiming (ms): Avg={avgTime:F0}, P50={p50}, P95={p95}, P99={p99}");

                if (_errorsByType.Any())
                {
                    _output.WriteLine($"\nErrors:");
                    foreach (var error in _errorsByType.OrderByDescending(e => e.Value))
                        _output.WriteLine($"  {error.Key}: {error.Value}");
                }
                _output.WriteLine($"{'═',80}\n");
            }

            public string GetFailureMessage()
            {
                return $"Success rate {SuccessRate:P2} below 95% threshold. Passed: {_passCount}, Failed: {_failCount}";
            }
        }
    }
}
#endif
