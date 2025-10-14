using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Monitoring;
using Newtonsoft.Json.Linq;

namespace CMPSimulator.StateMachines;

/// <summary>
/// Carrier State Machine (SEMI E87 Carrier Management)
/// Represents a FOUP (Front Opening Unified Pod) containing wafers
/// States: waiting → atLoadPort → transferring → atLoadPort → departed
/// </summary>
public class CarrierMachine
{
    private readonly IPureStateMachine _machine;
    private readonly StateMachineMonitor _monitor;
    private readonly Action<string> _logger;
    private readonly EventBusOrchestrator _orchestrator;
    private StateMachine? _underlyingMachine;

    public string CarrierId { get; }
    public string CurrentState => _machine.CurrentState;
    public List<int> WaferIds { get; private set; }
    public List<int> CompletedWafers { get; private set; }

    // Expose StateChanged event for Pub/Sub
    public event EventHandler<StateTransitionEventArgs>? StateChanged
    {
        add => _monitor.StateTransitioned += value;
        remove => _monitor.StateTransitioned -= value;
    }

    public CarrierMachine(string carrierId, List<int> waferIds, EventBusOrchestrator orchestrator, Action<string> logger)
    {
        CarrierId = carrierId;
        WaferIds = waferIds;
        CompletedWafers = new List<int>();
        _logger = logger;
        _orchestrator = orchestrator;

        var definition = $$"""
        {
            "id": "{{carrierId}}",
            "initial": "waiting",
            "states": {
                "waiting": {
                    "entry": ["reportWaiting"],
                    "on": {
                        "MOVE_TO_LOADPORT": {
                            "target": "transferring",
                            "actions": ["onMoveToLoadPort"]
                        }
                    }
                },
                "transferring": {
                    "entry": ["reportTransferring"],
                    "on": {
                        "ARRIVE_AT_LOADPORT": {
                            "target": "atLoadPort",
                            "actions": ["onArriveAtLoadPort"]
                        }
                    }
                },
                "atLoadPort": {
                    "entry": ["reportAtLoadPort"],
                    "on": {
                        "WAFER_COMPLETED": {
                            "actions": ["onWaferCompleted"]
                        },
                        "ALL_COMPLETE": {
                            "target": "departing",
                            "actions": ["onAllComplete"]
                        }
                    }
                },
                "departing": {
                    "entry": ["reportDeparting"],
                    "on": {
                        "DEPART": {
                            "target": "departed",
                            "actions": ["onDepart"]
                        }
                    }
                },
                "departed": {
                    "entry": ["reportDeparted"],
                    "type": "final"
                }
            }
        }
        """;

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["reportWaiting"] = (ctx) =>
            {
                _logger($"[Carrier {CarrierId}] Waiting in queue ({WaferIds.Count} wafers)");

                // Send status to scheduler
                ctx.RequestSend("scheduler", "CARRIER_STATUS", new JObject
                {
                    ["carrierId"] = CarrierId,
                    ["state"] = "waiting",
                    ["waferCount"] = WaferIds.Count
                });
            },

            ["onMoveToLoadPort"] = (ctx) =>
            {
                _logger($"[Carrier {CarrierId}] Moving to LoadPort");
            },

            ["reportTransferring"] = (ctx) =>
            {
                _logger($"[Carrier {CarrierId}] Transferring to LoadPort");

                ctx.RequestSend("scheduler", "CARRIER_STATUS", new JObject
                {
                    ["carrierId"] = CarrierId,
                    ["state"] = "transferring"
                });
            },

            ["onArriveAtLoadPort"] = (ctx) =>
            {
                _logger($"[Carrier {CarrierId}] Arrived at LoadPort");
            },

            ["reportAtLoadPort"] = (ctx) =>
            {
                _logger($"[Carrier {CarrierId}] At LoadPort - ready to process {WaferIds.Count} wafers");

                ctx.RequestSend("scheduler", "CARRIER_STATUS", new JObject
                {
                    ["carrierId"] = CarrierId,
                    ["state"] = "atLoadPort",
                    ["pendingWafers"] = WaferIds.Count - CompletedWafers.Count
                });
            },

            ["onWaferCompleted"] = (ctx) =>
            {
                // Extract wafer ID from event
                if (_underlyingMachine?.ContextMap?["_event"] is JObject data)
                {
                    int waferId = data["waferId"]?.ToObject<int>() ?? 0;
                    if (waferId > 0 && !CompletedWafers.Contains(waferId))
                    {
                        CompletedWafers.Add(waferId);
                        _logger($"[Carrier {CarrierId}] Wafer {waferId} completed ({CompletedWafers.Count}/{WaferIds.Count})");

                        // Send status update
                        ctx.RequestSend("scheduler", "CARRIER_STATUS", new JObject
                        {
                            ["carrierId"] = CarrierId,
                            ["state"] = "atLoadPort",
                            ["completedWafers"] = CompletedWafers.Count,
                            ["pendingWafers"] = WaferIds.Count - CompletedWafers.Count
                        });
                    }
                }
            },

            ["onAllComplete"] = (ctx) =>
            {
                _logger($"[Carrier {CarrierId}] All {WaferIds.Count} wafers completed");
            },

            ["reportDeparting"] = (ctx) =>
            {
                _logger($"[Carrier {CarrierId}] Ready to depart");

                ctx.RequestSend("scheduler", "CARRIER_STATUS", new JObject
                {
                    ["carrierId"] = CarrierId,
                    ["state"] = "departing"
                });
            },

            ["onDepart"] = (ctx) =>
            {
                _logger($"[Carrier {CarrierId}] Departed from LoadPort");
            },

            ["reportDeparted"] = (ctx) =>
            {
                _logger($"[Carrier {CarrierId}] Departed successfully");

                ctx.RequestSend("scheduler", "CARRIER_STATUS", new JObject
                {
                    ["carrierId"] = CarrierId,
                    ["state"] = "departed"
                });
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: carrierId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: null,
            services: null,
            enableGuidIsolation: false
        );

        _underlyingMachine = ((PureStateMachineAdapter)_machine).GetUnderlying() as StateMachine;
        _monitor = new StateMachineMonitor(_underlyingMachine!);
        _monitor.StartMonitoring();
    }

    public async Task<string> StartAsync()
    {
        var result = await _machine.StartAsync();

        var context = _orchestrator.GetOrCreateContext(CarrierId);
        await context.ExecuteDeferredSends();

        return result;
    }

    /// <summary>
    /// Send MOVE_TO_LOADPORT event
    /// </summary>
    public void SendMoveToLoadPort()
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "MOVE_TO_LOADPORT", new JObject());
    }

    /// <summary>
    /// Send ARRIVE_AT_LOADPORT event
    /// </summary>
    public void SendArriveAtLoadPort()
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "ARRIVE_AT_LOADPORT", new JObject());
    }

    /// <summary>
    /// Send WAFER_COMPLETED event when a wafer finishes processing
    /// </summary>
    public void SendWaferCompleted(int waferId)
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "WAFER_COMPLETED", new JObject
        {
            ["waferId"] = waferId
        });
    }

    /// <summary>
    /// Send ALL_COMPLETE event when all wafers are done
    /// </summary>
    public void SendAllComplete()
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "ALL_COMPLETE", new JObject());
    }

    /// <summary>
    /// Send DEPART event to remove carrier from load port
    /// </summary>
    public void SendDepart()
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "DEPART", new JObject());
    }

    /// <summary>
    /// Check if all wafers are completed
    /// </summary>
    public bool AreAllWafersCompleted()
    {
        return CompletedWafers.Count >= WaferIds.Count;
    }
}
