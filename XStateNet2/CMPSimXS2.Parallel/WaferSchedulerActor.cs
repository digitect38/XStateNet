using Akka.Actor;

namespace CMPSimXS2.Parallel;

/// <summary>
/// Layer 2: Individual wafer scheduler
/// Manages a single wafer's journey through the pipeline:
/// Carrier ??R-1 ??Platen ??R-2 ??Cleaner ??R-3 ??Buffer ??R-1 ??Carrier
/// </summary>
public class WaferSchedulerActor : ReceiveActor
{
    private readonly string _waferId;
    private readonly string _waferJson;
    private readonly IActorRef _robotSchedulers;
    private readonly IActorRef _coordinator;

    private readonly long _startTime;
    private string _currentState = "created";

    // Bitmasking-based guard conditions
    private GuardConditions _conditions = GuardConditions.None;

    public WaferSchedulerActor(string waferId, string waferJson, IActorRef robotSchedulers, IActorRef coordinator)
    {
        _waferId = waferId;
        _waferJson = waferJson;
        _robotSchedulers = robotSchedulers;
        _coordinator = coordinator;
        _startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Receive<StartWaferProcessing>(_ => StartProcessing());

        // Location permission responses
        Receive<ResourcePermissionGranted>(msg => HandleLocationPermissionGranted(msg));
        Receive<ResourcePermissionDenied>(msg => HandleLocationPermissionDenied(msg));
        Receive<RetryLocationPermission>(msg => HandleRetryLocationPermission(msg));

        // Robot 1 workflow
        Receive<Robot1Available>(msg => HandleRobot1Available(msg));

        // Polishing workflow
        Receive<PolishCompleted>(msg => HandlePolishCompleted(msg));

        // Robot 2 workflow
        Receive<Robot2Available>(msg => HandleRobot2Available(msg));

        // Cleaning workflow
        Receive<CleanCompleted>(msg => HandleCleanCompleted(msg));

        // Robot 3 workflow
        Receive<Robot3Available>(msg => HandleRobot3Available(msg));

        // Buffering workflow
        Receive<BufferCompleted>(msg => HandleBufferCompleted(msg));

        // Internal trigger messages
        Receive<RequestPolishNow>(_ =>
        {
            TableLogger.LogEvent("REQUEST_POLISH", "", "", _waferId);
            _robotSchedulers.Tell(new RequestPolish(_waferId));
        });

        Receive<RequestCleanNow>(_ =>
        {
            TableLogger.LogEvent("REQUEST_CLEAN", "", "", _waferId);
            _robotSchedulers.Tell(new RequestClean(_waferId));
        });

        Receive<RequestBufferNow>(_ =>
        {
            TableLogger.LogEvent("REQUEST_BUFFER", "", "", _waferId);
            _robotSchedulers.Tell(new RequestBuffer(_waferId));
        });

        // Report initial status to coordinator
        var waferSchId = _waferId.Replace("W-", "WSCH-");
        TableLogger.LogEvent("INIT_STATUS", waferSchId, "READY", _waferId);
    }

    private void HandleLocationPermissionGranted(ResourcePermissionGranted msg)
    {
        // Update conditions based on granted permission
        switch (msg.ResourceType)
        {
            case "PLATEN_LOCATION":
                _conditions = _conditions.Set(GuardConditions.HasPlatenPermission);
                TableLogger.Log($"[{_waferId}] Conditions updated: {_conditions.ToHexString()} (PLATEN_LOCATION granted)");
                break;
            case "CLEANER_LOCATION":
                _conditions = _conditions.Set(GuardConditions.HasCleanerPermission);
                TableLogger.Log($"[{_waferId}] Conditions updated: {_conditions.ToHexString()} (CLEANER_LOCATION granted)");
                break;
            case "BUFFER_LOCATION":
                _conditions = _conditions.Set(GuardConditions.HasBufferPermission);
                TableLogger.Log($"[{_waferId}] Conditions updated: {_conditions.ToHexString()} (BUFFER_LOCATION granted)");
                break;
        }

        // Location permission granted - proceed with placing wafer
        switch (_currentState)
        {
            case "waiting_platen_location":
                if (_conditions.HasAll(GuardConditions.CanPlaceOnPlaten))
                {
                    _currentState = "r1_placing_to_platen";
                    _robotSchedulers.Tell(new RequestRobot1("place", _waferId, 4)); // p1
                }
                break;
            case "waiting_cleaner_location":
                if (_conditions.HasAll(GuardConditions.CanPlaceOnCleaner))
                {
                    _currentState = "r2_placing_to_cleaner";
                    _robotSchedulers.Tell(new RequestRobot2("place", _waferId, 3)); // p2
                }
                break;
            case "waiting_buffer_location":
                if (_conditions.HasAll(GuardConditions.CanPlaceOnBuffer))
                {
                    _currentState = "r3_placing_to_buffer";
                    _robotSchedulers.Tell(new RequestRobot3("place", _waferId, 2)); // p3
                }
                break;
        }
    }

