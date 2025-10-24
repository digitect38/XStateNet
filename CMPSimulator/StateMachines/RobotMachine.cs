using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Monitoring;
using Newtonsoft.Json.Linq;
using LoggerHelper;

namespace CMPSimulator.StateMachines;

/// <summary>
/// Robot State Machine (R1, R2, R3)
/// States: Idle → PickingUp → Holding → PlacingDown → Returning → Idle
/// Receives commands from Scheduler only
/// </summary>
public class RobotMachine
{
    private readonly string _robotName;
    private readonly IPureStateMachine _machine;
    private readonly StateMachineMonitor _monitor;
    private readonly int _transferTimeMs;
    private readonly EventBusOrchestrator _orchestrator; // Already has this field
    private StateMachine? _underlyingMachine; // Access to underlying machine for ContextMap
    private int? _heldWafer;
    private string? _pickFrom;
    private string? _placeTo;

    public string RobotName => _robotName;
    public string CurrentState => _machine?.CurrentState ?? "initializing";
    public int? HeldWafer => _heldWafer;

    // Expose StateChanged event for Pub/Sub
    public event EventHandler<StateTransitionEventArgs>? StateChanged
    {
        add => _monitor.StateTransitioned += value;
        remove => _monitor.StateTransitioned -= value;
    }

    public RobotMachine(
        string robotName,
        EventBusOrchestrator orchestrator,
        int transferTimeMs)
    {
        _robotName = robotName;
        _transferTimeMs = transferTimeMs;
        _orchestrator = orchestrator;

        var definition = $$"""
        {
            "id": "{{robotName}}",
            "initial": "idle",
            "on": {
                "RESET": {
                    "target": ".idle",
                    "actions": ["clearTransferInfo"]
                }
            },
            "states": {
                "idle": {
                    "entry": ["reportIdle"],
                    "on": {
                        "TRANSFER": {
                            "target": "pickingUp",
                            "actions": ["storeTransferInfo"]
                        }
                    }
                },
                "pickingUp": {
                    "entry": ["logPickingUp"],
                    "invoke": {
                        "src": "moveToPickup",
                        "onDone": {
                            "target": "holding",
                            "actions": ["pickWafer"]
                        }
                    }
                },
                "holding": {
                    "entry": ["reportHolding", "logHolding"],
                    "on": {
                        "DESTINATION_READY": {
                            "target": "placingDown"
                        }
                    }
                },
                "waitingDestination": {
                    "entry": ["logWaitingDestination"],
                    "on": {
                        "DESTINATION_READY": {
                            "target": "placingDown"
                        }
                    }
                },
                "placingDown": {
                    "entry": ["logPlacingDown"],
                    "invoke": {
                        "src": "moveToPlace",
                        "onDone": {
                            "target": "returning",
                            "actions": ["placeWafer"]
                        }
                    }
                },
                "returning": {
                    "entry": ["logReturning"],
                    "invoke": {
                        "src": "returnToIdle",
                        "onDone": {
                            "target": "idle",
                            "actions": ["completeTransfer"]
                        }
                    }
                }
            }
        }
        """;

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["reportIdle"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[{_robotName}] Idle");
                ctx.RequestSend("scheduler", "ROBOT_STATUS", new JObject
                {
                    ["robot"] = _robotName,
                    ["state"] = "idle",
                    ["wafer"] = (int?)null
                });
            },

            ["storeTransferInfo"] = (ctx) =>
            {
                // Extract transfer info from underlying state machine's ContextMap
                if (_underlyingMachine?.ContextMap != null)
                {
                    var data = _underlyingMachine.ContextMap["_event"] as JObject;
                    if (data != null)
                    {
                        _heldWafer = data["waferId"]?.ToObject<int?>();
                        _pickFrom = data["from"]?.ToString();
                        _placeTo = data["to"]?.ToString();
                    }
                }

                LoggerHelper.Logger.Instance.Log($"[{_robotName}] Transfer command: {_pickFrom} → {_placeTo} (Wafer {_heldWafer})");
            },

            ["logPickingUp"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[{_robotName}] Moving to pick from {_pickFrom}...");
            },

            ["pickWafer"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[{_robotName}] Picked wafer {_heldWafer} from {_pickFrom}");

                // Defensive check: Ensure _pickFrom is not null
                if (string.IsNullOrEmpty(_pickFrom))
                {
                    LoggerHelper.Logger.Instance.Log($"[{_robotName}] ⚠ WARNING: Cannot send PICK - _pickFrom is null (wafer: {_heldWafer})");
                    return;
                }

                // Notify source station to update
                ctx.RequestSend(_pickFrom, "PICK", new JObject { ["wafer"] = _heldWafer });
            },

            ["reportHolding"] = (ctx) =>
            {
                ctx.RequestSend("scheduler", "ROBOT_STATUS", new JObject
                {
                    ["robot"] = _robotName,
                    ["state"] = "holding",
                    ["wafer"] = _heldWafer,
                    ["waitingFor"] = _placeTo  // Tell scheduler where we want to go
                });
            },

            ["logHolding"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[{_robotName}] Holding wafer {_heldWafer} (waiting for Scheduler to confirm destination ready)");
            },

            ["logWaitingDestination"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[{_robotName}] ⏸ Waiting for destination '{_placeTo}' to become empty (holding wafer {_heldWafer})");
                ctx.RequestSend("scheduler", "ROBOT_STATUS", new JObject
                {
                    ["robot"] = _robotName,
                    ["state"] = "waitingDestination",
                    ["wafer"] = _heldWafer,
                    ["waitingFor"] = _placeTo
                });
            },

            ["logPlacingDown"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[{_robotName}] Moving to place at {_placeTo}...");
            },

            ["placeWafer"] = (ctx) =>
            {
                int placedWafer = _heldWafer ?? 0;
                LoggerHelper.Logger.Instance.Log($"[{_robotName}] Placed wafer {placedWafer} at {_placeTo}");

                // Defensive check: Ensure _placeTo is not null
                if (string.IsNullOrEmpty(_placeTo))
                {
                    LoggerHelper.Logger.Instance.Log($"[{_robotName}] ⚠ WARNING: Cannot send PLACE - _placeTo is null (wafer: {placedWafer})");
                    _heldWafer = null;  // Clear wafer even if we can't send
                    return;
                }

                // Send event to destination station with wafer info
                ctx.RequestSend(_placeTo, "PLACE", new JObject
                {
                    ["wafer"] = placedWafer
                });

                // Clear held wafer immediately after placing
                _heldWafer = null;
            },

            ["logReturning"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[{_robotName}] Returning to idle position...");
            },

            ["completeTransfer"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[{_robotName}] Transfer complete");
                // _heldWafer is already cleared in placeWafer
                // Clear transfer info
                _pickFrom = null;
                _placeTo = null;
            },

            ["clearTransferInfo"] = (ctx) =>
            {
                // Log current state before reset to help debug interrupted transitions
                var currentState = CurrentState;
                if (currentState.Contains("."))
                {
                    currentState = currentState.Substring(currentState.LastIndexOf('.') + 1);
                }

                var waferInfo = _heldWafer.HasValue ? $"wafer {_heldWafer}" : "no wafer";
                var transferInfo = "";
                if (!string.IsNullOrEmpty(_pickFrom) || !string.IsNullOrEmpty(_placeTo))
                {
                    transferInfo = $" (transfer: {_pickFrom ?? "?"} → {_placeTo ?? "?"})";
                }

                LoggerHelper.Logger.Instance.Log($"[{_robotName}] 🔄 RESET from state '{currentState}' with {waferInfo}{transferInfo}");

                // Clear all transfer info
                _heldWafer = null;
                _pickFrom = null;
                _placeTo = null;
            }
        };

