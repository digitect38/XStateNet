using CMPSimXS2.Console.Models;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Interface for Wafer Journey Scheduler implementations
/// Orchestrates 8-step wafer lifecycle: Carrier → Polisher → Cleaner → Buffer → Carrier
/// </summary>
public interface IWaferJourneyScheduler
{
    /// <summary>
    /// Event fired when all wafers in a carrier are processed
    /// </summary>
    event Action<string>? OnCarrierCompleted;

    /// <summary>
    /// Register a station for monitoring
    /// </summary>
    void RegisterStation(string stationName, Station station);

    /// <summary>
    /// Process wafer journeys - called periodically to check and progress wafers
    /// </summary>
    void ProcessWaferJourneys();

    /// <summary>
    /// Handle carrier arrival with wafers
    /// </summary>
    void OnCarrierArrival(string carrierId, List<int> waferIds);

    /// <summary>
    /// Handle carrier departure
    /// </summary>
    void OnCarrierDeparture(string carrierId);

    /// <summary>
    /// Check if current carrier's wafers are all processed
    /// </summary>
    bool IsCurrentCarrierComplete();

    /// <summary>
    /// Get current carrier ID
    /// </summary>
    string? GetCurrentCarrierId();

    /// <summary>
    /// Reset scheduler state
    /// </summary>
    void Reset();
}
