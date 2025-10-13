using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Monitoring;
using Newtonsoft.Json.Linq;

namespace CMPSimulator.StateMachines;

/// <summary>
/// Enhanced Scheduler State Machine with Parallel States
/// Uses XState Parallel states to model concurrent scheduling activities
/// Architecture:
///   - Parallel regions for R1, R2, R3 robot management
///   - Each region has its own priority logic and state tracking
///   - LoadPort queue management coordinated across regions
/// </summary>
public class ParallelSchedulerMachine
{
    private readonly IPureStateMachine _machine;
    private readonly StateMachineMonitor _monitor;
    private readonly Action<string> _logger;
    private readonly EventBusOrchestrator _orchestrator;
    private StateMachine? _underlyingMachine;

    // Track station and robot states
    private readonly Dictionary<string, string> _stationStates = new();
    private readonly Dictionary<string, int?> _stationWafers = new();
    private readonly Dictionary<string, string> _robotStates = new();
    private readonly Dictionary<string, int?> _robotWafers = new();
    private readonly Dictionary<string, string> _robotWaitingFor = new();

    // LoadPort queue management
    private readonly List<int> _lPending = new();
    private readonly List<int> _lCompleted = new();

    // Track pending commands
    private readonly HashSet<string> _robotsWithPendingCommands = new();

    public string CurrentState => _machine.CurrentState;

    // Expose StateChanged event for Pub/Sub
    public event EventHandler<StateTransitionEventArgs>? StateChanged
    {
        add => _monitor.StateTransitioned += value;
        remove => _monitor.StateTransitioned -= value;
    }

    // Event for completion notification
    public event EventHandler? AllWafersCompleted;

    private readonly int _totalWafers;

