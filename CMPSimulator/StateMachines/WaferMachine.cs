using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace CMPSimulator.StateMachines;

/// <summary>
/// E90 Substrate (Wafer) Tracking State Machine
/// Implements SEMI E90 standard substrate lifecycle
/// </summary>
public class WaferMachine
{
    private readonly IPureStateMachine _machine;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly string _waferId;
    private readonly Action<string, string>? _onStateChanged;

    public string MachineId => $"WAFER_{_waferId}";
    public IPureStateMachine Machine => _machine;
    public DateTime AcquiredTime { get; }
    public DateTime? ProcessStartTime { get; private set; }
    public DateTime? ProcessEndTime { get; private set; }
    public TimeSpan? ProcessingTime { get; private set; }

    public WaferMachine(
        string waferId,
        EventBusOrchestrator orchestrator,
        Action<string, string>? onStateChanged = null)
    {
        _waferId = waferId;
        _orchestrator = orchestrator;
        _onStateChanged = onStateChanged;
        AcquiredTime = DateTime.UtcNow;

        // Inline XState JSON definition (SEMI E90 Substrate States)
        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'WaitingForHost',
            context: {
                waferId: '{{waferId}}'
            },
            states: {
                WaitingForHost: {
                    entry: 'notifyStateChange',
                    on: {
                        ACQUIRE: 'InCarrier',
                        PLACED_IN_CARRIER: 'InCarrier'
                    }
                },
                InCarrier: {
                    entry: 'notifyStateChange',
                    on: {
                        SELECT_FOR_PROCESS: 'NeedsProcessing',
                        SKIP: 'Skipped',
                        REJECT: 'Rejected'
                    }
                },
                NeedsProcessing: {
                    entry: 'notifyStateChange',
                    on: {
                        PLACED_IN_ALIGNER: 'Aligning',
                        PLACED_IN_PROCESS_MODULE: 'ReadyToProcess',
                        ABORT: 'Aborted'
                    }
                },
                Aligning: {
                    entry: 'notifyStateChange',
                    on: {
                        ALIGN_COMPLETE: 'ReadyToProcess',
                        ALIGN_FAIL: 'Rejected'
                    }
                },
                ReadyToProcess: {
                    entry: 'notifyStateChange',
                    on: {
                        START_PROCESS: 'InProcess',
                        ABORT: 'Aborted'
                    }
                },
                InProcess: {
                    entry: 'notifyStateChange',
                    initial: 'Polishing',
                    states: {
                        Polishing: {
                            entry: ['recordProcessStart', 'notifyStateChange'],
                            initial: 'Loading',
                            states: {
                                Loading: {
                                    entry: 'notifyStateChange',
                                    on: {
                                        LOADING_COMPLETE: 'Chucking'
                                    }
                                },
                                Chucking: {
                                    entry: 'notifyStateChange',
                                    on: {
                                        CHUCKING_COMPLETE: 'Polishing'
                                    }
                                },
                                Polishing: {
                                    entry: 'notifyStateChange',
                                    on: {
                                        POLISHING_SUBSTEP_COMPLETE: 'Dechucking'
                                    }
                                },
                                Dechucking: {
                                    entry: 'notifyStateChange',
                                    on: {
                                        DECHUCKING_COMPLETE: 'Unloading'
                                    }
                                },
                                Unloading: {
                                    entry: 'notifyStateChange',
                                    on: {
                                        UNLOADING_COMPLETE: 'PolishingComplete'
                                    }
                                },
                                PolishingComplete: {
                                    entry: 'notifyStateChange',
                                    type: 'final'
                                }
                            },
                            on: {
                                POLISHING_COMPLETE: 'Cleaning',
                                PROCESS_ABORT: '#{{MachineId}}.Aborted',
                                PROCESS_STOP: '#{{MachineId}}.Stopped'
                            }
                        },
                        Cleaning: {
                            entry: 'notifyStateChange',
                            exit: 'recordProcessEnd',
                            on: {
                                CLEANING_COMPLETE: '#{{MachineId}}.Processed',
                                PROCESS_ABORT: '#{{MachineId}}.Aborted',
                                PROCESS_STOP: '#{{MachineId}}.Stopped'
                            }
                        }
                    },
                    on: {
                        PROCESS_COMPLETE: 'Processed',
                        PROCESS_ABORT: 'Aborted',
                        PROCESS_STOP: 'Stopped'
                    }
                },
                Processed: {
                    entry: 'notifyStateChange',
                    on: {
                        PLACED_IN_CARRIER: 'Complete'
                    }
                },
                Aborted: {
                    entry: 'notifyStateChange',
                    on: {
                        PLACED_IN_CARRIER: 'Complete'
                    }
                },
                Stopped: {
                    entry: 'notifyStateChange',
                    on: {
                        RESUME: 'InProcess',
                        ABORT: 'Aborted'
                    }
                },
                Rejected: {
                    entry: 'notifyStateChange',
                    on: {
                        PLACED_IN_CARRIER: 'Complete'
                    }
                },
                Skipped: {
                    entry: 'notifyStateChange',
                    on: {
                        PLACED_IN_CARRIER: 'Complete'
                    }
                },
                Complete: {
                    entry: 'notifyStateChange',
                    type: 'final'
                }
            }
        }
        """;

        // Orchestrated actions
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["notifyStateChange"] = (ctx) =>
            {
                // Get current state from context's value (the state passed to entry action)
                // The context will have the current state information
                string currentState = "";

                // Try to get the current state from the machine if it's initialized
                if (_machine != null)
                {
                    currentState = _machine.CurrentState;
                }

                // If currentState is empty or null during transition, skip notification
                // This prevents spurious "WaitingForHost" notifications during compound state transitions
                if (string.IsNullOrEmpty(currentState))
                {
                    // Don't notify - state machine is in transition
                    return;
                }

                // Extract just the state name without the machine ID prefix (e.g., "#WAFER_W1.InCarrier" → "InCarrier")
                var stateName = currentState;
                if (currentState.Contains("."))
                {
                    stateName = currentState.Substring(currentState.LastIndexOf('.') + 1);
                }

                // DEBUG: Log state changes for wafers 2 and 10 to trace "Loading" bug
                if (_waferId == "W2" || _waferId == "W10")
                {
                    Console.WriteLine($"[DEBUG WaferMachine] {_waferId} state change: {currentState} → extracted: {stateName}");
                }

                _onStateChanged?.Invoke(_waferId, stateName);

                // Publish state change to event bus for other components
                ctx.RequestSend("SYSTEM", "WAFER_STATE_CHANGED", new JObject
                {
                    ["waferId"] = _waferId,
                    ["state"] = stateName,
                    ["timestamp"] = DateTime.UtcNow
                });
            },

            ["recordProcessStart"] = (ctx) =>
            {
                ProcessStartTime = DateTime.UtcNow;
            },

            ["recordProcessEnd"] = (ctx) =>
            {
                ProcessEndTime = DateTime.UtcNow;
                if (ProcessStartTime.HasValue)
                {
                    ProcessingTime = ProcessEndTime.Value - ProcessStartTime.Value;
                }
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: MachineId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            enableGuidIsolation: false
        );
    }

    public async Task<string> StartAsync()
    {
        return await _machine.StartAsync();
    }

    public string GetCurrentState()
    {
        return _machine.CurrentState;
    }

    // E90 Substrate Lifecycle API

    public async Task<EventResult> AcquireAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "ACQUIRE", null);
    }

    public async Task<EventResult> PlacedInCarrierAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PLACED_IN_CARRIER", null);
    }

    public async Task<EventResult> SelectForProcessAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SELECT_FOR_PROCESS", null);
    }

    public async Task<EventResult> PlacedInAlignerAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PLACED_IN_ALIGNER", null);
    }

    public async Task<EventResult> AlignCompleteAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "ALIGN_COMPLETE", null);
    }

    public async Task<EventResult> PlacedInProcessModuleAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PLACED_IN_PROCESS_MODULE", null);
    }

    public async Task<EventResult> StartProcessAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "START_PROCESS", null);
    }

    public async Task<EventResult> CompleteProcessAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PROCESS_COMPLETE", null);
    }

    public async Task<EventResult> CompletePolishingAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "POLISHING_COMPLETE", null);
    }

    public async Task<EventResult> CompleteCleaningAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "CLEANING_COMPLETE", null);
    }

    // Polishing sub-state transitions
    public async Task<EventResult> CompleteLoadingAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "LOADING_COMPLETE", null);
    }

    public async Task<EventResult> CompleteChuckingAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "CHUCKING_COMPLETE", null);
    }

    public async Task<EventResult> CompletePolishingSubstepAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "POLISHING_SUBSTEP_COMPLETE", null);
    }

    public async Task<EventResult> CompleteDechuckingAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "DECHUCKING_COMPLETE", null);
    }

    public async Task<EventResult> CompleteUnloadingAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "UNLOADING_COMPLETE", null);
    }

    public async Task<EventResult> AbortProcessAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PROCESS_ABORT", null);
    }

    public async Task<EventResult> StopProcessAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PROCESS_STOP", null);
    }

    public async Task<EventResult> ResumeAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "RESUME", null);
    }

    public async Task<EventResult> RejectAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "REJECT", null);
    }

    public async Task<EventResult> SkipAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SKIP", null);
    }
}
