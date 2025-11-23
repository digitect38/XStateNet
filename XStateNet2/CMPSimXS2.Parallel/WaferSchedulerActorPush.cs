using Akka.Actor;

namespace CMPSimXS2.Parallel;

/// <summary>
/// PUSH MODEL (NEW): Wafer scheduler is autonomous - commands robots based on coordinator's proceed signal
/// Coordinator evaluates WHEN to proceed, Wafer decides HOW to proceed
/// </summary>
public class WaferSchedulerActorPush : ReceiveActor
{
    private readonly string _waferId;
    private readonly string _waferJson;
    private readonly IActorRef _coordinator;
    private IActorRef? _robotSchedulers;

    private readonly long _startTime;
    private string _currentState = "created";

    // Guard conditions track what the wafer CAN do (prerequisites met)
    private GuardConditions _conditions = GuardConditions.None;

    // Track last reported state to avoid duplicate reports
    private string _lastReportedState = "";
    private GuardConditions _lastReportedConditions = GuardConditions.None;

    public WaferSchedulerActorPush(string waferId, string waferJson, IActorRef coordinator, IActorRef robotSchedulers)
    {
        _waferId = waferId;
        _waferJson = waferJson;
        _coordinator = coordinator;
        _robotSchedulers = robotSchedulers;
        _startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // NEW ARCHITECTURE: Wafer receives proceed signals and commands robots
        Receive<StartWaferProcessing>(_ => HandleStartProcessing());
        Receive<ProceedToNextStep>(msg => HandleProceedToNextStep(msg));
        Receive<ResourcePermissionGranted>(msg => HandleResourcePermissionGranted(msg));
        Receive<ResourcePermissionDenied>(msg => HandleResourcePermissionDenied(msg));
        Receive<TaskCompleted>(msg => HandleTaskCompleted(msg));

        // Report initial status
        var waferSchId = _waferId.Replace("W-", "WSCH-");
        TableLogger.LogEvent("INIT_STATUS", waferSchId, "READY", _waferId);
    }

    private void HandleStartProcessing()
    {
        TableLogger.Log($"[{_waferId}] PUSH-NEW: Starting processing - reporting initial state");

        // Initial state: waiting for R-1 to pick from carrier
        _currentState = "waiting_for_r1_pickup";
        _conditions = GuardConditions.CanPickFromCarrier;

        // Report state to coordinator
        ReportStateToCoordinator();
    }

    private void HandleProceedToNextStep(ProceedToNextStep msg)
    {
        TableLogger.Log($"[{_waferId}] PUSH-NEW: Coordinator says proceed from state '{_currentState}'");
        TableLogger.LogEvent("RECV_PUSH", "", $"ProceedToNextStep:{_currentState}", _waferId);

        // Based on current state, decide which robot/equipment to command
        switch (_currentState)
        {
            case "waiting_for_r1_pickup":
                // Command R-1 to pick from carrier
                CommandRobot("R-1", "pick", 4); // p1 priority
                _currentState = "r1_picking_from_carrier";
                break;

            case "r1_moving_to_platen":
                // Command R-1 to place on platen
                CommandRobot("R-1", "place", 4);
                _currentState = "r1_placing_to_platen";
                break;

            case "waiting_for_polisher":
                // Command PLATEN to start polishing
                CommandEquipment("PLATEN");
                _currentState = "polishing";
                break;

            case "waiting_for_r2_pickup":
                // Command R-2 to pick from platen
                CommandRobot("R-2", "pick", 3); // p2 priority
                _currentState = "r2_picking_from_platen";
                break;

            case "r2_moving_to_cleaner":
                // Command R-2 to place on cleaner
                CommandRobot("R-2", "place", 3);
                _currentState = "r2_placing_to_cleaner";
                break;

            case "waiting_for_cleaner":
                // Command CLEANER to start cleaning
                CommandEquipment("CLEANER");
                _currentState = "cleaning";
                break;

            case "waiting_for_r3_pickup":
                // Command R-3 to pick from cleaner
                CommandRobot("R-3", "pick", 2); // p3 priority
                _currentState = "r3_picking_from_cleaner";
                break;

            case "r3_moving_to_buffer":
                // Command R-3 to place on buffer
                CommandRobot("R-3", "place", 2);
                _currentState = "r3_placing_to_buffer";
                break;

            case "waiting_for_buffer":
                // Command BUFFER to start buffering
                CommandEquipment("BUFFER");
                _currentState = "buffering";
                break;

            case "waiting_for_r1_return":
                // Command R-1 to pick from buffer (return trip)
                CommandRobot("R-1", "pick", 1); // p4 priority (highest)
                _currentState = "r1_picking_from_buffer";
                break;

            case "r1_returning_to_carrier":
                // Command R-1 to place on carrier
                CommandRobot("R-1", "place", 1);
                _currentState = "r1_placing_to_carrier";
                break;

            default:
                TableLogger.Log($"[{_waferId}] PUSH-NEW: No action for state '{_currentState}'");
                break;
        }

        ReportStateToCoordinator();
    }

