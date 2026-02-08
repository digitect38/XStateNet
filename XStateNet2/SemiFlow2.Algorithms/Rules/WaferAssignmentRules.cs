using SemiFlow.Algorithms.Algorithms;
using SemiFlow.Algorithms.Models;

namespace SemiFlow.Algorithms.Rules;

/// <summary>
/// WAR_001: Cyclic Zip Distribution Rule
///
/// Distributes wafers across WSCs using cyclic zip pattern for balanced load.
/// </summary>
public class WAR_001 : IValidatableRule
{
    public string Id => "WAR_001";
    public string Name => "Cyclic_Zip_Distribution";
    public RuleCategory Category => RuleCategory.WaferAssignment;
    public int Priority => 1;
    public string Description =>
        "Distributes wafers across multiple WSCs using cyclic zip pattern. " +
        "Ensures balanced load where each scheduler receives approximately the same number of wafers.";

    /// <summary>
    /// Apply the Cyclic Zip distribution
    /// </summary>
    public DistributionResult Apply(IEnumerable<WaferId> wafers, int schedulerCount, int startOffset = 0)
    {
        return CyclicZip.Distribute(wafers, schedulerCount, startOffset);
    }

    /// <summary>
    /// Apply to a standard FOUP (25 wafers)
    /// </summary>
    public DistributionResult ApplyToFoup(int schedulerCount)
    {
        return CyclicZip.DistributeFoup(schedulerCount);
    }

    public ValidationResult Validate(RuleContext context)
    {
        var errors = new List<string>();

        if (context.TotalWafers < 1)
            errors.Add("Total wafers must be at least 1");

        if (context.SchedulerCount < 1)
            errors.Add("Scheduler count must be at least 1");

        if (context.SchedulerCount > context.TotalWafers)
            errors.Add("Scheduler count cannot exceed total wafers");

        return errors.Count > 0
            ? ValidationResult.Failure(errors)
            : ValidationResult.Success();
    }
}

/// <summary>
/// WAR_002: WSC Pipeline Slot Control Rule
///
/// Controls how wafers are assigned to pipeline slots within a WSC.
/// </summary>
public class WAR_002 : IValidatableRule
{
    public string Id => "WAR_002";
    public string Name => "WSC_Pipeline_Slot_Control";
    public RuleCategory Category => RuleCategory.WaferAssignment;
    public int Priority => 2;
    public string Description =>
        "Controls wafer assignment to pipeline slots within a Wafer Scheduler. " +
        "Ensures optimal pipeline utilization.";

    /// <summary>
    /// Assign wafers to pipeline slots
    /// </summary>
    public Dictionary<int, List<WaferId>> AssignToSlots(
        IEnumerable<WaferId> wafers,
        int pipelineDepth)
    {
        var slots = Enumerable.Range(0, pipelineDepth)
            .ToDictionary(i => i, _ => new List<WaferId>());

        int slotIndex = 0;
        foreach (var wafer in wafers)
        {
            slots[slotIndex].Add(wafer);
            slotIndex = (slotIndex + 1) % pipelineDepth;
        }

        return slots;
    }

    public ValidationResult Validate(RuleContext context)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (context.PipelineDepth < 1)
            errors.Add("Pipeline depth must be at least 1");

        if (context.PipelineDepth > 5)
            warnings.Add("Pipeline depth > 5 may cause scheduling complexity");

        return errors.Count > 0
            ? ValidationResult.Failure(errors)
            : ValidationResult.Success(warnings);
    }
}

/// <summary>
/// Collection of all WAR rules
/// </summary>
public static class WaferAssignmentRules
{
    public static WAR_001 WAR_001 { get; } = new();
    public static WAR_002 WAR_002 { get; } = new();

    public static ISchedulingRule? GetRule(string id) => id switch
    {
        "WAR_001" => WAR_001,
        "WAR_002" => WAR_002,
        _ => null
    };

    public static IEnumerable<ISchedulingRule> All
    {
        get
        {
            yield return WAR_001;
            yield return WAR_002;
        }
    }
}
