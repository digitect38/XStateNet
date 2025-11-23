using Akka.Actor;

namespace CMPSimXS2.Parallel;

/// <summary>
/// Layer 3: Manages all robot and equipment resources
/// Handles requests from wafer schedulers for robot tasks
/// </summary>
public class RobotSchedulersActor : ReceiveActor
{
    private readonly string _robotsJson;
    private readonly IActorRef _coordinator;

    // Robot availability
    private bool _robot1Busy = false;
    private bool _robot2Busy = false;
    private bool _robot3Busy = false;

    // Equipment availability
    private bool _platenBusy = false;
    private bool _cleanerBusy = false;
    private bool _bufferBusy = false;

    // Priority queues for waiting requests (lower priority number = higher priority)
    // p1=4 (lowest), p2=3, p3=2, p4=1 (highest)
    private readonly PriorityQueue<RobotTask, int> _robot1Queue = new();
    private readonly PriorityQueue<RobotTask, int> _robot2Queue = new();
    private readonly PriorityQueue<RobotTask, int> _robot3Queue = new();

    // Task wrapper for priority queue
    private record RobotTask(IActorRef Requester, string Task, string WaferId, int Priority);

    // Track current wafer using each robot
    private string? _robot1CurrentWafer = null;
    private string? _robot2CurrentWafer = null;
    private string? _robot3CurrentWafer = null;

    // Track current wafer using each equipment
    private string? _platenCurrentWafer = null;
    private string? _cleanerCurrentWafer = null;
    private string? _bufferCurrentWafer = null;

    // NEW ARCHITECTURE: Track which wafer actor requested each task
    private IActorRef? _robot1Requester = null;
    private IActorRef? _robot2Requester = null;
    private IActorRef? _robot3Requester = null;
    private IActorRef? _platenRequester = null;
    private IActorRef? _cleanerRequester = null;
    private IActorRef? _bufferRequester = null;

    public RobotSchedulersActor(string robotsJson, IActorRef coordinator)
    {
        _robotsJson = robotsJson;
        _coordinator = coordinator;

        // PUSH MODEL: Coordinator commands execution
        Receive<ExecuteRobotTask>(msg => HandleExecuteRobotTask(msg));
        Receive<ExecuteEquipmentTask>(msg => HandleExecuteEquipmentTask(msg));

        // Robot 1 requests (Carrier ??Platen ??Buffer) - LEGACY PULL MODEL
        Receive<RequestRobot1>(msg => HandleRobot1Request(msg));
        Receive<Robot1TaskComplete>(msg => HandleRobot1Complete(msg));

        // Robot 2 requests (Platen ??Cleaner) - LEGACY PULL MODEL
        Receive<RequestRobot2>(msg => HandleRobot2Request(msg));
        Receive<Robot2TaskComplete>(msg => HandleRobot2Complete(msg));

        // Robot 3 requests (Cleaner ??Buffer) - LEGACY PULL MODEL
        Receive<RequestRobot3>(msg => HandleRobot3Request(msg));
        Receive<Robot3TaskComplete>(msg => HandleRobot3Complete(msg));

        // Equipment requests - LEGACY PULL MODEL
        Receive<RequestPolish>(msg => HandlePolishRequest(msg));
        Receive<RequestClean>(msg => HandleCleanRequest(msg));
        Receive<RequestBuffer>(msg => HandleBufferRequest(msg));

        // Equipment completion
        Receive<PlatenTaskComplete>(_ => HandlePlatenComplete());
        Receive<CleanerTaskComplete>(_ => HandleCleanerComplete());
        Receive<BufferTaskComplete>(_ => HandleBufferComplete());

        // Permission protocol responses - LEGACY PULL MODEL
        Receive<ResourcePermissionGranted>(msg => HandlePermissionGranted(msg));
        Receive<ResourcePermissionDenied>(msg => HandlePermissionDenied(msg));
        Receive<RetryPermissionRequest>(msg => HandleRetryPermissionRequest(msg));

        TableLogger.Log("[ROBOTS] Robot schedulers initialized");

        // Report initial status to coordinator
        TableLogger.LogEvent("INIT_STATUS", "ROBOTS", "R-1:READY,R-2:READY,R-3:READY", "SYSTEM");
        TableLogger.LogEvent("INIT_STATUS", "EQUIPMENT", "PLATEN:READY,CLEANER:READY,BUFFER:READY", "SYSTEM");

        // PUSH MODEL: Report all resources as available to coordinator
        _coordinator.Tell(new ResourceAvailable("R-1"));
        _coordinator.Tell(new ResourceAvailable("R-2"));
        _coordinator.Tell(new ResourceAvailable("R-3"));
        _coordinator.Tell(new ResourceAvailable("PLATEN"));
        _coordinator.Tell(new ResourceAvailable("CLEANER"));
        _coordinator.Tell(new ResourceAvailable("BUFFER"));
    }

