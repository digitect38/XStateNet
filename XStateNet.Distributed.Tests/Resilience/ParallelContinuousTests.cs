using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using XStateNet.Orchestration;
using Xunit;
using Xunit.Abstractions;

namespace XStateNet.Distributed.Tests.Resilience
{
    /// <summary>
    /// Parallel continuous tests - run multiple test scenarios simultaneously 1000 times
    /// Maximum stress by running everything in parallel
    /// </summary>
    public class ParallelContinuousTests : ResilienceTestBase
    {
        
        private readonly ILoggerFactory _loggerFactory;

        public ParallelContinuousTests(ITestOutputHelper output) : base(output)
        {

            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddDebug().SetMinimumLevel(LogLevel.Error);
            });
        }

        [Fact]
        public async Task Parallel_AllScenarios_1000Iterations()
        {
            var overallTracker = new ParallelTestTracker("AllScenariosParallel", _output);
            var totalSw = Stopwatch.StartNew();

            _output.WriteLine($"\n{'═',80}");
            _output.WriteLine($"  PARALLEL CONTINUOUS TEST - ALL SCENARIOS");
            _output.WriteLine($"  Running 1000 iterations with 5 scenarios in parallel");
            _output.WriteLine($"  Total operations: 5,000");
            _output.WriteLine($"{'═',80}\n");

            for (int iteration = 0; iteration < 1000; iteration++)
            {
                var iterationSw = Stopwatch.StartNew();

                try
                {
                    // Run 5 different scenarios in parallel
                    var parallelTasks = new[]
                    {
                        Task.Run(() => RunConcurrentEventsScenario(iteration)),
                        Task.Run(() => RunCircuitBreakerScenario(iteration)),
                        Task.Run(() => RunMemoryStressScenario(iteration)),
                        Task.Run(() => RunStateTransitionScenario(iteration)),
                        Task.Run(() => RunNetworkChaosScenario(iteration))
                    };

                    await Task.WhenAll(parallelTasks);

                    iterationSw.Stop();
                    overallTracker.RecordSuccess("AllParallel", iterationSw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    iterationSw.Stop();
                    overallTracker.RecordFailure("AllParallel", ex, iterationSw.ElapsedMilliseconds);
                }

                if ((iteration + 1) % 50 == 0)
                {
                    overallTracker.ReportProgress(iteration + 1, 1000);
                }
            }

            totalSw.Stop();
            overallTracker.PrintFinalReport(totalSw.Elapsed);

            Assert.True(overallTracker.GetOverallSuccessRate() >= 0.95,
                $"Overall success rate {overallTracker.GetOverallSuccessRate():P} below 95%");
        }

        [Fact]
        public async Task Parallel_MixedLoad_1000Iterations()
        {
            var tracker = new ParallelTestTracker("MixedLoadParallel", _output);
            var totalSw = Stopwatch.StartNew();

            for (int iteration = 0; iteration < 1000; iteration++)
            {
                try
                {
                    // Create varying load patterns in parallel
                    var tasks = new List<Task>();

                    // Heavy load (10 tasks)
                    for (int i = 0; i < 10; i++)
                    {
                        var taskId = i; // Capture loop variable
                        tasks.Add(Task.Run(() => RunHeavyLoadScenario(iteration, taskId)));
                    }

                    // Medium load (20 tasks)
                    for (int i = 0; i < 20; i++)
                    {
                        var taskId = i; // Capture loop variable
                        tasks.Add(Task.Run(() => RunMediumLoadScenario(iteration, taskId)));
                    }

                    // Light load (30 tasks)
                    for (int i = 0; i < 30; i++)
                    {
                        var taskId = i; // Capture loop variable
                        tasks.Add(Task.Run(() => RunLightLoadScenario(iteration, taskId)));
                    }

                    var sw = Stopwatch.StartNew();
                    await Task.WhenAll(tasks);
                    sw.Stop();

                    tracker.RecordSuccess("MixedLoad", sw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    if (iteration == 0) // Log first failure for debugging
                    {
                        _output.WriteLine($"First failure: {ex.GetType().Name}: {ex.Message}");
                        _output.WriteLine($"Stack: {ex.StackTrace}");
                    }
                    tracker.RecordFailure("MixedLoad", ex, 0);
                }

                if ((iteration + 1) % 50 == 0)
                {
                    tracker.ReportProgress(iteration + 1, 1000);
                }
            }

            totalSw.Stop();
            tracker.PrintFinalReport(totalSw.Elapsed);

            Assert.True(tracker.GetOverallSuccessRate() >= 0.90,
                $"Mixed load success rate {tracker.GetOverallSuccessRate():P} below 90%");
        }

        [Fact]
        public async Task Parallel_ThunderingHerd_1000Iterations()
        {
            var tracker = new ParallelTestTracker("ThunderingHerd", _output);
            var totalSw = Stopwatch.StartNew();

            for (int iteration = 0; iteration < 1000; iteration++)
            {
                var iterationSw = Stopwatch.StartNew();

                try
                {
                    using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
                    {
                        EnableLogging = false,
                        PoolSize = 16,
                        EnableBackpressure = true,
                        MaxQueueDepth = 10000
                    });

                    var processedCount = 0;
                    var actions = new Dictionary<string, Action<OrchestratedContext>>
                    {
                        ["process"] = (ctx) => Interlocked.Increment(ref processedCount)
                    };

                    var json = @"{
                        id: 'herd',
                        initial: 'ready',
                        states: {
                            ready: {
                                on: { STAMPEDE: { target: 'processing' } }
                            },
                            processing: {
                                entry: ['process'],
                                always: [{ target: 'ready' }]
                            }
                        }
                    }";

                    var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                        id: "herd", json: json, orchestrator: orchestrator,
                        orchestratedActions: actions, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
                    await orchestrator.StartMachineAsync("herd");

                    // Simulate thundering herd - 1000 concurrent requests at exact same time
                    var barrier = new Barrier(1000);
                    var tasks = new List<Task>();

                    for (int i = 0; i < 1000; i++)
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            barrier.SignalAndWait(); // Wait for all threads
                            await orchestrator.SendEventFireAndForgetAsync("test", "herd", "STAMPEDE");
                        }));
                    }

                    await Task.WhenAll(tasks);
                    await WaitForConditionAsync(
                        condition: () => processedCount >= 800,
                        getProgress: () => processedCount,
                        timeoutSeconds: 5,
                        noProgressTimeoutMs: 1000);

                    iterationSw.Stop();
                    tracker.RecordSuccess("ThunderingHerd", iterationSw.ElapsedMilliseconds);

                    Assert.True(processedCount >= 800,
                        $"Iteration {iteration}: Only {processedCount}/1000 processed under thundering herd");
                }
                catch (Exception ex)
                {
                    iterationSw.Stop();
                    tracker.RecordFailure("ThunderingHerd", ex, iterationSw.ElapsedMilliseconds);
                }

                if ((iteration + 1) % 50 == 0)
                {
                    tracker.ReportProgress(iteration + 1, 1000);
                }
            }

            totalSw.Stop();
            tracker.PrintFinalReport(totalSw.Elapsed);

            Assert.True(tracker.GetOverallSuccessRate() >= 0.95,
                $"Thundering herd success rate {tracker.GetOverallSuccessRate():P} below 95%");
        }

        [Fact]
        public async Task Parallel_RapidCreateDestroy_1000Iterations()
        {
            var tracker = new ParallelTestTracker("RapidCreateDestroy", _output);
            var totalSw = Stopwatch.StartNew();

            for (int iteration = 0; iteration < 1000; iteration++)
            {
                var currentIteration = iteration; // Capture iteration variable
                var iterationSw = Stopwatch.StartNew();

                try
                {
                    // Create and destroy 100 orchestrators in parallel
                    var tasks = new List<Task>();

                    for (int i = 0; i < 100; i++)
                    {
                        var taskId = i; // Capture loop variable
                        tasks.Add(Task.Run(async () =>
                        {
                            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
                            {
                                EnableLogging = false,
                                PoolSize = 2
                            });

                            var machineId = $"temp-{currentIteration}-{taskId}";
                            var json = $@"{{
                                id: '{machineId}',
                                initial: 'active',
                                states: {{
                                    active: {{ on: {{ TICK: 'active' }} }}
                                }}
                            }}";

                            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                                id: machineId, json: json, orchestrator: orchestrator,
                                orchestratedActions: null, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
                            await orchestrator.StartMachineAsync(machineId);
                            await orchestrator.SendEventAsync("test", machineId, "TICK");
                        }));
                    }

                    await Task.WhenAll(tasks);

                    iterationSw.Stop();
                    tracker.RecordSuccess("CreateDestroy", iterationSw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    iterationSw.Stop();
                    tracker.RecordFailure("CreateDestroy", ex, iterationSw.ElapsedMilliseconds);
                }

                if ((iteration + 1) % 50 == 0)
                {
                    tracker.ReportProgress(iteration + 1, 1000);
                }
            }

            totalSw.Stop();
            tracker.PrintFinalReport(totalSw.Elapsed);

            Assert.True(tracker.GetOverallSuccessRate() >= 0.95,
                $"Create/Destroy success rate {tracker.GetOverallSuccessRate():P} below 95%");
        }

        // Scenario implementations
        private async Task RunConcurrentEventsScenario(int iteration)
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 2
            });

            var count = 0;
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["count"] = (ctx) => Interlocked.Increment(ref count)
            };

            var json = @"{
                id: 'concurrent',
                initial: 'ready',
                states: {
                    ready: {
                        on: { TICK: { target: 'processing' } }
                    },
                    processing: {
                        entry: ['count'],
                        always: [{ target: 'ready' }]
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "concurrent", json: json, orchestrator: orchestrator,
                orchestratedActions: actions, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
            await orchestrator.StartMachineAsync("concurrent");

            var tasks = Enumerable.Range(0, 20)
                .Select(_ => orchestrator.SendEventFireAndForgetAsync("test", "concurrent", "TICK"))
                .ToArray();
            await Task.WhenAll(tasks);
            await WaitForCountAsync(() => count, targetValue: 20, timeoutSeconds: 2);
        }

        private async Task RunCircuitBreakerScenario(int iteration)
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });
            var cb = new OrchestratedCircuitBreaker("cb", orchestrator, failureThreshold: 2,
                openDuration: TimeSpan.FromMilliseconds(50));
            await cb.StartAsync();

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    await cb.ExecuteAsync<bool>(async ct =>
                    {
                        if (i < 2) throw new InvalidOperationException();
                        return true;
                    }, CancellationToken.None);
                }
                catch { }
            }

            cb.Dispose();
        }

        private async Task RunMemoryStressScenario(int iteration)
        {
            var data = new byte[100000]; // 100KB
            Random.Shared.NextBytes(data);
            await Task.Yield();
        }

        private async Task RunStateTransitionScenario(int iteration)
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });

            var json = @"{
                id: 'states',
                initial: 's1',
                states: {
                    s1: { on: { E1: 's2' } },
                    s2: { on: { E2: 's3' } },
                    s3: { on: { E3: 's1' } }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "states", json: json, orchestrator: orchestrator,
                orchestratedActions: null, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
            await orchestrator.StartMachineAsync("states");

            await orchestrator.SendEventAsync("test", "states", "E1");
            await orchestrator.SendEventAsync("test", "states", "E2");
            await orchestrator.SendEventAsync("test", "states", "E3");
        }

        private async Task RunNetworkChaosScenario(int iteration)
        {
            var random = new Random(iteration);
            if (random.Next(100) < 10)
                throw new TimeoutException("Simulated network timeout");
            await Task.Delay(random.Next(5, 20));
        }

        private async Task RunHeavyLoadScenario(int iteration, int taskId)
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });
            var machineId = $"heavy-{iteration}-{taskId}";
            var json = $@"{{ id: '{machineId}', initial: 'active', states: {{ active: {{}} }} }}";
            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: machineId, json: json, orchestrator: orchestrator,
                orchestratedActions: null, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
            await orchestrator.StartMachineAsync(machineId);

            for (int i = 0; i < 50; i++)
                await orchestrator.SendEventFireAndForgetAsync("test", machineId, "WORK");
            await Task.Yield();
        }

        private async Task RunMediumLoadScenario(int iteration, int taskId)
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });
            var machineId = $"medium-{iteration}-{taskId}";
            var json = $@"{{ id: '{machineId}', initial: 'active', states: {{ active: {{}} }} }}";
            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: machineId, json: json, orchestrator: orchestrator,
                orchestratedActions: null, guards: null, services: null, delays: null, activities: null, enableGuidIsolation: false);
            await orchestrator.StartMachineAsync(machineId);

            for (int i = 0; i < 20; i++)
                await orchestrator.SendEventFireAndForgetAsync("test", machineId, "WORK");
            await Task.Yield();
        }

        private async Task RunLightLoadScenario(int iteration, int taskId)
        {
            await Task.Yield();
        }

        public override void Dispose()
        {
            _loggerFactory?.Dispose();
        }

        private class ParallelTestTracker
        {
            private readonly ITestOutputHelper _output;
            private readonly string _testName;
            
            private readonly ConcurrentDictionary<string, ScenarioStats> _scenarioStats = new();

            public ParallelTestTracker(string testName, ITestOutputHelper output)
            {
                _testName = testName;
                _output = output;
            }

            public void RecordSuccess(string scenario, long durationMs)
            {
                var stats = _scenarioStats.GetOrAdd(scenario, _ => new ScenarioStats());
                stats.RecordSuccess(durationMs);
            }

            public void RecordFailure(string scenario, Exception ex, long durationMs)
            {
                var stats = _scenarioStats.GetOrAdd(scenario, _ => new ScenarioStats());
                stats.RecordFailure(ex, durationMs);
            }

            public void ReportProgress(int current, int total)
            {
                var totalPass = _scenarioStats.Values.Sum(s => s.PassCount);
                var totalFail = _scenarioStats.Values.Sum(s => s.FailCount);
                var memoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

                _output.WriteLine($"[{current:D4}/{total}] " +
                    $"{current / (double)total:P0} | " +
                    $"✅ {totalPass} | " +
                    $"❌ {totalFail} | " +
                    $"Mem: {memoryMB:F1}MB");
            }

            public void PrintFinalReport(TimeSpan totalDuration)
            {
                _output.WriteLine($"\n{'═',80}");
                _output.WriteLine($"  PARALLEL TEST FINAL REPORT: {_testName}");
                _output.WriteLine($"{'═',80}");
                _output.WriteLine($"Total Duration: {totalDuration.TotalMinutes:F1} min");

                foreach (var (scenario, stats) in _scenarioStats.OrderBy(s => s.Key))
                {
                    _output.WriteLine($"\n--- {scenario} ---");
                    _output.WriteLine($"  Passed: {stats.PassCount:N0} ✅");
                    _output.WriteLine($"  Failed: {stats.FailCount:N0} ❌");
                    _output.WriteLine($"  Success Rate: {stats.SuccessRate:P2}");

                    if (stats.Times.Any())
                    {
                        var times = stats.Times.OrderBy(t => t).ToList();
                        _output.WriteLine($"  Timing: Avg={times.Average():F0}ms, " +
                            $"P50={times[times.Count / 2]}ms, " +
                            $"P95={times[(int)(times.Count * 0.95)]}ms");
                    }

                    if (stats.Errors.Any())
                    {
                        _output.WriteLine($"  Top Errors:");
                        foreach (var error in stats.Errors.OrderByDescending(e => e.Value).Take(3))
                            _output.WriteLine($"    {error.Key}: {error.Value}");
                    }
                }

                var overallPass = _scenarioStats.Values.Sum(s => s.PassCount);
                var overallFail = _scenarioStats.Values.Sum(s => s.FailCount);
                _output.WriteLine($"\n{'─',80}");
                _output.WriteLine($"OVERALL: ✅ {overallPass:N0} | ❌ {overallFail:N0} | " +
                    $"Rate: {overallPass / (double)(overallPass + overallFail):P2}");
                _output.WriteLine($"{'═',80}\n");
            }

            public double GetOverallSuccessRate()
            {
                var totalPass = _scenarioStats.Values.Sum(s => s.PassCount);
                var totalFail = _scenarioStats.Values.Sum(s => s.FailCount);
                return totalPass / (double)(totalPass + totalFail);
            }

            private class ScenarioStats
            {
                private int _passCount;
                private int _failCount;
                private readonly ConcurrentBag<long> _times = new();
                private readonly ConcurrentDictionary<string, int> _errors = new();

                public int PassCount => _passCount;
                public int FailCount => _failCount;
                public double SuccessRate => (_passCount + _failCount) > 0 ? _passCount / (double)(_passCount + _failCount) : 0;
                public ConcurrentBag<long> Times => _times;
                public ConcurrentDictionary<string, int> Errors => _errors;

                public void RecordSuccess(long durationMs)
                {
                    Interlocked.Increment(ref _passCount);
                    _times.Add(durationMs);
                }

                public void RecordFailure(Exception ex, long durationMs)
                {
                    Interlocked.Increment(ref _failCount);
                    _times.Add(durationMs);
                    _errors.AddOrUpdate(ex.GetType().Name, 1, (k, v) => v + 1);
                }
            }
        }
    }
}
