using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XStateNet.Orchestration;

namespace XStateNet.Benchmarking
{
    /// <summary>
    /// Comprehensive performance benchmarking framework for XStateNet orchestrator
    /// </summary>
    public class BenchmarkFramework
    {
        private readonly List<BenchmarkResult> _results = new();
        private readonly BenchmarkConfig _config;

        public BenchmarkFramework(BenchmarkConfig? config = null)
        {
            _config = config ?? new BenchmarkConfig();
        }

        /// <summary>
        /// Run a complete benchmark suite
        /// </summary>
        public async Task<BenchmarkSuiteResult> RunBenchmarkSuiteAsync()
        {
            Console.WriteLine("🏎️  Starting XStateNet Performance Benchmark Suite");
            Console.WriteLine($"📊 Configuration: {_config.WarmupIterations} warmup, {_config.MeasurementIterations} measurement iterations");
            Console.WriteLine();

            var suiteStopwatch = Stopwatch.StartNew();
            var benchmarks = new List<(string Name, Func<Task<BenchmarkResult>> Benchmark)>
            {
                ("Throughput - Sequential Events", () => BenchmarkSequentialThroughputAsync()),
                ("Throughput - Parallel Events", () => BenchmarkParallelThroughputAsync()),
                ("Latency - Single Events", () => BenchmarkSingleEventLatencyAsync()),
                ("Latency - Request-Response", () => BenchmarkRequestResponseLatencyAsync()),
                ("Scalability - Machine Count", () => BenchmarkMachineScalabilityAsync()),
                ("Scalability - Event Bus Pool", () => BenchmarkEventBusScalabilityAsync()),
                ("Memory - Sustained Load", () => BenchmarkMemoryUsageAsync()),
                ("Stress - High Concurrency", () => BenchmarkHighConcurrencyAsync()),
                ("Stress - Burst Traffic", () => BenchmarkBurstTrafficAsync()),
                ("Reliability - Long Duration", () => BenchmarkLongDurationAsync())
            };

            var results = new List<BenchmarkResult>();

            foreach (var (name, benchmark) in benchmarks)
            {
                Console.WriteLine($"🔄 Running: {name}");

                try
                {
                    var result = await benchmark();
                    result.BenchmarkName = name;
                    results.Add(result);

                    Console.WriteLine($"   ✅ {name} completed in {result.Duration.TotalSeconds:F2}s");
                    Console.WriteLine($"      Throughput: {result.EventsPerSecond:F0} events/sec, Latency: {result.AverageLatency:F2}ms");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ {name} failed: {ex.Message}");
                    results.Add(new BenchmarkResult
                    {
                        BenchmarkName = name,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }

                // Brief pause between benchmarks
                await Task.Delay(1000);
                Console.WriteLine();
            }

            suiteStopwatch.Stop();

            return new BenchmarkSuiteResult
            {
                Results = results,
                TotalDuration = suiteStopwatch.Elapsed,
                Timestamp = DateTime.UtcNow,
                Configuration = _config
            };
        }

        /// <summary>
        /// Benchmark sequential event processing throughput
        /// </summary>
        public async Task<BenchmarkResult> BenchmarkSequentialThroughputAsync()
        {
            return await RunBenchmarkAsync("Sequential Throughput", async (orchestrator, machines) =>
            {
                var eventCount = _config.ThroughputEventCount;
                var stopwatch = Stopwatch.StartNew();

                for (int i = 0; i < eventCount; i++)
                {
                    var machineId = machines[i % machines.Count];
                    await orchestrator.SendEventFireAndForgetAsync("bench", machineId, "PROCESS");
                }

                stopwatch.Stop();
                return (eventCount, stopwatch.Elapsed, null);
            });
        }

        /// <summary>
        /// Benchmark parallel event processing throughput
        /// </summary>
        public async Task<BenchmarkResult> BenchmarkParallelThroughputAsync()
        {
            return await RunBenchmarkAsync("Parallel Throughput", async (orchestrator, machines) =>
            {
                var eventCount = _config.ThroughputEventCount;
                var stopwatch = Stopwatch.StartNew();

                var tasks = new List<Task>();
                for (int i = 0; i < eventCount; i++)
                {
                    var machineId = machines[i % machines.Count];
                    tasks.Add(orchestrator.SendEventFireAndForgetAsync("bench", machineId, "PROCESS"));
                }

                await Task.WhenAll(tasks);
                stopwatch.Stop();

                return (eventCount, stopwatch.Elapsed, null);
            });
        }

        /// <summary>
        /// Benchmark single event latency
        /// </summary>
        public async Task<BenchmarkResult> BenchmarkSingleEventLatencyAsync()
        {
            return await RunBenchmarkAsync("Single Event Latency", async (orchestrator, machines) =>
            {
                var measurements = new List<double>();
                var eventCount = _config.LatencyEventCount;

                for (int i = 0; i < eventCount; i++)
                {
                    var machineId = machines[i % machines.Count];
                    var stopwatch = Stopwatch.StartNew();

                    var result = await orchestrator.SendEventAsync("bench", machineId, "PROCESS", null, 5000);
                    stopwatch.Stop();

                    if (result.Success)
                    {
                        measurements.Add(stopwatch.Elapsed.TotalMilliseconds);
                    }
                }

                var avgLatency = measurements.Average();
                var totalTime = TimeSpan.FromMilliseconds(measurements.Sum());

                return (measurements.Count, totalTime, measurements);
            });
        }

        /// <summary>
        /// Benchmark request-response latency
        /// </summary>
        public async Task<BenchmarkResult> BenchmarkRequestResponseLatencyAsync()
        {
            return await RunBenchmarkAsync("Request-Response Latency", async (orchestrator, machines) =>
            {
                var measurements = new List<double>();
                var eventCount = _config.LatencyEventCount;

                for (int i = 0; i < eventCount; i++)
                {
                    var machineId = machines[i % machines.Count];
                    var stopwatch = Stopwatch.StartNew();

                    var result = await orchestrator.SendEventAsync("bench", machineId, "REQUEST", $"data_{i}", 5000);
                    stopwatch.Stop();

                    if (result.Success)
                    {
                        measurements.Add(stopwatch.Elapsed.TotalMilliseconds);
                    }
                }

                var avgLatency = measurements.Average();
                var totalTime = TimeSpan.FromMilliseconds(measurements.Sum());

                return (measurements.Count, totalTime, measurements);
            });
        }

        /// <summary>
        /// Benchmark scalability with varying machine counts
        /// </summary>
        private async Task<BenchmarkResult> BenchmarkMachineScalabilityAsync()
        {
            var scalabilityResults = new List<ScalabilityDataPoint>();

            foreach (var machineCount in new[] { 1, 5, 10, 25, 50, 100 })
            {
                var result = await RunBenchmarkAsync($"Scalability {machineCount} machines",
                    async (orchestrator, machines) =>
                    {
                        // Use only the specified number of machines
                        var useMachines = machines.Take(Math.Min(machineCount, machines.Count)).ToList();
                        var eventCount = _config.ScalabilityEventCount;
                        var stopwatch = Stopwatch.StartNew();

                        var tasks = new List<Task>();
                        for (int i = 0; i < eventCount; i++)
                        {
                            var machineId = useMachines[i % useMachines.Count];
                            tasks.Add(orchestrator.SendEventFireAndForgetAsync("bench", machineId, "PROCESS"));
                        }

                        await Task.WhenAll(tasks);
                        stopwatch.Stop();

                        return (eventCount, stopwatch.Elapsed, null);
                    }, machineCount);

                scalabilityResults.Add(new ScalabilityDataPoint
                {
                    MachineCount = machineCount,
                    EventsPerSecond = result.EventsPerSecond,
                    AverageLatency = result.AverageLatency
                });
            }

            return new BenchmarkResult
            {
                BenchmarkName = "Machine Scalability",
                Success = true,
                ScalabilityData = scalabilityResults
            };
        }

        /// <summary>
        /// Benchmark scalability with varying event bus pool sizes
        /// </summary>
        private async Task<BenchmarkResult> BenchmarkEventBusScalabilityAsync()
        {
            var scalabilityResults = new List<ScalabilityDataPoint>();

            foreach (var poolSize in new[] { 1, 2, 4, 8, 16 })
            {
                using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
                {
                    PoolSize = poolSize,
                    EnableLogging = false,
                    EnableMetrics = false
                });

                var machines = await SetupBenchmarkMachinesAsync(orchestrator, _config.DefaultMachineCount);
                var eventCount = _config.ScalabilityEventCount;
                var stopwatch = Stopwatch.StartNew();

                var tasks = new List<Task>();
                for (int i = 0; i < eventCount; i++)
                {
                    var machineId = machines[i % machines.Count];
                    tasks.Add(orchestrator.SendEventFireAndForgetAsync("bench", machineId, "PROCESS"));
                }

                await Task.WhenAll(tasks);
                stopwatch.Stop();

                var eventsPerSecond = eventCount / stopwatch.Elapsed.TotalSeconds;

                scalabilityResults.Add(new ScalabilityDataPoint
                {
                    MachineCount = poolSize,
                    EventsPerSecond = eventsPerSecond,
                    AverageLatency = 0 // Not measured in this test
                });
            }

            return new BenchmarkResult
            {
                BenchmarkName = "Event Bus Pool Scalability",
                Success = true,
                ScalabilityData = scalabilityResults
            };
        }

        /// <summary>
        /// Benchmark memory usage under sustained load
        /// </summary>
        private async Task<BenchmarkResult> BenchmarkMemoryUsageAsync()
        {
            return await RunBenchmarkAsync("Memory Usage", async (orchestrator, machines) =>
            {
                var initialMemory = GC.GetTotalMemory(true);
                var memoryMeasurements = new List<long>();

                var eventCount = _config.MemoryTestEventCount;
                var batchSize = 1000;

                for (int batch = 0; batch < eventCount / batchSize; batch++)
                {
                    var tasks = new List<Task>();
                    for (int i = 0; i < batchSize; i++)
                    {
                        var machineId = machines[(batch * batchSize + i) % machines.Count];
                        tasks.Add(orchestrator.SendEventFireAndForgetAsync("bench", machineId, "PROCESS"));
                    }

                    await Task.WhenAll(tasks);

                    // Measure memory every few batches
                    if (batch % 10 == 0)
                    {
                        var currentMemory = GC.GetTotalMemory(false);
                        memoryMeasurements.Add(currentMemory);
                    }
                }

                var finalMemory = GC.GetTotalMemory(true);
                var memoryGrowth = finalMemory - initialMemory;

                return (eventCount, TimeSpan.FromSeconds(1), new List<double> { memoryGrowth });
            });
        }

        /// <summary>
        /// Benchmark high concurrency stress test
        /// </summary>
        public async Task<BenchmarkResult> BenchmarkHighConcurrencyAsync()
        {
            return await RunBenchmarkAsync("High Concurrency", async (orchestrator, machines) =>
            {
                var concurrencyLevel = _config.ConcurrencyLevel;
                var eventsPerThread = _config.StressEventCount / concurrencyLevel;
                var stopwatch = Stopwatch.StartNew();

                var tasks = new List<Task>();
                for (int thread = 0; thread < concurrencyLevel; thread++)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        for (int i = 0; i < eventsPerThread; i++)
                        {
                            var machineId = machines[(thread * eventsPerThread + i) % machines.Count];
                            await orchestrator.SendEventFireAndForgetAsync("bench", machineId, "PROCESS");
                        }
                    }));
                }

