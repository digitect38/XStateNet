using XStateNet;
using XStateNet.Orchestration;
using Newtonsoft.Json.Linq;

namespace CMPSimulator.StateMachines;

/// <summary>
/// Processing Station State Machine (Polisher, Cleaner)
/// States: Empty → Processing → Done → Empty
/// Reports state changes to Scheduler (no direct robot commands)
/// </summary>
public class ProcessingStationMachine
{
    private readonly string _stationName;
    private readonly IPureStateMachine _machine;
    private readonly int _processingTimeMs;
    private int? _currentWafer;

    public string StationName => _stationName;
    public string CurrentState => _machine.CurrentState;
    public int? CurrentWafer => _currentWafer;

    public ProcessingStationMachine(
        string stationName,
        EventBusOrchestrator orchestrator,
        int processingTimeMs,
        Action<string> logger)
    {
        _stationName = stationName;
        _processingTimeMs = processingTimeMs;

        var definition = $$"""
        {
            "id": "{{stationName}}",
            "initial": "empty",
            "states": {
                "empty": {
                    "entry": ["reportEmpty"],
                    "on": {
                        "PLACE": {
                            "target": "processing",
                            "actions": ["onPlace"]
                        }
                    }
                },
                "processing": {
                    "entry": ["reportProcessing", "startProcessing"],
                    "invoke": {
                        "src": "processWafer",
                        "onDone": {
                            "target": "done",
                            "actions": ["onDone"]
                        }
                    }
                },
                "done": {
                    "entry": ["reportDone"],
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
                logger($"[{_stationName}] Empty");
                ctx.RequestSend("scheduler", "STATION_STATUS", new JObject
                {
                    ["station"] = _stationName,
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
                logger($"[{_stationName}] Wafer {_currentWafer} placed");
            },

            ["reportProcessing"] = (ctx) =>
            {
                ctx.RequestSend("scheduler", "STATION_STATUS", new JObject
                {
                    ["station"] = _stationName,
                    ["state"] = "processing",
                    ["wafer"] = _currentWafer
                });
            },

            ["startProcessing"] = (ctx) =>
            {
                logger($"[{_stationName}] Processing wafer {_currentWafer}...");
            },

            ["onDone"] = (ctx) =>
            {
                logger($"[{_stationName}] Wafer {_currentWafer} processing complete");
            },

            ["reportDone"] = (ctx) =>
            {
                ctx.RequestSend("scheduler", "STATION_STATUS", new JObject
                {
                    ["station"] = _stationName,
                    ["state"] = "done",
                    ["wafer"] = _currentWafer
                });
            },

            ["onPick"] = (ctx) =>
            {
                int pickedWafer = _currentWafer ?? 0;
                logger($"[{_stationName}] Wafer {pickedWafer} picked");
                _currentWafer = null;
            }
        };

        var services = new Dictionary<string, Func<StateMachine, CancellationToken, Task<object>>>
        {
            ["processWafer"] = async (sm, ct) =>
            {
                await Task.Delay(_processingTimeMs, ct);
                return new { status = "SUCCESS" };
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: stationName,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: null,
            services: services,
            enableGuidIsolation: false
        );
    }

    public async Task<string> StartAsync() => await _machine.StartAsync();
}
