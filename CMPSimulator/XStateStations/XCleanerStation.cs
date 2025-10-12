using System.Linq;
using System.Collections.Generic;
using XStateNet;
using XStateNet.Orchestration;

namespace CMPSimulator.XStateStations;

/// <summary>
/// Cleaner Station using XStateNet state machine
/// States: idle → processing → ready
/// </summary>
public class XCleanerStation
{
    private readonly string _stationId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IStateMachine _machine;
    private int? _currentWaferId;

    public string MachineId => _stationId;
    public IStateMachine Machine => _machine;
    public event EventHandler<string>? LogMessage;
    public event EventHandler<WaferArrivedEventArgs>? WaferArrived;
    public event EventHandler<WaferPickedUpEventArgs>? WaferPickedUp;

    public XCleanerStation(string stationId, EventBusOrchestrator orchestrator)
    {
        _stationId = stationId;
        _orchestrator = orchestrator;

        var definition = $$"""
        {
            "id": "{{_stationId}}",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "WAFER_ARRIVED": {
                            "target": "processing",
                            "actions": ["acceptWafer"]
                        }
                    }
                },
                "processing": {
                    "entry": ["startProcessing"],
                    "after": {
                        "2500": {
                            "target": "ready",
                            "actions": ["processingComplete"]
                        }
                    }
                },
                "ready": {
                    "entry": ["notifyReady"],
                    "on": {
                        "WAFER_PICKED_UP": {
                            "target": "idle",
                            "actions": ["clearWafer"]
                        }
                    }
                }
            }
        }
        """;

        var actionMap = new ActionMap();

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

        AddAction("acceptWafer", (sm) =>
        {
            var eventData = sm.ContextMap?["_event"];
            if (eventData is Dictionary<string, object> dict && dict.ContainsKey("waferId"))
            {
                _currentWaferId = Convert.ToInt32(dict["waferId"]);
            }
            Log($"📥 Accepted Wafer {_currentWaferId}");
            WaferArrived?.Invoke(this, new WaferArrivedEventArgs(_currentWaferId!.Value, _stationId));
        });

        AddAction("startProcessing", (sm) =>
        {
            Log($"⚙️  Cleaning Wafer {_currentWaferId} (2500ms)");
        });

        AddAction("processingComplete", (sm) =>
        {
            Log($"✓ Wafer {_currentWaferId} cleaning complete");
        });

        AddAction("notifyReady", (sm) =>
        {
            if (_currentWaferId.HasValue)
            {
                Log($"📤 Wafer {_currentWaferId} ready for return via Buffer");

                // Request WTR2 to transfer to Buffer
                var payload = new Dictionary<string, object>
                {
                    ["waferId"] = _currentWaferId.Value,
                    ["from"] = "cleaner",
                    ["to"] = "buffer"
                };
                _ = _orchestrator.SendEventFireAndForgetAsync(
                    MachineId,
                    "wtr2",
                    "TRANSFER_REQUEST",
                    payload
                );
            }
        });

        AddAction("clearWafer", (sm) =>
        {
            Log($"✓ Wafer {_currentWaferId} picked up, now Idle");
            WaferPickedUp?.Invoke(this, new WaferPickedUpEventArgs(_currentWaferId!.Value, _stationId));
            _currentWaferId = null;
        });

        _machine = StateMachineFactory.CreateFromScript(
            jsonScript: definition,
            threadSafe: false,
            guidIsolate: false,
            actionCallbacks: actionMap
        );

        _orchestrator.RegisterMachine(MachineId, _machine);
    }

    public async Task<string> StartAsync() => await _machine.StartAsync();
    public string GetCurrentState() => _machine.GetActiveStateNames();

    private void Log(string message)
    {
        LogMessage?.Invoke(this, $"[Cleaner] {message}");
    }
}
