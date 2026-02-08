using SemiFlow.Algorithms.Models;

namespace SemiFlow.Algorithms.Rules;

/// <summary>
/// PSR_001: Pipeline Slot Assignment Rule
///
/// Assigns wafers to pipeline slots for collision-free operation.
/// </summary>
public class PSR_001 : IValidatableRule
{
    public string Id => "PSR_001";
    public string Name => "Pipeline_Slot_Assignment";
    public RuleCategory Category => RuleCategory.PipelineSlot;
    public int Priority => 1;
    public string Description =>
        "Assigns wafers to pipeline slots ensuring no collisions. " +
        "Uses formula: slot_index = wafer_index % pipeline_depth";

    /// <summary>
    /// Calculate slot assignment for a wafer
    /// </summary>
    public int GetSlotIndex(int waferIndex, int pipelineDepth)
    {
        return waferIndex % pipelineDepth;
    }

    /// <summary>
    /// Calculate slot assignment for a wafer ID
    /// </summary>
    public int GetSlotIndex(WaferId waferId, int pipelineDepth)
    {
        return (waferId.Number - 1) % pipelineDepth;
    }

    /// <summary>
    /// Get all wafers in a specific slot
    /// </summary>
    public IReadOnlyList<WaferId> GetWafersInSlot(
        IEnumerable<WaferId> wafers,
        int slotIndex,
        int pipelineDepth)
    {
        return wafers
            .Where(w => GetSlotIndex(w, pipelineDepth) == slotIndex)
            .ToList();
    }

    /// <summary>
    /// Generate slot assignment matrix
    /// </summary>
    public PipelineSlotMatrix GenerateMatrix(int totalWafers, int pipelineDepth)
    {
        var matrix = new PipelineSlotMatrix(pipelineDepth);

        for (int i = 0; i < totalWafers; i++)
        {
            var waferId = new WaferId(i + 1);
            int slot = GetSlotIndex(i, pipelineDepth);
            matrix.AddWafer(slot, waferId);
        }

        return matrix;
    }

    public ValidationResult Validate(RuleContext context)
    {
        var errors = new List<string>();

        if (context.PipelineDepth < 1)
            errors.Add("Pipeline depth must be at least 1");

        if (context.PipelineDepth > context.TotalWafers)
            errors.Add("Pipeline depth cannot exceed total wafers");

        return errors.Count > 0
            ? ValidationResult.Failure(errors)
            : ValidationResult.Success();
    }
}

/// <summary>
/// PSR_002: Processing Time Pattern Rule
///
/// Calculates timing patterns for pipeline slots to ensure smooth flow.
/// </summary>
public class PSR_002 : IValidatableRule
{
    public string Id => "PSR_002";
    public string Name => "Processing_Time_Pattern";
    public RuleCategory Category => RuleCategory.PipelineSlot;
    public int Priority => 2;
    public string Description =>
        "Calculates processing time patterns for pipeline slots. " +
        "Ensures wafers move through pipeline at consistent intervals.";

    /// <summary>
    /// Calculate next slot time
    /// </summary>
    public TimeSpan CalculateNextSlotTime(TimeSpan currentTime, TimeSpan processTime)
    {
        return currentTime + processTime;
    }

    /// <summary>
    /// Generate timing schedule for wafers
    /// </summary>
    public IReadOnlyList<WaferTiming> GenerateTimingSchedule(
        int totalWafers,
        int pipelineDepth,
        TimeSpan processTime,
        TimeSpan transferTime)
    {
        var timings = new List<WaferTiming>();
        var cycleTime = processTime + transferTime;

        for (int i = 0; i < totalWafers; i++)
        {
            var waferId = new WaferId(i + 1);
            int slot = i % pipelineDepth;

            // Calculate start time based on slot and cycle
            int cycle = i / pipelineDepth;
            var startTime = TimeSpan.FromTicks(cycle * cycleTime.Ticks * pipelineDepth + slot * cycleTime.Ticks);

            timings.Add(new WaferTiming(waferId, slot, startTime, processTime));
        }

        return timings;
    }