                await Task.WhenAll(tasks);
                stopwatch.Stop();

                return (concurrencyLevel * eventsPerThread, stopwatch.Elapsed, null);
            });
        }

        /// <summary>
        /// Benchmark burst traffic handling
        /// </summary>
        private async Task<BenchmarkResult> BenchmarkBurstTrafficAsync()
        {
            return await RunBenchmarkAsync("Burst Traffic", async (orchestrator, machines) =>
            {
                var burstSize = _config.BurstSize;
                var burstCount = _config.BurstCount;
                var totalEvents = burstSize * burstCount;
                var stopwatch = Stopwatch.StartNew();

                for (int burst = 0; burst < burstCount; burst++)
                {
                    // Send burst of events all at once
                    var tasks = new List<Task>();
                    for (int i = 0; i < burstSize; i++)
                    {
                        var machineId = machines[(burst * burstSize + i) % machines.Count];
                        tasks.Add(orchestrator.SendEventFireAndForgetAsync("bench", machineId, "PROCESS"));
                    }

                    await Task.WhenAll(tasks);

                    // Brief pause between bursts
                    await Task.Delay(100);
                }

                stopwatch.Stop();
                return (totalEvents, stopwatch.Elapsed, null);
            });
        }

        /// <summary>
        /// Benchmark long duration reliability
        /// </summary>
        private async Task<BenchmarkResult> BenchmarkLongDurationAsync()
        {
            return await RunBenchmarkAsync("Long Duration", async (orchestrator, machines) =>
            {
                var duration = _config.LongDurationTestTime;
                var eventsPerSecond = _config.LongDurationEventsPerSecond;
                var interval = TimeSpan.FromMilliseconds(1000.0 / eventsPerSecond);

                var stopwatch = Stopwatch.StartNew();
                var eventCount = 0;

                while (stopwatch.Elapsed < duration)
                {
                    var machineId = machines[eventCount % machines.Count];
                    await orchestrator.SendEventFireAndForgetAsync("bench", machineId, "PROCESS");
                    eventCount++;

                    await Task.Delay(interval);
                }

                stopwatch.Stop();
                return (eventCount, stopwatch.Elapsed, null);
            });
        }

        /// <summary>
        /// Generic benchmark runner with warmup and measurement phases
        /// </summary>
        private async Task<BenchmarkResult> RunBenchmarkAsync(
            string name,
            Func<EventBusOrchestrator, List<string>, Task<(int eventCount, TimeSpan duration, List<double>? measurements)>> benchmark,
            int? machineCount = null)
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                PoolSize = _config.EventBusPoolSize,
                EnableLogging = false,
                EnableMetrics = _config.EnableMetrics,
                EnableBackpressure = _config.EnableBackpressure,
                MaxQueueDepth = _config.MaxQueueDepth
            });

            var machines = await SetupBenchmarkMachinesAsync(orchestrator, machineCount ?? _config.DefaultMachineCount);

            // Warmup phase
            for (int i = 0; i < _config.WarmupIterations; i++)
            {
                await benchmark(orchestrator, machines);
                await Task.Delay(100);
            }

            // Measurement phase
            var measurements = new List<double>();
            var totalEvents = 0;
            var totalDuration = TimeSpan.Zero;

            for (int i = 0; i < _config.MeasurementIterations; i++)
            {
                var (eventCount, duration, iterationMeasurements) = await benchmark(orchestrator, machines);
                totalEvents += eventCount;
                totalDuration = totalDuration.Add(duration);

                if (iterationMeasurements != null)
                {
                    measurements.AddRange(iterationMeasurements);
                }

                await Task.Delay(100);
            }

            var avgDuration = TimeSpan.FromTicks(totalDuration.Ticks / _config.MeasurementIterations);
            var eventsPerSecond = totalEvents / totalDuration.TotalSeconds;
            var avgLatency = measurements.Any() ? measurements.Average() : 0;

            return new BenchmarkResult
            {
                BenchmarkName = name,
                Success = true,
                EventCount = totalEvents,
                Duration = avgDuration,
                EventsPerSecond = eventsPerSecond,
                AverageLatency = avgLatency,
                LatencyPercentiles = CalculatePercentiles(measurements),
                Measurements = measurements
            };
        }

        /// <summary>
        /// Setup benchmark machines for testing
        /// </summary>
        private async Task<List<string>> SetupBenchmarkMachinesAsync(EventBusOrchestrator orchestrator, int machineCount)
        {
            var machines = new List<string>();
            var processedCount = 0;

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["process"] = (ctx) =>
                {
                    Interlocked.Increment(ref processedCount);
                    // Simulate minimal processing work
                    Thread.SpinWait(100);
                },
                ["request"] = (ctx) =>
                {
                    Interlocked.Increment(ref processedCount);
                    // Simulate request processing
                    Thread.SpinWait(200);
                }
            };

            for (int i = 0; i < machineCount; i++)
            {
                var machineId = $"bench_machine_{i}";
                machines.Add(machineId);

                var json = $@"{{
                    ""id"": ""{machineId}"",
                    ""initial"": ""ready"",
                    ""states"": {{
                        ""ready"": {{
                            ""entry"": [""process""],
                            ""on"": {{
                                ""PROCESS"": ""ready"",
                                ""REQUEST"": ""ready""
                            }}
                        }}
                    }}
                }}";

                var machine = PureStateMachineFactory.CreateFromScript(machineId, json, orchestrator, actions);
                await orchestrator.StartMachineAsync(machineId);
            }

            return machines;
        }

        /// <summary>
        /// Calculate latency percentiles from measurements
        /// </summary>
        private Dictionary<string, double> CalculatePercentiles(List<double> measurements)
        {
            if (!measurements.Any())
                return new Dictionary<string, double>();

            var sorted = measurements.OrderBy(x => x).ToList();

            return new Dictionary<string, double>
            {
                ["P50"] = GetPercentile(sorted, 0.5),
                ["P90"] = GetPercentile(sorted, 0.9),
                ["P95"] = GetPercentile(sorted, 0.95),
                ["P99"] = GetPercentile(sorted, 0.99),
                ["P99.9"] = GetPercentile(sorted, 0.999)
            };
        }

        private double GetPercentile(List<double> sortedData, double percentile)
        {
            if (!sortedData.Any()) return 0;
            var index = (int)Math.Ceiling(sortedData.Count * percentile) - 1;
            return sortedData[Math.Max(0, Math.Min(index, sortedData.Count - 1))];
        }
    }

    #region Configuration and Result Types

    public class BenchmarkConfig
    {
        public int WarmupIterations { get; set; } = 3;
        public int MeasurementIterations { get; set; } = 5;

        public int DefaultMachineCount { get; set; } = 10;
        public int EventBusPoolSize { get; set; } = 4;
        public bool EnableMetrics { get; set; } = false;
        public bool EnableBackpressure { get; set; } = false;
        public int MaxQueueDepth { get; set; } = 10000;

        // Throughput benchmarks
        public int ThroughputEventCount { get; set; } = 10000;

        // Latency benchmarks
        public int LatencyEventCount { get; set; } = 1000;

        // Scalability benchmarks
        public int ScalabilityEventCount { get; set; } = 5000;

        // Memory benchmarks
        public int MemoryTestEventCount { get; set; } = 50000;

        // Stress benchmarks
        public int ConcurrencyLevel { get; set; } = 10;
        public int StressEventCount { get; set; } = 20000;

        // Burst benchmarks
        public int BurstSize { get; set; } = 1000;
        public int BurstCount { get; set; } = 10;

        // Long duration benchmarks
        public TimeSpan LongDurationTestTime { get; set; } = TimeSpan.FromMinutes(2);
        public int LongDurationEventsPerSecond { get; set; } = 100;
    }

    public class BenchmarkResult
    {
        public string BenchmarkName { get; set; } = "";
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        public int EventCount { get; set; }
        public TimeSpan Duration { get; set; }
        public double EventsPerSecond { get; set; }
        public double AverageLatency { get; set; }

        public Dictionary<string, double> LatencyPercentiles { get; set; } = new();
        public List<double> Measurements { get; set; } = new();
        public List<ScalabilityDataPoint> ScalabilityData { get; set; } = new();
    }

    public class BenchmarkSuiteResult
    {
        public List<BenchmarkResult> Results { get; set; } = new();
        public TimeSpan TotalDuration { get; set; }
        public DateTime Timestamp { get; set; }
        public BenchmarkConfig Configuration { get; set; } = new();

        public BenchmarkResult? GetResult(string benchmarkName)
        {
            return Results.FirstOrDefault(r => r.BenchmarkName.Contains(benchmarkName));
        }

        public List<BenchmarkResult> SuccessfulResults => Results.Where(r => r.Success).ToList();
        public List<BenchmarkResult> FailedResults => Results.Where(r => !r.Success).ToList();
    }

    public class ScalabilityDataPoint
    {
        public int MachineCount { get; set; }
        public double EventsPerSecond { get; set; }
        public double AverageLatency { get; set; }
    }

    #endregion
}