    // Permission tracking
    private readonly Dictionary<string, (IActorRef requester, string task, string waferId, int priority, string resourceType)> _pendingPermissions = new();

    private void HandlePermissionGranted(ResourcePermissionGranted msg)
    {
        var key = $"{msg.ResourceType}:{msg.WaferId}";
        if (_pendingPermissions.TryGetValue(key, out var pending))
        {
            _pendingPermissions.Remove(key);

            // Log permission grant for robots and equipment
            TableLogger.LogEvent("PERMIT_RESOURCE", msg.ResourceType, "", msg.WaferId);

            // Permission granted - proceed with resource usage
            switch (pending.resourceType)
            {
                case "R-1":
                    ProcessRobot1Task(pending.requester, pending.task, pending.waferId, pending.priority);
                    break;
                case "R-2":
                    ProcessRobot2Task(pending.requester, pending.task, pending.waferId, pending.priority);
                    break;
                case "R-3":
                    ProcessRobot3Task(pending.requester, pending.task, pending.waferId, pending.priority);
                    break;
                case "PLATEN":
                    ProcessPlatenTask(pending.requester, pending.waferId);
                    break;
                case "CLEANER":
                    ProcessCleanerTask(pending.requester, pending.waferId);
                    break;
                case "BUFFER":
                    ProcessBufferTask(pending.requester, pending.waferId);
                    break;
            }
        }
    }

    private void HandlePermissionDenied(ResourcePermissionDenied msg)
    {
        var key = $"{msg.ResourceType}:{msg.WaferId}";
        if (_pendingPermissions.TryGetValue(key, out var pending))
        {
            // Log WAIT instead of DENY
            TableLogger.LogEvent("WAIT_RESOURCE", msg.ResourceType, msg.Reason, msg.WaferId);

            // Notify wafer scheduler to wait
            const int retryDelayMs = 50; // Agreed wait time
            TableLogger.LogEvent("NOTIFY_WAIT", msg.ResourceType, $"retry in {retryDelayMs}ms", msg.WaferId);

            // Schedule retry after agreed wait time
            Context.System.Scheduler.ScheduleTellOnce(
                TimeSpan.FromMilliseconds(retryDelayMs),
                Self,
                new RetryPermissionRequest(msg.ResourceType, pending.requester, pending.task, pending.waferId, pending.priority),
                ActorRefs.NoSender
            );

            // Remove from pending for now (will be re-added on retry)
            _pendingPermissions.Remove(key);
        }
    }

    // New message type for retry
    private record RetryPermissionRequest(string ResourceType, IActorRef Requester, string Task, string WaferId, int Priority);

    // Add receiver in constructor
    private void HandleRetryPermissionRequest(RetryPermissionRequest msg)
    {
        TableLogger.Log($"[{msg.ResourceType}] Retrying permission request for {msg.WaferId}");
        var key = $"{msg.ResourceType}:{msg.WaferId}";
        _pendingPermissions[key] = (msg.Requester, msg.Task, msg.WaferId, msg.Priority, msg.ResourceType);
        TableLogger.LogEvent("REQUEST_PERMISSION", msg.ResourceType, "", msg.WaferId);
        _coordinator.Tell(new RequestResourcePermission(msg.ResourceType, msg.WaferId));
    }

