using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XStateNet.Benchmarking
{
    /// <summary>
    /// Comprehensive benchmark reporting and analysis
    /// </summary>
    public class BenchmarkReporter
    {
        private readonly BenchmarkSuiteResult _results;

        public BenchmarkReporter(BenchmarkSuiteResult results)
        {
            _results = results;
        }

        /// <summary>
        /// Generate and display a comprehensive console report
        /// </summary>
        public void GenerateConsoleReport()
        {
            Console.Clear();
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                     XSTATENET PERFORMANCE BENCHMARK REPORT                  ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Executive Summary
            GenerateExecutiveSummary();
            Console.WriteLine();

            // Detailed Results
            GenerateDetailedResults();
            Console.WriteLine();

            // Performance Analysis
            GeneratePerformanceAnalysis();
            Console.WriteLine();

            // Scalability Analysis
            GenerateScalabilityAnalysis();
            Console.WriteLine();

            // Recommendations
            GenerateRecommendations();
        }

        private void GenerateExecutiveSummary()
        {
            Console.WriteLine("📊 EXECUTIVE SUMMARY");
            Console.WriteLine("════════════════════════════════════════════════════════════════════════════════");

            var successful = _results.SuccessfulResults;
            var failed = _results.FailedResults;

            Console.WriteLine($"🎯 Overall Results: {successful.Count}/{_results.Results.Count} benchmarks passed");
            Console.WriteLine($"⏱️  Total Duration: {_results.TotalDuration.TotalMinutes:F1} minutes");
            Console.WriteLine($"📅 Timestamp: {_results.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine();

            if (successful.Any())
            {
                var throughputResults = successful.Where(r => r.BenchmarkName.Contains("Throughput")).ToList();
                if (throughputResults.Any())
                {
                    var maxThroughput = throughputResults.Max(r => r.EventsPerSecond);
                    var avgThroughput = throughputResults.Average(r => r.EventsPerSecond);
                    Console.WriteLine($"🚀 Peak Throughput: {maxThroughput:F0} events/second");
                    Console.WriteLine($"📈 Average Throughput: {avgThroughput:F0} events/second");
                }

                var latencyResults = successful.Where(r => r.BenchmarkName.Contains("Latency")).ToList();
                if (latencyResults.Any())
                {
                    var avgLatency = latencyResults.Where(r => r.AverageLatency > 0).Average(r => r.AverageLatency);
                    Console.WriteLine($"⚡ Average Latency: {avgLatency:F2} ms");
                }
            }

            if (failed.Any())
            {
                Console.WriteLine();
                Console.WriteLine("⚠️  Failed Benchmarks:");
                foreach (var fail in failed)
                {
                    Console.WriteLine($"   • {fail.BenchmarkName}: {fail.ErrorMessage}");
                }
            }
        }

        private void GenerateDetailedResults()
        {
            Console.WriteLine("📈 DETAILED BENCHMARK RESULTS");
            Console.WriteLine("════════════════════════════════════════════════════════════════════════════════");

            foreach (var result in _results.Results)
            {
                Console.WriteLine($"🔍 {result.BenchmarkName}");

                if (!result.Success)
                {
                    Console.WriteLine($"   ❌ FAILED: {result.ErrorMessage}");
                    Console.WriteLine();
                    continue;
                }

                Console.WriteLine($"   ✅ SUCCESS");
                Console.WriteLine($"   📊 Events Processed: {result.EventCount:N0}");
                Console.WriteLine($"   ⏱️  Duration: {result.Duration.TotalSeconds:F2} seconds");
                Console.WriteLine($"   🚀 Throughput: {result.EventsPerSecond:F0} events/second");

                if (result.AverageLatency > 0)
                {
                    Console.WriteLine($"   ⚡ Average Latency: {result.AverageLatency:F2} ms");

                    if (result.LatencyPercentiles.Any())
                    {
                        Console.WriteLine("   📊 Latency Percentiles:");
                        foreach (var percentile in result.LatencyPercentiles)
                        {
                            Console.WriteLine($"      {percentile.Key}: {percentile.Value:F2} ms");
                        }
                    }
                }

                if (result.ScalabilityData.Any())
                {
                    Console.WriteLine("   📈 Scalability Data:");
                    foreach (var point in result.ScalabilityData)
                    {
                        Console.WriteLine($"      {point.MachineCount} machines: {point.EventsPerSecond:F0} events/sec");
                    }
                }

                Console.WriteLine();
            }
        }

        private void GeneratePerformanceAnalysis()
        {
            Console.WriteLine("🔬 PERFORMANCE ANALYSIS");
            Console.WriteLine("════════════════════════════════════════════════════════════════════════════════");

            var successful = _results.SuccessfulResults;

            // Throughput Analysis
            var throughputResults = successful.Where(r => r.BenchmarkName.Contains("Throughput")).ToList();
            if (throughputResults.Any())
            {
                Console.WriteLine("🚀 Throughput Analysis:");
                var sequential = throughputResults.FirstOrDefault(r => r.BenchmarkName.Contains("Sequential"));
                var parallel = throughputResults.FirstOrDefault(r => r.BenchmarkName.Contains("Parallel"));

                if (sequential != null && parallel != null)
                {
                    var improvement = (parallel.EventsPerSecond / sequential.EventsPerSecond - 1) * 100;
                    Console.WriteLine($"   • Sequential: {sequential.EventsPerSecond:F0} events/sec");
                    Console.WriteLine($"   • Parallel: {parallel.EventsPerSecond:F0} events/sec");
                    Console.WriteLine($"   • Parallel Improvement: {improvement:F1}%");
                }
                Console.WriteLine();
            }

            // Latency Analysis
            var latencyResults = successful.Where(r => r.BenchmarkName.Contains("Latency") && r.AverageLatency > 0).ToList();
            if (latencyResults.Any())
            {
                Console.WriteLine("⚡ Latency Analysis:");
                foreach (var result in latencyResults)
                {
                    Console.WriteLine($"   • {result.BenchmarkName}: {result.AverageLatency:F2} ms average");
                    if (result.LatencyPercentiles.ContainsKey("P99"))
                    {
                        Console.WriteLine($"     P99: {result.LatencyPercentiles["P99"]:F2} ms");
                    }
                }
                Console.WriteLine();
            }

            // Stress Test Analysis
            var stressResults = successful.Where(r => r.BenchmarkName.Contains("Stress") || r.BenchmarkName.Contains("Concurrency")).ToList();
            if (stressResults.Any())
            {
                Console.WriteLine("💪 Stress Test Analysis:");
                foreach (var result in stressResults)
                {
                    Console.WriteLine($"   • {result.BenchmarkName}:");
                    Console.WriteLine($"     Throughput: {result.EventsPerSecond:F0} events/sec");
                    Console.WriteLine($"     Events: {result.EventCount:N0} in {result.Duration.TotalSeconds:F1}s");
                }
                Console.WriteLine();
            }
        }

        private void GenerateScalabilityAnalysis()
        {
            Console.WriteLine("📈 SCALABILITY ANALYSIS");
            Console.WriteLine("════════════════════════════════════════════════════════════════════════════════");

            var scalabilityResults = _results.SuccessfulResults.Where(r => r.ScalabilityData.Any()).ToList();

            foreach (var result in scalabilityResults)
            {
                Console.WriteLine($"🔍 {result.BenchmarkName}:");

                var data = result.ScalabilityData.OrderBy(d => d.MachineCount).ToList();
                var baseline = data.FirstOrDefault()?.EventsPerSecond ?? 0;

                foreach (var point in data)
                {
                    var efficiency = baseline > 0 ? (point.EventsPerSecond / baseline / point.MachineCount) * 100 : 0;
                    Console.WriteLine($"   • {point.MachineCount} units: {point.EventsPerSecond:F0} events/sec (Efficiency: {efficiency:F1}%)");
                }

                // Calculate scalability metrics
                if (data.Count >= 2)
                {
                    var first = data.First();
                    var last = data.Last();
                    var scalabilityFactor = last.EventsPerSecond / first.EventsPerSecond;
                    var resourceFactor = (double)last.MachineCount / first.MachineCount;
                    var efficiency = (scalabilityFactor / resourceFactor) * 100;

                    Console.WriteLine($"   📊 Overall Scalability: {scalabilityFactor:F2}x performance with {resourceFactor:F1}x resources");
                    Console.WriteLine($"   📊 Scaling Efficiency: {efficiency:F1}%");
                }

                Console.WriteLine();
            }
        }

        private void GenerateRecommendations()
        {
            Console.WriteLine("💡 PERFORMANCE RECOMMENDATIONS");
            Console.WriteLine("════════════════════════════════════════════════════════════════════════════════");

            var recommendations = new List<string>();
            var successful = _results.SuccessfulResults;

            // Throughput recommendations
            var throughputResults = successful.Where(r => r.BenchmarkName.Contains("Throughput")).ToList();
            if (throughputResults.Any())
            {
                var maxThroughput = throughputResults.Max(r => r.EventsPerSecond);
                if (maxThroughput < 10000)
                {
                    recommendations.Add("🚀 Consider enabling backpressure and increasing event bus pool size for higher throughput");
                }
                else if (maxThroughput > 50000)
                {
                    recommendations.Add("✅ Excellent throughput performance - current configuration is well-optimized");
                }
            }

            // Latency recommendations
            var latencyResults = successful.Where(r => r.BenchmarkName.Contains("Latency") && r.AverageLatency > 0).ToList();
            if (latencyResults.Any())
            {
                var avgLatency = latencyResults.Average(r => r.AverageLatency);
                if (avgLatency > 10)
                {
                    recommendations.Add("⚡ High latency detected - consider reducing queue depths or optimizing action processing");
                }
                else if (avgLatency < 1)
                {
                    recommendations.Add("✅ Excellent low-latency performance");
                }
            }

            // Scalability recommendations
            var scalabilityResults = successful.Where(r => r.ScalabilityData.Any()).ToList();
            foreach (var result in scalabilityResults)
            {
                var data = result.ScalabilityData.OrderBy(d => d.MachineCount).ToList();
                if (data.Count >= 2)
                {
                    var first = data.First();
                    var last = data.Last();
                    var efficiency = (last.EventsPerSecond / first.EventsPerSecond / last.MachineCount * first.MachineCount) * 100;

                    if (efficiency < 50)
                    {
                        recommendations.Add($"📈 {result.BenchmarkName} shows poor scaling efficiency ({efficiency:F1}%) - investigate bottlenecks");
                    }
                    else if (efficiency > 80)
                    {
                        recommendations.Add($"✅ {result.BenchmarkName} shows excellent scaling efficiency ({efficiency:F1}%)");
                    }
                }
            }

            // Memory recommendations
            var memoryResult = successful.FirstOrDefault(r => r.BenchmarkName.Contains("Memory"));
            if (memoryResult != null && memoryResult.Measurements.Any())
            {
                var memoryGrowth = memoryResult.Measurements.First();
                if (memoryGrowth > 100_000_000) // 100MB
                {
                    recommendations.Add("🧠 High memory usage detected - consider implementing more aggressive cleanup policies");
                }
            }

            // Configuration recommendations
            if (_results.Configuration.EventBusPoolSize < 4)
            {
                recommendations.Add("⚙️ Consider increasing EventBusPoolSize to 4 or higher for better parallelism");
            }

            if (!_results.Configuration.EnableBackpressure)
            {
                recommendations.Add("⚙️ Consider enabling backpressure for high-throughput scenarios");
            }

            // Display recommendations
            if (recommendations.Any())
            {
                foreach (var recommendation in recommendations)
                {
                    Console.WriteLine($"• {recommendation}");
                }
            }
            else
            {
                Console.WriteLine("✅ No specific recommendations - performance is excellent across all metrics!");
            }

            Console.WriteLine();
            Console.WriteLine("🎯 OPTIMAL CONFIGURATION SUGGESTIONS:");
            Console.WriteLine("   • EventBusPoolSize: 4-8 (based on CPU cores)");
            Console.WriteLine("   • EnableBackpressure: true (for high throughput)");
            Console.WriteLine("   • MaxQueueDepth: 10000-50000 (based on memory constraints)");
            Console.WriteLine("   • ThrottleDelay: 0-1ms (based on target latency)");
        }

        /// <summary>
        /// Export benchmark results to JSON file
        /// </summary>
        public void ExportToJson(string filePath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() }
            };

            var json = JsonSerializer.Serialize(_results, options);
            File.WriteAllText(filePath, json);

            Console.WriteLine($"📄 Benchmark results exported to: {filePath}");
        }

        /// <summary>
        /// Export benchmark results to CSV file
        /// </summary>
        public void ExportToCsv(string filePath)
        {
            var csv = new StringBuilder();
            csv.AppendLine("BenchmarkName,Success,EventCount,Duration(s),EventsPerSecond,AverageLatency(ms),P95Latency(ms),P99Latency(ms)");

            foreach (var result in _results.Results)
            {
                var p95 = result.LatencyPercentiles.GetValueOrDefault("P95", 0);
                var p99 = result.LatencyPercentiles.GetValueOrDefault("P99", 0);

                csv.AppendLine($"\"{result.BenchmarkName}\",{result.Success},{result.EventCount}," +
                              $"{result.Duration.TotalSeconds:F2},{result.EventsPerSecond:F0}," +
                              $"{result.AverageLatency:F2},{p95:F2},{p99:F2}");
            }

            File.WriteAllText(filePath, csv.ToString());
            Console.WriteLine($"📊 Benchmark results exported to: {filePath}");
        }

        /// <summary>
        /// Generate a markdown report
        /// </summary>
        public void ExportToMarkdown(string filePath)
        {
            var md = new StringBuilder();

            md.AppendLine("# XStateNet Performance Benchmark Report");
            md.AppendLine();
            md.AppendLine($"**Generated:** {_results.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
            md.AppendLine($"**Duration:** {_results.TotalDuration.TotalMinutes:F1} minutes");
            md.AppendLine($"**Results:** {_results.SuccessfulResults.Count}/{_results.Results.Count} benchmarks passed");
            md.AppendLine();

            md.AppendLine("## Executive Summary");
            md.AppendLine();

            var successful = _results.SuccessfulResults;
            if (successful.Any())
            {
                var throughputResults = successful.Where(r => r.BenchmarkName.Contains("Throughput")).ToList();
                if (throughputResults.Any())
                {
                    var maxThroughput = throughputResults.Max(r => r.EventsPerSecond);
                    md.AppendLine($"- **Peak Throughput:** {maxThroughput:F0} events/second");
                }

                var latencyResults = successful.Where(r => r.BenchmarkName.Contains("Latency") && r.AverageLatency > 0).ToList();
                if (latencyResults.Any())
                {
                    var avgLatency = latencyResults.Average(r => r.AverageLatency);
                    md.AppendLine($"- **Average Latency:** {avgLatency:F2} ms");
                }
            }

            md.AppendLine();
            md.AppendLine("## Detailed Results");
            md.AppendLine();
            md.AppendLine("| Benchmark | Status | Events | Duration | Throughput | Avg Latency | P95 | P99 |");
            md.AppendLine("|-----------|--------|--------|----------|------------|-------------|-----|-----|");

            foreach (var result in _results.Results)
            {
                var status = result.Success ? "✅" : "❌";
                var p95 = result.LatencyPercentiles.GetValueOrDefault("P95", 0);
                var p99 = result.LatencyPercentiles.GetValueOrDefault("P99", 0);

                md.AppendLine($"| {result.BenchmarkName} | {status} | {result.EventCount:N0} | " +
                             $"{result.Duration.TotalSeconds:F2}s | {result.EventsPerSecond:F0} evt/s | " +
                             $"{result.AverageLatency:F2}ms | {p95:F2}ms | {p99:F2}ms |");
            }

            File.WriteAllText(filePath, md.ToString());
            Console.WriteLine($"📝 Markdown report exported to: {filePath}");
        }
    }
}