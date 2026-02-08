using SemiFlow.Algorithms.Models;

namespace SemiFlow.Algorithms.Rules;

/// <summary>
/// SSR_001: Three Phase Steady State Rule
///
/// Manages the three phases of pipeline operation:
/// 1. RAMP_UP: Pipeline is filling
/// 2. STEADY: Pipeline is full, maximum throughput
/// 3. RAMP_DOWN: Pipeline is draining
/// </summary>
public class SSR_001 : IValidatableRule
{
    public string Id => "SSR_001";
    public string Name => "Three_Phase_Steady_State";
    public RuleCategory Category => RuleCategory.SteadyState;
    public int Priority => 1;
    public string Description =>
        "Maintains three-phase steady state operation. " +
        "Phases: RAMP_UP (filling), STEADY (full throughput), RAMP_DOWN (draining).";

    /// <summary>
    /// Determine current pipeline phase
    /// </summary>
    public PipelinePhase DeterminePhase(
        int processedWafers,
        int activeWafers,
        int totalWafers,
        int pipelineDepth)
    {
        // RAMP_UP: Pipeline is not yet full
        if (processedWafers == 0 && activeWafers < pipelineDepth)
            return PipelinePhase.RampUp;

        // RAMP_DOWN: No more wafers to add, pipeline is draining
        int remainingToStart = totalWafers - processedWafers - activeWafers;
        if (remainingToStart == 0 && activeWafers < pipelineDepth)
            return PipelinePhase.RampDown;

        // STEADY: Pipeline is full
        if (activeWafers >= pipelineDepth)
            return PipelinePhase.Steady;

        // Transitioning
        return activeWafers > 0 ? PipelinePhase.Steady : PipelinePhase.RampUp;
    }

    /// <summary>
    /// Calculate steady state metrics
    /// </summary>
    public SteadyStateMetrics CalculateMetrics(
        int totalWafers,
        int pipelineDepth,
        TimeSpan processTime)
    {
        // Ramp-up cycles: (pipelineDepth - 1) cycles to fill the pipeline
        int rampUpCycles = pipelineDepth - 1;

        // Steady state cycles: main processing cycles
        int steadyCycles = totalWafers - pipelineDepth;
        if (steadyCycles < 0) steadyCycles = 0;

        // Ramp-down cycles: (pipelineDepth - 1) cycles to drain
        int rampDownCycles = Math.Min(pipelineDepth - 1, totalWafers - 1);

        // Total time calculation
        var totalCycles = totalWafers + pipelineDepth - 1;
        var totalTime = TimeSpan.FromTicks(totalCycles * processTime.Ticks);

        // Throughput (wafers per cycle in steady state)
        double steadyThroughput = 1.0; // 1 wafer per cycle in steady state

        return new SteadyStateMetrics
        {
            TotalWafers = totalWafers,
            PipelineDepth = pipelineDepth,
            RampUpCycles = rampUpCycles,
            SteadyCycles = steadyCycles,
            RampDownCycles = rampDownCycles,
            TotalCycles = totalCycles,
            TotalTime = totalTime,
            SteadyStateThroughput = steadyThroughput,
            ProcessTime = processTime
        };
    }

    /// <summary>
    /// Generate phase timeline for visualization
    /// </summary>
    public IReadOnlyList<PhaseTimeline> GenerateTimeline(
        int totalWafers,
        int pipelineDepth)
    {
        var timeline = new List<PhaseTimeline>();
        int cycle = 1;

        // RAMP_UP phase
        for (int i = 0; i < pipelineDepth - 1 && i < totalWafers - 1; i++)
        {
            timeline.Add(new PhaseTimeline(
                cycle++,
                PipelinePhase.RampUp,
                ActiveWafers: i + 1,
                CompletedWafers: 0));
        }

        // STEADY phase
        int steadyCycles = totalWafers - pipelineDepth;
        for (int i = 0; i < steadyCycles; i++)
        {
            timeline.Add(new PhaseTimeline(
                cycle++,
                PipelinePhase.Steady,
                ActiveWafers: pipelineDepth,
                CompletedWafers: i + 1));
        }

        // If we have enough wafers for full pipeline
        if (totalWafers >= pipelineDepth)
        {
            timeline.Add(new PhaseTimeline(
                cycle++,
                PipelinePhase.Steady,
                ActiveWafers: pipelineDepth,
                CompletedWafers: Math.Max(0, totalWafers - pipelineDepth)));
        }

        // RAMP_DOWN phase
        for (int i = pipelineDepth - 1; i >= 1; i--)
        {
            int completed = totalWafers - i;
            if (completed > 0)
            {
                timeline.Add(new PhaseTimeline(
                    cycle++,
                    PipelinePhase.RampDown,
                    ActiveWafers: i,
                    CompletedWafers: completed));
            }
        }

        // Final completion
        timeline.Add(new PhaseTimeline(
            cycle,
            PipelinePhase.Complete,
            ActiveWafers: 0,
            CompletedWafers: totalWafers));

        return timeline;
    }