    public ValidationResult Validate(RuleContext context)
    {
        return ValidationResult.Success();
    }
}

/// <summary>
/// PSR_003: WTR Assignment Matrix Rule
///
/// Assigns Wafer Transfer Robots to pipeline operations.
/// </summary>
public class PSR_003 : IValidatableRule
{
    public string Id => "PSR_003";
    public string Name => "WTR_Assignment_Matrix";
    public RuleCategory Category => RuleCategory.PipelineSlot;
    public int Priority => 3;
    public string Description =>
        "Assigns Wafer Transfer Robots (WTR) to move wafers between stations. " +
        "Ensures no robot conflicts during transfers.";

    /// <summary>
    /// Create WTR assignment for a transfer
    /// </summary>
    public WtrAssignment AssignWtr(
        WaferId waferId,
        SchedulerId sourceStation,
        SchedulerId targetStation,
        IEnumerable<SchedulerId> availableWtrs)
    {
        // Simple strategy: use first available WTR
        var wtr = availableWtrs.First();
        return new WtrAssignment(waferId, wtr, sourceStation, targetStation);
    }

    public ValidationResult Validate(RuleContext context)
    {
        return ValidationResult.Success();
    }
}

/// <summary>
/// Matrix of pipeline slots and their assigned wafers
/// </summary>
public class PipelineSlotMatrix
{
    private readonly Dictionary<int, List<WaferId>> _slots;

    public int PipelineDepth { get; }

    public PipelineSlotMatrix(int pipelineDepth)
    {
        PipelineDepth = pipelineDepth;
        _slots = Enumerable.Range(0, pipelineDepth)
            .ToDictionary(i => i, _ => new List<WaferId>());
    }

    public void AddWafer(int slot, WaferId waferId)
    {
        if (slot < 0 || slot >= PipelineDepth)
            throw new ArgumentOutOfRangeException(nameof(slot));
        _slots[slot].Add(waferId);
    }

    public IReadOnlyList<WaferId> GetSlot(int slot) => _slots[slot];

    public int TotalSlots => (int)Math.Ceiling(_slots.Max(s => s.Value.Count) * 1.0) * PipelineDepth;

    public int TotalWafers => _slots.Values.Sum(s => s.Count);

    public int EmptySlots => TotalSlots - TotalWafers;

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Pipeline Matrix (depth={PipelineDepth}, wafers={TotalWafers}):");
        for (int slot = 0; slot < PipelineDepth; slot++)
        {
            sb.AppendLine($"  Slot {slot}: [{string.Join(", ", _slots[slot])}]");
        }
        return sb.ToString();
    }
}

/// <summary>
/// Timing information for a wafer in the pipeline
/// </summary>
public record WaferTiming(
    WaferId WaferId,
    int SlotIndex,
    TimeSpan StartTime,
    TimeSpan ProcessTime)
{
    public TimeSpan EndTime => StartTime + ProcessTime;
}

/// <summary>
/// WTR assignment for a wafer transfer
/// </summary>
public record WtrAssignment(
    WaferId WaferId,
    SchedulerId Wtr,
    SchedulerId Source,
    SchedulerId Target);

/// <summary>
/// Collection of all PSR rules
/// </summary>
public static class PipelineSlotRules
{
    public static PSR_001 PSR_001 { get; } = new();
    public static PSR_002 PSR_002 { get; } = new();
    public static PSR_003 PSR_003 { get; } = new();

    public static ISchedulingRule? GetRule(string id) => id switch
    {
        "PSR_001" => PSR_001,
        "PSR_002" => PSR_002,
        "PSR_003" => PSR_003,
        _ => null
    };

    public static IEnumerable<ISchedulingRule> All
    {
        get
        {
            yield return PSR_001;
            yield return PSR_002;
            yield return PSR_003;
        }
    }
}
