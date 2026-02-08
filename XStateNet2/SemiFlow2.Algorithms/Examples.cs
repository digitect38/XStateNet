using SemiFlow.Algorithms.Algorithms;
using SemiFlow.Algorithms.Models;
using SemiFlow.Algorithms.Rules;

namespace SemiFlow.Algorithms;

/// <summary>
/// Example usage of SemiFlow2 Algorithms library
/// </summary>
public static class Examples
{
    /// <summary>
    /// Example: Cyclic Zip distribution for 25 wafers across 3 schedulers
    /// </summary>
    public static void CyclicZipExample()
    {
        Console.WriteLine("=== Cyclic Zip Distribution Example ===\n");

        // Method 1: Direct API
        var result = CyclicZip.DistributeFoup(schedulerCount: 3);
        Console.WriteLine(result);

        // Method 2: Using builder
        var result2 = new CyclicZipBuilder()
            .WithFoup()
            .WithSchedulers(3)
            .Build();

        // Method 3: Get wafers for a specific scheduler
        var wsc001Wafers = CyclicZip.GetWafersForScheduler(
            offset: 0,
            stride: 3,
            totalWafers: 25);

        Console.WriteLine($"WSC_001 wafers: [{string.Join(", ", wsc001Wafers)}]");

        // Check which scheduler a wafer belongs to
        var waferId = new WaferId(7);
        int schedulerIndex = CyclicZip.GetSchedulerIndex(waferId, schedulerCount: 3);
        Console.WriteLine($"\n{waferId} belongs to WSC_{schedulerIndex + 1:D3}");
    }

    /// <summary>
    /// Example: Using WAR_001 rule
    /// </summary>
    public static void WAR001Example()
    {
        Console.WriteLine("\n=== WAR_001 Rule Example ===\n");

        var rule = new WAR_001();
        Console.WriteLine($"Rule: {rule.Id} - {rule.Name}");
        Console.WriteLine($"Description: {rule.Description}\n");

        // Apply the rule
        var wafers = WaferId.Foup();
        var result = rule.Apply(wafers, schedulerCount: 3);

        Console.WriteLine(result);

        // Validate
        var context = new RuleContext
        {
            TotalWafers = 25,
            SchedulerCount = 3
        };
        var validation = rule.Validate(context);
        Console.WriteLine($"Validation: {validation}");
    }

    /// <summary>
    /// Example: Pipeline slot assignment with PSR_001
    /// </summary>
    public static void PSR001Example()
    {
        Console.WriteLine("\n=== PSR_001 Pipeline Slot Assignment ===\n");

        var rule = new PSR_001();
        var matrix = rule.GenerateMatrix(totalWafers: 25, pipelineDepth: 3);

        Console.WriteLine(matrix);
    }

    /// <summary>
    /// Example: Steady state analysis with SSR_001
    /// </summary>
    public static void SSR001Example()
    {
        Console.WriteLine("\n=== SSR_001 Steady State Analysis ===\n");

        var rule = new SSR_001();

        // Calculate metrics
        var metrics = rule.CalculateMetrics(
            totalWafers: 25,
            pipelineDepth: 3,
            processTime: TimeSpan.FromSeconds(180));

        Console.WriteLine(metrics);

        // Generate timeline
        Console.WriteLine("\nPhase Timeline:");
        var timeline = rule.GenerateTimeline(totalWafers: 25, pipelineDepth: 3);
        foreach (var entry in timeline.Take(10)) // Show first 10
        {
            Console.WriteLine($"  Cycle {entry.Cycle,2}: {entry.Phase,-10} Active={entry.ActiveWafers} Completed={entry.CompletedWafers}");
        }
        Console.WriteLine("  ...");
    }

    /// <summary>
    /// Example: Using the Rule Engine
    /// </summary>
    public static void RuleEngineExample()
    {
        Console.WriteLine("\n=== Rule Engine Example ===\n");

        var engine = new RuleEngine();

        // List all rules
        Console.WriteLine("Available Rules:");
        foreach (var rule in engine.AllRules)
        {
            Console.WriteLine($"  {rule.Id}: {rule.Name} (Priority: {rule.Priority})");
        }

        // Apply rules using fluent API
        Console.WriteLine("\nApplying rules...");
        var result = new RuleApplicationBuilder(engine)
            .WithWafers(25)
            .WithSchedulers(3)
            .WithPipelineDepth(3)
            .ApplyRule("WAR_001")
            .ApplyRule("PSR_001")
            .ApplyRule("SSR_001")
            .Execute();

        Console.WriteLine(result);
    }

    /// <summary>
    /// Example: Complete production schedule
    /// </summary>
    public static void ProductionScheduleExample()
    {
        Console.WriteLine("\n=== Complete Production Schedule Example ===\n");

        // This simulates what APPLY_RULE does in SemiFlow2

        // Step 1: Apply WAR_001 - Distribute wafers
        Console.WriteLine("Step 1: Applying WAR_001 (Cyclic Zip Distribution)");
        var war001 = new WAR_001();
        var distribution = war001.ApplyToFoup(schedulerCount: 3);

        foreach (var assignment in distribution.Assignments)
        {
            Console.WriteLine($"  {assignment.SchedulerId}: {assignment.WaferCount} wafers");
        }

        // Step 2: Apply PSR_001 - Assign pipeline slots
        Console.WriteLine("\nStep 2: Applying PSR_001 (Pipeline Slot Assignment)");
        var psr001 = new PSR_001();
        var matrix = psr001.GenerateMatrix(totalWafers: 25, pipelineDepth: 3);
        Console.WriteLine($"  Total slots: {matrix.TotalSlots}, Empty: {matrix.EmptySlots}");

        // Step 3: Apply SSR_001 - Calculate steady state
        Console.WriteLine("\nStep 3: Applying SSR_001 (Steady State)");
        var ssr001 = new SSR_001();
        var metrics = ssr001.CalculateMetrics(
            totalWafers: 25,
            pipelineDepth: 3,
            processTime: TimeSpan.FromSeconds(180));

        Console.WriteLine($"  Ramp-Up: {metrics.RampUpCycles} cycles");
        Console.WriteLine($"  Steady: {metrics.SteadyCycles} cycles");
        Console.WriteLine($"  Ramp-Down: {metrics.RampDownCycles} cycles");
        Console.WriteLine($"  Total Time: {metrics.TotalTime}");
        Console.WriteLine($"  Efficiency: {metrics.OverallEfficiency:P1}");

        // Verification
        Console.WriteLine("\nVerification:");
        Console.WriteLine($"  All wafers assigned: {distribution.TotalWafers == 25}");
        Console.WriteLine($"  Load balanced: {distribution.IsBalanced}");
        distribution.ValidateNoConflicts(out var duplicates);
        Console.WriteLine($"  No conflicts: {duplicates.Count == 0}");
    }

    /// <summary>
    /// Run all examples
    /// </summary>
    public static void RunAll()
    {
        CyclicZipExample();
        WAR001Example();
        PSR001Example();
        SSR001Example();
        RuleEngineExample();
        ProductionScheduleExample();
    }
}