    private void CommandRobot(string robotId, string task, int priority)
    {
        TableLogger.Log($"[{_waferId}] → Commanding {robotId} to {task} (p{priority})");
        TableLogger.LogEvent("WAFER_CMD", robotId, $"{robotId}:{task}:p{priority}", _waferId);

        if (_robotSchedulers == null)
        {
            Console.Error.WriteLine($"ERROR: {_waferId} cannot command {robotId} - _robotSchedulers is null!");
            return;
        }

        _robotSchedulers.Tell(new ExecuteRobotTask(robotId, task, _waferId, priority));
    }

    private void CommandEquipment(string equipmentId)
    {
        TableLogger.Log($"[{_waferId}] → Commanding {equipmentId} to process");
        TableLogger.LogEvent("WAFER_CMD", equipmentId, $"{equipmentId}:process", _waferId);

        if (_robotSchedulers == null)
        {
            Console.Error.WriteLine($"ERROR: {_waferId} cannot command {equipmentId} - _robotSchedulers is null!");
            return;
        }

        _robotSchedulers.Tell(new ExecuteEquipmentTask(equipmentId, _waferId));
    }

    private void HandleTaskCompleted(TaskCompleted msg)
    {
        TableLogger.Log($"[{_waferId}] PUSH-NEW: {msg.ResourceId} completed task");

        // Update state based on which resource completed
        switch (msg.ResourceId)
        {
            case "R-1" when _currentState == "r1_picking_from_carrier":
                _currentState = "r1_moving_to_platen";
                CommandRobot("R-1", "move", 4);
                break;

            case "R-1" when _currentState == "r1_moving_to_platen":
                _currentState = "waiting_platen_location";
                // Request location permission from coordinator
                _coordinator.Tell(new RequestResourcePermission("PLATEN_LOCATION", _waferId));
                // Notify coordinator that wafer reached platen (triggers next wafer spawn)
                _coordinator.Tell(new WaferAtPlaten(_waferId));
                break;

            case "R-1" when _currentState == "r1_placing_to_platen":
                _currentState = "waiting_for_polisher";
                _conditions = GuardConditions.WaferAtPlaten | GuardConditions.CanStartPolish;
                break;

            case "PLATEN":
                _currentState = "waiting_for_r2_pickup";
                _conditions = GuardConditions.PolishComplete | GuardConditions.CanPickFromPlaten;
                TableLogger.LogEvent("POLISH_COMPLETE", "", "", _waferId);
                break;

            case "R-2" when _currentState == "r2_picking_from_platen":
                _currentState = "r2_moving_to_cleaner";
                CommandRobot("R-2", "move", 3);
                // Release platen location
                _coordinator.Tell(new ReleaseResource("PLATEN_LOCATION", _waferId));
                break;

            case "R-2" when _currentState == "r2_moving_to_cleaner":
                _currentState = "waiting_cleaner_location";
                // Request location permission
                _coordinator.Tell(new RequestResourcePermission("CLEANER_LOCATION", _waferId));
                break;

            case "R-2" when _currentState == "r2_placing_to_cleaner":
                _currentState = "waiting_for_cleaner";
                _conditions = GuardConditions.WaferAtCleaner | GuardConditions.CanStartClean;
                break;

            case "CLEANER":
                _currentState = "waiting_for_r3_pickup";
                _conditions = GuardConditions.CleanComplete | GuardConditions.CanPickFromCleaner;
                TableLogger.LogEvent("CLEAN_COMPLETE", "", "", _waferId);
                break;

            case "R-3" when _currentState == "r3_picking_from_cleaner":
                _currentState = "r3_moving_to_buffer";
                CommandRobot("R-3", "move", 2);
                // Release cleaner location
                _coordinator.Tell(new ReleaseResource("CLEANER_LOCATION", _waferId));
                break;

            case "R-3" when _currentState == "r3_moving_to_buffer":
                _currentState = "waiting_buffer_location";
                // Request location permission
                _coordinator.Tell(new RequestResourcePermission("BUFFER_LOCATION", _waferId));
                break;

            case "R-3" when _currentState == "r3_placing_to_buffer":
                _currentState = "waiting_for_buffer";
                _conditions = GuardConditions.WaferAtBuffer | GuardConditions.CanStartBuffer;
                break;

            case "BUFFER":
                _currentState = "waiting_for_r1_return";
                _conditions = GuardConditions.BufferComplete | GuardConditions.CanPickFromBuffer;
                TableLogger.LogEvent("BUFFER_COMPLETE", "", "", _waferId);
                break;

            case "R-1" when _currentState == "r1_picking_from_buffer":
                _currentState = "r1_returning_to_carrier";
                CommandRobot("R-1", "move", 1);
                // Release buffer location
                _coordinator.Tell(new ReleaseResource("BUFFER_LOCATION", _waferId));
                break;

            case "R-1" when _currentState == "r1_returning_to_carrier":
                _currentState = "r1_placing_to_carrier";
                // Ready to place
                break;

            case "R-1" when _currentState == "r1_placing_to_carrier":
                _currentState = "completed";
                var cycleTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _startTime;
                TableLogger.Log($"[{_waferId}] ✓ COMPLETED - cycle time: {cycleTime}ms");
                TableLogger.LogEvent("COMPLETE", "", "", _waferId);
                _coordinator.Tell(new WaferCompleted(_waferId));
                Context.Stop(Self);
                return;
        }

        ReportStateToCoordinator();
    }

