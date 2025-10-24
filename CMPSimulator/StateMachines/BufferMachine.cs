using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Monitoring;
using Newtonsoft.Json.Linq;
using LoggerHelper;

namespace CMPSimulator.StateMachines;

/// <summary>
/// Buffer Station State Machine
/// States: Empty → Occupied → Empty
/// Simple storage with no processing
/// </summary>
public class BufferMachine
{
    private readonly IPureStateMachine _machine;
    private readonly StateMachineMonitor _monitor;
    private readonly EventBusOrchestrator _orchestrator;
    private StateMachine? _underlyingMachine; // Access to underlying machine for ContextMap
    private int? _currentWafer;

    public string CurrentState => _machine?.CurrentState ?? "initializing";
    public int? CurrentWafer => _currentWafer;

    // Expose StateChanged event for Pub/Sub
    public event EventHandler<StateTransitionEventArgs>? StateChanged
    {
        add => _monitor.StateTransitioned += value;
        remove => _monitor.StateTransitioned -= value;
    }

    public BufferMachine(EventBusOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;

        var definition = """
        {
            "id": "buffer",
            "initial": "empty",
            "states": {
                "empty": {
                    "entry": ["reportEmpty"],
                    "on": {
                        "PLACE": {
                            "target": "occupied",
                            "actions": ["onPlace"]
                        }
                    }
                },
                "occupied": {
                    "entry": ["reportOccupied"],
                    "on": {
                        "PICK": {
                            "target": "empty",
                            "actions": ["onPick"]
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
                LoggerHelper.Logger.Instance.Log("[Buffer] Empty");
                ctx.RequestSend("scheduler", "STATION_STATUS", new JObject
                {
                    ["station"] = "buffer",
                    ["state"] = "empty",
                    ["wafer"] = (int?)null
                });
            },

            ["onPlace"] = (ctx) =>
            {
                // Extract wafer ID from underlying state machine's ContextMap
                if (_underlyingMachine?.ContextMap != null)
                {
                    var data = _underlyingMachine.ContextMap["_event"] as JObject;
                    if (data != null)
                    {
                        _currentWafer = data["wafer"]?.ToObject<int?>();
                    }
                }

                LoggerHelper.Logger.Instance.Log($"[Buffer] Wafer {_currentWafer} placed");
            },

            ["reportOccupied"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Buffer] → reportOccupied: Sending STATION_STATUS (wafer {_currentWafer})");
                ctx.RequestSend("scheduler", "STATION_STATUS", new JObject
                {
                    ["station"] = "buffer",
                    ["state"] = "occupied",
                    ["wafer"] = _currentWafer
                });
                LoggerHelper.Logger.Instance.Log($"[Buffer] → reportOccupied: STATION_STATUS queued in deferred sends");
            },

            ["onPick"] = (ctx) =>
            {
                int pickedWafer = _currentWafer ?? 0;
                LoggerHelper.Logger.Instance.Log($"[Buffer] Wafer {pickedWafer} picked");
                _currentWafer = null;
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: "buffer",
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: null,
            services: null,
            enableGuidIsolation: false
        );

        // Create and start monitor for state change notifications
        // Also store reference to underlying machine for ContextMap access
        _underlyingMachine = ((PureStateMachineAdapter)_machine).GetUnderlying() as StateMachine;
        _monitor = new StateMachineMonitor(_underlyingMachine!);
        _monitor.StartMonitoring();

        // Note: ExecuteDeferredSends is now automatically handled by EventBusOrchestrator
    }

    public async Task<string> StartAsync()
    {
        var result = await _machine.StartAsync();

        // NOTE: ExecuteDeferredSends is now automatically handled by StateChanged event
        // Do NOT call it manually here or messages will be sent twice!

        return result;
    }

    public void SetWafer(int waferId)
    {
        _currentWafer = waferId;
    }

    /// <summary>
    /// Reset the station's wafer reference
    /// Used during carrier swap to clear old wafer references
    /// </summary>
    public void ResetWafer()
    {
        _currentWafer = null;
    }

    /// <summary>
    /// Broadcast current station status to scheduler
    /// Used after carrier swap to inform scheduler of current state
    /// </summary>
    public void BroadcastStatus(OrchestratedContext context)
    {
        // Extract leaf state name (e.g., "#buffer.empty" → "empty")
        var state = CurrentState;
        if (state.Contains("."))
        {
            state = state.Substring(state.LastIndexOf('.') + 1);
        }
        else if (state.StartsWith("#"))
        {
            state = state.Substring(1);
        }

        context.RequestSend("scheduler", "STATION_STATUS", new JObject
        {
            ["station"] = "buffer",
            ["state"] = state,
            ["wafer"] = _currentWafer
        });
    }
}
