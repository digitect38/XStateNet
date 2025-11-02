using CMPSimXS2.Console.Models;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Message types for WaferJourneyScheduler Actor
/// </summary>
public static class WaferJourneySchedulerMessages
{
    public record RegisterStation(string StationName, Station Station);
    public record ProcessJourneys();
    public record CarrierArrival(string CarrierId, List<int> WaferIds);
    public record CarrierDeparture(string CarrierId);
    public record IsCarrierComplete();
    public record CarrierCompleteResponse(bool IsComplete);
    public record GetCurrentCarrier();
    public record CurrentCarrierResponse(string? CarrierId);
    public record ResetScheduler();
}
