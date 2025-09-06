using System;
using XStateNet;

namespace BenchmarkApp
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "memory")
            {
                Console.WriteLine("XStateNet Memory Allocation Benchmark");
                Console.WriteLine("======================================");
                Console.WriteLine();
                
                var memBenchmark = new MemoryBenchmark();
                memBenchmark.RunMemoryBenchmark();
            }
            else
            {
                Console.WriteLine("XStateNet Performance Benchmark");
                Console.WriteLine("================================");
                Console.WriteLine();
                
                // Run the comprehensive benchmark
                PerformanceBenchmark.BenchmarkTransitionPaths();
                
                Console.WriteLine("================================");
                Console.WriteLine();
                
                // Run a quick focused benchmark
                PerformanceBenchmark.RunQuickBenchmark();
            }
            
            Console.WriteLine();
            Console.WriteLine("Benchmark complete.");
        }
    }
}