using System.Collections.Concurrent;
using System.Diagnostics;
using XStateNet.Orchestration;
using Xunit;
using Xunit.Abstractions;

namespace XStateNet.Distributed.Tests.Resilience
{
    /// <summary>
    /// Quick continuous tests - simplified versions that run faster for rapid validation
    /// </summary>
    public class QuickContinuousTests : ResilienceTestBase
    {
        private EventBusOrchestrator? _orchestrator;

        public QuickContinuousTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task QuickContinuous_ConcurrentEvents_1000Times()
        {
            var results = new TestTracker("ConcurrentEvents");
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < 1000; i++)
            {
                try
                {
                    using var localOrchestrator = new EventBusOrchestrator(new OrchestratorConfig
                    {
                        EnableLogging = false,
                        PoolSize = 4
                    });

                    var processedCount = 0;
                    var actions = new Dictionary<string, Action<OrchestratedContext>>
                    {
                        ["process"] = (ctx) => Interlocked.Increment(ref processedCount)
                    };

                    var json = @"{
                        id: 'test',
                        initial: 'idle',
                        states: {
                            idle: {
                                on: { TICK: { target: 'active', actions: ['process'] } }
                            },
                            active: {
                                on: { TICK: { target: 'idle', actions: ['process'] } }
                            }
                        }
                    }";

                    var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                        id: "test", json: json, orchestrator: localOrchestrator,
                        orchestratedActions: actions, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
                    await localOrchestrator.StartMachineAsync("test");

                    // Send concurrent events
                    var tasks = new List<Task>();
                    for (int j = 0; j < 50; j++)
                    {
                        tasks.Add(localOrchestrator.SendEventFireAndForgetAsync("test", "test", "TICK"));
                    }
                    await Task.WhenAll(tasks);

                    // Wait deterministically for processing to complete
                    var success = await WaitForCountAsync(() => processedCount, targetValue: 50, timeoutSeconds: 5);

                    if (!success && i % 100 == 0)
                    {
                        _output.WriteLine($"Iteration {i}: Only {processedCount}/50 processed");
                    }

                    // Record success first, then assert (so we can see the failure in tracker)
                    if (processedCount >= 40)
                    {
                        results.RecordSuccess();
                    }
                    else
                    {
                        throw new InvalidOperationException($"Only {processedCount}/50 processed");
                    }
                }
                catch (Exception ex)
                {
                    results.RecordFailure(ex);
                }

                if ((i + 1) % 100 == 0)
                {
                    _output.WriteLine($"Progress: {i + 1}/1000 - Pass: {results.PassCount}, Fail: {results.FailCount}");
                }
            }

