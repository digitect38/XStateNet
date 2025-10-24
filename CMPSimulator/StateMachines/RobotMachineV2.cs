using Akka.Actor;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using LoggerHelper;
using System.Text.Json;
using System.IO;

namespace CMPSimulator.StateMachines;

/// <summary>
/// Robot State Machine V2 (XStateNet2 + Akka.NET)
/// States: Idle → PickingUp → Holding → PlacingDown → Returning → Idle
/// </summary>
public class RobotMachineV2
{
    private readonly string _robotName;
    private readonly IActorRef _machine;
    private readonly int _transferTimeMs;

    public string RobotName => _robotName;
    public IActorRef MachineRef => _machine;

    // State change notification via Ask pattern or custom messages
    public event EventHandler<(string From, string To)>? StateChanged;

    public RobotMachineV2(
        string robotName,
        ActorSystem actorSystem,
        IActorRef scheduler,
        Dictionary<string, IActorRef> stations,
        int transferTimeMs)
    {
        _robotName = robotName;
        _transferTimeMs = transferTimeMs;

        // Load JSON definition from file
        var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StateMachines", "RobotMachine.json");
        var json = File.ReadAllText(jsonPath);

        var factory = new XStateMachineFactory(actorSystem);

        _machine = factory.FromJson(json)
            // Logging actions
            .WithAction("onReset", (ctx, _) =>
            {
                var wafer = ctx.Get<int?>("heldWafer");
                var from = ctx.Get<string>("pickFrom");
                var to = ctx.Get<string>("placeTo");

                var waferInfo = wafer.HasValue ? $"wafer {wafer}" : "no wafer";
                var transferInfo = "";
                if (!string.IsNullOrEmpty(from) || !string.IsNullOrEmpty(to))
                {
                    transferInfo = $" (transfer: {from ?? "?"} → {to ?? "?"})";
                }

                Logger.Instance.Log($"[{_robotName}] 🔄 RESET with {waferInfo}{transferInfo}");
            })
            .WithAction("onIdle", (ctx, _) =>
            {
                Logger.Instance.Log($"[{_robotName}] Idle");
            })
            .WithAction("storeTransferInfo", (ctx, data) =>
            {
                // Extract values from event data and update context
                if (data != null)
                {
                    var eventData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                        JsonSerializer.Serialize(data));

                    if (eventData != null)
                    {
                        if (eventData.TryGetValue("waferId", out var waferElem))
                            ctx.Set("heldWafer", waferElem.GetInt32());
                        if (eventData.TryGetValue("from", out var fromElem))
                            ctx.Set("pickFrom", fromElem.GetString());
                        if (eventData.TryGetValue("to", out var toElem))
                            ctx.Set("placeTo", toElem.GetString());
                    }
                }

                var wafer = ctx.Get<int?>("heldWafer");
                var from = ctx.Get<string>("pickFrom");
                var to = ctx.Get<string>("placeTo");
                Logger.Instance.Log($"[{_robotName}] Transfer command: {from} → {to} (Wafer {wafer})");
            })
            .WithAction("onTransferCommand", (ctx, _) =>
            {
                // Additional logging if needed
            })
            .WithAction("onPickingUp", (ctx, _) =>
            {
                var from = ctx.Get<string>("pickFrom");
                Logger.Instance.Log($"[{_robotName}] Moving to pick from {from}...");
            })
            .WithAction("onPickedWafer", (ctx, _) =>
            {
                var wafer = ctx.Get<int?>("heldWafer");
                var from = ctx.Get<string>("pickFrom");
                Logger.Instance.Log($"[{_robotName}] Picked wafer {wafer} from {from}");

                // Send PICK event to source station
                if (!string.IsNullOrEmpty(from) && stations.TryGetValue(from, out var station))
                {
                    station.Tell(new SendEvent("PICK", new { wafer }));
                }
            })
            .WithAction("onHolding", (ctx, _) =>
            {
                var wafer = ctx.Get<int?>("heldWafer");
                var to = ctx.Get<string>("placeTo");
                Logger.Instance.Log($"[{_robotName}] Holding wafer {wafer} (waiting for destination {to} ready)");
            })
            .WithAction("onPlacingDown", (ctx, _) =>
            {
                var to = ctx.Get<string>("placeTo");
                Logger.Instance.Log($"[{_robotName}] Moving to place at {to}...");
            })
            .WithAction("onPlacedWafer", (ctx, _) =>
            {
                var wafer = ctx.Get<int?>("heldWafer");
                var to = ctx.Get<string>("placeTo");
                Logger.Instance.Log($"[{_robotName}] Placed wafer {wafer} at {to}");

                // Send PLACE event to destination station
                if (!string.IsNullOrEmpty(to) && stations.TryGetValue(to, out var station))
                {
                    station.Tell(new SendEvent("PLACE", new { wafer }));
                }
            })
            .WithAction("onReturning", (ctx, _) =>
            {
                Logger.Instance.Log($"[{_robotName}] Returning to idle position...");
            })
            .WithAction("onTransferComplete", (ctx, _) =>
            {
                Logger.Instance.Log($"[{_robotName}] Transfer complete");
            })
            // Services for movement delays
            .WithDelayService("moveToPickup", _transferTimeMs)
            .WithDelayService("moveToPlace", _transferTimeMs)
            .WithDelayService("returnToIdle", _transferTimeMs / 2)
            // Actors for message routing
            .WithActor("scheduler", scheduler)
            .BuildAndStart(_robotName);
    }

    public void SendTransfer(int waferId, string from, string to)
    {
        _machine.Tell(new SendEvent("TRANSFER", new
        {
            waferId,
            from,
            to
        }));
    }

    public void SendDestinationReady()
    {
        _machine.Tell(new SendEvent("DESTINATION_READY", null));
    }

    public void Reset()
    {
        _machine.Tell(new SendEvent("RESET", null));
    }

    public void Stop()
    {
        _machine.Tell(new StopMachine());
    }
}