    private void HandleLocationPermissionDenied(ResourcePermissionDenied msg)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[{_waferId}] Location permission DENIED for {msg.ResourceType}: {msg.Reason} - will retry");
        Console.ResetColor();

        // Retry after a short delay
        Context.System.Scheduler.ScheduleTellOnce(
            TimeSpan.FromMilliseconds(50),
            Self,
            new RetryLocationPermission(msg.ResourceType),
            ActorRefs.NoSender
        );
    }

    private void HandleRetryLocationPermission(RetryLocationPermission msg)
    {
        // Retry requesting the location permission
        _coordinator.Tell(new RequestResourcePermission(msg.ResourceType, _waferId));
    }

    private void StartProcessing()
    {
        TableLogger.Log("[{_waferId}] Created - requesting R-1 for pickup from carrier");
        _currentState = "waiting_for_r1_pickup";
        TableLogger.LogEvent("REQUEST_ROBOT", "R-1(p1)", "", _waferId);
        _robotSchedulers.Tell(new RequestRobot1("pick", _waferId, 4)); // p1: Carrier ??Platen (lowest priority)
    }

    private void HandleRobot1Available(Robot1Available msg)
    {
        // Set Robot1Permission when robot becomes available
        _conditions = _conditions.Set(GuardConditions.HasRobot1Permission);

        switch (_currentState)
        {
            case "waiting_for_r1_pickup":
                if (_conditions.HasAll(GuardConditions.CanPickFromCarrier))
                {
                    TableLogger.Log($"[{_waferId}] R-1 available [{_conditions.ToHexString()}] - picking from carrier");
                    _currentState = "r1_moving_to_platen";
                    _conditions = _conditions.Set(GuardConditions.WaferOnRobot);
                    _robotSchedulers.Tell(new RequestRobot1("move", _waferId, 4)); // p1
                }
                break;

            case "r1_moving_to_platen":
                if (_conditions.HasAll(GuardConditions.CanMoveToPlaten))
                {
                    TableLogger.Log($"[{_waferId}] R-1 at platen [{_conditions.ToHexString()}] - requesting location permission");
                    _currentState = "waiting_platen_location";
                    // Request permission to occupy platen location before placing
                    _coordinator.Tell(new RequestResourcePermission("PLATEN_LOCATION", _waferId));

                    // Notify coordinator that wafer is at platen (triggers next wafer spawn)
                    _coordinator.Tell(new WaferAtPlaten(_waferId));
                }
                break;

            case "r1_placing_to_platen":
                TableLogger.Log($"[{_waferId}] R-1 placement complete [{_conditions.ToHexString()}] - moving back to carrier");
                _currentState = "r1_returning_to_carrier_from_platen";
                // Clear WaferOnRobot, set WaferAtPlaten
                _conditions = _conditions.Clear(GuardConditions.WaferOnRobot | GuardConditions.HasPlatenPermission);
                _conditions = _conditions.Set(GuardConditions.WaferAtPlaten);
                _robotSchedulers.Tell(new RequestRobot1("move", _waferId, 4)); // p1
                break;

            case "r1_returning_to_carrier_from_platen":
                TableLogger.Log("[{_waferId}] R-1 back at carrier - ready for polishing");
                _currentState = "waiting_for_polisher";
                // Schedule polish request
                Context.System.Scheduler.ScheduleTellOnce(
                    TimeSpan.FromMilliseconds(30),
                    Self,
                    new RequestPolishNow(),
                    ActorRefs.NoSender
                );
                break;

            case "waiting_for_r1_return":
                TableLogger.Log("[{_waferId}] R-1 available - picking from buffer");
                _currentState = "r1_returning_to_carrier";
                _robotSchedulers.Tell(new RequestRobot1("pick", _waferId, 1)); // p4: Buffer ??Carrier (highest priority)
                // Release buffer location after picking up
                _coordinator.Tell(new ReleaseResource("BUFFER_LOCATION", _waferId));
                break;

            case "r1_returning_to_carrier":
                TableLogger.Log("[{_waferId}] R-1 moving to carrier");
                _currentState = "r1_placing_to_carrier";
                _robotSchedulers.Tell(new RequestRobot1("move", _waferId, 1)); // p4
                break;

            case "r1_placing_to_carrier":
                TableLogger.Log("[{_waferId}] R-1 placing wafer at carrier");
                var cycleTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _startTime;
                TableLogger.Log("[{_waferId}] ??COMPLETED - cycle time: {cycleTime}ms");
                TableLogger.LogEvent("COMPLETE", "", "", _waferId);
                _coordinator.Tell(new WaferCompleted(_waferId));
                Context.Stop(Self);
                break;
        }
    }

    private void HandlePolishCompleted(PolishCompleted msg)
    {
        // Set PolishComplete condition
        _conditions = _conditions.Set(GuardConditions.PolishComplete);
        TableLogger.Log($"[{_waferId}] Polishing completed [{_conditions.ToHexString()}] - requesting R-2");
        TableLogger.LogEvent("POLISH_COMPLETE", "", "", _waferId);
        _currentState = "waiting_for_r2_pickup";
        TableLogger.LogEvent("REQUEST_ROBOT", "R-2(p2)", "", _waferId);
        _robotSchedulers.Tell(new RequestRobot2("move", _waferId, 3)); // p2: Platen ??Cleaner
    }

    private void HandleRobot2Available(Robot2Available msg)
    {
        // Set Robot2Permission when robot becomes available
        _conditions = _conditions.Set(GuardConditions.HasRobot2Permission);

        switch (_currentState)
        {
            case "waiting_for_r2_pickup":
                if (_conditions.HasAll(GuardConditions.CanPickFromPlaten))
                {
                    TableLogger.Log($"[{_waferId}] R-2 available [{_conditions.ToHexString()}] - picking from platen");
                    _currentState = "r2_moving_to_cleaner";
                    _conditions = _conditions.Set(GuardConditions.WaferOnRobot);
                    _conditions = _conditions.Clear(GuardConditions.WaferAtPlaten);
                    _robotSchedulers.Tell(new RequestRobot2("pick", _waferId, 3)); // p2
                    // Release platen location after picking up
                    _coordinator.Tell(new ReleaseResource("PLATEN_LOCATION", _waferId));
                }
                break;

            case "r2_moving_to_cleaner":
                if (_conditions.HasAll(GuardConditions.CanMoveToCleaner))
                {
                    TableLogger.Log($"[{_waferId}] R-2 at cleaner [{_conditions.ToHexString()}] - requesting location permission");
                    _currentState = "waiting_cleaner_location";
                    // Request permission to occupy cleaner location before placing
                    _coordinator.Tell(new RequestResourcePermission("CLEANER_LOCATION", _waferId));
                }
                break;

            case "r2_placing_to_cleaner":
                TableLogger.Log($"[{_waferId}] R-2 placement complete [{_conditions.ToHexString()}] - moving back to platen");
                _currentState = "r2_returning_to_platen";
                // Clear WaferOnRobot and CleanerPermission, set WaferAtCleaner
                _conditions = _conditions.Clear(GuardConditions.WaferOnRobot | GuardConditions.HasCleanerPermission);
                _conditions = _conditions.Set(GuardConditions.WaferAtCleaner);
                _robotSchedulers.Tell(new RequestRobot2("move", _waferId, 3)); // p2
                break;

            case "r2_returning_to_platen":
                TableLogger.Log($"[{_waferId}] R-2 back at platen [{_conditions.ToHexString()}] - ready for cleaning");
                _currentState = "waiting_for_cleaner";
                // Schedule clean request
                Context.System.Scheduler.ScheduleTellOnce(
                    TimeSpan.FromMilliseconds(30),
                    Self,
                    new RequestCleanNow(),
                    ActorRefs.NoSender
                );
                break;
        }
    }

    private void HandleCleanCompleted(CleanCompleted msg)
    {
        // Set CleanComplete condition
        _conditions = _conditions.Set(GuardConditions.CleanComplete);
        TableLogger.Log($"[{_waferId}] Cleaning completed [{_conditions.ToHexString()}] - requesting R-3");
        TableLogger.LogEvent("CLEAN_COMPLETE", "", "", _waferId);
        _currentState = "waiting_for_r3_pickup";
        TableLogger.LogEvent("REQUEST_ROBOT", "R-3(p3)", "", _waferId);
        _robotSchedulers.Tell(new RequestRobot3("move", _waferId, 2)); // p3: Cleaner ??Buffer
    }

    private void HandleRobot3Available(Robot3Available msg)
    {
        // Set Robot3Permission when robot becomes available
        _conditions = _conditions.Set(GuardConditions.HasRobot3Permission);

        switch (_currentState)
        {
            case "waiting_for_r3_pickup":
                if (_conditions.HasAll(GuardConditions.CanPickFromCleaner))
                {
                    TableLogger.Log($"[{_waferId}] R-3 available [{_conditions.ToHexString()}] - picking from cleaner");
                    _currentState = "r3_moving_to_buffer";
                    _conditions = _conditions.Set(GuardConditions.WaferOnRobot);
                    _conditions = _conditions.Clear(GuardConditions.WaferAtCleaner);
                    _robotSchedulers.Tell(new RequestRobot3("pick", _waferId, 2)); // p3
                    // Release cleaner location after picking up
                    _coordinator.Tell(new ReleaseResource("CLEANER_LOCATION", _waferId));
                }
                break;

            case "r3_moving_to_buffer":
                if (_conditions.HasAll(GuardConditions.CanMoveToBuffer))
                {
                    TableLogger.Log($"[{_waferId}] R-3 at buffer [{_conditions.ToHexString()}] - requesting location permission");
                    _currentState = "waiting_buffer_location";
                    // Request permission to occupy buffer location before placing
                    _coordinator.Tell(new RequestResourcePermission("BUFFER_LOCATION", _waferId));
                }
                break;

            case "r3_placing_to_buffer":
                TableLogger.Log($"[{_waferId}] R-3 placement complete [{_conditions.ToHexString()}] - moving back to cleaner");
                _currentState = "r3_returning_to_cleaner";
                // Clear WaferOnRobot and BufferPermission, set WaferAtBuffer
                _conditions = _conditions.Clear(GuardConditions.WaferOnRobot | GuardConditions.HasBufferPermission);
                _conditions = _conditions.Set(GuardConditions.WaferAtBuffer);
                _robotSchedulers.Tell(new RequestRobot3("move", _waferId, 2)); // p3
                break;

            case "r3_returning_to_cleaner":
                TableLogger.Log($"[{_waferId}] R-3 back at cleaner [{_conditions.ToHexString()}] - ready for buffering");
                _currentState = "waiting_for_buffer";
                // Schedule buffer request
                Context.System.Scheduler.ScheduleTellOnce(
                    TimeSpan.FromMilliseconds(30),
                    Self,
                    new RequestBufferNow(),
                    ActorRefs.NoSender
                );
                break;
        }
    }

    private void HandleBufferCompleted(BufferCompleted msg)
    {
        // Set BufferComplete condition
        _conditions = _conditions.Set(GuardConditions.BufferComplete);
        TableLogger.Log($"[{_waferId}] Buffering completed [{_conditions.ToHexString()}] - requesting R-1 for return");
        TableLogger.LogEvent("BUFFER_COMPLETE", "", "", _waferId);
        _currentState = "waiting_for_r1_return";
        TableLogger.LogEvent("REQUEST_ROBOT", "R-1(p4)", "", _waferId);
        _robotSchedulers.Tell(new RequestRobot1("move", _waferId, 1)); // p4: Buffer ??Carrier (highest priority)
    }
}

// Internal messages
record RequestPolishNow();
record RequestCleanNow();
record RequestBufferNow();
record RetryLocationPermission(string ResourceType);