    // ===== PUSH MODEL: Coordinator Commands =====

    private void HandleExecuteRobotTask(ExecuteRobotTask msg)
    {
        Console.WriteLine($"DEBUG: RECEIVED ExecuteRobotTask for {msg.RobotId} {msg.Task} {msg.WaferId}");
        TableLogger.LogEvent("RECV_CMD", "ROBOTS", $"{msg.RobotId}:{msg.Task}:p{msg.Priority}", msg.WaferId);
        TableLogger.Log($"[{msg.RobotId}] ⚡ COMMAND received from wafer: {msg.Task} for {msg.WaferId} (p{msg.Priority})");

        // NEW ARCHITECTURE: Capture sender (wafer actor) to send TaskCompleted back
        var requester = Sender;

        switch (msg.RobotId)
        {
            case "R-1":
                ExecuteRobot1Task(msg.Task, msg.WaferId, msg.Priority, requester);
                break;
            case "R-2":
                ExecuteRobot2Task(msg.Task, msg.WaferId, msg.Priority, requester);
                break;
            case "R-3":
                ExecuteRobot3Task(msg.Task, msg.WaferId, msg.Priority, requester);
                break;
        }

        Console.WriteLine($"DEBUG: DISPATCHED ExecuteRobotTask to {msg.RobotId}");
        TableLogger.LogEvent("EXEC_CMD", msg.RobotId, $"Executing:{msg.Task}", msg.WaferId);
    }

    private void HandleExecuteEquipmentTask(ExecuteEquipmentTask msg)
    {
        TableLogger.Log($"[{msg.EquipmentId}] ⚡ COMMAND received from wafer: PROCESS {msg.WaferId}");

        // NEW ARCHITECTURE: Capture sender (wafer actor) to send TaskCompleted back
        var requester = Sender;

        switch (msg.EquipmentId)
        {
            case "PLATEN":
                ExecutePlatenTask(msg.WaferId, requester);
                break;
            case "CLEANER":
                ExecuteCleanerTask(msg.WaferId, requester);
                break;
            case "BUFFER":
                ExecuteBufferTask(msg.WaferId, requester);
                break;
        }
    }

    private void ExecuteRobot1Task(string task, string waferId, int priority, IActorRef requester)
    {
        _robot1Busy = true;
        _robot1CurrentWafer = waferId;
        _robot1Requester = requester; // Store requester to send TaskCompleted back

        // Map task to detailed action description
        string action = task switch
        {
            "pick" => priority == 4 ? "pick from carrier" : priority == 1 ? "pick from buffer" : "pick",
            "move" => priority == 4 ? "move to platen" : priority == 1 ? "move to carrier" : "move",
            "place" => priority == 4 ? "place on platen" : priority == 1 ? "place on carrier" : "place",
            _ => task
        };

        TableLogger.Log($"[R-1] Executing {GetStageName(priority)} task for {waferId}: {action}");
        TableLogger.LogEvent("R1_ACTION", "R-1", action, waferId, GetStageName(priority));

        // Simulate task execution time
        Context.System.Scheduler.ScheduleTellOnce(
            TimeSpan.FromMilliseconds(GetTaskDuration(task)),
            Self,
            new Robot1TaskComplete(task),
            ActorRefs.NoSender
        );
    }

    private void ExecuteRobot2Task(string task, string waferId, int priority, IActorRef requester)
    {
        _robot2Busy = true;
        _robot2CurrentWafer = waferId;
        _robot2Requester = requester; // Store requester to send TaskCompleted back

        // Map task to detailed action description
        string action = task switch
        {
            "pick" => priority == 3 ? "pick from platen" : "pick",
            "move" => priority == 3 ? "move to cleaner" : "move",
            "place" => priority == 3 ? "place on cleaner" : "place",
            _ => task
        };

        TableLogger.Log($"[R-2] Executing {GetStageName(priority)} task for {waferId}: {action}");
        TableLogger.LogEvent("R2_ACTION", "R-2", action, waferId, GetStageName(priority));

        Context.System.Scheduler.ScheduleTellOnce(
            TimeSpan.FromMilliseconds(GetTaskDuration(task)),
            Self,
            new Robot2TaskComplete(task),
            ActorRefs.NoSender
        );
    }

