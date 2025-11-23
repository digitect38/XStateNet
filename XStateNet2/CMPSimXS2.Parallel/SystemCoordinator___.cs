using Akka.Actor;

namespace CMPSimXS2.Parallel;

/// <summary>
/// Top-level coordinator that manages the 3-layer architecture:
/// Layer 1: Master Scheduler (manages system state)
/// Layer 2: Wafer Schedulers (one per wafer)
/// Layer 3: Robot Schedulers (shared resources)
/// </summary>
public class SystemCoordinator : ReceiveActor
{
    private readonly string _masterJson;
    private readonly string _waferJson;
    private readonly string _robotsJson;

    private IActorRef? _robotSchedulers;
    private IActorRef? _masterScheduler;
    private readonly Dictionary<string, IActorRef> _waferSchedulers = new();

    private int _waferCounter = 0;
    private int _completedWafers = 0;
    private const int TotalWafers = 25;
    private const int MaxActiveWafers = 3;

    // Resource allocation tracking for collision prevention (One-to-One Rule)
    private readonly Dictionary<string, string> _resourceOwnership = new(); // resource -> waferId
    private readonly HashSet<string> _allResources = new()
    {
        "R-1", "R-2", "R-3",                              // Robots
        "PLATEN", "CLEANER", "BUFFER",                    // Equipment (processing)
        "PLATEN_LOCATION", "CLEANER_LOCATION", "BUFFER_LOCATION"  // Physical locations (storage)
    };

    // FIFO queues for location permission requests to maintain wafer order
    private readonly Dictionary<string, Queue<(IActorRef requester, string waferId)>> _locationQueues = new()
    {
        { "PLATEN_LOCATION", new Queue<(IActorRef, string)>() },
        { "CLEANER_LOCATION", new Queue<(IActorRef, string)>() },
        { "BUFFER_LOCATION", new Queue<(IActorRef, string)>() }
    };

    public SystemCoordinator(string masterJson, string waferJson, string robotsJson)
    {
        _masterJson = masterJson;
        _waferJson = waferJson;
        _robotsJson = robotsJson;

        Receive<StartSystem>(_ => HandleStartSystem());
        Receive<SpawnWafer>(msg => HandleSpawnWafer(msg));
        Receive<SpawnNextWafer>(_ => HandleSpawnNextWafer());
        Receive<WaferCompleted>(msg => HandleWaferCompleted(msg));
        Receive<WaferFailed>(msg => HandleWaferFailed(msg));
        Receive<WaferAtPlaten>(msg => HandleWaferAtPlaten(msg));

        // Resource permission protocol
        Receive<RequestResourcePermission>(msg => HandleResourcePermissionRequest(msg));
        Receive<ReleaseResource>(msg => HandleResourceRelease(msg));
    }

    private void HandleStartSystem()
    {
        TableLogger.Log("[COORD] Starting 3-layer CMP system");

        // Layer 3: Start robot schedulers (shared resources)
        _robotSchedulers = Context.ActorOf(
            Props.Create(() => new RobotSchedulersActor(_robotsJson, Self)),
            "robot-schedulers"
        );

        TableLogger.Log("[COORD] Layer 3: Robot schedulers started");

        // Layer 1: Start master scheduler
        _masterScheduler = Context.ActorOf(
            Props.Create(() => new MasterSchedulerActor(_masterJson, Self)),
            "master-scheduler"
        );

        TableLogger.Log("[COORD] Layer 1: Master scheduler started");

        // Layer 2: Spawn first batch of wafer schedulers
        SpawnInitialWafers();
    }

    private void SpawnInitialWafers()
    {
        // Pipeline mode: Spawn only the first wafer
        // Next wafers spawn when previous ones reach platen (pipeline trigger)
        if (_waferCounter < TotalWafers)
        {
            Self.Tell(new SpawnWafer($"W-{++_waferCounter:D3}"));
        }
    }