    public ValidationResult Validate(RuleContext context)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (context.PipelineDepth < 1)
            errors.Add("Pipeline depth must be at least 1");

        if (context.TotalWafers < context.PipelineDepth)
            warnings.Add("Total wafers less than pipeline depth - no steady state achieved");

        return errors.Count > 0
            ? ValidationResult.Failure(errors)
            : ValidationResult.Success(warnings);
    }
}

/// <summary>
/// SSR_002: Pipeline State Detection Rule
///
/// Detects current pipeline state and transitions.
/// </summary>
public class SSR_002 : IValidatableRule
{
    public string Id => "SSR_002";
    public string Name => "Pipeline_State_Detection";
    public RuleCategory Category => RuleCategory.SteadyState;
    public int Priority => 2;
    public string Description =>
        "Detects current pipeline state and monitors transitions between phases.";

    /// <summary>
    /// Check if pipeline is in steady state
    /// </summary>
    public bool IsInSteadyState(int activeWafers, int pipelineDepth)
    {
        return activeWafers >= pipelineDepth;
    }

    /// <summary>
    /// Calculate pipeline utilization
    /// </summary>
    public double CalculateUtilization(int activeWafers, int pipelineDepth)
    {
        if (pipelineDepth == 0) return 0;
        return Math.Min(1.0, (double)activeWafers / pipelineDepth);
    }

    /// <summary>
    /// Detect phase transition
    /// </summary>
    public PhaseTransition? DetectTransition(
        PipelinePhase previousPhase,
        PipelinePhase currentPhase)
    {
        if (previousPhase == currentPhase)
            return null;

        return new PhaseTransition(previousPhase, currentPhase);
    }

    public ValidationResult Validate(RuleContext context)
    {
        return ValidationResult.Success();
    }
}

/// <summary>
/// Pipeline operation phase
/// </summary>
public enum PipelinePhase
{
    /// <summary>
    /// Initial state, no wafers in pipeline
    /// </summary>
    Idle,

    /// <summary>
    /// Pipeline is filling up
    /// </summary>
    RampUp,

    /// <summary>
    /// Pipeline is full, maximum throughput
    /// </summary>
    Steady,

    /// <summary>
    /// Pipeline is draining
    /// </summary>
    RampDown,

    /// <summary>
    /// All wafers processed
    /// </summary>
    Complete
}

/// <summary>
/// Steady state calculation metrics
/// </summary>
public class SteadyStateMetrics
{
    public int TotalWafers { get; init; }
    public int PipelineDepth { get; init; }
    public int RampUpCycles { get; init; }
    public int SteadyCycles { get; init; }
    public int RampDownCycles { get; init; }
    public int TotalCycles { get; init; }
    public TimeSpan TotalTime { get; init; }
    public TimeSpan ProcessTime { get; init; }
    public double SteadyStateThroughput { get; init; }

    public double OverallEfficiency =>
        TotalCycles > 0 ? (double)TotalWafers / TotalCycles : 0;

    public override string ToString()
    {
        return $"""
            Steady State Metrics:
              Wafers: {TotalWafers}, Pipeline Depth: {PipelineDepth}
              Ramp-Up: {RampUpCycles} cycles
              Steady: {SteadyCycles} cycles
              Ramp-Down: {RampDownCycles} cycles
              Total: {TotalCycles} cycles ({TotalTime})
              Efficiency: {OverallEfficiency:P1}
            """;
    }
}

/// <summary>
/// Timeline entry for a pipeline phase
/// </summary>
public record PhaseTimeline(
    int Cycle,
    PipelinePhase Phase,
    int ActiveWafers,
    int CompletedWafers);

/// <summary>
/// Phase transition event
/// </summary>
public record PhaseTransition(
    PipelinePhase FromPhase,
    PipelinePhase ToPhase);

/// <summary>
/// Collection of all SSR rules
/// </summary>
public static class SteadyStateRules
{
    public static SSR_001 SSR_001 { get; } = new();
    public static SSR_002 SSR_002 { get; } = new();

    public static ISchedulingRule? GetRule(string id) => id switch
    {
        "SSR_001" => SSR_001,
        "SSR_002" => SSR_002,
        _ => null
    };

    public static IEnumerable<ISchedulingRule> All
    {
        get
        {
            yield return SSR_001;
            yield return SSR_002;
        }
    }
}
