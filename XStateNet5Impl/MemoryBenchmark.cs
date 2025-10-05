using System.Diagnostics;

namespace XStateNet;

public class MemoryBenchmark
{
    private const int Iterations = 10000;

    public void RunMemoryBenchmark()
    {
        Console.WriteLine("=== Memory Allocation Benchmark ===\n");
        Console.WriteLine("Testing List pooling effectiveness...\n");

        // Test pool effectiveness
        TestPoolStats();

        Console.WriteLine("\n=== Transition Path Memory Test ===\n");
        TestTransitionMemory();
    }

    private void TestPoolStats()
    {
        Console.WriteLine("Pool Effectiveness Test:");
        Console.WriteLine("------------------------");

        // Clear pools first
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocsBefore = GC.GetTotalMemory(false);
        var lists = new List<List<string>>();

        // Test without pooling (simulate)
        for (int i = 0; i < 1000; i++)
        {
            lists.Add(new List<string>(32));
        }

        var allocsWithoutPool = GC.GetTotalMemory(false) - allocsBefore;
        lists.Clear();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        allocsBefore = GC.GetTotalMemory(false);

        // Test with pooling
        for (int i = 0; i < 1000; i++)
        {
            var list = PerformanceOptimizations.RentStringList();
            list.Add("test");
            PerformanceOptimizations.ReturnStringList(list);
        }

        var allocsWithPool = GC.GetTotalMemory(false) - allocsBefore;

        Console.WriteLine($"Memory without pooling: {allocsWithoutPool / 1024.0:F2} KB");
        Console.WriteLine($"Memory with pooling: {allocsWithPool / 1024.0:F2} KB");
        Console.WriteLine($"Memory saved: {(allocsWithoutPool - allocsWithPool) / 1024.0:F2} KB");
        Console.WriteLine($"Reduction: {(1 - (double)allocsWithPool / allocsWithoutPool) * 100:F1}%");
    }

    private void TestTransitionMemory()
    {
        Console.WriteLine("Testing memory allocations in transition methods:");
        Console.WriteLine("--------------------------------------------------");

        // Warm up pooling
        for (int i = 0; i < 100; i++)
        {
            var warmupList = PerformanceOptimizations.RentStringList();
            PerformanceOptimizations.ReturnStringList(warmupList);

            var warmupTransitionList = PerformanceOptimizations.RentTransitionList();
            PerformanceOptimizations.ReturnTransitionList(warmupTransitionList);
        }

        // Measure before
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var initialMemory = GC.GetTotalMemory(false);
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);

        var sw = Stopwatch.StartNew();

        // Simulate heavy list usage with pooling
        for (int i = 0; i < Iterations; i++)
        {
            // Simulate string list operations
            var stringList = PerformanceOptimizations.RentStringList();
            for (int j = 0; j < 10; j++)
            {
                stringList.Add($"state{j}");
            }
            PerformanceOptimizations.ReturnStringList(stringList);

            // Simulate transition list operations
            var transitionList = PerformanceOptimizations.RentTransitionList();
            // Just simulating usage without actual transitions
            PerformanceOptimizations.ReturnTransitionList(transitionList);

            // Simulate path building
            var path = PerformanceOptimizations.BuildPath("parent", "child", "subchild");
        }

        sw.Stop();

        // Measure after
        var finalMemory = GC.GetTotalMemory(false);
        var gen0After = GC.CollectionCount(0);
        var gen1After = GC.CollectionCount(1);
        var gen2After = GC.CollectionCount(2);

        var memoryUsed = finalMemory - initialMemory;
        var gen0Collections = gen0After - gen0Before;
        var gen1Collections = gen1After - gen1Before;
        var gen2Collections = gen2After - gen2Before;

        Console.WriteLine($"Iterations: {Iterations:N0}");
        Console.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"Throughput: {Iterations * 1000.0 / sw.ElapsedMilliseconds:F2} ops/sec");
        Console.WriteLine();
        Console.WriteLine($"Memory allocated: {memoryUsed / 1024.0:F2} KB");
        Console.WriteLine($"Memory per iteration: {memoryUsed / (double)Iterations:F2} bytes");
        Console.WriteLine();
        Console.WriteLine($"Gen 0 collections: {gen0Collections}");
        Console.WriteLine($"Gen 1 collections: {gen1Collections}");
        Console.WriteLine($"Gen 2 collections: {gen2Collections}");

        // Compare with non-pooled version
        Console.WriteLine("\n--- Comparison with non-pooled allocations ---");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        initialMemory = GC.GetTotalMemory(false);
        gen0Before = GC.CollectionCount(0);

        sw.Restart();

        // Simulate same operations without pooling
        for (int i = 0; i < Iterations; i++)
        {
            var stringList = new List<string>(32);
            for (int j = 0; j < 10; j++)
            {
                stringList.Add($"state{j}");
            }

            var transitionList = new List<(CompoundState state, Transition transition, string @event)>(16);

            var pathParts = new[] { "parent", "child", "subchild" };
            var path = string.Join(".", pathParts);
        }

        sw.Stop();

        finalMemory = GC.GetTotalMemory(false);
        gen0After = GC.CollectionCount(0);

        var memoryUsedNoPool = finalMemory - initialMemory;
        var gen0CollectionsNoPool = gen0After - gen0Before;

        Console.WriteLine($"Memory without pooling: {memoryUsedNoPool / 1024.0:F2} KB");
        Console.WriteLine($"Memory with pooling: {memoryUsed / 1024.0:F2} KB");
        Console.WriteLine($"Memory saved: {(memoryUsedNoPool - memoryUsed) / 1024.0:F2} KB");
        Console.WriteLine($"GC Gen0 without pooling: {gen0CollectionsNoPool}");
        Console.WriteLine($"GC Gen0 with pooling: {gen0Collections}");
        Console.WriteLine($"GC reduction: {((gen0CollectionsNoPool - gen0Collections) / (double)gen0CollectionsNoPool * 100):F1}%");
    }

    public static void Main(string[] args)
    {
        var benchmark = new MemoryBenchmark();
        benchmark.RunMemoryBenchmark();
    }
}