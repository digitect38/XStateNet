using System;
using System.Collections.Generic;
using System.Linq;

namespace OrchestratorTestApp
{
    public static class BarChartDemo
    {
        public static void ShowPerformanceBarChart()
        {
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                  XSTATENET PERFORMANCE VISUALIZATION                        ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Throughput data from actual benchmark
            var throughputData = new Dictionary<string, double>
            {
                { "Sequential Events", 3910 },
                { "Parallel Events", 3695 },
                { "High Concurrency", 2217516 }
            };

            Console.WriteLine("🚀 THROUGHPUT COMPARISON (events/sec)");
            Console.WriteLine("════════════════════════════════════════════════════════════════════════════════");
            Console.WriteLine();

            var maxThroughput = throughputData.Values.Max();
            var barWidth = 50;

            // Show on linear scale (limited to max of first two for visibility)
            var maxLinear = Math.Max(throughputData["Sequential Events"], throughputData["Parallel Events"]);
            Console.WriteLine("Linear Scale (Sequential vs Parallel):");
            foreach (var kvp in throughputData.OrderBy(x => x.Value).Take(2))
            {
                var ratio = kvp.Value / maxLinear;
                var barLength = (int)(ratio * barWidth);
                var bar = new string('█', barLength);
                var spaces = new string(' ', barWidth - barLength);

                Console.WriteLine($"   {kvp.Key,-25} {bar}{spaces} {kvp.Value,12:N0} evt/s");
            }

            var improvement = (throughputData["Parallel Events"] / throughputData["Sequential Events"] - 1) * 100;
            Console.WriteLine();
            Console.WriteLine($"   📊 Parallel vs Sequential: {improvement:F1}% degradation");
            Console.WriteLine($"   💡 Why slower? Task creation overhead > benefit for fast operations");
            Console.WriteLine();

            // Show High Concurrency separately (logarithmic comparison)
            Console.WriteLine("High Concurrency (8 parallel threads):");
            var hcRatio = throughputData["High Concurrency"] / maxThroughput;
            var hcBar = new string('█', barWidth);
            Console.WriteLine($"   {"High Concurrency",-25} {hcBar} {throughputData["High Concurrency"],12:N0} evt/s");
            Console.WriteLine();
            Console.WriteLine($"   📊 High Concurrency vs Sequential: {throughputData["High Concurrency"] / throughputData["Sequential Events"]:F0}x faster");
            Console.WriteLine($"   💡 Why so fast? Multi-threaded fire-and-forget (measures queuing, not processing)");
            Console.WriteLine();

            // Actual processing capacity
            Console.WriteLine("════════════════════════════════════════════════════════════════════════════════");
            Console.WriteLine("REAL-WORLD PROCESSING CAPACITY");
            Console.WriteLine("════════════════════════════════════════════════════════════════════════════════");
            Console.WriteLine();

            var processingData = new Dictionary<string, double>
            {
                { "Per Machine", 185 },
                { "20 Machines Total", 3700 },
                { "Queue Capacity/sec", 2217516 }
            };

            Console.WriteLine("Actual Event Processing Rate:");
            var maxProcessing = processingData["20 Machines Total"];
            foreach (var kvp in processingData.Take(2))
            {
                var ratio = kvp.Value / maxProcessing;
                var barLength = (int)(ratio * barWidth);
                var bar = new string('█', barLength);
                var spaces = new string(' ', barWidth - barLength);

                Console.WriteLine($"   {kvp.Key,-25} {bar}{spaces} {kvp.Value,12:N0} evt/s");
            }

            Console.WriteLine();
            Console.WriteLine("   ⚠️  Queue fills in: 36 milliseconds at max rate");
            Console.WriteLine("   ✅ Sustainable throughput: ~3,700 events/sec");
            Console.WriteLine();

            // Latency data
            Console.WriteLine("════════════════════════════════════════════════════════════════════════════════");
            Console.WriteLine("⚡ LATENCY COMPARISON (lower is better)");
            Console.WriteLine("════════════════════════════════════════════════════════════════════════════════");
            Console.WriteLine();

            var latencyData = new Dictionary<string, (double avg, double p99)>
            {
                { "Single Event", (0.15, 0.25) },
                { "Request-Response", (1.2, 2.5) },
                { "Through Orchestrator", (5.4, 12.0) }
            };

            var maxLatency = latencyData.Values.Max(x => x.avg);
            foreach (var kvp in latencyData.OrderByDescending(x => x.Value.avg))
            {
                var ratio = kvp.Value.avg / maxLatency;
                var barLength = (int)(ratio * barWidth);
                var bar = new string('█', barLength);
                var spaces = new string(' ', barWidth - barLength);

                Console.WriteLine($"   {kvp.Key,-25} {bar}{spaces} {kvp.Value.avg,8:F2} ms");

                // P99
                var p99Ratio = kvp.Value.p99 / maxLatency;
                var p99BarLength = (int)(p99Ratio * barWidth);
                var p99Bar = new string('░', p99BarLength);
                var p99Spaces = new string(' ', barWidth - p99BarLength);
                Console.WriteLine($"   {"  └─ P99",-25} {p99Bar}{p99Spaces} {kvp.Value.p99,8:F2} ms");
            }

            Console.WriteLine();
            Console.WriteLine("════════════════════════════════════════════════════════════════════════════════");
            Console.WriteLine();
            Console.WriteLine("📊 KEY INSIGHTS:");
            Console.WriteLine("   • Sequential is faster than Parallel for fast operations (<1ms)");
            Console.WriteLine("   • High Concurrency = queuing speed, NOT processing speed");
            Console.WriteLine("   • Real throughput bottleneck = state machine processing (~3.7K evt/s)");
            Console.WriteLine("   • Lower latency with direct machine access vs orchestrator");
            Console.WriteLine();
        }
    }
}