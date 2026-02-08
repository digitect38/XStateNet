namespace SemiFlow.Algorithms.Models;

/// <summary>
/// Represents the assignment of wafers to a scheduler
/// </summary>
public class WaferAssignment
{
    public SchedulerId SchedulerId { get; }
    public IReadOnlyList<WaferId> Wafers { get; }
    public int Offset { get; }
    public int Stride { get; }

    public WaferAssignment(SchedulerId schedulerId, IEnumerable<WaferId> wafers, int offset = 0, int stride = 1)
    {
        SchedulerId = schedulerId;
        Wafers = wafers.ToList();
        Offset = offset;
        Stride = stride;
    }

    public int WaferCount => Wafers.Count;

    public override string ToString()
        => $"{SchedulerId}: [{string.Join(", ", Wafers)}] ({WaferCount} wafers)";
}

/// <summary>
/// Result of a wafer distribution algorithm
/// </summary>
public class DistributionResult
{
    public string AlgorithmName { get; }
    public IReadOnlyList<WaferAssignment> Assignments { get; }
    public int TotalWafers { get; }
    public int SchedulerCount { get; }
    public bool IsBalanced { get; }
    public int MaxLoadDifference { get; }

    public DistributionResult(
        string algorithmName,
        IEnumerable<WaferAssignment> assignments)
    {
        AlgorithmName = algorithmName;
        Assignments = assignments.ToList();
        TotalWafers = Assignments.Sum(a => a.WaferCount);
        SchedulerCount = Assignments.Count;

        var counts = Assignments.Select(a => a.WaferCount).ToList();
        MaxLoadDifference = counts.Count > 0 ? counts.Max() - counts.Min() : 0;
        IsBalanced = MaxLoadDifference <= 1;
    }

    /// <summary>
    /// Get the assignment for a specific scheduler
    /// </summary>
    public WaferAssignment? GetAssignment(SchedulerId schedulerId)
        => Assignments.FirstOrDefault(a => a.SchedulerId == schedulerId);

    /// <summary>
    /// Find which scheduler a wafer is assigned to
    /// </summary>
    public SchedulerId? FindSchedulerForWafer(WaferId waferId)
        => Assignments.FirstOrDefault(a => a.Wafers.Contains(waferId))?.SchedulerId;

    /// <summary>
    /// Validate that all wafers are assigned exactly once
    /// </summary>
    public bool ValidateNoConflicts(out List<WaferId> duplicates)
    {
        var allWafers = Assignments.SelectMany(a => a.Wafers).ToList();
        duplicates = allWafers
            .GroupBy(w => w)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        return duplicates.Count == 0;
    }

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Distribution: {AlgorithmName}");
        sb.AppendLine($"Total Wafers: {TotalWafers}, Schedulers: {SchedulerCount}");
        sb.AppendLine($"Balanced: {IsBalanced} (max diff: {MaxLoadDifference})");
        sb.AppendLine();
        foreach (var assignment in Assignments)
        {
            sb.AppendLine($"  {assignment}");
        }
        return sb.ToString();
    }
}
