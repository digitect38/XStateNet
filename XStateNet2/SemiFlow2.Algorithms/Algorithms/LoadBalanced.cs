using SemiFlow.Algorithms.Models;

namespace SemiFlow.Algorithms.Algorithms;

/// <summary>
/// Load Balanced Distribution Algorithm
///
/// Distributes wafers based on current scheduler load, assigning each wafer
/// to the scheduler with the lowest current load. Useful when schedulers
/// have different processing capacities or when some schedulers are already
/// processing wafers.
/// </summary>
public static class LoadBalanced
{
    /// <summary>
    /// Distribute wafers based on scheduler capacity
    /// </summary>
    /// <param name="wafers">Wafers to distribute</param>
    /// <param name="schedulerCapacities">Map of scheduler ID to capacity weight (higher = more capacity)</param>
    /// <returns>Distribution result</returns>
    public static DistributionResult Distribute(
        IEnumerable<WaferId> wafers,
        Dictionary<SchedulerId, double> schedulerCapacities)
    {
        if (schedulerCapacities.Count == 0)
            throw new ArgumentException("Must have at least one scheduler", nameof(schedulerCapacities));

        var waferList = wafers.ToList();
        var buckets = schedulerCapacities.Keys
            .ToDictionary(k => k, _ => new List<WaferId>());
        var loads = schedulerCapacities.Keys
            .ToDictionary(k => k, _ => 0.0);

        foreach (var wafer in waferList)
        {
            // Find scheduler with lowest normalized load
            var bestScheduler = schedulerCapacities.Keys
                .OrderBy(s => loads[s] / schedulerCapacities[s])
                .First();

            buckets[bestScheduler].Add(wafer);
            loads[bestScheduler] += 1.0;
        }

        var assignments = buckets.Select(kvp =>
            new WaferAssignment(kvp.Key, kvp.Value)).ToList();

        return new DistributionResult("LOAD_BALANCED", assignments);
    }

    /// <summary>
    /// Distribute wafers with equal capacity schedulers
    /// </summary>
    public static DistributionResult Distribute(
        IEnumerable<WaferId> wafers,
        int schedulerCount)
    {
        var capacities = Enumerable.Range(1, schedulerCount)
            .ToDictionary(
                i => SchedulerId.Wafer(i),
                _ => 1.0);
        return Distribute(wafers, capacities);
    }

    /// <summary>
    /// Distribute wafers considering current load
    /// </summary>
    public static DistributionResult DistributeWithCurrentLoad(
        IEnumerable<WaferId> wafers,
        Dictionary<SchedulerId, int> currentLoads,
        int maxCapacityPerScheduler = int.MaxValue)
    {
        var waferList = wafers.ToList();
        var buckets = currentLoads.Keys
            .ToDictionary(k => k, _ => new List<WaferId>());
        var loads = new Dictionary<SchedulerId, int>(currentLoads);

        foreach (var wafer in waferList)
        {
            // Find scheduler with lowest load that hasn't exceeded capacity
            var available = loads
                .Where(kvp => kvp.Value < maxCapacityPerScheduler)
                .OrderBy(kvp => kvp.Value)
                .ToList();

            if (available.Count == 0)
                throw new InvalidOperationException("All schedulers at capacity");

            var bestScheduler = available.First().Key;
            buckets[bestScheduler].Add(wafer);
            loads[bestScheduler]++;
        }

        var assignments = buckets.Select(kvp =>
            new WaferAssignment(kvp.Key, kvp.Value)).ToList();

        return new DistributionResult("LOAD_BALANCED", assignments);
    }
}