    private void ExecuteRobot3Task(string task, string waferId, int priority, IActorRef requester)
    {
        _robot3Busy = true;
        _robot3CurrentWafer = waferId;
        _robot3Requester = requester; // Store requester to send TaskCompleted back

        // Map task to detailed action description
        string action = task switch
        {
            "pick" => priority == 2 ? "pick from cleaner" : "pick",
            "move" => priority == 2 ? "move to buffer" : "move",
            "place" => priority == 2 ? "place on buffer" : "place",
            _ => task
        };

        TableLogger.Log($"[R-3] Executing {GetStageName(priority)} task for {waferId}: {action}");
        TableLogger.LogEvent("R3_ACTION", "R-3", action, waferId, GetStageName(priority));

        Context.System.Scheduler.ScheduleTellOnce(
            TimeSpan.FromMilliseconds(GetTaskDuration(task)),
            Self,
            new Robot3TaskComplete(task),
            ActorRefs.NoSender
        );
    }

    private void ExecutePlatenTask(string waferId, IActorRef requester)
    {
        _platenBusy = true;
        _platenCurrentWafer = waferId;
        _platenRequester = requester; // Store requester to send TaskCompleted back
        TableLogger.Log($"[PLATEN] Executing POLISH for {waferId}");
        TableLogger.LogEvent("POLISHING", "PLATEN", "", waferId);

        Context.System.Scheduler.ScheduleTellOnce(
            TimeSpan.FromMilliseconds(200),
            Self,
            new PlatenTaskComplete(),
            ActorRefs.NoSender
        );
    }

    private void ExecuteCleanerTask(string waferId, IActorRef requester)
    {
        _cleanerBusy = true;
        _cleanerCurrentWafer = waferId;
        _cleanerRequester = requester; // Store requester to send TaskCompleted back
        TableLogger.Log($"[CLEANER] Executing CLEAN for {waferId}");
        TableLogger.LogEvent("CLEANING", "CLEANER", "", waferId);

        Context.System.Scheduler.ScheduleTellOnce(
            TimeSpan.FromMilliseconds(150),
            Self,
            new CleanerTaskComplete(),
            ActorRefs.NoSender
        );
    }

    private void ExecuteBufferTask(string waferId, IActorRef requester)
    {
        _bufferBusy = true;
        _bufferCurrentWafer = waferId;
        _bufferRequester = requester; // Store requester to send TaskCompleted back
        TableLogger.Log($"[BUFFER] Executing BUFFER for {waferId}");
        TableLogger.LogEvent("BUFFERING", "BUFFER", "", waferId);

        Context.System.Scheduler.ScheduleTellOnce(
            TimeSpan.FromMilliseconds(100),
            Self,
            new BufferTaskComplete(),
            ActorRefs.NoSender
        );
    }

    private void HandleRobot1Request(RequestRobot1 msg)
    {
        if (_robot1Busy)
        {
            var task = new RobotTask(Sender, msg.Task, msg.WaferId, msg.Priority);
            _robot1Queue.Enqueue(task, msg.Priority);
            TableLogger.Log("[R-1] Busy, queued {GetStageName(msg.Priority)} task for {msg.WaferId}: {msg.Task} (queue: {_robot1Queue.Count})");
        }
        else
        {
            // Request permission from coordinator before using robot
            var key = $"R-1:{msg.WaferId}";
            _pendingPermissions[key] = (Sender, msg.Task, msg.WaferId, msg.Priority, "R-1");
            TableLogger.LogEvent("REQUEST_PERMISSION", "R-1", "", msg.WaferId);
            _coordinator.Tell(new RequestResourcePermission("R-1", msg.WaferId));
        }
    }

