using Akka.Actor;
using CMPSimXS2.Console.Models;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// XState-based WaferJourneyScheduler
/// XSTATE VERSION - Uses declarative state machine (wraps lock-based core logic)
/// </summary>
public class WaferJourneySchedulerXState : IWaferJourneyScheduler
{
    private readonly WaferJourneyScheduler _innerScheduler;
    private readonly IActorRef _machine;

    // XState machine definition for journey scheduler
    private const string MachineJson = """
    {
      "id": "waferJourneyScheduler",
      "initial": "idle",
      "states": {
        "idle": {
          "description": "Waiting for operations",
          "on": {
            "REGISTER_STATION": {
              "actions": ["registerStation"]
            },
            "PROCESS": {
              "target": "processing"
            },
            "CARRIER_ARRIVAL": {
              "actions": ["carrierArrival"]
            },
            "CARRIER_DEPARTURE": {
              "actions": ["carrierDeparture"]
            },
            "RESET": {
              "actions": ["reset"]
            }
          }
        },
        "processing": {
          "description": "Processing wafer journeys",
          "entry": ["processJourneys"],
          "always": {
            "target": "idle"
          }
        }
      }
    }
    """;

    public event Action<string>? OnCarrierCompleted
    {
        add => _innerScheduler.OnCarrierCompleted += value;
        remove => _innerScheduler.OnCarrierCompleted -= value;
    }

    public WaferJourneySchedulerXState(ActorSystem actorSystem, IRobotScheduler robotScheduler, List<Wafer> wafers, string? actorName = null)
    {
        _innerScheduler = new WaferJourneyScheduler(robotScheduler, wafers);

        // Create XState machine
        var factory = new XStateMachineFactory(actorSystem);
        _machine = factory.FromJson(MachineJson)
            .WithAction("registerStation", (ctx, data) =>
            {
                var dict = data as Dictionary<string, object>;
                if (dict != null && dict.TryGetValue("stationName", out var name) && dict.TryGetValue("station", out var station))
                {
                    _innerScheduler.RegisterStation(name.ToString()!, (Station)station);
                }
            })
            .WithAction("processJourneys", (ctx, data) => _innerScheduler.ProcessWaferJourneys())
            .WithAction("carrierArrival", (ctx, data) =>
            {
                var dict = data as Dictionary<string, object>;
                if (dict != null && dict.TryGetValue("carrierId", out var id) && dict.TryGetValue("waferIds", out var ids))
                {
                    _innerScheduler.OnCarrierArrival(id.ToString()!, (List<int>)ids);
                }
            })
            .WithAction("carrierDeparture", (ctx, data) =>
            {
                var dict = data as Dictionary<string, object>;
                if (dict != null && dict.TryGetValue("carrierId", out var id))
                {
                    _innerScheduler.OnCarrierDeparture(id.ToString()!);
                }
            })
            .WithAction("reset", (ctx, data) => _innerScheduler.Reset())
            .BuildAndStart(actorName);
    }

    public void RegisterStation(string stationName, Station station)
    {
        var data = new Dictionary<string, object>
        {
            ["stationName"] = stationName,
            ["station"] = station
        };
        _machine.Tell(new SendEvent("REGISTER_STATION", data));
    }

    public void ProcessWaferJourneys()
    {
        _machine.Tell(new SendEvent("PROCESS"));
    }

    public void OnCarrierArrival(string carrierId, List<int> waferIds)
    {
        var data = new Dictionary<string, object>
        {
            ["carrierId"] = carrierId,
            ["waferIds"] = waferIds
        };
        _machine.Tell(new SendEvent("CARRIER_ARRIVAL", data));
    }

    public void OnCarrierDeparture(string carrierId)
    {
        var data = new Dictionary<string, object>
        {
            ["carrierId"] = carrierId
        };
        _machine.Tell(new SendEvent("CARRIER_DEPARTURE", data));
    }

    public bool IsCurrentCarrierComplete()
    {
        return _innerScheduler.IsCurrentCarrierComplete();
    }

    public string? GetCurrentCarrierId()
    {
        return _innerScheduler.GetCurrentCarrierId();
    }

    public void Reset()
    {
        _machine.Tell(new SendEvent("RESET"));
    }
}
