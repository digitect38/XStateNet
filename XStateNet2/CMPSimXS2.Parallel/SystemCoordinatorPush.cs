using Akka.Actor;

namespace CMPSimXS2.Parallel;

/// <summary>
/// Push-based SystemCoordinator using bitmask for resource availability
/// Proactively schedules wafers when resources become available and conditions are met
/// Synchronized scheduling for optimal performance
/// </summary>
public class SystemCoordinatorPush : ReceiveActor
{
    private readonly string _waferJson;
    private readonly string _robotsJson;

    private IActorRef? _robotSchedulers;
    private readonly Dictionary<string, IActorRef> _waferSchedulers = new();
    private readonly Dictionary<string, (string state, GuardConditions conditions)> _waferStates = new();

    private int _waferCounter = 0;
    private int _completedWafers = 0;
    private const int TotalWafers = 25;
    private const int MaxActiveWafers = 3;

    // Resource availability bitmask - core of push scheduling
    private ResourceAvailability _resourceAvailability = ResourceAvailability.AllResourcesFree;

    // Track which wafer owns which resource (One-to-One Rule still applies)
    private readonly Dictionary<string, string> _resourceOwnership = new();

    // Track which wafers have pending commands to prevent duplicate commands
    private readonly HashSet<string> _wafersWithPendingCommands = new();

    // FIFO queues for locations (fairness still maintained)
    private readonly Dictionary<string, Queue<string>> _locationQueues = new()
    {
        { "PLATEN_LOCATION", new Queue<string>() },
        { "CLEANER_LOCATION", new Queue<string>() },
        { "BUFFER_LOCATION", new Queue<string>() }
    };

    // Scheduling interval for synchronized evaluation
    private ICancelable? _schedulingTimer;
    private const int SchedulingIntervalMs = 10; // Evaluate every 10ms for responsiveness

    public SystemCoordinatorPush(string waferJson, string robotsJson)
    {
        Console.WriteLine("DEBUG: SystemCoordinatorPush constructor called");
        _waferJson = waferJson;
        _robotsJson = robotsJson;

        Receive<StartSystem>(_ => HandleStartSystem());
        Receive<SpawnWafer>(msg => HandleSpawnWafer(msg));
        Receive<WaferCompleted>(msg => HandleWaferCompleted(msg));
        Receive<WaferAtPlaten>(msg => HandleWaferAtPlaten(msg));

        // Push model: Coordinator receives state updates from wafers
        Receive<WaferStateUpdate>(msg => HandleWaferStateUpdate(msg));

        // Push model: Resources report availability changes
        Receive<ResourceAvailable>(msg => HandleResourceAvailable(msg));
        Receive<ResourceBusy>(msg => HandleResourceBusy(msg));

        // Location resource management
        Receive<RequestResourcePermission>(msg => HandleRequestResourcePermission(msg));
        Receive<ReleaseResource>(msg => HandleReleaseResource(msg));

        // Synchronized scheduling evaluation
        Receive<EvaluateScheduling>(_ => HandleEvaluateScheduling());
    }

    protected override void PreRestart(Exception reason, object message)
    {
        Console.Error.WriteLine($"ACTOR RESTART: SystemCoordinatorPush is being restarted! Reason: {reason.Message}");
        Console.Error.WriteLine($"ACTOR RESTART: Message that caused restart: {message}");
        Console.Error.WriteLine($"ACTOR RESTART: Stack trace: {reason.StackTrace}");
        base.PreRestart(reason, message);
    }

