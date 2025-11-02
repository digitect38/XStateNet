using Akka.Actor;
using CMPSimXS2.Console.Models;
using static CMPSimXS2.Console.Schedulers.WaferJourneySchedulerMessages;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Actor-based WaferJourneyScheduler implementation
/// ACTOR-BASED VERSION (NO LOCKS - uses actor message passing)
/// </summary>
public class WaferJourneySchedulerActor : ReceiveActor
{
    private readonly WaferJourneyScheduler _innerScheduler;

    public WaferJourneySchedulerActor(IRobotScheduler robotScheduler, List<Wafer> wafers)
    {
        _innerScheduler = new WaferJourneyScheduler(robotScheduler, wafers);

        // Forward event from inner scheduler
        _innerScheduler.OnCarrierCompleted += (carrierId) =>
        {
            Context.System.EventStream.Publish(new CarrierCompleted(carrierId));
        };

        Receive<RegisterStation>(msg => HandleRegisterStation(msg));
        Receive<ProcessJourneys>(msg => HandleProcessJourneys());
        Receive<CarrierArrival>(msg => HandleCarrierArrival(msg));
        Receive<CarrierDeparture>(msg => HandleCarrierDeparture(msg));
        Receive<IsCarrierComplete>(msg => HandleIsCarrierComplete());
        Receive<GetCurrentCarrier>(msg => HandleGetCurrentCarrier());
        Receive<ResetScheduler>(msg => HandleReset());
    }

    private void HandleRegisterStation(RegisterStation msg)
    {
        _innerScheduler.RegisterStation(msg.StationName, msg.Station);
    }

    private void HandleProcessJourneys()
    {
        _innerScheduler.ProcessWaferJourneys();
    }

    private void HandleCarrierArrival(CarrierArrival msg)
    {
        _innerScheduler.OnCarrierArrival(msg.CarrierId, msg.WaferIds);
    }

    private void HandleCarrierDeparture(CarrierDeparture msg)
    {
        _innerScheduler.OnCarrierDeparture(msg.CarrierId);
    }

    private void HandleIsCarrierComplete()
    {
        var isComplete = _innerScheduler.IsCurrentCarrierComplete();
        Sender.Tell(new CarrierCompleteResponse(isComplete));
    }

    private void HandleGetCurrentCarrier()
    {
        var carrierId = _innerScheduler.GetCurrentCarrierId();
        Sender.Tell(new CurrentCarrierResponse(carrierId));
    }

    private void HandleReset()
    {
        _innerScheduler.Reset();
    }

    public record CarrierCompleted(string CarrierId);
}

/// <summary>
/// Proxy wrapper for WaferJourneySchedulerActor
/// Provides same API as WaferJourneyScheduler but uses actor messaging
/// ACTOR-BASED VERSION
/// </summary>
public class WaferJourneySchedulerActorProxy : IWaferJourneyScheduler
{
    private readonly IActorRef _schedulerActor;
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(5);
    private readonly ActorSystem _actorSystem;

    public event Action<string>? OnCarrierCompleted;

    public WaferJourneySchedulerActorProxy(ActorSystem actorSystem, IRobotScheduler robotScheduler, List<Wafer> wafers, string? actorName = null)
    {
        _actorSystem = actorSystem;
        var name = actorName ?? $"wafer-journey-scheduler-{Guid.NewGuid():N}";
        _schedulerActor = actorSystem.ActorOf(Props.Create(() => new WaferJourneySchedulerActor(robotScheduler, wafers)), name);

        // Subscribe to carrier completion events
        var subscriber = actorSystem.ActorOf(Props.Create(() => new CarrierCompletedSubscriber(this)));
        actorSystem.EventStream.Subscribe(subscriber, typeof(WaferJourneySchedulerActor.CarrierCompleted));
    }

    public void RegisterStation(string stationName, Station station)
    {
        _schedulerActor.Tell(new RegisterStation(stationName, station));
    }

    public void ProcessWaferJourneys()
    {
        _schedulerActor.Tell(new ProcessJourneys());
    }

    public void OnCarrierArrival(string carrierId, List<int> waferIds)
    {
        _schedulerActor.Tell(new CarrierArrival(carrierId, waferIds));
    }

    public void OnCarrierDeparture(string carrierId)
    {
        _schedulerActor.Tell(new CarrierDeparture(carrierId));
    }

    public bool IsCurrentCarrierComplete()
    {
        var result = _schedulerActor.Ask<CarrierCompleteResponse>(new IsCarrierComplete(), _defaultTimeout).Result;
        return result.IsComplete;
    }

    public string? GetCurrentCarrierId()
    {
        var result = _schedulerActor.Ask<CurrentCarrierResponse>(new GetCurrentCarrier(), _defaultTimeout).Result;
        return result.CarrierId;
    }

    public void Reset()
    {
        _schedulerActor.Tell(new ResetScheduler());
    }

    internal void RaiseCarrierCompleted(string carrierId)
    {
        OnCarrierCompleted?.Invoke(carrierId);
    }

    private class CarrierCompletedSubscriber : ReceiveActor
    {
        public CarrierCompletedSubscriber(WaferJourneySchedulerActorProxy proxy)
        {
            Receive<WaferJourneySchedulerActor.CarrierCompleted>(msg =>
            {
                proxy.RaiseCarrierCompleted(msg.CarrierId);
            });
        }
    }
}
