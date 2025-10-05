using System.Diagnostics;

namespace XStateNet;

/// <summary>
/// Simple performance benchmark for transition path calculations
/// </summary>
public static class PerformanceBenchmark
{
    /// <summary>
    /// Benchmark the old LINQ Except approach vs new HashSet approach
    /// </summary>
    public static void BenchmarkTransitionPaths()
    {
        Console.WriteLine("=== XStateNet Performance Benchmark ===");
        Console.WriteLine();

        // Create test data sets of varying sizes
        var testSizes = new[] { 10, 50, 100, 500, 1000 };

        foreach (var size in testSizes)
        {
            var sourceStates = GenerateStateList("source", size);
            var targetStates = GenerateStateList("target", size);

            // Add some overlap (50% common states)
            var commonStates = GenerateStateList("common", size / 2);
            sourceStates.AddRange(commonStates);
            targetStates.AddRange(commonStates);

            Console.WriteLine($"Test with {size} states (plus {size / 2} common):");

            // Benchmark old LINQ approach
            var linqTime = BenchmarkLinqApproach(sourceStates, targetStates, 1000);
            Console.WriteLine($"  LINQ Except:  {linqTime:F3}ms");

            // Benchmark new HashSet approach
            var hashSetTime = BenchmarkHashSetApproach(sourceStates, targetStates, 1000);
            Console.WriteLine($"  HashSet:      {hashSetTime:F3}ms");

            // Calculate improvement
            var improvement = ((linqTime - hashSetTime) / linqTime) * 100;
            Console.WriteLine($"  Improvement:  {improvement:F1}%");
            Console.WriteLine();
        }
    }

    private static List<string> GenerateStateList(string prefix, int count)
    {
        var list = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add($"#{prefix}.state{i}.substate{i}");
        }
        return list;
    }

    private static double BenchmarkLinqApproach(List<string> source, List<string> target, int iterations)
    {
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            // Old LINQ approach
            var sourceExit = source.Except(target).ToList();
            var targetEntry = target.Except(source).ToList();
        }

        sw.Stop();
        return sw.Elapsed.TotalMilliseconds / iterations;
    }

    private static double BenchmarkHashSetApproach(List<string> source, List<string> target, int iterations)
    {
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            // New HashSet approach
            var targetSet = new HashSet<string>(target);
            var sourceSet = new HashSet<string>(source);

            var sourceExit = new List<string>();
            var targetEntry = new List<string>();

            foreach (var state in source)
            {
                if (!targetSet.Contains(state))
                    sourceExit.Add(state);
            }

            foreach (var state in target)
            {
                if (!sourceSet.Contains(state))
                    targetEntry.Add(state);
            }
        }

        sw.Stop();
        return sw.Elapsed.TotalMilliseconds / iterations;
    }

    /// <summary>
    /// Run a simple benchmark and report results
    /// </summary>
    public static void RunQuickBenchmark()
    {
        Console.WriteLine("Quick Performance Test:");

        // Simulate a complex state hierarchy
        var source = new List<string>
        {
            "#machine.parent1.child1.subchild1",
            "#machine.parent1.child1.subchild2",
            "#machine.parent1.child2",
            "#machine.parent2.child1",
            "#machine.common1",
            "#machine.common2",
            "#machine.common3"
        };

        var target = new List<string>
        {
            "#machine.parent3.child1.subchild1",
            "#machine.parent3.child2",
            "#machine.parent4.child1",
            "#machine.common1",
            "#machine.common2",
            "#machine.common3",
            "#machine.common4"
        };

        const int iterations = 10000;

        var linqTime = BenchmarkLinqApproach(source, target, iterations);
        var hashSetTime = BenchmarkHashSetApproach(source, target, iterations);

        Console.WriteLine($"LINQ Approach:    {linqTime * 1000:F2} microseconds");
        Console.WriteLine($"HashSet Approach: {hashSetTime * 1000:F2} microseconds");
        Console.WriteLine($"Speed improvement: {(linqTime / hashSetTime):F2}x faster");
    }
}