    private void HandleSpawnWafer(SpawnWafer msg)
    {
        if (_waferSchedulers.ContainsKey(msg.WaferId))
        {
            TableLogger.Log("[COORD] Wafer {msg.WaferId} already exists");
            return;
        }

        TableLogger.Log("[COORD] Layer 2: Spawning wafer scheduler for {msg.WaferId} (active: {_waferSchedulers.Count}/{MaxActiveWafers})");
        TableLogger.LogEvent("SPAWN", "", "", msg.WaferId);

        var waferScheduler = Context.ActorOf(
            Props.Create(() => new WaferSchedulerActor(msg.WaferId, _waferJson, _robotSchedulers!, Self)),
            $"wafer-{msg.WaferId}"
        );

        _waferSchedulers[msg.WaferId] = waferScheduler;

        // After first wafer spawned and reported ready, coordinator confirms all systems ready
        if (_waferCounter == 1)
        {
            // Coordinator broadcasts: All systems are ready to begin processing
            TableLogger.LogEvent("SYSTEM_READY", "COORD", "ALL SYSTEMS READY", "SYSTEM");
        }

        // Start the wafer processing
        waferScheduler.Tell(new StartWaferProcessing());

        // In pipeline mode, don't spawn additional wafers here
        // They will be spawned when previous wafer reaches platen
    }

    private void HandleSpawnNextWafer()
    {
        // This is triggered by WaferAtPlaten event for pipeline spacing
        if (_waferSchedulers.Count < MaxActiveWafers && _waferCounter < TotalWafers)
        {
            Self.Tell(new SpawnWafer($"W-{++_waferCounter:D3}"));
        }
    }

    private void HandleWaferCompleted(WaferCompleted msg)
    {
        _completedWafers++;
        TableLogger.Log("[COORD] Wafer {msg.WaferId} completed ({_completedWafers}/{TotalWafers})");

        // Remove stopped actor from active wafers (actor has already stopped itself)
        if (_waferSchedulers.Remove(msg.WaferId))
        {
            TableLogger.Log("[COORD] Wafer scheduler for {msg.WaferId} terminated (active: {_waferSchedulers.Count}/{MaxActiveWafers})");
        }

        // Spawn next wafer if more to process AND we're under max active limit
        if (_waferCounter < TotalWafers && _waferSchedulers.Count < MaxActiveWafers)
        {
            Self.Tell(new SpawnWafer($"W-{++_waferCounter:D3}"));
        }
        else if (_completedWafers >= TotalWafers)
        {
            TableLogger.Log("[COORD] All {TotalWafers} wafers completed!");
            Context.System.Terminate();
        }
    }

    private void HandleWaferFailed(WaferFailed msg)
    {
        TableLogger.Log("[COORD] Wafer {msg.WaferId} failed: {msg.Reason}");
        _waferSchedulers.Remove(msg.WaferId);

        // Could implement retry logic here
    }

    private void HandleWaferAtPlaten(WaferAtPlaten msg)
    {
        TableLogger.Log("[COORD] Wafer {msg.WaferId} at platen - pipeline trigger (active: {_waferSchedulers.Count}/{MaxActiveWafers})");

        // Pipeline mode: Spawn next wafer when current wafer reaches platen
        // This creates pipeline spacing while respecting max active limit
        if (_waferSchedulers.Count < MaxActiveWafers && _waferCounter < TotalWafers)
        {
            // Use SpawnNextWafer message for consistency
            Self.Tell(new SpawnNextWafer());
        }
    }

    // ===== COLLISION PREVENTION: One-to-One Rule Checker =====