    private void HandleStartSystem()
    {
        Console.WriteLine("DEBUG: HandleStartSystem called");
        TableLogger.Log("[COORD-PUSH] Starting push-based 3-layer CMP system");

        // Initialize all resources as available
        _resourceAvailability = ResourceAvailability.AllResourcesFree;
        TableLogger.Log($"[COORD-PUSH] Resources initialized: {_resourceAvailability.ToReadableString()} [{_resourceAvailability.ToHexString()}]");

        // Start robot/equipment schedulers
        Console.WriteLine("DEBUG: About to create RobotSchedulersActor");
        _robotSchedulers = Context.ActorOf(
            Props.Create(() => new RobotSchedulersActor(_robotsJson, Self)),
            "robot-schedulers"
        );
        Console.WriteLine($"DEBUG: Created RobotSchedulersActor, _robotSchedulers = {_robotSchedulers}");

        TableLogger.LogEvent("INIT_STATUS", "ROBOTS", "R-1:READY,R-2:READY,R-3:READY", "SYSTEM");
        TableLogger.LogEvent("INIT_STATUS", "EQUIPMENT", "PLATEN:READY,CLEANER:READY,BUFFER:READY", "SYSTEM");

        // Spawn first wafer
        SpawnInitialWafers();

        // Broadcast SYSTEM_READY
        TableLogger.LogEvent("SYSTEM_READY", "COORD", "ALL SYSTEMS READY", "SYSTEM");

        // Start synchronized scheduling timer
        _schedulingTimer = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
            TimeSpan.FromMilliseconds(SchedulingIntervalMs),
            TimeSpan.FromMilliseconds(SchedulingIntervalMs),
            Self,
            new EvaluateScheduling(),
            ActorRefs.NoSender
        );