            sw.Stop();
            results.PrintReport(_output, sw.Elapsed);
            Assert.True(results.SuccessRate >= 0.95, $"Success rate {results.SuccessRate:P} below 95%");
        }

        //[Fact]
        public async Task QuickContinuous_CircuitBreaker_1000Times()
        {
            var results = new TestTracker("CircuitBreaker");
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < 1000; i++)
            {
                try
                {
                    using var localOrchestrator = new EventBusOrchestrator(new OrchestratorConfig
                    {
                        EnableLogging = false
                    });

                    var cb = new OrchestratedCircuitBreaker(
                        "test-cb", localOrchestrator,
                        failureThreshold: 3,
                        openDuration: TimeSpan.FromMilliseconds(100));
                    await cb.StartAsync();

                    // Trigger failures
                    for (int j = 0; j < 3; j++)
                    {
                        try
                        {
                            await cb.ExecuteAsync<bool>(async ct =>
                            {
                                await Task.Yield();
                                throw new InvalidOperationException("Test");
                            }, CancellationToken.None);
                        }
                        catch { }
                    }

                    await Task.Delay(50);

                    // Verify circuit opened
                    Assert.Contains("open", cb.CurrentState, StringComparison.OrdinalIgnoreCase);

                    cb.Dispose();
                    results.RecordSuccess();
                }
                catch (Exception ex)
                {
                    results.RecordFailure(ex);
                }

                if ((i + 1) % 100 == 0)
                {
                    _output.WriteLine($"Progress: {i + 1}/1000 - Pass: {results.PassCount}, Fail: {results.FailCount}");
                }
            }

            sw.Stop();
            results.PrintReport(_output, sw.Elapsed);
            Assert.True(results.SuccessRate >= 0.95, $"Success rate {results.SuccessRate:P} below 95%");
        }

        //[Fact]
        public async Task QuickContinuous_RaceConditions_1000Times()
        {
            var results = new TestTracker("RaceConditions");
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < 1000; i++)
            {
                try
                {
                    using var localOrchestrator = new EventBusOrchestrator(new OrchestratorConfig
                    {
                        EnableLogging = false,
                        PoolSize = 8
                    });

                    var counter = 0;
                    var actions = new Dictionary<string, Action<OrchestratedContext>>
                    {
                        ["increment"] = (ctx) =>
                        {
                            var temp = counter;
                            Thread.Yield(); // Force race condition
                            counter = temp + 1;
                        }
                    };

                    var json = @"{
                        id: 'racer',
                        initial: 'idle',
                        states: {
                            idle: {
                                on: { RACE: 'racing' }
                            },
                            racing: {
                                entry: ['increment'],
                                on: { RACE: 'idle' }
                            }
                        }
                    }";

                    var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                        id: "racer", json: json, orchestrator: localOrchestrator,
                        orchestratedActions: actions, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
                    await localOrchestrator.StartMachineAsync("racer");

                    // Concurrent events
                    var tasks = Enumerable.Range(0, 100)
                        .Select(_ => localOrchestrator.SendEventFireAndForgetAsync("test", "racer", "RACE"))
                        .ToArray();
                    await Task.WhenAll(tasks);

                    // Wait deterministically for all events to process
                    await WaitForCountAsync(() => counter, targetValue: 100, timeoutSeconds: 3);

                    // Should be 100 if properly serialized (100 RACE events bouncing idle→racing)
                    if (counter == 100)
                    {
                        results.RecordSuccess();
                    }
                    else
                    {
                        throw new InvalidOperationException($"Race detected - counter = {counter}, expected 100");
                    }
                }
                catch (Exception ex)
                {
                    results.RecordFailure(ex);
                }

                if ((i + 1) % 100 == 0)
                {
                    _output.WriteLine($"Progress: {i + 1}/1000 - Pass: {results.PassCount}, Fail: {results.FailCount}");
                }
            }

            sw.Stop();
            results.PrintReport(_output, sw.Elapsed);
            Assert.True(results.SuccessRate >= 0.95, $"Success rate {results.SuccessRate:P} below 95%");
        }

        //[Fact]
        public async Task QuickContinuous_MemoryStress_1000Times()
        {
            var results = new TestTracker("MemoryStress");
            var sw = Stopwatch.StartNew();
            var initialMemory = GC.GetTotalMemory(false);

            for (int i = 0; i < 1000; i++)
            {
                try
                {
                    using var localOrchestrator = new EventBusOrchestrator(new OrchestratorConfig
                    {
                        EnableLogging = false,
                        PoolSize = 4
                    });

                    var actions = new Dictionary<string, Action<OrchestratedContext>>
                    {
                        ["allocate"] = (ctx) =>
                        {
                            // Allocate and release
                            var data = new byte[10000];
                            Array.Fill(data, (byte)Random.Shared.Next(256));
                        }
                    };

                    var json = @"{
                        id: 'mem',
                        initial: 'idle',
                        states: {
                            idle: {
                                on: { ALLOC: 'active' }
                            },
                            active: {
                                entry: ['allocate'],
                                on: { ALLOC: 'idle' }
                            }
                        }
                    }";

                    var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                        id: "mem", json: json, orchestrator: localOrchestrator,
                        orchestratedActions: actions, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
                    await localOrchestrator.StartMachineAsync("mem");

                    // Allocate memory
                    var allocCount = 0;
                    var allocActions = new Dictionary<string, Action<OrchestratedContext>>
                    {
                        ["allocate"] = (ctx) =>
                        {
                            // Allocate and release
                            var data = new byte[10000];
                            Array.Fill(data, (byte)Random.Shared.Next(256));
                            Interlocked.Increment(ref allocCount);
                        }
                    };

                    // Recreate machine with tracked allocations
                    var machine2 = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                        id: "mem2", json: @"{
                            id: 'mem2',
                            initial: 'idle',
                            states: {
                                idle: {
                                    on: { ALLOC: 'active' }
                                },
                                active: {
                                    entry: ['allocate'],
                                    on: { ALLOC: 'idle' }
                                }
                            }
                        }", orchestrator: localOrchestrator,
                        orchestratedActions: allocActions, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
                    await localOrchestrator.StartMachineAsync("mem2");

                    for (int j = 0; j < 50; j++)
                    {
                        await localOrchestrator.SendEventFireAndForgetAsync("test", "mem2", "ALLOC");
                    }

                    // Wait deterministically for allocations to complete (50 ALLOC events)
                    await WaitForCountAsync(() => allocCount, targetValue: 50, timeoutSeconds: 3);

                    if (allocCount >= 45)
                    {
                        results.RecordSuccess();
                    }
                    else
                    {
                        throw new InvalidOperationException($"Only {allocCount}/50 allocations completed");
                    }
                }
                catch (Exception ex)
                {
                    results.RecordFailure(ex);
                }

                // GC every 100 iterations
                if ((i + 1) % 100 == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    var currentMemory = GC.GetTotalMemory(false);
                    var memoryMB = (currentMemory - initialMemory) / (1024.0 * 1024.0);
                    _output.WriteLine($"Progress: {i + 1}/1000 - Pass: {results.PassCount}, Fail: {results.FailCount}, Mem: {memoryMB:F2}MB");
                }
            }

            sw.Stop();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var finalMemory = GC.GetTotalMemory(false);
            var totalMemoryGrowthMB = (finalMemory - initialMemory) / (1024.0 * 1024.0);

            results.PrintReport(_output, sw.Elapsed);
            _output.WriteLine($"Memory Growth: {totalMemoryGrowthMB:F2} MB");

            Assert.True(results.SuccessRate >= 0.95, $"Success rate {results.SuccessRate:P} below 95%");
            Assert.True(totalMemoryGrowthMB < 200, $"Memory leak: {totalMemoryGrowthMB:F2} MB growth");
        }

        [Fact]
        public async Task QuickContinuous_AllTests_1000Times()
        {
            var overallResults = new TestTracker("AllQuickTests");
            var sw = Stopwatch.StartNew();

            _output.WriteLine("\n" + new string('═', 80));
            _output.WriteLine("  RUNNING ALL QUICK CONTINUOUS TESTS - 1000 ITERATIONS");
            _output.WriteLine(new string('═', 80) + "\n");

            var testMethods = new (string name, Func<Task> test)[]
            {
                ("Concurrent Events", QuickContinuous_ConcurrentEvents_1000Times),
                ("Circuit Breaker", QuickContinuous_CircuitBreaker_1000Times),
                ("Race Conditions", QuickContinuous_RaceConditions_1000Times),
                ("Memory Stress", QuickContinuous_MemoryStress_1000Times)
            };

            foreach (var (name, test) in testMethods)
            {
                _output.WriteLine($"\n--- Running: {name} ---");
                try
                {
                    await test();
                    overallResults.RecordSuccess();
                    _output.WriteLine($"✅ {name} PASSED\n");
                }
                catch (Exception ex)
                {
                    overallResults.RecordFailure(ex);
                    _output.WriteLine($"❌ {name} FAILED: {ex.Message}\n");
                }
            }

            sw.Stop();

            _output.WriteLine("\n" + new string('═', 80));
            _output.WriteLine("  OVERALL SUMMARY");
            _output.WriteLine(new string('═', 80));
            overallResults.PrintReport(_output, sw.Elapsed);

            Assert.True(overallResults.SuccessRate == 1.0,
                $"Some test suites failed: {overallResults.FailCount}/{testMethods.Length}");
        }

        public override void Dispose()
        {
            _orchestrator?.Dispose();
            base.Dispose();
        }

        private class TestTracker
        {
            private readonly string _testName;
            private int _passCount;
            private int _failCount;
            private readonly ConcurrentDictionary<string, int> _errorsByType = new();

            public TestTracker(string testName)
            {
                _testName = testName;
            }

            public int PassCount => _passCount;
            public int FailCount => _failCount;
            public double SuccessRate => (_passCount + _failCount) > 0 ? _passCount / (double)(_passCount + _failCount) : 0;

            public void RecordSuccess() => Interlocked.Increment(ref _passCount);

            public void RecordFailure(Exception ex)
            {
                Interlocked.Increment(ref _failCount);
                var errorType = ex.GetType().Name;
                _errorsByType.AddOrUpdate(errorType, 1, (k, v) => v + 1);
            }

            public void PrintReport(ITestOutputHelper output, TimeSpan duration)
            {
                output.WriteLine($"\n{'─',60}");
                output.WriteLine($"TEST REPORT: {_testName}");
                output.WriteLine($"{'─',60}");
                output.WriteLine($"Duration: {duration.TotalSeconds:F1}s");
                output.WriteLine($"Passed: {_passCount:N0} ✅");
                output.WriteLine($"Failed: {_failCount:N0} ❌");
                output.WriteLine($"Success Rate: {SuccessRate:P2}");
                output.WriteLine($"Throughput: {(_passCount + _failCount) / duration.TotalSeconds:F2} tests/sec");

                if (_errorsByType.Any())
                {
                    output.WriteLine($"\nError Breakdown:");
                    foreach (var error in _errorsByType.OrderByDescending(e => e.Value))
                    {
                        output.WriteLine($"  {error.Key}: {error.Value}");
                    }
                }
                output.WriteLine($"{'─',60}\n");
            }
        }
    }
}
