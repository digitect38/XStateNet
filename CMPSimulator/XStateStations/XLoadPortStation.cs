using System.Linq;
using System.Collections.Generic;
using XStateNet;
using XStateNet.Orchestration;

namespace CMPSimulator.XStateStations;

/// <summary>
/// LoadPort Station using XStateNet state machine
/// States: ready → dispatching → waiting → ready (또는 receiving)
/// </summary>
public class XLoadPortStation
{
    private readonly string _stationId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IStateMachine _machine;
    private int _waferCount = 25;
    private int _nextWaferId = 1;
    private int _processedCount = 0;

    public string MachineId => _stationId;
    public IStateMachine Machine => _machine;
    public event EventHandler<string>? LogMessage;
    public event EventHandler<WaferDispatchEventArgs>? WaferDispatched;
    public event EventHandler<WaferReturnEventArgs>? WaferReturned;

    public XLoadPortStation(string stationId, EventBusOrchestrator orchestrator)
    {
        _stationId = stationId;
        _orchestrator = orchestrator;

        var definition = $$"""
        {
            "id": "{{_stationId}}",
            "initial": "ready",
            "states": {
                "ready": {
                    "on": {
                        "START_SIMULATION": {
                            "target": "dispatching",
                            "cond": "hasWafersToDispatch"
                        },
                        "WAFER_ARRIVED": {
                            "target": "receiving",
                            "actions": ["acceptReturningWafer"]
                        }
                    }
                },
                "dispatching": {
                    "entry": ["requestWTR1Transfer"],
                    "on": {
                        "WAFER_PICKED_UP": {
                            "target": "waiting",
                            "actions": ["decrementCount"]
                        }
                    }
                },
                "waiting": {
                    "after": {
                        "3500": [
                            {
                                "target": "dispatching",
                                "cond": "hasWafersToDispatch"
                            },
                            {
                                "target": "ready"
                            }
                        ]
                    }
                },
                "receiving": {
                    "entry": ["placeWaferInSlot"],
                    "after": {
                        "200": {
                            "target": "ready",
                            "actions": ["incrementCount"]
                        }
                    }
                }
            }
        }
        """;

        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["hasWafersToDispatch"] = (machine) =>
            {
                return _nextWaferId <= 25 && _waferCount > 0;
            }
        };

        // We'll use a custom action map to access StateMachine for event data
        var actionMap = new ActionMap();

        // Helper to create actions that can access event data
        void AddAction(string name, Action<StateMachine> handler)
        {
            actionMap[name] = new List<NamedAction>
            {
                new NamedAction(name, async (sm) =>
                {
                    handler(sm);
                    await Task.CompletedTask;
                })
            };
        }

        AddAction("requestWTR1Transfer", (sm) =>
        {
            Console.WriteLine($"[{MachineId}] ACTION: requestWTR1Transfer STARTED");
            var waferId = _nextWaferId;
            Log($"📦 Dispatching Wafer {waferId}");

            // Notify UI
            WaferDispatched?.Invoke(this, new WaferDispatchEventArgs(waferId));

            // Request WTR1 to transfer to Polisher (fire-and-forget through orchestrator)
            Console.WriteLine($"[{MachineId}] Sending TRANSFER_REQUEST to wtr1 for wafer {waferId}");
            var payload = new Dictionary<string, object>
            {
                ["waferId"] = waferId,
                ["from"] = "loadport",
                ["to"] = "polisher"
            };
            _ = _orchestrator.SendEventFireAndForgetAsync(
                MachineId,
                "wtr1",
                "TRANSFER_REQUEST",
                payload
            );
            Console.WriteLine($"[{MachineId}] ACTION: requestWTR1Transfer COMPLETED");
        });

        AddAction("decrementCount", (sm) =>
        {
            _waferCount--;
            _nextWaferId++;
            Log($"📦 Wafer dispatched. Remaining: {_waferCount}/25");
        });

        AddAction("acceptReturningWafer", (sm) =>
        {
            // Extract waferId from event data using ContextMap
            var eventData = sm.ContextMap?["_event"];

            int waferId = 0;
            if (eventData is Dictionary<string, object> dict && dict.ContainsKey("waferId"))
            {
                waferId = Convert.ToInt32(dict["waferId"]);
            }

            Log($"📦 Receiving returned Wafer {waferId}");
            _processedCount++;
            _nextWaferId = waferId; // Store for use in placeWaferInSlot
            Log($"✓ Processed count: {_processedCount}/25");
        });

        AddAction("placeWaferInSlot", (sm) =>
        {
            var waferId = _nextWaferId; // Use stored value

            // Notify UI to place wafer back in original slot
            WaferReturned?.Invoke(this, new WaferReturnEventArgs(waferId));

            Log($"✓ Wafer {waferId} returned to original slot");
        });

        AddAction("incrementCount", (sm) =>
        {
            _waferCount++;
            _processedCount++;
            Log($"🏁 Wafer journey complete ({_processedCount}/25)");
        });

        // Create guard map
        var guardMap = new GuardMap();
        foreach (var (guardName, guardFunc) in guards)
        {
            guardMap[guardName] = new NamedGuard(guardFunc, guardName);
        }

        // Create machine directly with actionMap
        Console.WriteLine($"[{_stationId}] Creating StateMachine...");
        _machine = StateMachineFactory.CreateFromScript(
            jsonScript: definition,
            threadSafe: false,
            guidIsolate: false,
            actionCallbacks: actionMap,
            guardCallbacks: guardMap
        );
        Console.WriteLine($"[{_stationId}] StateMachine created, ID: {_machine.machineId}");

        // Register with orchestrator
        Console.WriteLine($"[{_stationId}] Registering with orchestrator...");
        _orchestrator.RegisterMachine(MachineId, _machine);
        Console.WriteLine($"[{_stationId}] Registration complete");
    }

    public async Task<string> StartAsync() => await _machine.StartAsync();
    public string GetCurrentState() => _machine.GetActiveStateNames();

    public async Task StartSimulation()
    {
        Console.WriteLine($"[{MachineId}] StartSimulation() called - sending START_SIMULATION event");
        var result = await _orchestrator.SendEventAsync(
            fromMachineId: "system",
            toMachineId: MachineId,
            eventName: "START_SIMULATION"
        );
        Console.WriteLine($"[{MachineId}] SendEventAsync returned: {result}");
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(this, $"[LoadPort] {message}");
    }
}

public class WaferDispatchEventArgs : EventArgs
{
    public int WaferId { get; }
    public WaferDispatchEventArgs(int waferId) => WaferId = waferId;
}

public class WaferReturnEventArgs : EventArgs
{
    public int WaferId { get; }
    public WaferReturnEventArgs(int waferId) => WaferId = waferId;
}
