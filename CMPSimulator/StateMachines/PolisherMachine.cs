using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Monitoring;
using Newtonsoft.Json.Linq;

namespace CMPSimulator.StateMachines;

/// <summary>
/// Polisher State Machine
/// States: empty → processing → done → idle → empty
/// Reports state changes to Scheduler (no direct robot commands)
/// </summary>
public class PolisherMachine
{
    private readonly string _stationName;
    private readonly IPureStateMachine _machine;
    private readonly StateMachineMonitor _monitor;
    private readonly int _processingTimeMs;
    private readonly EventBusOrchestrator _orchestrator;
    private StateMachine? _underlyingMachine;
    private int? _currentWafer;
    private DateTime _processingStartTime;
    private readonly Dictionary<int, WaferMachine>? _waferMachines;

    public string StationName => _stationName;
    public string CurrentState => _machine?.CurrentState ?? "initializing";
    public int? CurrentWafer => _currentWafer;
    public int RemainingTimeMs
    {
        get
        {
            // CurrentState can be "#polisher.processing" or just "processing"
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

    public PolisherMachine(
        string stationName,
        EventBusOrchestrator orchestrator,
        int processingTimeMs,
        Action<string> logger,
        Dictionary<int, WaferMachine>? waferMachines = null)
    {
        _stationName = stationName;
        _processingTimeMs = processingTimeMs;
        _orchestrator = orchestrator;
        _waferMachines = waferMachines;

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
                    "initial": "Loading",
                    "states": {
                        "Loading": {
                            "entry": ["reportSubState"],
                            "invoke": {
                                "src": "loadingStep",
                                "onDone": "Chucking"
                            }
                        },
                        "Chucking": {
                            "entry": ["reportSubState"],
                            "invoke": {
                                "src": "chuckingStep",
                                "onDone": "Polishing"
                            }
                        },
                        "Polishing": {
                            "entry": ["reportSubState"],
                            "invoke": {
                                "src": "polishingStep",
                                "onDone": "Dechucking"
                            }
                        },
                        "Dechucking": {
                            "entry": ["reportSubState"],
                            "invoke": {
                                "src": "dechuckingStep",
                                "onDone": "Unloading"
                            }
                        },
                        "Unloading": {
                            "entry": ["reportSubState"],
                            "invoke": {
                                "src": "unloadingStep",
                                "onDone": {
                                    "target": "#{{stationName}}.done",
                                    "actions": ["onDone"]
                                }
                            }
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

                // NOTE: WaferMachine transitions are handled by UpdateWaferPositions when wafer is picked by R1
                // SELECT_FOR_PROCESS is already sent when R1 holds the wafer (InCarrier → NeedsProcessing)
                // We don't need to send it again here
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

            ["reportSubState"] = (ctx) =>
            {
                // Extract the sub-state name from the current state
                var currentState = _machine.CurrentState;
                var subState = currentState.Contains(".") ? currentState.Substring(currentState.LastIndexOf('.') + 1) : currentState;
                logger($"[{_stationName}] Sub-state: {subState} (wafer {_currentWafer})");
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
            ["loadingStep"] = async (sm, ct) =>
            {
                // DETERMINISTIC FIX: Ensure WaferMachine reaches Loading state before processing
                // Send the complete transition sequence: InCarrier → NeedsProcessing → ReadyToProcess → Loading
                if (_waferMachines != null && _currentWafer.HasValue && _waferMachines.ContainsKey(_currentWafer.Value))
                {
                    var waferMachine = _waferMachines[_currentWafer.Value];

                    // Send complete transition sequence using async/await (no deadlock):
                    // 1. InCarrier → NeedsProcessing (if UpdateWaferPositions already sent this, it will be ignored)
                    await _orchestrator.SendEventAsync("SYSTEM", waferMachine.MachineId, "SELECT_FOR_PROCESS", null);

                    // 2. NeedsProcessing → ReadyToProcess
                    await _orchestrator.SendEventAsync("SYSTEM", waferMachine.MachineId, "PLACED_IN_PROCESS_MODULE", null);

                    // 3. ReadyToProcess → InProcess.Polishing.Loading
                    await _orchestrator.SendEventAsync("SYSTEM", waferMachine.MachineId, "START_PROCESS", null);

                    logger($"[{_stationName}] Wafer {_currentWafer} transitioned to {waferMachine.GetCurrentState()}");
                }

                int timePerStep = _processingTimeMs / 5;
                await Task.Delay(timePerStep, ct);

                // Send LOADING_COMPLETE event to WaferMachine
                if (_waferMachines != null && _currentWafer.HasValue && _waferMachines.ContainsKey(_currentWafer.Value))
                {
                    await _waferMachines[_currentWafer.Value].CompleteLoadingAsync();
                }

                return new { status = "SUCCESS" };
            },

            ["chuckingStep"] = async (sm, ct) =>
            {
                int timePerStep = _processingTimeMs / 5;
                await Task.Delay(timePerStep, ct);

                // Also update wafer machine if available
                if (_waferMachines != null && _currentWafer.HasValue && _waferMachines.ContainsKey(_currentWafer.Value))
                {
                    await _waferMachines[_currentWafer.Value].CompleteChuckingAsync();
                }

                return new { status = "SUCCESS" };
            },

            ["polishingStep"] = async (sm, ct) =>
            {
                int timePerStep = _processingTimeMs / 5;
                await Task.Delay(timePerStep, ct);

                // Also update wafer machine if available
                if (_waferMachines != null && _currentWafer.HasValue && _waferMachines.ContainsKey(_currentWafer.Value))
                {
                    await _waferMachines[_currentWafer.Value].CompletePolishingSubstepAsync();
                }

                return new { status = "SUCCESS" };
            },

            ["dechuckingStep"] = async (sm, ct) =>
            {
                int timePerStep = _processingTimeMs / 5;
                await Task.Delay(timePerStep, ct);

                // Also update wafer machine if available
                if (_waferMachines != null && _currentWafer.HasValue && _waferMachines.ContainsKey(_currentWafer.Value))
                {
                    await _waferMachines[_currentWafer.Value].CompleteDechuckingAsync();
                }

                return new { status = "SUCCESS" };
            },

            ["unloadingStep"] = async (sm, ct) =>
            {
                int timePerStep = _processingTimeMs / 5;
                await Task.Delay(timePerStep, ct);

                // Also update wafer machine if available
                if (_waferMachines != null && _currentWafer.HasValue && _waferMachines.ContainsKey(_currentWafer.Value))
                {
                    await _waferMachines[_currentWafer.Value].CompleteUnloadingAsync();
                }

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

        // Create and start monitor
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
    /// Reset the station's wafer reference and processing timer
    /// Used during carrier swap to clear old wafer references
    /// </summary>
    public void ResetWafer()
    {
        _currentWafer = null;
        _processingStartTime = DateTime.MinValue; // Reset timer to prevent stale remaining time
    }

    /// <summary>
    /// Broadcast current station status to scheduler
    /// Used after carrier swap to inform scheduler of current state
    /// </summary>
    public void BroadcastStatus(OrchestratedContext context)
    {
        // Extract leaf state name (e.g., "#polisher.empty" → "empty")
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
