using System.Linq;
using System.Collections.Generic;
using XStateNet;
using XStateNet.Orchestration;

namespace CMPSimulator.XStateStations;

/// <summary>
/// Buffer Station using XStateNet state machine
/// States: empty → occupied → ready
/// </summary>
public class XBufferStation
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

    public XBufferStation(string stationId, EventBusOrchestrator orchestrator)
    {
        _stationId = stationId;
        _orchestrator = orchestrator;

        var definition = $$"""
        {
            "id": "{{_stationId}}",
            "initial": "empty",
            "states": {
                "empty": {
                    "on": {
                        "WAFER_ARRIVED": {
                            "target": "occupied",
                            "actions": ["storeWafer"]
                        }
                    }
                },
                "occupied": {
                    "entry": ["checkReady"],
                    "after": {
                        "100": {
                            "target": "ready"
                        }
                    }
                },
                "ready": {
                    "entry": ["notifyReady"],
                    "on": {
                        "WAFER_PICKED_UP": {
                            "target": "empty",
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

        AddAction("storeWafer", (sm) =>
        {
            var eventData = sm.ContextMap?["_event"];
            if (eventData is Dictionary<string, object> dict && dict.ContainsKey("waferId"))
            {
                _currentWaferId = Convert.ToInt32(dict["waferId"]);
            }
            Log($"🟡 Stored Wafer {_currentWaferId}");
            WaferArrived?.Invoke(this, new WaferArrivedEventArgs(_currentWaferId!.Value, _stationId));
        });

        AddAction("checkReady", (sm) =>
        {
            Log($"🟡 Checking if ready to dispatch Wafer {_currentWaferId}");
        });

        AddAction("notifyReady", (sm) =>
        {
            if (_currentWaferId.HasValue)
            {
                Log($"📤 Wafer {_currentWaferId} ready for return to LoadPort");

                // Request WTR1 to transfer to LoadPort
                var payload = new Dictionary<string, object>
                {
                    ["waferId"] = _currentWaferId.Value,
                    ["from"] = "buffer",
                    ["to"] = "loadport"
                };
                _ = _orchestrator.SendEventFireAndForgetAsync(
                    MachineId,
                    "wtr1",
                    "TRANSFER_REQUEST",
                    payload
                );
            }
        });

        AddAction("clearWafer", (sm) =>
        {
            Log($"✓ Wafer {_currentWaferId} picked up, now Empty");
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
        LogMessage?.Invoke(this, $"[Buffer] {message}");
    }
}