    private void ProcessRobot1Task(IActorRef requester, string task, string waferId, int priority)
    {
        _robot1Busy = true;
        _robot1CurrentWafer = waferId;

        // Map task to detailed action description
        string action = task switch
        {
            "pick" => priority == 4 ? "pick from carrier" : priority == 1 ? "pick from buffer" : "pick",
            "move" => priority == 4 ? "move to platen" : priority == 1 ? "move to carrier" : "move",
            "place" => priority == 4 ? "place on platen" : priority == 1 ? "place on carrier" : "place",
            _ => task
        };

        TableLogger.Log("[R-1] Starting {GetStageName(priority)} task for {waferId}: {action}");
        TableLogger.LogEvent("R1_ACTION", "R-1", action, waferId, GetStageName(priority));
        requester.Tell(new Robot1Available(task));

        // Simulate task execution time
        Context.System.Scheduler.ScheduleTellOnce(
            TimeSpan.FromMilliseconds(GetTaskDuration(task)),
            Self,
            new Robot1TaskComplete(task),
            ActorRefs.NoSender
        );
    }

    private void HandleRobot1Complete(Robot1TaskComplete msg)
    {
        TableLogger.Log($"[R-1] Task completed: {msg.Task}");

        // NEW ARCHITECTURE: Send TaskCompleted to wafer that requested the task
        if (_robot1CurrentWafer != null && _robot1Requester != null)
        {
            _robot1Requester.Tell(new TaskCompleted("R-1", _robot1CurrentWafer));
            TableLogger.LogEvent("FREE_ROBOT", "R-1", "", _robot1CurrentWafer);
            _robot1CurrentWafer = null;
            _robot1Requester = null;
        }

        // Process next queued task (priority queue automatically returns highest priority)
        // NOTE: In PUSH model, this queue should be empty as coordinator commands directly
        if (_robot1Queue.TryDequeue(out var robotTask, out var priority))
        {
            // PULL MODEL: Request permission for next wafer in queue
            var key = $"R-1:{robotTask.WaferId}";
            _pendingPermissions[key] = (robotTask.Requester, robotTask.Task, robotTask.WaferId, priority, "R-1");
            _coordinator.Tell(new RequestResourcePermission("R-1", robotTask.WaferId));
            TableLogger.Log($"[R-1] Processing queued {GetStageName(priority)} task for {robotTask.WaferId}: {robotTask.Task} (remaining queue: {_robot1Queue.Count})");
        }
        else
        {
            _robot1Busy = false;
            TableLogger.Log("[R-1] Now idle");
            // PUSH MODEL: Report available to coordinator
            _coordinator.Tell(new ResourceAvailable("R-1"));
        }
    }

    private void HandleRobot2Request(RequestRobot2 msg)
    {
        if (_robot2Busy)
        {
            var task = new RobotTask(Sender, msg.Task, msg.WaferId, msg.Priority);
            _robot2Queue.Enqueue(task, msg.Priority);
            TableLogger.Log("[R-2] Busy, queued {GetStageName(msg.Priority)} task for {msg.WaferId}: {msg.Task} (queue: {_robot2Queue.Count})");
        }
        else
        {
            // Request permission from coordinator before using robot
            var key = $"R-2:{msg.WaferId}";
            _pendingPermissions[key] = (Sender, msg.Task, msg.WaferId, msg.Priority, "R-2");
            TableLogger.LogEvent("REQUEST_PERMISSION", "R-2", "", msg.WaferId);
            _coordinator.Tell(new RequestResourcePermission("R-2", msg.WaferId));
        }
    }

    private void ProcessRobot2Task(IActorRef requester, string task, string waferId, int priority)
    {
        _robot2Busy = true;
        _robot2CurrentWafer = waferId;

        // Map task to detailed action description based on priority
        string action = task switch
        {
            "pick" => priority == 3 ? "pick from platen" : "pick",
            "move" => priority == 3 ? "move to cleaner" : "move",
            "place" => priority == 3 ? "place on cleaner" : "place",
            _ => task
        };

        TableLogger.Log("[R-2] Starting {GetStageName(priority)} task for {waferId}: {action}");
        TableLogger.LogEvent("R2_ACTION", "R-2", action, waferId, GetStageName(priority));
        requester.Tell(new Robot2Available(task));

        Context.System.Scheduler.ScheduleTellOnce(
            TimeSpan.FromMilliseconds(GetTaskDuration(task)),
            Self,
            new Robot2TaskComplete(task),
            ActorRefs.NoSender
        );
    }

