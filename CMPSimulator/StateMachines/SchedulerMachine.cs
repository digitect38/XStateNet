using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Monitoring;
using Newtonsoft.Json.Linq;

namespace CMPSimulator.StateMachines;

/// <summary>
/// Scheduler State Machine
/// Receives STATION_STATUS and ROBOT_STATUS events
/// Executes Forward Priority scheduling logic
/// Priority: P1(C→B) >= P2(P→C) >= P3(L→P) >= P4(B→L)
/// </summary>
public class SchedulerMachine
{
    private readonly IPureStateMachine _machine;
    private readonly StateMachineMonitor _monitor;
    private readonly Action<string> _logger;
    private readonly EventBusOrchestrator _orchestrator;
    private StateMachine? _underlyingMachine; // Access to underlying machine for Context Map

    // Track station and robot states
    private readonly Dictionary<string, string> _stationStates = new();
    private readonly Dictionary<string, int?> _stationWafers = new();
    private readonly Dictionary<string, string> _robotStates = new();
    private readonly Dictionary<string, int?> _robotWafers = new();
    private readonly Dictionary<string, string> _robotWaitingFor = new(); // robot -> destination station

    // LoadPort queue management
    private readonly List<int> _lPending = new();
    private readonly List<int> _lCompleted = new();

    // Track pending commands (commands sent but not yet reflected in robot state)
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