    private void HandleResourcePermissionRequest(RequestResourcePermission msg)
    {
        var resource = msg.ResourceType;
        var waferId = msg.WaferId;

        // Validate resource exists
        if (!_allResources.Contains(resource))
        {
            Console.WriteLine($"[COLLISION-CHECK] ❌ INVALID RESOURCE: {resource} requested by {waferId}");
            Sender.Tell(new ResourcePermissionDenied(resource, waferId, "Invalid resource"));
            return;
        }

        // Check if this is a location resource that needs FIFO ordering
        if (_locationQueues.ContainsKey(resource))
        {
            HandleLocationPermissionRequest(resource, waferId, Sender);
            return;
        }

        // For non-location resources (robots, equipment), use immediate grant/deny
        // Check if resource is already allocated (One-to-One Rule)
        if (_resourceOwnership.ContainsKey(resource))
        {
            var currentOwner = _resourceOwnership[resource];
            if (currentOwner != waferId)
            {
                // COLLISION DETECTED: Resource is owned by another wafer
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[COLLISION-CHECK] ❌ COLLISION PREVENTED: {resource} requested by {waferId} but owned by {currentOwner}");
                Console.ResetColor();
                Sender.Tell(new ResourcePermissionDenied(resource, waferId, $"Resource owned by {currentOwner}"));
                return;
            }
            else
            {
                // Same wafer requesting again - this is allowed (re-entrant)
                // Don't log re-grants to reduce noise
                Sender.Tell(new ResourcePermissionGranted(resource, waferId));
                return;
            }
        }

        // Resource is available - grant permission
        _resourceOwnership[resource] = waferId;

        Sender.Tell(new ResourcePermissionGranted(resource, waferId));
    }

    private void HandleLocationPermissionRequest(string location, string waferId, IActorRef requester)
    {
        var queue = _locationQueues[location];

        // Check if resource is currently owned
        if (_resourceOwnership.ContainsKey(location))
        {
            var currentOwner = _resourceOwnership[location];
            if (currentOwner == waferId)
            {
                // Same wafer requesting again - grant immediately
                requester.Tell(new ResourcePermissionGranted(location, waferId));
                return;
            }

            // Resource is busy - add to queue if not already in queue
            if (!queue.Any(entry => entry.waferId == waferId))
            {
                queue.Enqueue((requester, waferId));
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[ORDER-CHECK] {waferId} queued for {location} (position {queue.Count}, owner: {currentOwner})");
                Console.ResetColor();
            }

            // Deny for now - will be granted when it's this wafer's turn
            requester.Tell(new ResourcePermissionDenied(location, waferId, $"Queued (position {queue.Count})"));
            return;
        }

        // Resource is available
        // Check if there's a queue - grant only to the first in queue
        if (queue.Count > 0)
        {
            var (firstRequester, firstWaferId) = queue.Peek();
            if (firstWaferId == waferId)
            {
                // This is the first in queue - grant permission
                queue.Dequeue();
                _resourceOwnership[location] = waferId;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[ORDER-CHECK] ✓ {waferId} granted {location} (was first in queue)");
                Console.ResetColor();
                requester.Tell(new ResourcePermissionGranted(location, waferId));
            }
            else
            {
                // Not first in queue - add to queue if not already there
                if (!queue.Any(entry => entry.waferId == waferId))
                {
                    queue.Enqueue((requester, waferId));
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[ORDER-CHECK] {waferId} queued for {location} (position {queue.Count}, waiting for {firstWaferId})");
                    Console.ResetColor();
                }
                requester.Tell(new ResourcePermissionDenied(location, waferId, $"Queued (position {queue.Count})"));
            }
        }
        else
        {
            // No queue and available - grant immediately
            _resourceOwnership[location] = waferId;
            requester.Tell(new ResourcePermissionGranted(location, waferId));
        }
    }

    private void HandleResourceRelease(ReleaseResource msg)
    {
        var resource = msg.ResourceType;
        var waferId = msg.WaferId;

        if (_resourceOwnership.TryGetValue(resource, out var owner))
        {
            if (owner == waferId)
            {
                _resourceOwnership.Remove(resource);

                // If this is a location resource with a queue, grant to next in line
                if (_locationQueues.TryGetValue(resource, out var queue) && queue.Count > 0)
                {
                    var (nextRequester, nextWaferId) = queue.Dequeue();
                    _resourceOwnership[resource] = nextWaferId;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[ORDER-CHECK] ✓ {nextWaferId} auto-granted {resource} (next in queue after {waferId})");
                    Console.ResetColor();
                    nextRequester.Tell(new ResourcePermissionGranted(resource, nextWaferId));
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[COLLISION-CHECK] ⚠️  WARNING: {waferId} tried to release {resource} owned by {owner}");
                Console.ResetColor();
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[COLLISION-CHECK] ⚠️  WARNING: {waferId} tried to release unallocated {resource}");
            Console.ResetColor();
        }
    }
}

public record SpawnNextWafer();