    private void HandleRobot2Complete(Robot2TaskComplete msg)
    {
        TableLogger.Log("[R-2] Task completed: {msg.Task}");

        // NEW ARCHITECTURE: Send TaskCompleted to wafer that requested the task
        if (_robot2CurrentWafer != null && _robot2Requester != null)
        {
            _robot2Requester.Tell(new TaskCompleted("R-2", _robot2CurrentWafer));
            TableLogger.LogEvent("FREE_ROBOT", "R-2", "", _robot2CurrentWafer);
            _robot2CurrentWafer = null;
            _robot2Requester = null;
        }

        if (_robot2Queue.TryDequeue(out var robotTask, out var priority))
        {
            // Request permission for next wafer in queue
            var key = $"R-2:{robotTask.WaferId}";
            _pendingPermissions[key] = (robotTask.Requester, robotTask.Task, robotTask.WaferId, priority, "R-2");
            _coordinator.Tell(new RequestResourcePermission("R-2", robotTask.WaferId));
            TableLogger.Log("[R-2] Processing queued {GetStageName(priority)} task for {robotTask.WaferId}: {robotTask.Task} (remaining queue: {_robot2Queue.Count})");
        }
        else
        {
            _robot2Busy = false;
            TableLogger.Log("[R-2] Now idle");
            // PUSH MODEL: Report available to coordinator
            _coordinator.Tell(new ResourceAvailable("R-2"));
        }
    }

    private void HandleRobot3Request(RequestRobot3 msg)
    {
        if (_robot3Busy)
        {
            var task = new RobotTask(Sender, msg.Task, msg.WaferId, msg.Priority);
            _robot3Queue.Enqueue(task, msg.Priority);
            TableLogger.Log("[R-3] Busy, queued {GetStageName(msg.Priority)} task for {msg.WaferId}: {msg.Task} (queue: {_robot3Queue.Count})");
        }
        else
        {
            // Request permission from coordinator before using robot
            var key = $"R-3:{msg.WaferId}";
            _pendingPermissions[key] = (Sender, msg.Task, msg.WaferId, msg.Priority, "R-3");
            TableLogger.LogEvent("REQUEST_PERMISSION", "R-3", "", msg.WaferId);
            _coordinator.Tell(new RequestResourcePermission("R-3", msg.WaferId));
        }
    }

    private void ProcessRobot3Task(IActorRef requester, string task, string waferId, int priority)
    {
        _robot3Busy = true;
        _robot3CurrentWafer = waferId;

        // Map task to detailed action description based on priority
        string action = task switch
        {
            "pick" => priority == 2 ? "pick from cleaner" : "pick",
            "move" => priority == 2 ? "move to buffer" : "move",
            "place" => priority == 2 ? "place on buffer" : "place",
            _ => task
        };

        TableLogger.Log("[R-3] Starting {GetStageName(priority)} task for {waferId}: {action}");
        TableLogger.LogEvent("R3_ACTION", "R-3", action, waferId, GetStageName(priority));
        requester.Tell(new Robot3Available(task));

        Context.System.Scheduler.ScheduleTellOnce(
            TimeSpan.FromMilliseconds(GetTaskDuration(task)),
            Self,
            new Robot3TaskComplete(task),
            ActorRefs.NoSender
        );
    }

