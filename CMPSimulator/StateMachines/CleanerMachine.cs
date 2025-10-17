using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Monitoring;
using Newtonsoft.Json.Linq;

namespace CMPSimulator.StateMachines;

/// <summary>
/// Cleaner State Machine
/// States: empty → processing → done → idle → empty
/// Reports state changes to Scheduler (no direct robot commands)
/// </summary>
public class CleanerMachine
{
    private readonly string _stationName;
    private readonly IPureStateMachine _machine;
    private readonly StateMachineMonitor _monitor;
    private readonly int _processingTimeMs;
    private readonly EventBusOrchestrator _orchestrator;
    private StateMachine? _underlyingMachine; // Access to underlying machine for ContextMap
    private int? _currentWafer;
    private DateTime _processingStartTime;

    public string StationName => _stationName;
    public string CurrentState => _machine.CurrentState;
    public int? CurrentWafer => _currentWafer;
    public int RemainingTimeMs
    {
        get
        {
            // CurrentState can be "#cleaner.processing" or just "processing"
            if (string.IsNullOrEmpty(CurrentState) || !CurrentState.Contains("processing")) return 0;
            var elapsed = (DateTime.Now - _processingStartTime).TotalMilliseconds;
            var remaining = _processingTimeMs - elapsed;
            return Math.Max(0, (int)remaining);
        }
    }

    // Expose StateChanged event for Pub/Sub
    public event EventHandler<StateTransitionEventArgs>? StateChanged
    {
        add => _monitor.StateTransitioned += value;
        remove => _monitor.StateTransitioned -= value;
    }

    public CleanerMachine(
        string stationName,
        EventBusOrchestrator orchestrator,
        int processingTimeMs,
        Action<string> logger)
    {
        _stationName = stationName;
        _processingTimeMs = processingTimeMs;
        _orchestrator = orchestrator;

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
                    "entry": ["reportProcessing"],
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
                            "target": "idle",
                            "actions": ["onPick"]
                        }
                    }
                },
                "idle": {
                    "entry": ["reportIdle"],
                    "after": {
                        "1": {
                            "target": "empty"
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
                logger($"[{_stationName}] State: empty");
                ctx.RequestSend("scheduler", "STATION_STATUS", new JObject
                {
                    ["station"] = _stationName,
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

                logger($"[{_stationName}] Wafer {_currentWafer} placed");
            },

            ["reportProcessing"] = (ctx) =>
            {
                _processingStartTime = DateTime.Now;
                logger($"[{_stationName}] State: processing (wafer {_currentWafer})");
                ctx.RequestSend("scheduler", "STATION_STATUS", new JObject
                {
                    ["station"] = _stationName,
                    ["state"] = "processing",
                    ["wafer"] = _currentWafer
                });
            },

            ["onDone"] = (ctx) =>
            {
                logger($"[{_stationName}] Processing complete for wafer {_currentWafer}");
            },

            ["reportDone"] = (ctx) =>
            {
                logger($"[{_stationName}] State: done (wafer {_currentWafer} ready for pickup)");
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
            },

            ["reportIdle"] = (ctx) =>
            {
                logger($"[{_stationName}] State: idle");
                ctx.RequestSend("scheduler", "STATION_STATUS", new JObject
                {
                    ["station"] = _stationName,
                    ["state"] = "idle",
                    ["wafer"] = (int?)null
                });
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

        // Execute deferred sends from entry actions
        var context = _orchestrator.GetOrCreateContext(_stationName);
        await context.ExecuteDeferredSends();

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
        // Extract leaf state name (e.g., "#cleaner.empty" → "empty")
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
            ["station"] = _stationName,
            ["state"] = state,
            ["wafer"] = _currentWafer
        });
    }
}
