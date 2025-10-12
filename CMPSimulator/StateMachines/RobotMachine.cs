using XStateNet;
using XStateNet.Orchestration;
using Newtonsoft.Json.Linq;

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
    private readonly int _transferTimeMs;
    private int? _heldWafer;
    private string? _pickFrom;
    private string? _placeTo;

    public string RobotName => _robotName;
    public string CurrentState => _machine.CurrentState;
    public int? HeldWafer => _heldWafer;

    public RobotMachine(
        string robotName,
        EventBusOrchestrator orchestrator,
        int transferTimeMs,
        Action<string> logger)
    {
        _robotName = robotName;
        _transferTimeMs = transferTimeMs;

        var definition = $$"""
        {
            "id": "{{robotName}}",
            "initial": "idle",
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
                    "entry": ["reportHolding", "logHolding", "autoPlace"],
                    "on": {
                        "PLACE": {
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
                logger($"[{_robotName}] Idle");
                ctx.RequestSend("scheduler", "ROBOT_STATUS", new JObject
                {
                    ["robot"] = _robotName,
                    ["state"] = "idle",
                    ["wafer"] = (int?)null
                });
            },

            ["storeTransferInfo"] = (ctx) =>
            {
                // Transfer info should be set externally before sending TRANSFER event
                logger($"[{_robotName}] Transfer command: {_pickFrom} → {_placeTo} (Wafer {_heldWafer})");
            },

            ["logPickingUp"] = (ctx) =>
            {
                logger($"[{_robotName}] Moving to pick from {_pickFrom}...");
            },

            ["pickWafer"] = (ctx) =>
            {
                logger($"[{_robotName}] Picked wafer {_heldWafer} from {_pickFrom}");
                // Notify source station to update
                ctx.RequestSend(_pickFrom!, "PICK", new JObject { ["wafer"] = _heldWafer });
            },

            ["reportHolding"] = (ctx) =>
            {
                ctx.RequestSend("scheduler", "ROBOT_STATUS", new JObject
                {
                    ["robot"] = _robotName,
                    ["state"] = "holding",
                    ["wafer"] = _heldWafer
                });
            },

            ["logHolding"] = (ctx) =>
            {
                logger($"[{_robotName}] Holding wafer {_heldWafer}");
            },

            ["autoPlace"] = (ctx) =>
            {
                // Immediately transition to placingDown
                ctx.RequestSend(_robotName, "PLACE");
            },

            ["logPlacingDown"] = (ctx) =>
            {
                logger($"[{_robotName}] Moving to place at {_placeTo}...");
            },

            ["placeWafer"] = (ctx) =>
            {
                logger($"[{_robotName}] Placed wafer {_heldWafer} at {_placeTo}");
                // Notify destination station
                ctx.RequestSend(_placeTo!, "PLACE", new JObject { ["wafer"] = _heldWafer });
            },

            ["logReturning"] = (ctx) =>
            {
                logger($"[{_robotName}] Returning to idle position...");
            },

            ["completeTransfer"] = (ctx) =>
            {
                logger($"[{_robotName}] Transfer complete");
                _heldWafer = null;
                _pickFrom = null;
                _placeTo = null;
            }
        };

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
            guards: null,
            services: services,
            enableGuidIsolation: false
        );
    }

    public async Task<string> StartAsync() => await _machine.StartAsync();

    public void SetTransferInfo(int waferId, string from, string to)
    {
        _heldWafer = waferId;
        _pickFrom = from;
        _placeTo = to;
    }
}
