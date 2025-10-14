using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Monitoring;
using Newtonsoft.Json.Linq;

namespace CMPSimulator.StateMachines;

/// <summary>
/// LoadPort State Machine (E84 Handshake + E87 Carrier Management)
/// Represents the physical docking station for carriers
/// States: empty → carrierArrived → docked → processing → unloading → empty
/// </summary>
public class LoadPortMachine
{
    private readonly IPureStateMachine _machine;
    private readonly StateMachineMonitor _monitor;
    private readonly Action<string> _logger;
    private readonly EventBusOrchestrator _orchestrator;
    private StateMachine? _underlyingMachine;

    public string CurrentState => _machine.CurrentState;
    public string? DockedCarrierId { get; private set; }

    // Expose StateChanged event for Pub/Sub
    public event EventHandler<StateTransitionEventArgs>? StateChanged
    {
        add => _monitor.StateTransitioned += value;
        remove => _monitor.StateTransitioned -= value;
    }

    // Events for carrier lifecycle
    public event EventHandler<string>? CarrierDocked;
    public event EventHandler<string>? CarrierUndocked;

    public LoadPortMachine(string id, EventBusOrchestrator orchestrator, Action<string> logger)
    {
        _logger = logger;
        _orchestrator = orchestrator;

        var definition = $$"""
        {
            "id": "{{id}}",
            "initial": "empty",
            "states": {
                "empty": {
                    "entry": ["reportEmpty"],
                    "on": {
                        "CARRIER_ARRIVE": {
                            "target": "carrierArrived",
                            "actions": ["onCarrierArrive"]
                        }
                    }
                },
                "carrierArrived": {
                    "entry": ["reportCarrierArrived"],
                    "on": {
                        "DOCK": {
                            "target": "docked",
                            "actions": ["onDock"]
                        }
                    }
                },
                "docked": {
                    "entry": ["reportDocked"],
                    "on": {
                        "START_PROCESSING": {
                            "target": "processing",
                            "actions": ["onStartProcessing"]
                        }
                    }
                },
                "processing": {
                    "entry": ["reportProcessing"],
                    "on": {
                        "COMPLETE": {
                            "target": "unloading",
                            "actions": ["onComplete"]
                        }
                    }
                },
                "unloading": {
                    "entry": ["reportUnloading"],
                    "on": {
                        "UNDOCK": {
                            "target": "empty",
                            "actions": ["onUndock"]
                        }
                    }
                }
            }
        }
        """;

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["reportEmpty"] = (ctx) =>
            {
                DockedCarrierId = null;
                _logger($"[LoadPort] Empty - waiting for carrier");

                // Send status update to orchestrator
                ctx.RequestSend("scheduler", "LOADPORT_STATUS", new JObject
                {
                    ["station"] = "LoadPort",
                    ["state"] = "empty"
                });
            },

            ["onCarrierArrive"] = (ctx) =>
            {
                // Extract carrier ID from event
                if (_underlyingMachine?.ContextMap?["_event"] is JObject data)
                {
                    DockedCarrierId = data["carrierId"]?.ToString();
                    _logger($"[LoadPort] Carrier {DockedCarrierId} arrived at load port");
                }
            },

            ["reportCarrierArrived"] = (ctx) =>
            {
                _logger($"[LoadPort] Carrier {DockedCarrierId} ready to dock");

                // Send status update
                ctx.RequestSend("scheduler", "LOADPORT_STATUS", new JObject
                {
                    ["station"] = "LoadPort",
                    ["state"] = "carrierArrived",
                    ["carrierId"] = DockedCarrierId
                });
            },

            ["onDock"] = (ctx) =>
            {
                _logger($"[LoadPort] Docking carrier {DockedCarrierId}");
            },

            ["reportDocked"] = (ctx) =>
            {
                _logger($"[LoadPort] Carrier {DockedCarrierId} docked successfully");

                // Notify about carrier docking
                CarrierDocked?.Invoke(this, DockedCarrierId ?? "UNKNOWN");

                // Send status update
                ctx.RequestSend("scheduler", "LOADPORT_STATUS", new JObject
                {
                    ["station"] = "LoadPort",
                    ["state"] = "docked",
                    ["carrierId"] = DockedCarrierId
                });
            },

            ["onStartProcessing"] = (ctx) =>
            {
                _logger($"[LoadPort] Starting to process wafers from carrier {DockedCarrierId}");
            },

            ["reportProcessing"] = (ctx) =>
            {
                _logger($"[LoadPort] Processing carrier {DockedCarrierId}");

                // Send status update
                ctx.RequestSend("scheduler", "LOADPORT_STATUS", new JObject
                {
                    ["station"] = "LoadPort",
                    ["state"] = "processing",
                    ["carrierId"] = DockedCarrierId
                });
            },

            ["onComplete"] = (ctx) =>
            {
                _logger($"[LoadPort] All wafers from carrier {DockedCarrierId} processed");
            },

            ["reportUnloading"] = (ctx) =>
            {
                _logger($"[LoadPort] Unloading carrier {DockedCarrierId}");

                // Send status update
                ctx.RequestSend("scheduler", "LOADPORT_STATUS", new JObject
                {
                    ["station"] = "LoadPort",
                    ["state"] = "unloading",
                    ["carrierId"] = DockedCarrierId
                });
            },

            ["onUndock"] = (ctx) =>
            {
                _logger($"[LoadPort] Undocking carrier {DockedCarrierId}");

                // Notify about carrier undocking
                CarrierUndocked?.Invoke(this, DockedCarrierId ?? "UNKNOWN");
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: id,
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

        var context = _orchestrator.GetOrCreateContext("LoadPort");
        await context.ExecuteDeferredSends();

        return result;
    }

    /// <summary>
    /// Send CARRIER_ARRIVE event with carrier ID
    /// </summary>
    public void SendCarrierArrive(string carrierId)
    {
        var context = _orchestrator.GetOrCreateContext("LoadPort");
        context.RequestSend("LoadPort", "CARRIER_ARRIVE", new JObject
        {
            ["carrierId"] = carrierId
        });
    }

    /// <summary>
    /// Send DOCK event to dock the arrived carrier
    /// </summary>
    public void SendDock()
    {
        var context = _orchestrator.GetOrCreateContext("LoadPort");
        context.RequestSend("LoadPort", "DOCK", new JObject());
    }

    /// <summary>
    /// Send START_PROCESSING event
    /// </summary>
    public void SendStartProcessing()
    {
        var context = _orchestrator.GetOrCreateContext("LoadPort");
        context.RequestSend("LoadPort", "START_PROCESSING", new JObject());
    }

    /// <summary>
    /// Send COMPLETE event when all wafers are processed
    /// </summary>
    public void SendComplete()
    {
        var context = _orchestrator.GetOrCreateContext("LoadPort");
        context.RequestSend("LoadPort", "COMPLETE", new JObject());
    }

    /// <summary>
    /// Send UNDOCK event to undock the carrier
    /// </summary>
    public void SendUndock()
    {
        var context = _orchestrator.GetOrCreateContext("LoadPort");
        context.RequestSend("LoadPort", "UNDOCK", new JObject());
    }
}