    public ParallelSchedulerMachine(EventBusOrchestrator orchestrator, Action<string> logger, int totalWafers = 10)
    {
        _logger = logger;
        _orchestrator = orchestrator;
        _totalWafers = totalWafers;

        // Initialize LoadPort queue with wafers
        for (int i = 1; i <= _totalWafers; i++)
        {
            _lPending.Add(i);
        }

        var definition = """
        {
            "id": "parallelScheduler",
            "type": "parallel",
            "states": {
                "r1Manager": {
                    "initial": "monitoring",
                    "states": {
                        "monitoring": {
                            "entry": ["initR1"],
                            "on": {
                                "STATION_STATUS": {
                                    "actions": ["onStationStatusR1"]
                                },
                                "ROBOT_STATUS": {
                                    "actions": ["onRobotStatusR1"],
                                    "cond": "isR1Status"
                                }
                            }
                        }
                    }
                },
                "r2Manager": {
                    "initial": "monitoring",
                    "states": {
                        "monitoring": {
                            "entry": ["initR2"],
                            "on": {
                                "STATION_STATUS": {
                                    "actions": ["onStationStatusR2"]
                                },
                                "ROBOT_STATUS": {
                                    "actions": ["onRobotStatusR2"],
                                    "cond": "isR2Status"
                                }
                            }
                        }
                    }
                },
                "r3Manager": {
                    "initial": "monitoring",
                    "states": {
                        "monitoring": {
                            "entry": ["initR3"],
                            "on": {
                                "STATION_STATUS": {
                                    "actions": ["onStationStatusR3"]
                                },
                                "ROBOT_STATUS": {
                                    "actions": ["onRobotStatusR3"],
                                    "cond": "isR3Status"
                                }
                            }
                        }
                    }
                },
                "globalCoordinator": {
                    "initial": "coordinating",
                    "states": {
                        "coordinating": {
                            "entry": ["initCoordinator"],
                            "on": {
                                "STATION_STATUS": {
                                    "actions": ["updateGlobalState"]
                                },
                                "ROBOT_STATUS": {
                                    "actions": ["checkGlobalPriorities"]
                                }
                            }
                        }
                    }
                }
            }
        }
        """;

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["initR1"] = (ctx) =>
            {
                _logger("[Scheduler::R1Manager] Initialized - Managing R1 priorities (B→L, L→HOLD, HOLD→P)");
            },

            ["initR2"] = (ctx) =>
            {
                _logger("[Scheduler::R2Manager] Initialized - Managing R2 priorities (P→C)");
            },

            ["initR3"] = (ctx) =>
            {
                _logger("[Scheduler::R3Manager] Initialized - Managing R3 priorities (C→B)");
            },

            ["initCoordinator"] = (ctx) =>
            {
                _logger("[Scheduler::GlobalCoordinator] Initialized - Coordinating cross-robot dependencies");
            },

            ["onStationStatusR1"] = (ctx) =>
            {
                UpdateStationState(ctx);

                var data = _underlyingMachine?.ContextMap?["_event"] as JObject;
                var station = data?["station"]?.ToString();
                var state = data?["state"]?.ToString();

                // R1 cares about: LoadPort (wafer source), Polisher (wafer destination), Buffer (return wafers)
                if (station == "buffer" || station == "polisher" || station == "LoadPort")
                {
                    if (state == "empty" || state == "done" || state == "IDLE" || state == "COMPLETE" || state == "occupied")
                    {
                        CheckR1Waiting(ctx, station);
                        CheckR1Priorities(ctx);
                    }
                }
            },

            ["onStationStatusR2"] = (ctx) =>
            {
                UpdateStationState(ctx);

                var data = _underlyingMachine?.ContextMap?["_event"] as JObject;
                var station = data?["station"]?.ToString();
                var state = data?["state"]?.ToString();

                // R2 cares about: Polisher (wafer source), Cleaner (wafer destination)
                if (station == "polisher" || station == "cleaner")
                {
                    if (state == "empty" || state == "done" || state == "IDLE" || state == "COMPLETE")
                    {
                        CheckR2Waiting(ctx, station);
                        CheckR2Priorities(ctx);
                    }
                }
            },

            ["onStationStatusR3"] = (ctx) =>
            {
                UpdateStationState(ctx);

                var data = _underlyingMachine?.ContextMap?["_event"] as JObject;
                var station = data?["station"]?.ToString();
                var state = data?["state"]?.ToString();

                // R3 cares about: Cleaner (wafer source), Buffer (wafer destination)
                if (station == "cleaner" || station == "buffer")
                {
                    if (state == "empty" || state == "done" || state == "IDLE" || state == "COMPLETE")
                    {
                        CheckR3Waiting(ctx, station);
                        CheckR3Priorities(ctx);
                    }
                }
            },

            ["onRobotStatusR1"] = (ctx) =>
            {
                UpdateRobotState(ctx);

                var data = _underlyingMachine?.ContextMap?["_event"] as JObject;
                var robot = data?["robot"]?.ToString();
                var state = data?["state"]?.ToString();
                var waitingFor = data?["waitingFor"]?.ToString();

                if (robot != "R1") return;

                _robotsWithPendingCommands.Remove(robot);

                if (state == "holding" && waitingFor != null)
                {
                    _robotWaitingFor[robot] = waitingFor;
                    CheckR1DestinationReady(ctx, robot, waitingFor);
                }
                else if (state == "waitingDestination" && waitingFor != null)
                {
                    _robotWaitingFor[robot] = waitingFor;
                    CheckR1DestinationReady(ctx, robot, waitingFor);
                }
                else
                {
                    _robotWaitingFor.Remove(robot);
                }

                if (state == "holding" || state == "idle")
                {
                    CheckR1Priorities(ctx);
                }
            },

            ["onRobotStatusR2"] = (ctx) =>
            {
                UpdateRobotState(ctx);

                var data = _underlyingMachine?.ContextMap?["_event"] as JObject;
                var robot = data?["robot"]?.ToString();
                var state = data?["state"]?.ToString();
                var waitingFor = data?["waitingFor"]?.ToString();

                if (robot != "R2") return;

                _robotsWithPendingCommands.Remove(robot);

                if (state == "holding" && waitingFor != null)
                {
                    _robotWaitingFor[robot] = waitingFor;
                    CheckR2DestinationReady(ctx, robot, waitingFor);
                }
                else if (state == "waitingDestination" && waitingFor != null)
                {
                    _robotWaitingFor[robot] = waitingFor;
                    CheckR2DestinationReady(ctx, robot, waitingFor);
                }
                else
                {
                    _robotWaitingFor.Remove(robot);
                }

                if (state == "holding" || state == "idle")
                {
                    CheckR2Priorities(ctx);
                }
            },

            ["onRobotStatusR3"] = (ctx) =>
            {
                UpdateRobotState(ctx);

                var data = _underlyingMachine?.ContextMap?["_event"] as JObject;
                var robot = data?["robot"]?.ToString();
                var state = data?["state"]?.ToString();
                var waitingFor = data?["waitingFor"]?.ToString();

                if (robot != "R3") return;

                _robotsWithPendingCommands.Remove(robot);

                if (state == "holding" && waitingFor != null)
                {
                    _robotWaitingFor[robot] = waitingFor;
                    CheckR3DestinationReady(ctx, robot, waitingFor);
                }
                else if (state == "waitingDestination" && waitingFor != null)
                {
                    _robotWaitingFor[robot] = waitingFor;
                    CheckR3DestinationReady(ctx, robot, waitingFor);
                }
                else
                {
                    _robotWaitingFor.Remove(robot);
                }

                if (state == "holding" || state == "idle")
                {
                    CheckR3Priorities(ctx);
                }
            },

            ["updateGlobalState"] = (ctx) =>
            {
                UpdateStationState(ctx);
            },

            ["checkGlobalPriorities"] = (ctx) =>
            {
                UpdateRobotState(ctx);

                // Global coordinator checks if any high-priority cross-robot actions needed
                // For now, this is handled by individual managers
            }
        };

        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["isR1Status"] = (sm) =>
            {
                var data = sm.ContextMap?["_event"] as JObject;
                return data?["robot"]?.ToString() == "R1";
            },

