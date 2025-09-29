using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using XStateNet.Benchmarking;

namespace OrchestratorTestApp
{
    public static class BenchmarkRunner
    {
        public static async Task RunFullBenchmarkSuite()
        {
            Console.WriteLine("🏎️  XStateNet Performance Benchmark Suite");
            Console.WriteLine("==========================================\n");

            try
            {
                // Configure benchmark parameters
                var config = new BenchmarkConfig
                {
                    WarmupIterations = 2,
                    MeasurementIterations = 3,
                    DefaultMachineCount = 10,
                    EventBusPoolSize = 4,
                    EnableMetrics = false, // Disable for pure performance measurement
                    EnableBackpressure = true,
                    MaxQueueDepth = 10000,

                    // Adjust event counts for reasonable test duration
                    ThroughputEventCount = 10000,
                    LatencyEventCount = 500,
                    ScalabilityEventCount = 5000,
                    MemoryTestEventCount = 20000,
                    StressEventCount = 15000,

                    // Stress test parameters
                    ConcurrencyLevel = 8,
                    BurstSize = 500,
                    BurstCount = 10,

                    // Long duration test (shorter for demo)
                    LongDurationTestTime = TimeSpan.FromSeconds(30),
                    LongDurationEventsPerSecond = 200
                };

                // Run the benchmark suite
                var framework = new BenchmarkFramework(config);
                var results = await framework.RunBenchmarkSuiteAsync();

                // Generate comprehensive report
                var reporter = new BenchmarkReporter(results);
                reporter.GenerateConsoleReport();

                // Export results
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var exportDir = Path.Combine(Environment.CurrentDirectory, "BenchmarkResults");
                Directory.CreateDirectory(exportDir);

                var jsonPath = Path.Combine(exportDir, $"benchmark_results_{timestamp}.json");
                var csvPath = Path.Combine(exportDir, $"benchmark_results_{timestamp}.csv");
                var mdPath = Path.Combine(exportDir, $"benchmark_report_{timestamp}.md");

                reporter.ExportToJson(jsonPath);
                reporter.ExportToCsv(csvPath);
                reporter.ExportToMarkdown(mdPath);

                Console.WriteLine("\n📁 Results exported to:");
                Console.WriteLine($"   • JSON: {jsonPath}");
                Console.WriteLine($"   • CSV: {csvPath}");
                Console.WriteLine($"   • Markdown: {mdPath}");

                // Show summary statistics
                ShowBenchmarkSummary(results);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Benchmark suite failed: {ex.Message}");
                if (ex.StackTrace != null)
                {
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        public static async Task RunQuickBenchmark()
        {
            Console.WriteLine("⚡ XStateNet Quick Performance Benchmark");
            Console.WriteLine("========================================\n");

            try
            {
                // Quick benchmark configuration
                var config = new BenchmarkConfig
                {
                    WarmupIterations = 1,
                    MeasurementIterations = 1,
                    DefaultMachineCount = 5,
                    EventBusPoolSize = 4,
                    EnableMetrics = false,

                    ThroughputEventCount = 1000,
                    LatencyEventCount = 100,
                    ScalabilityEventCount = 500
                };

                var framework = new BenchmarkFramework(config);

                // Run only core benchmarks
                Console.WriteLine("Running core performance benchmarks...\n");

                var results = await RunCoreBenchmarks(framework);

                // Quick summary
                Console.WriteLine("\n" + "=".PadLeft(60, '='));
                Console.WriteLine("QUICK BENCHMARK SUMMARY");
                Console.WriteLine("=".PadLeft(60, '='));

                foreach (var result in results)
                {
                    if (result.Success)
                    {
                        Console.WriteLine($"✅ {result.BenchmarkName}:");
                        Console.WriteLine($"   Throughput: {result.EventsPerSecond:F0} events/sec");
                        if (result.AverageLatency > 0)
                        {
                            Console.WriteLine($"   Latency: {result.AverageLatency:F2} ms");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"❌ {result.BenchmarkName}: {result.ErrorMessage}");
                    }
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Quick benchmark failed: {ex.Message}");
            }
        }

        private static async Task<BenchmarkResult[]> RunCoreBenchmarks(BenchmarkFramework framework)
        {
            var results = new BenchmarkResult[3];

            // Test parallel throughput
            Console.WriteLine("🔄 Testing parallel throughput...");
            results[0] = await framework.BenchmarkParallelThroughputAsync();

            // Test single event latency
            Console.WriteLine("⚡ Testing single event latency...");
            results[1] = await framework.BenchmarkSingleEventLatencyAsync();

            // Test concurrency
            Console.WriteLine("🚀 Testing high concurrency...");
            results[2] = await framework.BenchmarkHighConcurrencyAsync();

            return results;
        }

        public static async Task RunLatencyFocusedBenchmark()
        {
            Console.WriteLine("⚡ XStateNet Latency-Focused Benchmark");
            Console.WriteLine("=====================================\n");

            var config = new BenchmarkConfig
            {
                WarmupIterations = 3,
                MeasurementIterations = 5,
                DefaultMachineCount = 1, // Single machine for minimal overhead
                EventBusPoolSize = 1,
                EnableMetrics = false,
                EnableBackpressure = false, // Minimize latency

                LatencyEventCount = 1000
            };

            var framework = new BenchmarkFramework(config);

            try
            {
                Console.WriteLine("Running latency-optimized benchmarks...\n");

                var singleLatency = await framework.BenchmarkSingleEventLatencyAsync();
                var requestResponse = await framework.BenchmarkRequestResponseLatencyAsync();

                Console.WriteLine("LATENCY BENCHMARK RESULTS");
                Console.WriteLine("========================");

                if (singleLatency.Success)
                {
                    Console.WriteLine($"⚡ Single Event Latency: {singleLatency.AverageLatency:F3} ms average");
                    if (singleLatency.LatencyPercentiles.Any())
                    {
                        Console.WriteLine("   Percentiles:");
                        foreach (var p in singleLatency.LatencyPercentiles)
                        {
                            Console.WriteLine($"   {p.Key}: {p.Value:F3} ms");
                        }
                    }
                }

                if (requestResponse.Success)
                {
                    Console.WriteLine($"\n🔄 Request-Response Latency: {requestResponse.AverageLatency:F3} ms average");
                    if (requestResponse.LatencyPercentiles.Any())
                    {
                        Console.WriteLine("   Percentiles:");
                        foreach (var p in requestResponse.LatencyPercentiles)
                        {
                            Console.WriteLine($"   {p.Key}: {p.Value:F3} ms");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Latency benchmark failed: {ex.Message}");
            }
        }

        public static async Task RunThroughputFocusedBenchmark()
        {
            Console.WriteLine("🚀 XStateNet Throughput-Focused Benchmark");
            Console.WriteLine("=========================================\n");

            var config = new BenchmarkConfig
            {
                WarmupIterations = 2,
                MeasurementIterations = 3,
                DefaultMachineCount = 20, // Many machines for parallelism
                EventBusPoolSize = 8,
                EnableMetrics = false,
                EnableBackpressure = true,
                MaxQueueDepth = 50000,

                ThroughputEventCount = 50000
            };

            var framework = new BenchmarkFramework(config);

            try
            {
                Console.WriteLine("Running throughput-optimized benchmarks...\n");

                var sequential = await framework.BenchmarkSequentialThroughputAsync();
                var parallel = await framework.BenchmarkParallelThroughputAsync();
                var concurrency = await framework.BenchmarkHighConcurrencyAsync();

                Console.WriteLine("THROUGHPUT BENCHMARK RESULTS");
                Console.WriteLine("===========================");

                if (sequential.Success)
                {
                    Console.WriteLine($"📊 Sequential Throughput: {sequential.EventsPerSecond:F0} events/sec");
                }

                if (parallel.Success)
                {
                    Console.WriteLine($"🚀 Parallel Throughput: {parallel.EventsPerSecond:F0} events/sec");

                    if (sequential.Success)
                    {
                        var improvement = (parallel.EventsPerSecond / sequential.EventsPerSecond - 1) * 100;
                        Console.WriteLine($"   Parallel Improvement: {improvement:F1}%");
                    }
                }

                if (concurrency.Success)
                {
                    Console.WriteLine($"⚡ High Concurrency: {concurrency.EventsPerSecond:F0} events/sec");
                }

                // Calculate theoretical maximum
                var machineCount = config.DefaultMachineCount;
                var poolSize = config.EventBusPoolSize;
                Console.WriteLine($"\n📈 Configuration: {machineCount} machines, {poolSize} event buses");

                if (parallel.Success)
                {
                    var eventsPerMachine = parallel.EventsPerSecond / machineCount;
                    Console.WriteLine($"📊 Events per machine: {eventsPerMachine:F0} events/sec");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Throughput benchmark failed: {ex.Message}");
            }
        }

        private static void ShowBenchmarkSummary(BenchmarkSuiteResult results)
        {
            Console.WriteLine("\n🎯 BENCHMARK SUITE SUMMARY");
            Console.WriteLine("===========================");

            var successful = results.SuccessfulResults;
            var throughputResults = successful.Where(r => r.BenchmarkName.Contains("Throughput")).ToList();
            var latencyResults = successful.Where(r => r.BenchmarkName.Contains("Latency") && r.AverageLatency > 0).ToList();

            if (throughputResults.Any())
            {
                var maxThroughput = throughputResults.Max(r => r.EventsPerSecond);
                var avgThroughput = throughputResults.Average(r => r.EventsPerSecond);

                Console.WriteLine($"🚀 Peak Throughput: {maxThroughput:F0} events/second");
                Console.WriteLine($"📊 Average Throughput: {avgThroughput:F0} events/second");

                // Performance classification
                if (maxThroughput > 100000)
                    Console.WriteLine("   🏆 EXCELLENT - Enterprise-grade performance");
                else if (maxThroughput > 50000)
                    Console.WriteLine("   ✅ GOOD - Production-ready performance");
                else if (maxThroughput > 10000)
                    Console.WriteLine("   ⚠️  FAIR - Suitable for moderate loads");
                else
                    Console.WriteLine("   ❌ POOR - May need optimization");
            }

            if (latencyResults.Any())
            {
                var avgLatency = latencyResults.Average(r => r.AverageLatency);
                Console.WriteLine($"⚡ Average Latency: {avgLatency:F2} ms");

                // Latency classification
                if (avgLatency < 1)
                    Console.WriteLine("   🏆 EXCELLENT - Ultra-low latency");
                else if (avgLatency < 5)
                    Console.WriteLine("   ✅ GOOD - Low latency");
                else if (avgLatency < 20)
                    Console.WriteLine("   ⚠️  FAIR - Moderate latency");
                else
                    Console.WriteLine("   ❌ POOR - High latency");
            }

            Console.WriteLine($"\n📊 Total Tests: {results.Results.Count}");
            Console.WriteLine($"✅ Passed: {results.SuccessfulResults.Count}");
            Console.WriteLine($"❌ Failed: {results.FailedResults.Count}");
            Console.WriteLine($"⏱️  Duration: {results.TotalDuration.TotalMinutes:F1} minutes");
        }
    }
}