        TableLogger.Log($"[COORD-PUSH] Synchronized scheduling started (interval: {SchedulingIntervalMs}ms)");
    }

    private void SpawnInitialWafers()
    {
        if (_waferCounter < TotalWafers)
        {
            Self.Tell(new SpawnWafer($"W-{++_waferCounter:D3}"));
        }
    }

    private void HandleSpawnWafer(SpawnWafer msg)
    {
        if (_waferSchedulers.ContainsKey(msg.WaferId))
        {
            return;
        }

        TableLogger.Log($"[COORD-PUSH] Spawning wafer {msg.WaferId}");
        TableLogger.LogEvent("SPAWN", "", "", msg.WaferId);

        // NEW ARCHITECTURE: Pass robotSchedulers to wafer so it can command robots
        if (_robotSchedulers == null)
        {
            Console.Error.WriteLine($"ERROR: Cannot spawn {msg.WaferId} - _robotSchedulers is null!");
            return;
        }

        var waferScheduler = Context.ActorOf(
            Props.Create(() => new WaferSchedulerActorPush(msg.WaferId, _waferJson, Self, _robotSchedulers)),
            $"wafer-{msg.WaferId}"
        );

        _waferSchedulers[msg.WaferId] = waferScheduler;
        _waferStates[msg.WaferId] = ("created", GuardConditions.None);

        // First wafer triggers SYSTEM_READY
        if (_waferCounter == 1)
        {
            TableLogger.LogEvent("SYSTEM_READY", "COORD", "ALL SYSTEMS READY", "SYSTEM");
        }

        // Tell wafer to start (it will report state back)
        waferScheduler.Tell(new StartWaferProcessing());
    }

    private void HandleWaferStateUpdate(WaferStateUpdate msg)
    {
        // Check if this is a new state (different from last commanded state)
        bool isNewState = true;
        if (_waferStates.TryGetValue(msg.WaferId, out var oldStateInfo))
        {
            var (oldState, _) = oldStateInfo;
            if (oldState == msg.State)
            {
                isNewState = false;
            }
        }

        // Wafer reports its state and conditions
        _waferStates[msg.WaferId] = (msg.State, msg.Conditions);

        TableLogger.Log($"[COORD-PUSH] {msg.WaferId} state: {msg.State}, conditions: {msg.Conditions.ToHexString()}");

        // If wafer transitioned to a new state, clear any pending command flag
        // This allows the wafer to be scheduled for the next step
        if (isNewState && _wafersWithPendingCommands.Contains(msg.WaferId))
        {
            TableLogger.Log($"[COORD-PUSH] {msg.WaferId} transitioned to new state, clearing pending command");
            _wafersWithPendingCommands.Remove(msg.WaferId);
        }

        // Immediately evaluate if we can schedule next step for this wafer
        TryScheduleWafer(msg.WaferId);
    }

    private void HandleResourceAvailable(ResourceAvailable msg)
    {
        // Resource reports it's now free
        var resourceFlag = ResourceAvailabilityExtensions.FromResourceName(msg.ResourceId);
        _resourceAvailability = _resourceAvailability.MarkAvailable(resourceFlag);

        TableLogger.Log($"[COORD-PUSH] {msg.ResourceId} now available - resources: {_resourceAvailability.ToReadableString()} [{_resourceAvailability.ToHexString()}]");

        // Remove from ownership
        if (_resourceOwnership.ContainsKey(msg.ResourceId))
        {
            _resourceOwnership.Remove(msg.ResourceId);
        }

        // Trigger scheduling evaluation
        Self.Tell(new EvaluateScheduling());
    }

    private void HandleResourceBusy(ResourceBusy msg)
    {
        // Resource reports it's now busy
        var resourceFlag = ResourceAvailabilityExtensions.FromResourceName(msg.ResourceId);
        _resourceAvailability = _resourceAvailability.MarkBusy(resourceFlag);

        TableLogger.Log($"[COORD-PUSH] {msg.ResourceId} now busy with {msg.WaferId} - resources: {_resourceAvailability.ToReadableString()} [{_resourceAvailability.ToHexString()}]");

        // Track ownership
        _resourceOwnership[msg.ResourceId] = msg.WaferId;
    }

    private void HandleEvaluateScheduling()
    {
        // Synchronized scheduling: evaluate all wafers and find optimal matches

        foreach (var (waferId, (state, conditions)) in _waferStates.ToList())
        {
            // Skip completed wafers
            if (state == "completed")
                continue;

            TryScheduleWafer(waferId);
        }
    }

    private void TryScheduleWafer(string waferId)
    {
        if (!_waferStates.TryGetValue(waferId, out var stateInfo))
            return;

        // Skip if wafer already has a pending command
        if (_wafersWithPendingCommands.Contains(waferId))
        {
            TableLogger.Log($"[COORD-PUSH-NEW] {waferId} has pending command, skipping");
            return;
        }

        var (state, conditions) = stateInfo;

        // NEW ARCHITECTURE: Coordinator evaluates WHEN to proceed based on resources
        // Wafer decides HOW to proceed (which robot/equipment to command)
        bool canProceed = false;

        switch (state)
        {
            case "waiting_for_r1_pickup":
                // Need: R-1 available + can pick from carrier
                canProceed = _resourceAvailability.HasAny(ResourceAvailability.Robot1Free) &&
                             conditions.HasAll(GuardConditions.CanPickFromCarrier);
                break;

            case "r1_moving_to_platen":
                // Need: R-1 available + wafer on robot
                canProceed = _resourceAvailability.HasAny(ResourceAvailability.Robot1Free) &&
                             conditions.HasAll(GuardConditions.CanMoveToPlaten);
                break;

            case "waiting_platen_location":
                // Location permission handled in HandleRequestResourcePermission
                return; // Don't send ProceedToNextStep - wait for permission grant

            case "r1_placing_to_platen":
                canProceed = _resourceAvailability.HasAny(ResourceAvailability.Robot1Free) &&
                             conditions.HasAll(GuardConditions.CanPlaceOnPlaten);
                break;

            case "waiting_for_polisher":
                canProceed = _resourceAvailability.HasAny(ResourceAvailability.PlatenFree) &&
                             conditions.HasAll(GuardConditions.CanStartPolish);
                break;

            case "waiting_for_r2_pickup":
                canProceed = _resourceAvailability.HasAny(ResourceAvailability.Robot2Free) &&
                             conditions.HasAll(GuardConditions.CanPickFromPlaten);
                break;

            case "r2_moving_to_cleaner":
                canProceed = _resourceAvailability.HasAny(ResourceAvailability.Robot2Free) &&
                             conditions.HasAll(GuardConditions.CanMoveToCleaner);
                break;

            case "waiting_cleaner_location":
                // Location permission handled in HandleRequestResourcePermission
                return;

            case "r2_placing_to_cleaner":
                canProceed = _resourceAvailability.HasAny(ResourceAvailability.Robot2Free) &&
                             conditions.HasAll(GuardConditions.CanPlaceOnCleaner);
                break;

            case "waiting_for_cleaner":
                canProceed = _resourceAvailability.HasAny(ResourceAvailability.CleanerFree) &&
                             conditions.HasAll(GuardConditions.CanStartClean);
                break;

            case "waiting_for_r3_pickup":
                canProceed = _resourceAvailability.HasAny(ResourceAvailability.Robot3Free) &&
                             conditions.HasAll(GuardConditions.CanPickFromCleaner);
                break;

            case "r3_moving_to_buffer":
                canProceed = _resourceAvailability.HasAny(ResourceAvailability.Robot3Free) &&
                             conditions.HasAll(GuardConditions.CanMoveToBuffer);
                break;

            case "waiting_buffer_location":
                // Location permission handled in HandleRequestResourcePermission
                return;

            case "r3_placing_to_buffer":
                canProceed = _resourceAvailability.HasAny(ResourceAvailability.Robot3Free) &&
                             conditions.HasAll(GuardConditions.CanPlaceOnBuffer);
                break;

            case "waiting_for_buffer":
                canProceed = _resourceAvailability.HasAny(ResourceAvailability.BufferFree) &&
                             conditions.HasAll(GuardConditions.CanStartBuffer);
                break;

            case "waiting_for_r1_return":
                canProceed = _resourceAvailability.HasAny(ResourceAvailability.Robot1Free) &&
                             conditions.HasAll(GuardConditions.CanPickFromBuffer);
                break;

            case "r1_returning_to_carrier":
                canProceed = _resourceAvailability.HasAny(ResourceAvailability.Robot1Free) &&
                             conditions.HasAll(GuardConditions.CanReturnToCarrier);
                break;

            case "r1_placing_to_carrier":
                canProceed = _resourceAvailability.HasAny(ResourceAvailability.Robot1Free);
                break;

            case "completed":
                return;
        }

        // If coordinator determined wafer can proceed, tell wafer to proceed
        if (canProceed && _waferSchedulers.TryGetValue(waferId, out var waferActor))
        {
            TableLogger.Log($"[COORD-PUSH-NEW] ✓ Telling {waferId} to proceed from '{state}'");
            TableLogger.LogEvent("PUSH", "COORD", $"ProceedToNextStep:{state}", waferId);
            _wafersWithPendingCommands.Add(waferId);
            waferActor.Tell(new ProceedToNextStep(waferId));
        }
    }

    private bool TryAcquireLocation(string location, string waferId)
    {
        var queue = _locationQueues[location];

        // Check if already owned
        if (_resourceOwnership.ContainsKey(location))
        {
            var owner = _resourceOwnership[location];
            if (owner == waferId)
            {
                // Re-entrant request
                return true;
            }

            // Owned by another wafer - add to queue if not already there
            if (!queue.Contains(waferId))
            {
                queue.Enqueue(waferId);
                TableLogger.Log($"[COORD-PUSH] {waferId} queued for {location} (position {queue.Count}, owner: {owner})");
            }
            return false;
        }

        // Location is free
        if (queue.Count > 0)
        {
            // Check if this wafer is first in queue
            if (queue.Peek() == waferId)
            {
                queue.Dequeue();
                _resourceOwnership[location] = waferId;

                // Mark as busy
                var resourceFlag = ResourceAvailabilityExtensions.FromResourceName(location);
                _resourceAvailability = _resourceAvailability.MarkBusy(resourceFlag);

                TableLogger.Log($"[COORD-PUSH] ✓ {waferId} granted {location} (first in queue)");
                return true;
            }
            else
            {
                // Not first - add to queue
                if (!queue.Contains(waferId))
                {
                    queue.Enqueue(waferId);
                    TableLogger.Log($"[COORD-PUSH] {waferId} queued for {location} (position {queue.Count})");
                }
                return false;
            }
        }

        // No queue and available - grant immediately
        _resourceOwnership[location] = waferId;

        var flag = ResourceAvailabilityExtensions.FromResourceName(location);
        _resourceAvailability = _resourceAvailability.MarkBusy(flag);

        TableLogger.Log($"[COORD-PUSH] ✓ {waferId} granted {location}");
        return true;
    }

    private void HandleWaferCompleted(WaferCompleted msg)
    {
        _completedWafers++;
        TableLogger.Log($"[COORD-PUSH] Wafer {msg.WaferId} completed ({_completedWafers}/{TotalWafers})");

        _waferSchedulers.Remove(msg.WaferId);
        _waferStates.Remove(msg.WaferId);

        // Spawn next wafer
        if (_waferCounter < TotalWafers && _waferSchedulers.Count < MaxActiveWafers)
        {
            Self.Tell(new SpawnWafer($"W-{++_waferCounter:D3}"));
        }
        else if (_completedWafers >= TotalWafers)
        {
            TableLogger.Log($"[COORD-PUSH] All {TotalWafers} wafers completed!");
            _schedulingTimer?.Cancel();
            Context.System.Terminate();
        }
    }

    private void HandleWaferAtPlaten(WaferAtPlaten msg)
    {
        // Wafer reached platen - trigger next wafer spawn
        TableLogger.Log($"[COORD-PUSH] {msg.WaferId} reached platen - spawning next wafer");

        if (_waferCounter < TotalWafers && _waferSchedulers.Count < MaxActiveWafers)
        {
            Self.Tell(new SpawnWafer($"W-{++_waferCounter:D3}"));
        }
    }

    private void HandleRequestResourcePermission(RequestResourcePermission msg)
    {
        // Wafer requests location permission
        TableLogger.Log($"[COORD-PUSH] {msg.WaferId} requesting {msg.ResourceType}");

        if (TryAcquireLocation(msg.ResourceType, msg.WaferId))
        {
            // Grant permission
            if (_waferSchedulers.TryGetValue(msg.WaferId, out var waferActor))
            {
                waferActor.Tell(new ResourcePermissionGranted(msg.ResourceType, msg.WaferId));
            }
        }
    }

    private void HandleReleaseResource(ReleaseResource msg)
    {
        // Wafer releases location resource
        TableLogger.Log($"[COORD-PUSH] {msg.WaferId} releasing {msg.ResourceType}");

        if (_resourceOwnership.ContainsKey(msg.ResourceType))
        {
            _resourceOwnership.Remove(msg.ResourceType);

            // Mark as available
            var resourceFlag = ResourceAvailabilityExtensions.FromResourceName(msg.ResourceType);
            _resourceAvailability = _resourceAvailability.MarkAvailable(resourceFlag);

            // Check if anyone waiting in queue
            var queue = _locationQueues[msg.ResourceType];
            if (queue.Count > 0)
            {
                var nextWafer = queue.Dequeue();
                TableLogger.Log($"[COORD-PUSH] Granting {msg.ResourceType} to {nextWafer} from queue");

                _resourceOwnership[msg.ResourceType] = nextWafer;
                _resourceAvailability = _resourceAvailability.MarkBusy(resourceFlag);

                if (_waferSchedulers.TryGetValue(nextWafer, out var waferActor))
                {
                    waferActor.Tell(new ResourcePermissionGranted(msg.ResourceType, nextWafer));
                }
            }
        }
    }

    protected override void PostStop()
    {
        _schedulingTimer?.Cancel();
        base.PostStop();
    }
}
