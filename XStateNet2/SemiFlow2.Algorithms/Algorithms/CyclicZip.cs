using SemiFlow.Algorithms.Models;

namespace SemiFlow.Algorithms.Algorithms;

/// <summary>
/// Cyclic Zip Distribution Algorithm (WAR_001)
///
/// Distributes wafers across schedulers in a round-robin (cyclic) pattern.
/// This ensures balanced load distribution where each scheduler receives
/// approximately the same number of wafers.
///
/// Example with 25 wafers and 3 schedulers:
///   WSC_001 (offset=0): W01, W04, W07, W10, W13, W16, W19, W22, W25 (9 wafers)
///   WSC_002 (offset=1): W02, W05, W08, W11, W14, W17, W20, W23      (8 wafers)
///   WSC_003 (offset=2): W03, W06, W09, W12, W15, W18, W21, W24      (8 wafers)
///
/// Formula: scheduler_index = (wafer_index + start_offset) % scheduler_count
/// </summary>
public static class CyclicZip
{
    /// <summary>
    /// Distribute wafers using Cyclic Zip algorithm
    /// </summary>
    /// <param name="wafers">List of wafers to distribute</param>
    /// <param name="schedulerCount">Number of schedulers (WSCs)</param>
    /// <param name="startOffset">Starting offset for distribution (default: 0)</param>
    /// <returns>Distribution result with assignments</returns>
    public static DistributionResult Distribute(
        IEnumerable<WaferId> wafers,
        int schedulerCount,
        int startOffset = 0)
    {
        if (schedulerCount < 1)
            throw new ArgumentOutOfRangeException(nameof(schedulerCount), "Must have at least 1 scheduler");

        var waferList = wafers.ToList();
        var buckets = new List<List<WaferId>>(schedulerCount);

        // Initialize buckets
        for (int i = 0; i < schedulerCount; i++)
            buckets.Add(new List<WaferId>());

        // Distribute wafers using cyclic zip
        for (int i = 0; i < waferList.Count; i++)
        {
            int schedulerIndex = (i + startOffset) % schedulerCount;
            buckets[schedulerIndex].Add(waferList[i]);
        }

        // Create assignments
        var assignments = buckets.Select((bucket, index) =>
            new WaferAssignment(
                SchedulerId.Wafer(index + 1),
                bucket,
                offset: index,
                stride: schedulerCount
            )).ToList();

        return new DistributionResult("CYCLIC_ZIP", assignments);
    }

    /// <summary>
    /// Distribute a FOUP (25 wafers) using Cyclic Zip
    /// </summary>
    public static DistributionResult DistributeFoup(int schedulerCount, int startWaferNumber = 1)
        => Distribute(WaferId.Foup(startWaferNumber), schedulerCount);

    /// <summary>
    /// Get wafers for a specific scheduler using Cyclic Zip formula
    /// </summary>
    /// <param name="offset">Scheduler offset (0-based index)</param>
    /// <param name="stride">Number of schedulers (stride between wafers)</param>
    /// <param name="totalWafers">Total number of wafers</param>
    /// <param name="startWaferNumber">Starting wafer number (default: 1)</param>
    /// <returns>List of wafer IDs assigned to this scheduler</returns>
    public static IReadOnlyList<WaferId> GetWafersForScheduler(
        int offset,
        int stride,
        int totalWafers,
        int startWaferNumber = 1)
    {
        if (offset < 0 || offset >= stride)
            throw new ArgumentOutOfRangeException(nameof(offset),
                $"Offset must be between 0 and {stride - 1}");

        var wafers = new List<WaferId>();

        for (int i = offset; i < totalWafers; i += stride)
        {
            wafers.Add(new WaferId(startWaferNumber + i));
        }

        return wafers;
    }

    /// <summary>
    /// Calculate which scheduler a wafer belongs to
    /// </summary>
    public static int GetSchedulerIndex(WaferId waferId, int schedulerCount, int startOffset = 0)
    {
        return ((waferId.Number - 1) + startOffset) % schedulerCount;
    }

    /// <summary>
    /// Calculate the expected wafer count per scheduler
    /// </summary>
    public static (int baseCount, int extraCount) CalculateDistribution(int totalWafers, int schedulerCount)
    {
        int baseCount = totalWafers / schedulerCount;
        int extraCount = totalWafers % schedulerCount;
        return (baseCount, extraCount);
    }

    /// <summary>
    /// Verify that a distribution follows Cyclic Zip pattern
    /// </summary>
    public static bool VerifyDistribution(DistributionResult result)
    {
        if (!result.IsBalanced)
            return false;

        // Check that wafers follow the cyclic pattern
        for (int i = 0; i < result.Assignments.Count; i++)
        {
            var assignment = result.Assignments[i];
            var expected = GetWafersForScheduler(
                i,
                result.SchedulerCount,
                result.TotalWafers);

            if (!assignment.Wafers.SequenceEqual(expected))
                return false;
        }

        return true;
    }
}

/// <summary>
/// Fluent builder for Cyclic Zip distribution
/// </summary>
public class CyclicZipBuilder
{
    private List<WaferId>? _wafers;
    private int _schedulerCount = 3;
    private int _startOffset = 0;
    private int _foupStartNumber = 1;

    /// <summary>
    /// Use a standard FOUP (25 wafers)
    /// </summary>
    public CyclicZipBuilder WithFoup(int startNumber = 1)
    {
        _foupStartNumber = startNumber;
        _wafers = null; // Will use FOUP
        return this;
    }

    /// <summary>
    /// Use specific wafers
    /// </summary>
    public CyclicZipBuilder WithWafers(IEnumerable<WaferId> wafers)
    {
        _wafers = wafers.ToList();
        return this;
    }

    /// <summary>
    /// Use wafers from range
    /// </summary>
    public CyclicZipBuilder WithWaferRange(int start, int count)
    {
        _wafers = WaferId.Range(start, count).ToList();
        return this;
    }

    /// <summary>
    /// Set number of schedulers
    /// </summary>
    public CyclicZipBuilder WithSchedulers(int count)
    {
        _schedulerCount = count;
        return this;
    }

    /// <summary>
    /// Set starting offset
    /// </summary>
    public CyclicZipBuilder WithOffset(int offset)
    {
        _startOffset = offset;
        return this;
    }

    /// <summary>
    /// Build and execute the distribution
    /// </summary>
    public DistributionResult Build()
    {
        var wafers = _wafers ?? WaferId.Foup(_foupStartNumber);
        return CyclicZip.Distribute(wafers, _schedulerCount, _startOffset);
    }
}