    private void HandleRobot3Complete(Robot3TaskComplete msg)
    {
        TableLogger.Log("[R-3] Task completed: {msg.Task}");

        // NEW ARCHITECTURE: Send TaskCompleted to wafer that requested the task
        if (_robot3CurrentWafer != null && _robot3Requester != null)
        {
            _robot3Requester.Tell(new TaskCompleted("R-3", _robot3CurrentWafer));
            TableLogger.LogEvent("FREE_ROBOT", "R-3", "", _robot3CurrentWafer);
            _robot3CurrentWafer = null;
            _robot3Requester = null;
        }

        if (_robot3Queue.TryDequeue(out var robotTask, out var priority))
        {
            // Request permission for next wafer in queue
            var key = $"R-3:{robotTask.WaferId}";
            _pendingPermissions[key] = (robotTask.Requester, robotTask.Task, robotTask.WaferId, priority, "R-3");
            _coordinator.Tell(new RequestResourcePermission("R-3", robotTask.WaferId));
            TableLogger.Log("[R-3] Processing queued {GetStageName(priority)} task for {robotTask.WaferId}: {robotTask.Task} (remaining queue: {_robot3Queue.Count})");
        }
        else
        {
            _robot3Busy = false;
            TableLogger.Log("[R-3] Now idle");
            // PUSH MODEL: Report available to coordinator
            _coordinator.Tell(new ResourceAvailable("R-3"));
        }
    }

    private void HandlePolishRequest(RequestPolish msg)
    {
        if (_platenBusy)
        {
            TableLogger.Log($"[PLATEN] Busy with another wafer, request from {msg.WaferId} ignored!");
            return;
        }

        // Request permission from coordinator before using equipment
        var key = $"PLATEN:{msg.WaferId}";
        _pendingPermissions[key] = (Sender, "", msg.WaferId, 0, "PLATEN");
        _coordinator.Tell(new RequestResourcePermission("PLATEN", msg.WaferId));
    }

    private void ProcessPlatenTask(IActorRef requester, string waferId)
    {
        _platenBusy = true;
        _platenCurrentWafer = waferId;
        TableLogger.Log($"[PLATEN] Polishing wafer {waferId}");
        TableLogger.LogEvent("POLISHING", "PLATEN", "", waferId);
        Context.System.Scheduler.ScheduleTellOnce(
            TimeSpan.FromMilliseconds(200),
            requester,
            new PolishCompleted(waferId),
            ActorRefs.NoSender
        );
        Context.System.Scheduler.ScheduleTellOnce(
            TimeSpan.FromMilliseconds(200),
            Self,
            new PlatenTaskComplete(),
            ActorRefs.NoSender
        );
    }

    private void HandleCleanRequest(RequestClean msg)
    {
        if (_cleanerBusy)
        {
            TableLogger.Log($"[CLEANER] Busy with another wafer, request from {msg.WaferId} ignored!");
            return;
        }

        // Request permission from coordinator before using equipment
        var key = $"CLEANER:{msg.WaferId}";
        _pendingPermissions[key] = (Sender, "", msg.WaferId, 0, "CLEANER");
        _coordinator.Tell(new RequestResourcePermission("CLEANER", msg.WaferId));
    }

    private void ProcessCleanerTask(IActorRef requester, string waferId)
    {
        _cleanerBusy = true;
        _cleanerCurrentWafer = waferId;
        TableLogger.Log($"[CLEANER] Cleaning wafer {waferId}");
        TableLogger.LogEvent("CLEANING", "CLEANER", "", waferId);
        Context.System.Scheduler.ScheduleTellOnce(
            TimeSpan.FromMilliseconds(150),
            requester,
            new CleanCompleted(waferId),
            ActorRefs.NoSender
        );
        Context.System.Scheduler.ScheduleTellOnce(
            TimeSpan.FromMilliseconds(150),
            Self,
            new CleanerTaskComplete(),
            ActorRefs.NoSender
        );
    }

    private void HandleBufferRequest(RequestBuffer msg)
    {
        if (_bufferBusy)
        {
            TableLogger.Log($"[BUFFER] Busy with another wafer, request from {msg.WaferId} ignored!");
            return;
        }

        // Request permission from coordinator before using equipment
        var key = $"BUFFER:{msg.WaferId}";
        _pendingPermissions[key] = (Sender, "", msg.WaferId, 0, "BUFFER");
        _coordinator.Tell(new RequestResourcePermission("BUFFER", msg.WaferId));
    }

