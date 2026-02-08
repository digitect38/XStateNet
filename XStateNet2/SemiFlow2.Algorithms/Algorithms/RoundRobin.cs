using SemiFlow.Algorithms.Models;

namespace SemiFlow.Algorithms.Algorithms;

/// <summary>
/// Round Robin Distribution Algorithm
///
/// Similar to Cyclic Zip but assigns wafers one at a time to each scheduler
/// in sequence. The difference is primarily in how the algorithm is conceptualized:
/// - Cyclic Zip: Think of wafers being "zipped" together
/// - Round Robin: Think of dealing cards to players
///
/// For practical purposes, the result is identical to Cyclic Zip with offset=0.
/// </summary>
public static class RoundRobin
{
    /// <summary>
    /// Distribute wafers using Round Robin algorithm
    /// </summary>
    public static DistributionResult Distribute(
        IEnumerable<WaferId> wafers,
        int schedulerCount)
    {
        if (schedulerCount < 1)
            throw new ArgumentOutOfRangeException(nameof(schedulerCount));

        var waferList = wafers.ToList();
        var buckets = Enumerable.Range(0, schedulerCount)
            .Select(_ => new List<WaferId>())
            .ToList();

        int currentScheduler = 0;
        foreach (var wafer in waferList)
        {
            buckets[currentScheduler].Add(wafer);
            currentScheduler = (currentScheduler + 1) % schedulerCount;
        }

        var assignments = buckets.Select((bucket, index) =>
            new WaferAssignment(
                SchedulerId.Wafer(index + 1),
                bucket
            )).ToList();

        return new DistributionResult("ROUND_ROBIN", assignments);
    }

    /// <summary>
    /// Distribute a FOUP using Round Robin
    /// </summary>
    public static DistributionResult DistributeFoup(int schedulerCount)
        => Distribute(WaferId.Foup(), schedulerCount);
}