            ["isR2Status"] = (sm) =>
            {
                var data = sm.ContextMap?["_event"] as JObject;
                return data?["robot"]?.ToString() == "R2";
            },

            ["isR3Status"] = (sm) =>
            {
                var data = sm.ContextMap?["_event"] as JObject;
                return data?["robot"]?.ToString() == "R3";
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: "parallelScheduler",
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: guards,
            services: null,
            enableGuidIsolation: false
        );

        _underlyingMachine = ((PureStateMachineAdapter)_machine).GetUnderlying() as StateMachine;
        _monitor = new StateMachineMonitor(_underlyingMachine!);
        _monitor.StartMonitoring();
    }

    public async Task<string> StartAsync()
    {
        var result = await _machine.StartAsync();
        var context = _orchestrator.GetOrCreateContext("parallelScheduler");
        await context.ExecuteDeferredSends();
        return result;
    }

    // Helper methods for updating global state
    private void UpdateStationState(OrchestratedContext ctx)
    {
        if (_underlyingMachine?.ContextMap == null) return;
        var data = _underlyingMachine.ContextMap["_event"] as JObject;
        if (data == null) return;

        var station = data["station"]?.ToString();
        var state = data["state"]?.ToString();
        var wafer = data["wafer"]?.ToObject<int?>();

        if (station == null || state == null) return;

        _stationStates[station] = state;
        _stationWafers[station] = wafer;

        if (state == "empty" || state == "done" || state == "IDLE" || state == "COMPLETE")
        {
            _logger($"[Scheduler] 📥 STATION_STATUS: {station} = {state} (wafer: {wafer})");
        }
    }

    private void UpdateRobotState(OrchestratedContext ctx)
    {
        if (_underlyingMachine?.ContextMap == null) return;
        var data = _underlyingMachine.ContextMap["_event"] as JObject;
        if (data == null) return;

        var robot = data["robot"]?.ToString();
        var state = data["state"]?.ToString();
        var wafer = data["wafer"]?.ToObject<int?>();

        if (robot == null || state == null) return;

        _robotStates[robot] = state;
        _robotWafers[robot] = wafer;

        if (state == "holding" || state == "idle")
        {
            _logger($"[Scheduler] 📥 ROBOT_STATUS: {robot} = {state} (wafer: {wafer})");
        }
    }

    // R1 Priority Logic: B→L (P1), L→HOLD (P2), HOLD→P (handled by destination ready)
    private void CheckR1Priorities(OrchestratedContext ctx)
    {
        // Priority 1: B→L (return completed wafers)
        if (CanExecuteBtoL())
        {
            ExecuteBtoL(ctx);
            return;
        }

        // Priority 2: L→HOLD (proactively pick from LoadPort)
        if (CanExecuteLtoHold())
        {
            ExecuteLtoHold(ctx);
            return;
        }
    }

    private void CheckR1Waiting(OrchestratedContext ctx, string station)
    {
        if (_robotWaitingFor.GetValueOrDefault("R1") == station)
        {
            CheckR1DestinationReady(ctx, "R1", station);
        }
    }

    private void CheckR1DestinationReady(OrchestratedContext ctx, string robot, string waitingFor)
    {
        var destState = GetStationState(waitingFor);
        bool destReady = false;

        if (waitingFor == "LoadPort")
        {
            destReady = true;
        }
        else if (waitingFor == "polisher")
        {
            destReady = (destState == "empty" || destState == "idle" || destState == "IDLE");
        }

        if (destReady)
        {
            _logger($"[Scheduler::R1Manager] ✓ Destination {waitingFor} ready! Sending DESTINATION_READY to {robot}");
            ctx.RequestSend(robot, "DESTINATION_READY", new JObject());
            _robotWaitingFor.Remove(robot);
        }
        else
        {
            _logger($"[Scheduler::R1Manager] ⏸ Destination {waitingFor} not ready (state={destState ?? "N/A"}). {robot} waiting.");
        }
    }

    // R2 Priority Logic: P→C
    private void CheckR2Priorities(OrchestratedContext ctx)
    {
        if (CanExecutePtoC())
        {
            ExecutePtoC(ctx);
        }
    }

    private void CheckR2Waiting(OrchestratedContext ctx, string station)
    {
        if (_robotWaitingFor.GetValueOrDefault("R2") == station)
        {
            CheckR2DestinationReady(ctx, "R2", station);
        }
    }

    private void CheckR2DestinationReady(OrchestratedContext ctx, string robot, string waitingFor)
    {
        var destState = GetStationState(waitingFor);
        bool destReady = false;

        if (waitingFor == "cleaner")
        {
            destReady = (destState == "empty" || destState == "idle" || destState == "done");
        }

        if (destReady)
        {
            _logger($"[Scheduler::R2Manager] ✓ Destination {waitingFor} ready! Sending DESTINATION_READY to {robot}");
            ctx.RequestSend(robot, "DESTINATION_READY", new JObject());
            _robotWaitingFor.Remove(robot);
        }
        else
        {
            _logger($"[Scheduler::R2Manager] ⏸ Destination {waitingFor} not ready (state={destState ?? "N/A"}). {robot} waiting.");
        }
    }

    // R3 Priority Logic: C→B
    private void CheckR3Priorities(OrchestratedContext ctx)
    {
        if (CanExecuteCtoB())
        {
            ExecuteCtoB(ctx);
        }
    }

    private void CheckR3Waiting(OrchestratedContext ctx, string station)
    {
        if (_robotWaitingFor.GetValueOrDefault("R3") == station)
        {
            CheckR3DestinationReady(ctx, "R3", station);
        }
    }

    private void CheckR3DestinationReady(OrchestratedContext ctx, string robot, string waitingFor)
    {
        var destState = GetStationState(waitingFor);
        bool destReady = false;

        if (waitingFor == "buffer")
        {
            destReady = (destState == "empty" || destState == "idle");
        }

        if (destReady)
        {
            _logger($"[Scheduler::R3Manager] ✓ Destination {waitingFor} ready! Sending DESTINATION_READY to {robot}");
            ctx.RequestSend(robot, "DESTINATION_READY", new JObject());
            _robotWaitingFor.Remove(robot);
        }
        else
        {
            _logger($"[Scheduler::R3Manager] ⏸ Destination {waitingFor} not ready (state={destState ?? "N/A"}). {robot} waiting.");
        }
    }

    // Transfer execution methods
    private bool CanExecuteCtoB()
    {
        return GetStationState("cleaner") == "done" &&
               GetRobotState("R3") == "idle";
    }

    private void ExecuteCtoB(OrchestratedContext ctx)
    {
        int? waferId = _stationWafers.GetValueOrDefault("cleaner");
        if (waferId == null || waferId == 0) return;

        _logger($"[Scheduler::R3Manager] [P1] C→B: Commanding R3 to transfer wafer {waferId}");
        _robotsWithPendingCommands.Add("R3");

        ctx.RequestSend("R3", "TRANSFER", new JObject
        {
            ["waferId"] = waferId,
            ["from"] = "cleaner",
            ["to"] = "buffer"
        });
    }

    private bool CanExecutePtoC()
    {
        var cleanerState = GetStationState("cleaner");
        bool cleanerAvailable = cleanerState == "empty" || cleanerState == "done";

        var polisherState = GetStationState("polisher");
        bool polisherDone = (polisherState == "done" || polisherState == "COMPLETE");

        return polisherDone &&
               GetRobotState("R2") == "idle" &&
               cleanerAvailable;
    }

    private void ExecutePtoC(OrchestratedContext ctx)
    {
        int? waferId = _stationWafers.GetValueOrDefault("polisher");
        if (waferId == null || waferId == 0) return;

        _logger($"[Scheduler::R2Manager] [P2] P→C: Commanding R2 to transfer wafer {waferId}");
        _robotsWithPendingCommands.Add("R2");

        ctx.RequestSend("R2", "TRANSFER", new JObject
        {
            ["waferId"] = waferId,
            ["from"] = "polisher",
            ["to"] = "cleaner"
        });
    }

    private bool CanExecuteLtoHold()
    {
        var r1State = GetRobotState("R1");
        return _lPending.Count > 0 && r1State == "idle";
    }

    private void ExecuteLtoHold(OrchestratedContext ctx)
    {
        if (_lPending.Count == 0) return;

        int waferId = _lPending[0];
        _lPending.RemoveAt(0);

        _logger($"[Scheduler::R1Manager] [P4] L→HOLD: Commanding R1 to pick wafer {waferId} from LoadPort (Pending: {_lPending.Count} left)");
        _robotsWithPendingCommands.Add("R1");

        ctx.RequestSend("R1", "TRANSFER", new JObject
        {
            ["waferId"] = waferId,
            ["from"] = "LoadPort",
            ["to"] = "polisher"
        });
    }

    private bool CanExecuteBtoL()
    {
        return GetStationState("buffer") == "occupied" &&
               GetRobotState("R1") == "idle";
    }

    private void ExecuteBtoL(OrchestratedContext ctx)
    {
        int? waferId = _stationWafers.GetValueOrDefault("buffer");
        if (waferId == null || waferId == 0) return;

        _logger($"[Scheduler::R1Manager] [P3] B→L: Commanding R1 to return wafer {waferId}");
        _robotsWithPendingCommands.Add("R1");

        ctx.RequestSend("R1", "TRANSFER", new JObject
        {
            ["waferId"] = waferId.Value,
            ["from"] = "buffer",
            ["to"] = "LoadPort"
        });

        _lCompleted.Add(waferId.Value);
        _logger($"[Scheduler] ✓ Wafer {waferId} completed ({_lCompleted.Count}/{_totalWafers})");

        if (_lCompleted.Count >= _totalWafers)
        {
            _logger($"[Scheduler] ✅ All {_totalWafers} wafers completed!");
            AllWafersCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private string? GetStationState(string station)
    {
        return _stationStates.GetValueOrDefault(station);
    }

    private string? GetRobotState(string robot)
    {
        if (_robotsWithPendingCommands.Contains(robot))
        {
            return "busy";
        }
        return _robotStates.GetValueOrDefault(robot);
    }

    // Public accessors
    public int PendingCount => _lPending.Count;
    public int CompletedCount => _lCompleted.Count;
    public IReadOnlyList<int> Completed => _lCompleted.AsReadOnly();
}