    public SchedulerMachine(EventBusOrchestrator orchestrator, Action<string> logger, int totalWafers = 10)
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
            "id": "scheduler",
            "initial": "running",
            "states": {
                  "running": {
                    "entry": ["reportRunning"],
                    "on": {
                        "STATION_STATUS": {
                            "actions": ["onStationStatus"]
                        },
                        "ROBOT_STATUS": {
                            "actions": ["onRobotStatus"]
                        }
                    }
                }
            }
        }
        """;

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["reportRunning"] = (ctx) =>
            {
                _logger("[Scheduler] Running (Event-driven mode)");
            },

            ["onStationStatus"] = (ctx) =>
            {
                // Extract event data from underlying state machine's ContextMap
                if (_underlyingMachine?.ContextMap == null) return;
                var data = _underlyingMachine.ContextMap["_event"] as JObject;
                if (data == null) return;

                var station = data["station"]?.ToString();
                var state = data["state"]?.ToString();
                var wafer = data["wafer"]?.ToObject<int?>();

                if (station == null || state == null) return;

                // Update tracking
                _stationStates[station] = state;
                _stationWafers[station] = wafer;

                // Only log meaningful state changes (empty, done) to reduce log noise
                if (state == "empty" || state == "done" || state == "IDLE" || state == "COMPLETE")
                {
                    _logger($"[Scheduler] 📥 STATION_STATUS: {station} = {state} (wafer: {wafer})");
                }

                // Check if any robot is waiting for this station to become empty/ready
                // E157 states: IDLE, COMPLETE correspond to empty, done
                if (state == "empty" || state == "done" || state == "IDLE" || state == "COMPLETE")
                {
                    CheckWaitingRobots(ctx, station);
                }

                // Only check priorities when station becomes empty or done (meaningful state changes)
                // Don't check on "processing" or "idle" transitions to reduce overhead
                if (state == "empty" || state == "done" || state == "IDLE" || state == "COMPLETE")
                {
                    CheckPriorities(ctx);
                }
            },

            ["onRobotStatus"] = (ctx) =>
            {
                // Extract event data from underlying state machine's ContextMap
                if (_underlyingMachine?.ContextMap == null) return;
                var data = _underlyingMachine.ContextMap["_event"] as JObject;
                if (data == null) return;

                var robot = data["robot"]?.ToString();
                var state = data["state"]?.ToString();
                var wafer = data["wafer"]?.ToObject<int?>();
                var waitingFor = data["waitingFor"]?.ToString();

                if (robot == null || state == null) return;

                // Update tracking
                _robotStates[robot] = state;
                _robotWafers[robot] = wafer;

                // Clear pending command flag when robot state changes
                _robotsWithPendingCommands.Remove(robot);

                // Only log important state transitions (holding, idle) to reduce log noise
                if (state == "holding" || state == "idle")
                {
                    _logger($"[Scheduler] 📥 ROBOT_STATUS: {robot} = {state} (wafer: {wafer})");
                }

                // When robot enters holding state, check priorities (especially for R3 holding triggering R1)
                if (state == "holding")
                {
                    CheckPriorities(ctx);
                }

                // When robot enters holding state, check if destination is ready
                if (state == "holding")
                {
                    // Get destination from robot's waitingFor field (if robot reports it)
                    // Otherwise we need to track it based on the transfer command
                    if (waitingFor != null)
                    {
                        _robotWaitingFor[robot] = waitingFor;

                        // Check immediately if destination is ready
                        bool destReady;
                        string? destState = null;

                        // LoadPort is always ready (B→L: processed wafer return)
                        if (waitingFor == "LoadPort")
                        {
                            destReady = true;
                        }
                        else
                        {
                            destState = GetStationState(waitingFor);

                            // Different ready conditions for different robots:
                            // R1: Can place to Polisher only when empty/idle (Polisher can't accept PLACE in done state)
                            // R2: Can place to Cleaner when empty/idle OR done (R3 will pick immediately)
                            // R3: Only when Buffer is empty (strict - no downstream robot)
                            if (robot == "R1")
                            {
                                // R1 → Polisher: Only when truly empty/idle
                                // Polisher state machine only accepts PLACE in empty state!
                                destReady = (destState == "empty" || destState == "idle" || destState == "IDLE");
                            }
                            else if (robot == "R2")
                            {
                                // R2 → Cleaner: Can place when empty/idle OR done
                                // This allows overlapping: R2 places while R3 picks
                                destReady = (destState == "empty" || destState == "idle" || destState == "done");
                            }
                            else if (robot == "R3")
                            {
                                // R3 must wait until Buffer is truly empty (no downstream robot)
                                destReady = (destState == "empty" || destState == "idle");
                            }
                            else
                            {
                                // Should not reach here
                                destReady = (destState == "empty" || destState == "idle");
                            }
                        }

                        if (destReady)
                        {
                            _logger($"[Scheduler] ✓ Destination {waitingFor} is ready! Sending DESTINATION_READY to {robot}");
                            ctx.RequestSend(robot, "DESTINATION_READY", new JObject());
                            _robotWaitingFor.Remove(robot);
                        }
                        else
                        {
                            _logger($"[Scheduler] ⏸ Destination {waitingFor} not ready (state={destState ?? "N/A"}). Robot {robot} will wait. [DEBUG: Robot={robot}, WaitingFor={waitingFor}, DestState={destState}]");
                        }
                    }
                }
                else if (state == "waitingDestination" && waitingFor != null)
                {
                    // Robot explicitly waiting - track and check periodically
                    _robotWaitingFor[robot] = waitingFor;

                    bool destReady;

                    // LoadPort is always ready (B→L)
                    if (waitingFor == "LoadPort")
                    {
                        destReady = true;
                    }
                    else
                    {
                        var destState = GetStationState(waitingFor);

                        // Same logic as holding state
                        // R1: Only when Polisher is empty/idle
                        // R2: Can place when Cleaner is empty/idle OR done
                        // R3: Only when Buffer is empty/idle
                        if (robot == "R1")
                        {
                            destReady = (destState == "empty" || destState == "idle" || destState == "IDLE");
                        }
                        else if (robot == "R2")
                        {
                            destReady = (destState == "empty" || destState == "idle" || destState == "done");
                        }
                        else if (robot == "R3")
                        {
                            destReady = (destState == "empty" || destState == "idle");
                        }
                        else
                        {
                            // Should not reach here
                            destReady = (destState == "empty" || destState == "idle");
                        }
                    }

                    if (destReady)
                    {
                        _logger($"[Scheduler] ✓ Destination {waitingFor} became ready! Sending DESTINATION_READY to {robot}");
                        ctx.RequestSend(robot, "DESTINATION_READY", new JObject());
                        _robotWaitingFor.Remove(robot);
                    }
                }
                else
                {
                    // Robot is no longer waiting
                    _robotWaitingFor.Remove(robot);
                }

                // Check priorities when robot becomes idle
                if (state == "idle")
                {
                    CheckPriorities(ctx);
                }
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: "scheduler",
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: null,
            services: null,
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
        var context = _orchestrator.GetOrCreateContext("scheduler");
        await context.ExecuteDeferredSends();

        return result;
    }

    private void CheckWaitingRobots(OrchestratedContext ctx, string station)
    {
        // Check if any robot is waiting for this station
        foreach (var kvp in _robotWaitingFor.ToList())
        {
            if (kvp.Value == station)
            {
                var robot = kvp.Key;
                var destState = GetStationState(station);
                bool destReady = false;

                // Apply robot-specific state checks (same logic as holding state)
                // R1: Can place to Polisher only when empty/idle (Polisher can't accept PLACE in done state)
                // R2: Can place to Cleaner when empty/idle OR done (R3 will pick immediately)
                // R3: Only when Buffer is empty (strict - no downstream robot)
                if (robot == "R1")
                {
                    // R1 → Polisher: Only when truly empty/idle
                    // Polisher state machine only accepts PLACE in empty state!
                    destReady = (destState == "empty" || destState == "idle" || destState == "IDLE");
                }
                else if (robot == "R2")
                {
                    // R2 → Cleaner: Can place when empty/idle OR done
                    // This allows overlapping: R2 places while R3 picks
                    destReady = (destState == "empty" || destState == "idle" || destState == "done");
                }
                else if (robot == "R3")
                {
                    // R3 → Buffer: Must wait until Buffer is truly empty (no downstream robot)
                    destReady = (destState == "empty" || destState == "idle");
                }
                else
                {
                    // Fallback for any other robot
                    destReady = (destState == "empty" || destState == "idle");
                }

                if (destReady)
                {
                    _logger($"[Scheduler] ✓ Station {station} is now ready! Sending DESTINATION_READY to {robot}");
                    ctx.RequestSend(robot, "DESTINATION_READY", new JObject());
                    _robotWaitingFor.Remove(robot);
                }
                else
                {
                    _logger($"[Scheduler] ⏸ Station {station} changed but not ready for {robot} (state={destState ?? "N/A"})");
                }
            }
        }
    }

    private void CheckPriorities(OrchestratedContext ctx)
    {
        // Check ALL priorities and execute ALL that can run simultaneously
        // This allows R2 and R3 to move in parallel when both P and C are done
        //
        // NEW STRATEGY for R1:
        // - R1's top priority is B→L (return completed wafers)
        // - R1 should always hold a wafer from LoadPort when idle
        // - R1 delivers held wafer to Polisher when it becomes available

        bool anyExecuted = false;

        // Priority 1: C → B (R3)
        if (CanExecuteCtoB())
        {
            ExecuteCtoB(ctx);
            anyExecuted = true;
            // Don't return - check other priorities too!
        }

        // Priority 2: P → C (R2)
        if (CanExecutePtoC())
        {
            ExecutePtoC(ctx);
            anyExecuted = true;
            // Don't return - check other priorities too!
        }

        // Priority 3: B → L (R1) - HIGHEST priority for R1!
        // R1 constantly monitors Buffer and returns completed wafers immediately
        if (CanExecuteBtoL())
        {
            ExecuteBtoL(ctx);
            anyExecuted = true;
            // Don't return - check other priorities too!
        }

        // Priority 4: L → HOLD (R1) - Proactively pick and hold wafer from LoadPort
        // R1 should always have a wafer ready to deliver to Polisher
        if (CanExecuteLtoHold())
        {
            ExecuteLtoHold(ctx);
            anyExecuted = true;
            // Don't return - check other priorities too!
        }

        // Priority 5: HOLD → P (R1) - Deliver held wafer to Polisher when ready
        if (CanExecuteHoldToP())
        {
            ExecuteHoldToP(ctx);
            anyExecuted = true;
        }

        // No logging when no priority is met - this is normal during processing
    }

    // Priority 1: C → B (R3)
    private bool CanExecuteCtoB()
    {
        // R3 is dedicated C→B robot
        // Pick from Cleaner immediately when done, regardless of Buffer state
        // R3 will wait in holding state until Buffer becomes empty
        return GetStationState("cleaner") == "done" &&
               GetRobotState("R3") == "idle";
    }

    private void ExecuteCtoB(OrchestratedContext ctx)
    {
        int? waferId = _stationWafers.GetValueOrDefault("cleaner");
        if (waferId == null || waferId == 0) return;

        _logger($"[Scheduler] [P1] C→B: Commanding R3 to transfer wafer {waferId}");

        // Mark robot as having pending command
        _robotsWithPendingCommands.Add("R3");

        // Send TRANSFER command to R3
        ctx.RequestSend("R3", "TRANSFER", new JObject
        {
            ["waferId"] = waferId,
            ["from"] = "cleaner",
            ["to"] = "buffer"
        });
    }

    // Priority 2: P → C (R2)
    private bool CanExecutePtoC()
    {
        // R2 can pick from Polisher and place to Cleaner when:
        // 1. Polisher is done (wafer ready to pick)
        // 2. R2 is idle
        // 3. Cleaner is empty OR done (if done, R3 will pick it up - they can work in parallel!)
        var cleanerState = GetStationState("cleaner");
        bool cleanerAvailable = cleanerState == "empty" || cleanerState == "done";

        // Polisher uses E157 states: COMPLETE = done
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

        _logger($"[Scheduler] [P2] P→C: Commanding R2 to transfer wafer {waferId}");

        // Mark robot as having pending command
        _robotsWithPendingCommands.Add("R2");

        // Send TRANSFER command to R2
        ctx.RequestSend("R2", "TRANSFER", new JObject
        {
            ["waferId"] = waferId,
            ["from"] = "polisher",
            ["to"] = "cleaner"
        });
    }

    // Priority 4: L → HOLD (R1) - Proactively pick wafer from LoadPort
    // R1 should always have a wafer ready to deliver to Polisher
    private bool CanExecuteLtoHold()
    {
        var r1State = GetRobotState("R1");

        // R1 can pick from LoadPort when:
        // 1. R1 is idle (not holding a wafer, not busy)
        // 2. There are pending wafers in LoadPort queue
        return _lPending.Count > 0 && r1State == "idle";
    }

    private void ExecuteLtoHold(OrchestratedContext ctx)
    {
        if (_lPending.Count == 0) return;

        int waferId = _lPending[0];
        _lPending.RemoveAt(0);

        _logger($"[Scheduler] [P4] L→HOLD: Commanding R1 to pick wafer {waferId} from LoadPort (Pending: {_lPending.Count} left)");

        // Mark robot as having pending command
        _robotsWithPendingCommands.Add("R1");

        // Send PICK command to R1 (just pick, don't place yet)
        ctx.RequestSend("R1", "TRANSFER", new JObject
        {
            ["waferId"] = waferId,
            ["from"] = "LoadPort",
            ["to"] = "polisher"  // Destination is Polisher, but R1 will wait in holding state until it's ready
        });
    }

    // Priority 5: HOLD → P (R1) - Deliver held wafer to Polisher when ready
    private bool CanExecuteHoldToP()
    {
        var r1State = GetRobotState("R1");
        var polisherState = GetStationState("polisher");
        var cleanerState = GetStationState("cleaner");
        var r2State = GetRobotState("R2");
        var r3State = GetRobotState("R3");

        // R1 can deliver held wafer to Polisher when:
        // 1. R1 is holding a wafer (already picked from LoadPort)
        // 2. Polisher is available (empty/idle OR will be empty soon)

        // This is automatically handled by the robot's holding state logic!
        // When R1 is holding and destination (Polisher) becomes ready,
        // the scheduler sends DESTINATION_READY event to R1
        // So we don't need explicit logic here - return false
        // (This priority check is just for documentation)

        return false; // Handled automatically by onRobotStatus when R1 is "holding"
    }

    private void ExecuteHoldToP(OrchestratedContext ctx)
    {
        // This is handled automatically by the holding state logic
        // No action needed here
    }

    // Priority 3: B → L (R1) - HIGHEST priority for R1!
    private bool CanExecuteBtoL()
    {
        // R1's top priority: Return completed wafers from Buffer to LoadPort
        // Execute B→L when:
        // 1. Buffer is occupied (has wafer to return)
        // 2. R1 is idle (not currently executing a transfer)
        //
        // NOTE: R1 should drop whatever it's doing to handle B→L
        // If R1 is holding a wafer from LoadPort, it should first return the Buffer wafer,
        // then resume delivering the held wafer to Polisher

        return GetStationState("buffer") == "occupied" &&
               GetRobotState("R1") == "idle";
    }

    private void ExecuteBtoL(OrchestratedContext ctx)
    {
        int? waferId = _stationWafers.GetValueOrDefault("buffer");
        if (waferId == null || waferId == 0) return;

        _logger($"[Scheduler] [P4] B→L: Commanding R1 to return wafer {waferId}");

        // Mark robot as having pending command
        _robotsWithPendingCommands.Add("R1");

        // Send TRANSFER command to R1
        ctx.RequestSend("R1", "TRANSFER", new JObject
        {
            ["waferId"] = waferId.Value,
            ["from"] = "buffer",
            ["to"] = "LoadPort"
        });

        // Mark as completed
        _lCompleted.Add(waferId.Value);
        _logger($"[Scheduler] ✓ Wafer {waferId} completed ({_lCompleted.Count}/{_totalWafers})");

        // Check if all wafers completed
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
        // If robot has a pending command, treat it as not idle
        if (_robotsWithPendingCommands.Contains(robot))
        {
            return "busy"; // Virtual state to prevent duplicate commands
        }

        return _robotStates.GetValueOrDefault(robot);
    }

    // Public accessors for debugging/monitoring
    public int PendingCount => _lPending.Count;
    public int CompletedCount => _lCompleted.Count;
    public IReadOnlyList<int> Completed => _lCompleted.AsReadOnly();
}
