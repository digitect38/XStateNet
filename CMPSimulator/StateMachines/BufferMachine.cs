using XStateNet;
using XStateNet.Orchestration;
using Newtonsoft.Json.Linq;

namespace CMPSimulator.StateMachines;

/// <summary>
/// Buffer Station State Machine
/// States: Empty → Occupied → Empty
/// Simple storage with no processing
/// </summary>
public class BufferMachine
{
    private readonly IPureStateMachine _machine;
    private int? _currentWafer;

    public string CurrentState => _machine.CurrentState;
    public int? CurrentWafer => _currentWafer;

    public BufferMachine(EventBusOrchestrator orchestrator, Action<string> logger)
    {
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
                logger("[Buffer] Empty");
                ctx.RequestSend("scheduler", "STATION_STATUS", new JObject
                {
                    ["station"] = "buffer",
                    ["state"] = "empty",
                    ["wafer"] = (int?)null
                });
            },

            ["onPlace"] = (ctx) =>
            {
                // Extract wafer ID from event data
                if (ctx.Event.Data is JObject jObj && jObj["wafer"] != null)
                {
                    _currentWafer = jObj["wafer"]!.Value<int>();
                }
                logger($"[Buffer] Wafer {_currentWafer} placed");
            },

            ["reportOccupied"] = (ctx) =>
            {
                ctx.RequestSend("scheduler", "STATION_STATUS", new JObject
                {
                    ["station"] = "buffer",
                    ["state"] = "occupied",
                    ["wafer"] = _currentWafer
                });
            },

            ["onPick"] = (ctx) =>
            {
                int pickedWafer = _currentWafer ?? 0;
                logger($"[Buffer] Wafer {pickedWafer} picked");
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
    }

    public async Task<string> StartAsync() => await _machine.StartAsync();
}