    private void HandleResourcePermissionGranted(ResourcePermissionGranted msg)
    {
        // Coordinator granted location permission - update conditions
        TableLogger.Log($"[{_waferId}] PUSH-NEW: Location permission granted for {msg.ResourceType}");

        switch (msg.ResourceType)
        {
            case "PLATEN_LOCATION":
                _conditions = _conditions.Set(GuardConditions.HasPlatenPermission);
                _conditions = _conditions.Set(GuardConditions.CanPlaceOnPlaten);
                _currentState = "r1_placing_to_platen";
                // Proceed to place on platen
                Self.Tell(new ProceedToNextStep(_waferId));
                break;

            case "CLEANER_LOCATION":
                _conditions = _conditions.Set(GuardConditions.HasCleanerPermission);
                _conditions = _conditions.Set(GuardConditions.CanPlaceOnCleaner);
                _currentState = "r2_placing_to_cleaner";
                // Proceed to place on cleaner
                Self.Tell(new ProceedToNextStep(_waferId));
                break;

            case "BUFFER_LOCATION":
                _conditions = _conditions.Set(GuardConditions.HasBufferPermission);
                _conditions = _conditions.Set(GuardConditions.CanPlaceOnBuffer);
                _currentState = "r3_placing_to_buffer";
                // Proceed to place on buffer
                Self.Tell(new ProceedToNextStep(_waferId));
                break;
        }

        ReportStateToCoordinator();
    }

    private void HandleResourcePermissionDenied(ResourcePermissionDenied msg)
    {
        // Coordinator denied location permission - wait
        TableLogger.Log($"[{_waferId}] PUSH-NEW: Location permission denied for {msg.ResourceType}: {msg.Reason}");
        // Stay in current state and wait for permission
    }

    private void ReportStateToCoordinator()
    {
        // Only report if state or conditions changed
        if (_currentState == _lastReportedState && _conditions == _lastReportedConditions)
        {
            TableLogger.Log($"[{_waferId}] PUSH: State unchanged, skipping duplicate report");
            return;
        }

        TableLogger.Log($"[{_waferId}] PUSH: Reporting state '{_currentState}' with conditions {_conditions.ToHexString()}");

        _coordinator.Tell(new WaferStateUpdate(
            _waferId,
            _currentState,
            _conditions
        ));

        _lastReportedState = _currentState;
        _lastReportedConditions = _conditions;
    }
}