    private void ProcessBufferTask(IActorRef requester, string waferId)
    {
        _bufferBusy = true;
        _bufferCurrentWafer = waferId;
        TableLogger.Log($"[BUFFER] Buffering wafer {waferId}");
        TableLogger.LogEvent("BUFFERING", "BUFFER", "", waferId);
        Context.System.Scheduler.ScheduleTellOnce(
            TimeSpan.FromMilliseconds(100),
            requester,
            new BufferCompleted(waferId),
            ActorRefs.NoSender
        );
        Context.System.Scheduler.ScheduleTellOnce(
            TimeSpan.FromMilliseconds(100),
            Self,
            new BufferTaskComplete(),
            ActorRefs.NoSender
        );
    }

    private void HandlePlatenComplete()
    {
        // NEW ARCHITECTURE: Send TaskCompleted to wafer that requested the task
        if (_platenCurrentWafer != null && _platenRequester != null)
        {
            _platenRequester.Tell(new TaskCompleted("PLATEN", _platenCurrentWafer));
            _platenCurrentWafer = null;
            _platenRequester = null;
        }

        _platenBusy = false;
        TableLogger.Log("[PLATEN] Now idle");
        // PUSH MODEL: Report available to coordinator
        _coordinator.Tell(new ResourceAvailable("PLATEN"));
    }

    private void HandleCleanerComplete()
    {
        // NEW ARCHITECTURE: Send TaskCompleted to wafer that requested the task
        if (_cleanerCurrentWafer != null && _cleanerRequester != null)
        {
            _cleanerRequester.Tell(new TaskCompleted("CLEANER", _cleanerCurrentWafer));
            _cleanerCurrentWafer = null;
            _cleanerRequester = null;
        }

        _cleanerBusy = false;
        TableLogger.Log("[CLEANER] Now idle");
        // PUSH MODEL: Report available to coordinator
        _coordinator.Tell(new ResourceAvailable("CLEANER"));
    }

    private void HandleBufferComplete()
    {
        // NEW ARCHITECTURE: Send TaskCompleted to wafer that requested the task
        if (_bufferCurrentWafer != null && _bufferRequester != null)
        {
            _bufferRequester.Tell(new TaskCompleted("BUFFER", _bufferCurrentWafer));
            _bufferCurrentWafer = null;
            _bufferRequester = null;
        }

        _bufferBusy = false;
        TableLogger.Log("[BUFFER] Now idle");
        // PUSH MODEL: Report available to coordinator
        _coordinator.Tell(new ResourceAvailable("BUFFER"));
    }

    private int GetTaskDuration(string task)
    {
        return task switch
        {
            "pick" => 30,
            "place" => 30,
            "move" => 50,
            _ => 50
        };
    }

    private string GetStageName(int priority)
    {
        return priority switch
        {
            1 => "p4", // Buffer ??Carrier (highest priority)
            2 => "p3", // Cleaner ??Buffer
            3 => "p2", // Platen ??Cleaner
            4 => "p1", // Carrier ??Platen (lowest priority)
            _ => $"p?({priority})"
        };
    }
}

// Robot 1 messages (p1: Carrier?�Platen priority=4, p4: Buffer?�Carrier priority=1)
public record RequestRobot1(string Task, string WaferId, int Priority);
public record Robot1Available(string Task);
public record Robot1TaskComplete(string Task);

// Robot 2 messages (p2: Platen?�Cleaner priority=3)
public record RequestRobot2(string Task, string WaferId, int Priority);
public record Robot2Available(string Task);
public record Robot2TaskComplete(string Task);

// Robot 3 messages (p3: Cleaner?�Buffer priority=2)
public record RequestRobot3(string Task, string WaferId, int Priority);
public record Robot3Available(string Task);
public record Robot3TaskComplete(string Task);

// Equipment messages
public record RequestPolish(string WaferId);
public record PolishCompleted(string WaferId);
public record PlatenTaskComplete();

public record RequestClean(string WaferId);
public record CleanCompleted(string WaferId);
public record CleanerTaskComplete();

public record RequestBuffer(string WaferId);
public record BufferCompleted(string WaferId);
public record BufferTaskComplete();