        // No guards needed - Scheduler handles all decision making
        Dictionary<string, Func<StateMachine, bool>>? guards = null;

        var services = new Dictionary<string, Func<StateMachine, CancellationToken, Task<object>>>
        {
            ["moveToPickup"] = async (sm, ct) =>
            {
                await Task.Delay(_transferTimeMs, ct);
                return new { status = "SUCCESS" };
            },

            ["moveToPlace"] = async (sm, ct) =>
            {
                await Task.Delay(_transferTimeMs, ct);
                return new { status = "SUCCESS" };
            },

            ["returnToIdle"] = async (sm, ct) =>
            {
                await Task.Delay(_transferTimeMs / 2, ct); // Faster return
                return new { status = "SUCCESS" };
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: robotName,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: guards,
            services: services,
            enableGuidIsolation: false
        );

        // Create and start monitor for state change notifications
        // Also store reference to underlying machine for ContextMap access
        _underlyingMachine = ((PureStateMachineAdapter)_machine).GetUnderlying() as StateMachine;
        _monitor = new StateMachineMonitor(_underlyingMachine!);
        _monitor.StartMonitoring();

        // Note: ExecuteDeferredSends is now automatically handled by EventBusOrchestrator
        // when it registers the machine via RegisterMachine()
    }

    public async Task<string> StartAsync()
    {
        var result = await _machine.StartAsync();

        // NOTE: ExecuteDeferredSends is now automatically handled by StateChanged event
        // Do NOT call it manually here or messages will be sent twice!

        return result;
    }

    public void SetTransferInfo(int waferId, string from, string to)
    {
        _heldWafer = waferId;
        _pickFrom = from;
        _placeTo = to;
    }

    /// <summary>
    /// Reset the robot's wafer reference and state machine
    /// Used during carrier swap to clear old wafer references
    /// </summary>
    public void ResetWafer()
    {
        // Send RESET event to transition state machine back to idle
        var context = _orchestrator.GetOrCreateContext("RobotReset");
        context.RequestSend(_robotName, "RESET", null);

        // Execute deferred sends immediately
        _ = Task.Run(async () => await context.ExecuteDeferredSends());
    }

    /// <summary>
    /// Broadcast current robot status to scheduler
    /// Used after carrier swap to inform scheduler of current state
    /// </summary>
    public void BroadcastStatus(OrchestratedContext context)
    {
        // Extract leaf state name (e.g., "#R1.idle" → "idle")
        var state = CurrentState;
        if (state.Contains("."))
        {
            state = state.Substring(state.LastIndexOf('.') + 1);
        }
        else if (state.StartsWith("#"))
        {
            state = state.Substring(1);
        }

        context.RequestSend("scheduler", "ROBOT_STATUS", new JObject
        {
            ["robot"] = _robotName,
            ["state"] = state,
            ["wafer"] = _heldWafer,
            ["waitingFor"] = _placeTo
        });
    }
}
