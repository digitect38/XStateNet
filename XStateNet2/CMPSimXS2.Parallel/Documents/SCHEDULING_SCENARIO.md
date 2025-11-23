# CMP Parallel Scheduling Scenario

## Overview

This document explains the 3-layer parallel scheduling architecture for the CMP (Chemical Mechanical Planarization) wafer processing system. The architecture enables **true parallel pipelining** where multiple wafers are processed simultaneously at different stages of the production line.

**Key Features**:
- **SYSTEM_READY Protocol**: Mutual knowledge initialization handshake
- **8-Column Event Logging**: Complete visibility of all communications
- **Guard Conditions with Bitmasking**: Efficient state transition validation
- **WAIT/Retry Mechanism**: Cooperative resource waiting with 50ms agreed delay
- **One-to-One Resource Rule**: Collision prevention with FIFO queuing
- **Priority-Based Scheduling**: p4 > p3 > p2 > p1 for optimal throughput

---

## System Architecture

### 3-Layer Hierarchy with SystemCoordinator

```
┌─────────────────────────────────────────────────────────┐
│  Layer 1: SystemCoordinator                             │
│  - Resource allocation (One-to-One Rule)                │
│  - FIFO queue management for locations                  │
│  - Collision prevention and detection                   │
│  - Wafer lifecycle management                           │
│  - SYSTEM_READY broadcast confirmation                  │
│  - Max concurrent wafers enforcement (3)                │
│  - Pipeline trigger (spawn on platen arrival)           │
└─────────────────┬───────────────────────────────────────┘
                  │
        ┌─────────┴─────────┬─────────────────────────────┐
        │                   │                             │
┌───────▼────────┐  ┌──────▼──────────┐  ┌──────▼───────────────┐
│ Layer 2:       │  │ Layer 3:        │  │ Subsystems:          │
│ Wafer          │  │ Robot           │  │ - MasterScheduler    │
│ Schedulers     │◄─┤ Schedulers      │  │ - Platen (POLISHER)  │
│ (One per wafer)│  │ (Shared)        │  │ - Cleaner            │
│                │  │                 │  │ - Buffer             │
│ W-001          │  │ - R-1 (p1+p4)   │  │                      │
│ W-002          │  │ - R-2 (p2)      │  │                      │
│ W-003          │  │ - R-3 (p3)      │  │                      │
│ ...            │  │                 │  │                      │
└────────────────┘  └─────────────────┘  └──────────────────────┘
```

### Layer Responsibilities

#### Layer 1: SystemCoordinator (Resource Manager)
- **Purpose**: Central orchestration and resource safety
- **Key Responsibilities**:
  - **One-to-One Rule Enforcement**: Ensures each resource owned by max one wafer
  - **FIFO Queue Management**: Maintains fair ordering for location resources
  - **Collision Detection**: Prevents resource conflicts between wafers
  - **SYSTEM_READY Broadcast**: Confirms all subsystems ready before processing
  - **Wafer Spawning**: Pipeline-based spawning (on platen arrival)
  - **Lifecycle Tracking**: Monitors active/completed wafers
- **Resources Managed**:
  - Robots: R-1, R-2, R-3
  - Equipment: PLATEN, CLEANER, BUFFER
  - Locations: PLATEN_LOCATION, CLEANER_LOCATION, BUFFER_LOCATION
- **Actor Lifetime**: Exists for entire simulation

#### Layer 2: WaferSchedulerActor (Individual Wafer FSM)
- **Purpose**: Individual wafer lifecycle management
- **Key Responsibilities**:
  - **State Machine Execution**: 22-state wafer journey
  - **Guard Condition Evaluation**: Bitmasking for efficient multi-condition checks
  - **Robot/Equipment Requests**: Layer 2 → Layer 3 communication
  - **Station-to-Station Orchestration**: Complete carrier → carrier flow
  - **Permission Management**: Request and release resources via coordinator
- **State Flow**: created → readyToStart → waitingForR1Pickup → ... → completed
- **Guard Conditions**: 0x000000 - 0x3FFFFF (22 flags + 7 combinations)
- **Actor Lifetime**: Created on spawn, terminated on completion
- **Concurrency**: Up to `MaxActiveWafers` (default: 3) active simultaneously

#### Layer 3: RobotSchedulersActor (Shared Resource Pool)
- **Purpose**: Shared resource management and execution
- **Key Responsibilities**:
  - **Robot Task Execution**: Pick, place, move operations
  - **Equipment Operation**: Polishing, cleaning, buffering
  - **Priority Queue Management**: p4 > p3 > p2 > p1 ordering
  - **Permission Requests**: Layer 3 → Layer 1 coordination
  - **WAIT/Retry Handling**: 50ms automatic retry on resource busy
  - **Status Reporting**: INIT_STATUS on startup
- **Robot Assignments**:
  - R-1: p1 (Carrier → Platen) + p4 (Buffer → Carrier)
  - R-2: p2 (Platen → Cleaner)
  - R-3: p3 (Cleaner → Buffer)
- **Actor Lifetime**: Exists for entire simulation

---

## System Initialization Protocol ⭐

### SYSTEM_READY Handshake (Steps 1-4)

Before any wafer processing begins, all subsystems must report ready and the coordinator must broadcast confirmation:

```
Step      COORD                              R1_FWD    POLISHER  R2  CLEANER  R3  BUFFER  R1_RET
--------------------------------------------------------------------------------------------------
Step 1    [ ROBOTS -> COORD ] R-1:READY,R-2:READY,R-3:READY
          └─ RobotSchedulersActor reports all robots initialized

Step 2    [ EQUIPMENT -> COORD ] PLATEN:READY,CLEANER:READY,BUFFER:READY
          └─ RobotSchedulersActor reports all equipment initialized

Step 3    [ WSCH-001 -> COORD ] READY
          └─ First WaferSchedulerActor reports initialized

Step 4    [ COORD -> ALL ] ALL SYSTEMS READY
          └─ SystemCoordinator broadcasts confirmation
          └─ ⭐ Everyone now knows everyone is ready (mutual knowledge)

Step 5+   (Wafer processing begins)
```

**Protocol Rules**:
1. **ROBOTS report ready** (Step 1) - RobotSchedulersActor constructor
2. **EQUIPMENT reports ready** (Step 2) - RobotSchedulersActor constructor
3. **WSCH-001 reports ready** (Step 3) - WaferSchedulerActor constructor
4. **COORD broadcasts SYSTEM_READY** (Step 4) - After `_waferCounter == 1`
5. **Processing begins** (Step 5+) - After confirmation received

**Code Implementation**:
```csharp
// RobotSchedulersActor constructor
TableLogger.LogEvent("INIT_STATUS", "ROBOTS", "R-1:READY,R-2:READY,R-3:READY", "SYSTEM");
TableLogger.LogEvent("INIT_STATUS", "EQUIPMENT", "PLATEN:READY,CLEANER:READY,BUFFER:READY", "SYSTEM");

// WaferSchedulerActor constructor
var waferSchId = _waferId.Replace("W-", "WSCH-");
TableLogger.LogEvent("INIT_STATUS", waferSchId, "READY", _waferId);

// SystemCoordinator.HandleSpawnWafer()
if (_waferCounter == 1)
{
    TableLogger.LogEvent("SYSTEM_READY", "COORD", "ALL SYSTEMS READY", "SYSTEM");
}
```

**Why This Matters**:
- Ensures all actors are initialized before processing
- Provides mutual knowledge (everyone knows everyone is ready)
- Prevents race conditions on startup
- Creates clear system state boundary

---

## 8-Column Event Logging Architecture

### Physical Layout Visualization

The system logs all communications using an 8-column layout representing the physical CMP system:

| Column | Station | Purpose | Example Events |
|--------|---------|---------|----------------|
| **COORD** | Coordinator | Non-positional communications | PERMIT, WAIT, FREE, SYSTEM_READY |
| **R1_FWD** | Robot1 Forward | R1 moving carrier → platen (p1, p4) | REQUEST_ROBOT_p1, pick from carrier |
| **POLISHER** | Platen Station | Polishing operations | REQUEST_POLISH, POLISHING, POLISH_COMPLETE |
| **R2** | Robot2 | R2 moving platen → cleaner (p2) | REQUEST_ROBOT_p2, pick from platen |
| **CLEANER** | Cleaner Station | Cleaning operations | REQUEST_CLEAN, CLEANING, CLEAN_COMPLETE |
| **R3** | Robot3 | R3 moving cleaner → buffer (p3) | REQUEST_ROBOT_p3, pick from cleaner |
| **BUFFER** | Buffer Station | Buffering operations | REQUEST_BUFFER, BUFFERING, BUFFER_COMPLETE |
| **R1_RET** | Robot1 Return | R1 returning buffer → carrier | REQUEST_ROBOT_p4, pick from buffer |

### Column Headers (Printed Once)

At initialization, the system prints column headers followed by a separator line:

```csharp
public static void Initialize()
{
    // ... clear state ...
    PrintColumnHeader();
}

private static void PrintColumnHeader()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    var header = new StringBuilder();
    header.Append("Step".PadRight(10));
    foreach (var column in FixedColumns)
    {
        header.Append(column.PadRight(ColumnWidth));
    }
    Console.WriteLine(header.ToString());
    Console.WriteLine(new string('-', 10 + (ColumnWidth * FixedColumns.Length)));
    Console.ResetColor();
}
```

**Output**:
```
Step      COORD                              R1_FWD                             POLISHER                           R2                                 CLEANER                            R3                                 BUFFER                             R1_RET
----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
```

---

## Wafer Processing Flow

### Complete Pipeline with Guard Conditions

Each wafer follows a 22-state journey with bitmasked guard conditions:

```
Stage 0: Initialization
┌──────────┐    SYSTEM_READY      ┌──────────────┐    START_PROCESSING    ┌─────────────────────┐
│ created  │ ──────────────────> │ readyToStart │ ───────────────────> │ waitingForR1Pickup  │
└──────────┘   Guard: None        └──────────────┘   Guard: None          └─────────────────────┘
  entry: reportReadyToCoordinator                                            │
                                                                              │
Stage p1 (Priority: 4 - Lowest): Carrier → Platen                            │
┌──────────────────────┐    R1 AVAILABLE (p1)       ┌───────────────────┐   │
│ CARRIER              │ ◄──────────────────────────┤ (from above)      │◄──┘
└──────────────────────┘    Guard: CanPickFromCarrier (0x000200)        │
        │                   = HasRobot1Permission                         │
        │ R-1 picks                                                       │
        ▼                                                                 │
┌──────────────────────┐    Guard: CanMoveToPlaten (0x008200)           │
│ r1MovingToPlaten     │    = WaferOnRobot | HasRobot1Permission        │
└──────────────────────┘                                                 │
        │                                                                 │
        │ R-1 moves                                                       │
        ▼                                                                 │
┌──────────────────────┐    Guard: CanPlaceOnPlaten (0x009000)          │
│ waitingPlatenLocation│    = WaferOnRobot | HasPlatenPermission        │
└──────────────────────┘                                                 │
        │                                                                 │
        │ Location granted (FIFO)                                        │
        ▼                                                                 │
┌──────────────────────┐    R-1 places    ┌──────────┐                  │
│ r1PlacingToPlaten    │ ───────────────> │ PLATEN   │                  │
└──────────────────────┘                   └──────────┘                  │
        │                                      │                         │
        │ WaferAtPlaten = true                 │ Polish                  │
        │ WaferOnRobot = false                 │ 200ms                   │
        ▼                                      ▼                         │
┌──────────────────────┐    Guard: CanStartPolish (0x010008)            │
│ r1ReturningToCarrier │    = WaferAtPlaten | PlatenFree                │
└──────────────────────┘                                                 │
        │                                                                 │
        │ R-1 returns to carrier (idle)                                  │
        ▼                                                                 │
┌──────────────────────┐    REQUEST_POLISH          ┌─────────────┐     │
│ waitingForPolisher   │ ───────────────────────> │ POLISHING   │     │
└──────────────────────┘                            └─────────────┘     │
        │                                                │               │
        │ PolishComplete = true (0x080000)              │               │
        ▼                                                ▼               │
        POLISH_COMPLETE                                                  │
                                                                         │
Stage p2 (Priority: 3): Platen → Cleaner                                │
┌──────────────────────┐    R2 AVAILABLE (p2)       Guard: CanPickFromPlaten
│ waitingForR2Pickup   │ ◄────────────────────────  = PolishComplete | HasRobot2Permission (0x080400)
└──────────────────────┘
        │
        │ R-2 picks from platen, WaferOnRobot = true
        ▼
┌──────────────────────┐    Guard: CanMoveToCleaner (0x008400)
│ r2MovingToCleaner    │    = WaferOnRobot | HasRobot2Permission
└──────────────────────┘
        │
        │ Similar flow through waitingCleanerLocation → r2PlacingToCleaner → ...
        ▼
    [CLEANER STATION - 150ms]
        │
        ▼
Stage p3 (Priority: 2): Cleaner → Buffer
        │
        ▼
    [BUFFER STATION - 100ms]
        │
        ▼
Stage p4 (Priority: 1 - Highest): Buffer → Carrier
┌──────────────────────┐    R1 AVAILABLE (p4)       Guard: CanPickFromBuffer
│ waitingForR1Return   │ ◄────────────────────────  = BufferComplete | HasRobot1Permission
└──────────────────────┘
        │
        │ R-1 picks from buffer (p4 priority!)
        ▼
┌──────────────────────┐    R-1 returns    ┌──────────┐
│ r1ReturningFromBuffer│ ───────────────> │ CARRIER  │
└──────────────────────┘                   │ (DONE)   │
        │                                   └──────────┘
        ▼
    ✓ COMPLETED
```

**Guard Condition Transitions**:
```
0x000000 (None) → 0x000200 (HasR1Permission)
                → 0x008200 (WaferOnRobot | HasR1Permission)
                → 0x009200 (WaferOnRobot | HasR1Permission | HasPlatenPermission)
                → 0x010200 (WaferAtPlaten | HasR1Permission)
                → 0x090408 (WaferAtPlaten | PlatenFree | PolishComplete | HasR2Permission)
                ... continues through all stages ...
```

### Timing Details (Per Operation)

| Operation | Duration | Notes |
|-----------|----------|-------|
| Pick wafer | 30ms | Simulated delay |
| Place wafer | 30ms | Simulated delay |
| Move robot | 50ms | Simulated delay |
| Polish | 200ms | Equipment processing |
| Clean | 150ms | Equipment processing |
| Buffer | 100ms | Equipment processing |

**Total Sequential Time per Wafer**: ~530ms minimum (without contention)
**Actual Average with Contention**: ~960ms (from test results)

---

## Guard Conditions System (Bitmasking)

### 22 Individual Flags

```csharp
[Flags]
public enum GuardConditions : uint
{
    None = 0,

    // Resource Availability (Bits 0-5)
    Robot1Free        = 1 << 0,   // 0x000001
    Robot2Free        = 1 << 1,   // 0x000002
    Robot3Free        = 1 << 2,   // 0x000004
    PlatenFree        = 1 << 3,   // 0x000008
    CleanerFree       = 1 << 4,   // 0x000010
    BufferFree        = 1 << 5,   // 0x000020

    // Location Availability (Bits 6-8)
    PlatenLocationFree  = 1 << 6,   // 0x000040
    CleanerLocationFree = 1 << 7,   // 0x000080
    BufferLocationFree  = 1 << 8,   // 0x000100

    // Robot Permissions (Bits 9-11)
    HasRobot1Permission = 1 << 9,   // 0x000200
    HasRobot2Permission = 1 << 10,  // 0x000400
    HasRobot3Permission = 1 << 11,  // 0x000800

    // Location Permissions (Bits 12-14)
    HasPlatenPermission  = 1 << 12,  // 0x001000
    HasCleanerPermission = 1 << 13,  // 0x002000
    HasBufferPermission  = 1 << 14,  // 0x004000

    // Wafer State (Bits 15-18)
    WaferOnRobot   = 1 << 15,  // 0x008000
    WaferAtPlaten  = 1 << 16,  // 0x010000
    WaferAtCleaner = 1 << 17,  // 0x020000
    WaferAtBuffer  = 1 << 18,  // 0x040000

    // Process State (Bits 19-21)
    PolishComplete = 1 << 19,  // 0x080000
    CleanComplete  = 1 << 20,  // 0x100000
    BufferComplete = 1 << 21,  // 0x200000

    // Complex Combinations
    CanPickFromCarrier = HasRobot1Permission,                              // 0x000200
    CanMoveToPlaten = WaferOnRobot | HasRobot1Permission,                 // 0x008200
    CanPlaceOnPlaten = WaferOnRobot | HasPlatenPermission,                // 0x009000
    CanStartPolish = WaferAtPlaten | PlatenFree,                          // 0x010008
    CanPickFromPlaten = PolishComplete | HasRobot2Permission,             // 0x080400
    CanMoveToCleaner = WaferOnRobot | HasRobot2Permission,                // 0x008400
    CanPlaceOnCleaner = WaferOnRobot | HasCleanerPermission,              // 0x00A000
    CanStartClean = WaferAtCleaner | CleanerFree,                         // 0x020010
    CanPickFromCleaner = CleanComplete | HasRobot3Permission,             // 0x100800
    CanMoveToBuffer = WaferOnRobot | HasRobot3Permission,                 // 0x008800
    CanPlaceOnBuffer = WaferOnRobot | HasBufferPermission,                // 0x00C000
    CanStartBuffer = WaferAtBuffer | BufferFree,                          // 0x040020
    CanPickFromBuffer = BufferComplete | HasRobot1Permission,             // 0x200200
    CanReturnToCarrier = WaferOnRobot | HasRobot1Permission,              // 0x008200
}
```

### Extension Methods

```csharp
public static class GuardConditionsExtensions
{
    public static bool HasAll(this GuardConditions current, GuardConditions required)
    {
        return (current & required) == required;
    }

    public static bool HasAny(this GuardConditions current, GuardConditions check)
    {
        return (current & check) != GuardConditions.None;
    }

    public static GuardConditions Set(this GuardConditions current, GuardConditions toSet)
    {
        return current | toSet;
    }

    public static GuardConditions Clear(this GuardConditions current, GuardConditions toClear)
    {
        return current & ~toClear;
    }

    public static GuardConditions Toggle(this GuardConditions current, GuardConditions toToggle)
    {
        return current ^ toToggle;
    }

    public static string ToHexString(this GuardConditions conditions)
    {
        return $"0x{(uint)conditions:X6}";
    }
}
```

### Usage in WaferSchedulerActor

```csharp
private GuardConditions _conditions = GuardConditions.None;

private void HandleRobot1Available(Robot1Available msg)
{
    _conditions = _conditions.Set(GuardConditions.HasRobot1Permission);

    switch (_currentState)
    {
        case "waiting_for_r1_pickup":
            if (_conditions.HasAll(GuardConditions.CanPickFromCarrier))
            {
                TableLogger.Log($"[{_waferId}] R-1 available [{_conditions.ToHexString()}] - picking from carrier");
                _currentState = "r1_moving_to_platen";
                _conditions = _conditions.Set(GuardConditions.WaferOnRobot);
                _robotSchedulers.Tell(new RequestRobot1("move", _waferId, 4));
            }
            break;
    }
}
```

**Benefits**:
- Fast bitwise operations (O(1))
- Compact representation (32-bit uint)
- Easy debugging (hex string format)
- Type-safe with C# enum
- Combinable conditions

---

## WAIT/Retry Mechanism

### Protocol Overview

Instead of hard DENY, the system implements cooperative WAIT with automatic retry:

```
┌──────────────┐
│ Wafer        │
│ Requests     │
│ Resource     │
└──────┬───────┘
       │
       ▼
┌──────────────┐
│ Robot        │
│ Requests     │
│ Permission   │
└──────┬───────┘
       │
       ▼
  ┌────────────┐
  │ Resource   │
  │ Available? │
  └─┬────────┬─┘
    │ YES    │ NO
    ▼        ▼
 ┌──────┐ ┌──────────────────────┐
 │PERMIT│ │ WAIT                 │
 │      │ │ (owned by W-XXX)     │
 └──┬───┘ └────┬─────────────────┘
    │          │
    │          ▼
    │     ┌──────────────────────┐
    │     │ NOTIFY_WAIT          │
    │     │ (retry in 50ms)      │
    │     └────┬─────────────────┘
    │          │
    │          ▼
    │     ┌──────────────────────┐
    │     │ Schedule Retry       │
    │     │ (50ms delay)         │
    │     └────┬─────────────────┘
    │          │
    │          └──────┐
    │                 │ (after 50ms)
    ▼                 ▼
 ┌──────────────────────────────┐
 │ Robot Available / Retry      │
 │ Wafer Scheduler Proceeds     │
 └──────────────────────────────┘
```

### Configuration

```csharp
private const int RetryDelayMs = 50;  // Agreed constant delay
```

### Implementation

**RobotSchedulersActor.cs:**
```csharp
private void HandlePermissionDenied(ResourcePermissionDenied msg)
{
    var key = $"{msg.ResourceType}:{msg.WaferId}";
    if (_pendingPermissions.TryGetValue(key, out var pending))
    {
        // Log WAIT instead of DENY
        TableLogger.LogEvent("WAIT_RESOURCE", msg.ResourceType, msg.Reason, msg.WaferId);

        // Notify wafer scheduler
        TableLogger.LogEvent("NOTIFY_WAIT", msg.ResourceType,
            $"retry in {RetryDelayMs}ms", msg.WaferId);

        // Schedule automatic retry
        Context.System.Scheduler.ScheduleTellOnce(
            TimeSpan.FromMilliseconds(RetryDelayMs),
            Self,
            new RetryPermissionRequest(
                msg.ResourceType,
                pending.requester,
                pending.task,
                pending.waferId,
                pending.priority
            ),
            ActorRefs.NoSender
        );

        _pendingPermissions.Remove(key);
    }
}

private void HandleRetryPermissionRequest(RetryPermissionRequest msg)
{
    TableLogger.Log($"[{msg.ResourceType}] Retrying permission for {msg.WaferId}");

    var key = $"{msg.ResourceType}:{msg.WaferId}";
    _pendingPermissions[key] = (
        msg.Requester,
        msg.Task,
        msg.WaferId,
        msg.Priority,
        msg.ResourceType
    );

    // Re-request permission
    TableLogger.LogEvent("REQUEST_PERMISSION", msg.ResourceType, "", msg.WaferId);
    _coordinator.Tell(new RequestResourcePermission(msg.ResourceType, msg.WaferId));
}
```

### Example Output

```
Step 13   [ R-1 -> COORD ] REQUEST_PERMISSION
Step 14   [ COORD -> R-1 ] WAIT (owned by W-001)
Step 15                                      [ R-1 -> WSCH-002 ] WAIT (retry in 50ms)
...
(50ms delay - automatic retry scheduled)
...
Step 16   [ R-1 -> COORD ] REQUEST_PERMISSION
Step 17   [ COORD -> R-1 ] PERMIT
Step 18                                      [ R-1 -> WSCH-002 ] R1AVAILABLE_PICK_P1
```

**Key Properties**:
1. **Non-blocking**: Wafer scheduler doesn't block
2. **Automatic**: Retry happens automatically after agreed delay
3. **Transparent**: All WAIT notifications visible in COORD column
4. **Fair**: FIFO queues for locations ensure fairness
5. **Constant Delay**: 50ms agreed constant prevents retry storms

---

## Resource Management (One-to-One Rule)

### Collision Prevention

**Principle**: At any given time, a resource can be owned by at most one wafer.

**Resources**:
```csharp
private readonly HashSet<string> _allResources = new()
{
    // Robots (active resources)
    "R-1", "R-2", "R-3",

    // Equipment (processing resources)
    "PLATEN", "CLEANER", "BUFFER",

    // Physical locations (storage resources)
    "PLATEN_LOCATION", "CLEANER_LOCATION", "BUFFER_LOCATION"
};
```

**SystemCoordinator.cs:**
```csharp
private void HandleResourcePermissionRequest(RequestResourcePermission msg)
{
    var resource = msg.ResourceType;
    var waferId = msg.WaferId;

    // Validate resource exists
    if (!_allResources.Contains(resource))
    {
        Console.WriteLine($"[COLLISION-CHECK] ❌ INVALID RESOURCE: {resource}");
        Sender.Tell(new ResourcePermissionDenied(resource, waferId, "Invalid resource"));
        return;
    }

    // Check if location resource (needs FIFO)
    if (_locationQueues.ContainsKey(resource))
    {
        HandleLocationPermissionRequest(resource, waferId, Sender);
        return;
    }

    // For non-location resources, check One-to-One Rule
    if (_resourceOwnership.ContainsKey(resource))
    {
        var currentOwner = _resourceOwnership[resource];
        if (currentOwner != waferId)
        {
            // COLLISION DETECTED
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[COLLISION-CHECK] ❌ COLLISION PREVENTED: {resource} " +
                            $"requested by {waferId} but owned by {currentOwner}");
            Console.ResetColor();
            Sender.Tell(new ResourcePermissionDenied(resource, waferId,
                $"Resource owned by {currentOwner}"));
            return;
        }
        else
        {
            // Re-entrant request allowed
            Sender.Tell(new ResourcePermissionGranted(resource, waferId));
            return;
        }
    }

    // Resource available - grant permission
    _resourceOwnership[resource] = waferId;
    Sender.Tell(new ResourcePermissionGranted(resource, waferId));
}
```

### FIFO Queue Management (Locations)

**Location resources** maintain FIFO queues to ensure wafer ordering:

```csharp
private readonly Dictionary<string, Queue<(IActorRef requester, string waferId)>> _locationQueues = new()
{
    { "PLATEN_LOCATION", new Queue<(IActorRef, string)>() },
    { "CLEANER_LOCATION", new Queue<(IActorRef, string)>() },
    { "BUFFER_LOCATION", new Queue<(IActorRef, string)>() }
};

private void HandleLocationPermissionRequest(string location, string waferId, IActorRef requester)
{
    var queue = _locationQueues[location];

    // If resource currently owned
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
            Console.WriteLine($"[ORDER-CHECK] {waferId} queued for {location} " +
                            $"(position {queue.Count}, owner: {currentOwner})");
            Console.ResetColor();
        }

        // Deny for now - will be granted when it's this wafer's turn
        requester.Tell(new ResourcePermissionDenied(location, waferId,
            $"Queued (position {queue.Count})"));
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
                Console.WriteLine($"[ORDER-CHECK] {waferId} queued for {location} " +
                                $"(position {queue.Count}, waiting for {firstWaferId})");
                Console.ResetColor();
            }
            requester.Tell(new ResourcePermissionDenied(location, waferId,
                $"Queued (position {queue.Count})"));
        }
    }
    else
    {
        // No queue and available - grant immediately
        _resourceOwnership[location] = waferId;
        requester.Tell(new ResourcePermissionGranted(location, waferId));
    }
}
```

### Auto-Grant on Release

When a resource is released, the next wafer in queue automatically receives permission:

```csharp
private void HandleResourceRelease(ReleaseResource msg)
{
    if (_resourceOwnership.TryGetValue(msg.ResourceType, out var owner) && owner == msg.WaferId)
    {
        _resourceOwnership.Remove(msg.ResourceType);

        // If this is a location resource with a queue, grant to next in line
        if (_locationQueues.TryGetValue(msg.ResourceType, out var queue) && queue.Count > 0)
        {
            var (nextRequester, nextWaferId) = queue.Dequeue();
            _resourceOwnership[msg.ResourceType] = nextWaferId;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[ORDER-CHECK] ✓ {nextWaferId} auto-granted {msg.ResourceType} " +
                            $"(next in queue after {msg.WaferId})");
            Console.ResetColor();
            nextRequester.Tell(new ResourcePermissionGranted(msg.ResourceType, nextWaferId));
        }
    }
}
```

---

## Priority Scheduling Details

### Priority Levels

| Priority | Stage | Robot | Task | Reason |
|----------|-------|-------|------|--------|
| 1 (Highest) | p4 | R-1 | Buffer → Carrier | Complete wafer, free buffer space |
| 2 | p3 | R-3 | Cleaner → Buffer | Move wafer to final stage |
| 3 | p2 | R-2 | Platen → Cleaner | Move polished wafer onwards |
| 4 (Lowest) | p1 | R-1 | Carrier → Platen | Load new wafer (can wait) |

### R-1 Priority Queue Algorithm

R-1 handles **two different priority levels** (p1 and p4):

```csharp
// Priority queue in RobotSchedulersActor
private readonly SortedSet<(int priority, long timestamp, IActorRef requester, string task, string waferId)> _robot1Queue;

private void HandleRobot1Request(RequestRobot1 msg)
{
    if (_robot1Busy)
    {
        // Add to priority queue
        _robot1Queue.Add((msg.Priority, DateTime.UtcNow.Ticks, Sender, msg.Task, msg.WaferId));
        TableLogger.Log($"[R-1] Busy, queued {msg.Priority} task (queue: {_robot1Queue.Count})");
    }
    else
    {
        // Request permission from coordinator
        var key = $"R-1:{msg.WaferId}";
        _pendingPermissions[key] = (Sender, msg.Task, msg.WaferId, msg.Priority, "R-1");
        TableLogger.LogEvent("REQUEST_PERMISSION", "R-1", "", msg.WaferId);
        _coordinator.Tell(new RequestResourcePermission("R-1", msg.WaferId));
    }
}

// When R-1 completes task, process highest priority from queue
private void ProcessNextRobot1Task()
{
    if (_robot1Queue.Count > 0)
    {
        var (priority, timestamp, requester, task, waferId) = _robot1Queue.Min;
        _robot1Queue.Remove(_robot1Queue.Min);

        TableLogger.Log($"[R-1] Processing queued p{priority} task for {waferId}");

        // Request permission for highest priority task
        var key = $"R-1:{waferId}";
        _pendingPermissions[key] = (requester, task, waferId, priority, "R-1");
        TableLogger.LogEvent("REQUEST_PERMISSION", "R-1", "", waferId);
        _coordinator.Tell(new RequestResourcePermission("R-1", waferId));
    }
}
```

**Example Scenario**:
```
Time    R-1 State           Queue Contents                 Action
─────────────────────────────────────────────────────────────────────
0ms     Busy (W-005 p1)     []                             -
10ms    Busy (W-005 p1)     [p1:W-006]                     W-006 requests p1
20ms    Busy (W-005 p1)     [p1:W-006, p1:W-007]           W-007 requests p1
30ms    Busy (W-005 p1)     [p1:W-006, p1:W-007, p4:W-001] W-001 requests p4
40ms    Task complete       [p4:W-001, p1:W-006, p1:W-007] Sort by priority!
40ms    Processing          [p1:W-006, p1:W-007]           ✅ Execute p4:W-001
70ms    Task complete       [p1:W-006, p1:W-007]           -
70ms    Processing          [p1:W-007]                     ✅ Execute p1:W-006
100ms   Task complete       [p1:W-007]                     -
100ms   Processing          []                             ✅ Execute p1:W-007
130ms   Idle                []                             -
```

**Key Insight**: W-001's p4 task gets priority even though W-006 and W-007 requested first, because returning completed wafers is more critical than loading new ones.

---

## Communication Protocols (Message Flows)

### Complete Event Type Reference

| Event Type | Direction | Purpose | Example | Column |
|------------|-----------|---------|---------|--------|
| `INIT_STATUS` | Subsystem → COORD | Initial readiness report | `[ ROBOTS -> COORD ] R-1:READY,R-2:READY,R-3:READY` | COORD |
| `SYSTEM_READY` | COORD → ALL | Coordinator broadcasts all systems ready | `[ COORD -> ALL ] ALL SYSTEMS READY` | COORD |
| `REQUEST_ROBOT` | WSCH → Robot | Request robot with priority | `[ WSCH-001 -> R-1 ] REQUEST_ROBOT_p1` | R1_FWD |
| `REQUEST_PERMISSION` | Robot → COORD | Request resource permission | `[ R-1 -> COORD ] REQUEST_PERMISSION` | COORD |
| `PERMIT_RESOURCE` | COORD → Robot | Grant permission | `[ COORD -> R-1 ] PERMIT` | COORD |
| `WAIT_RESOURCE` | COORD → Robot | Signal wait (resource busy) | `[ COORD -> R-1 ] WAIT (owned by W-002)` | COORD |
| `NOTIFY_WAIT` | Robot → WSCH | Notify wafer of wait | `[ R-1 -> WSCH-001 ] WAIT (retry in 50ms)` | R1_FWD |
| `FREE_ROBOT` | WSCH → COORD | Release robot permission | `[ WSCH-001 -> COORD ] FREE_R-1` | COORD |
| `R1_ACTION` | R-1 → WSCH | Robot action detail | `[ R-1 -> WSCH-001 ] pick from carrier` | R1_FWD |
| `R2_ACTION` | R-2 → WSCH | Robot action detail | `[ R-2 -> WSCH-001 ] pick from platen` | R2 |
| `R3_ACTION` | R-3 → WSCH | Robot action detail | `[ R-3 -> WSCH-001 ] pick from cleaner` | R3 |
| `START_TASK` | Robot → WSCH | Robot available notification | `[ R-1 -> WSCH-001 ] R1AVAILABLE_PICK_P1` | R1_FWD |
| `REQUEST_POLISH` | WSCH → PLATEN | Request polishing | `[ WSCH-001 -> PLATEN ] REQUEST_POLISH` | POLISHER |
| `POLISHING` | PLATEN → WSCH | Polishing started | `[ PLATEN -> WSCH-001 ] POLISHING` | POLISHER |
| `POLISH_COMPLETE` | PLATEN → WSCH | Polishing finished | `[ PLATEN -> WSCH-001 ] POLISH_COMPLETE` | POLISHER |
| `REQUEST_CLEAN` | WSCH → CLEANER | Request cleaning | `[ WSCH-001 -> CLEANER ] REQUEST_CLEAN` | CLEANER |
| `CLEANING` | CLEANER → WSCH | Cleaning started | `[ CLEANER -> WSCH-001 ] CLEANING` | CLEANER |
| `CLEAN_COMPLETE` | CLEANER → WSCH | Cleaning finished | `[ CLEANER -> WSCH-001 ] CLEAN_COMPLETE` | CLEANER |
| `REQUEST_BUFFER` | WSCH → BUFFER | Request buffering | `[ WSCH-001 -> BUFFER ] REQUEST_BUFFER` | BUFFER |
| `BUFFERING` | BUFFER → WSCH | Buffering started | `[ BUFFER -> WSCH-001 ] BUFFERING` | BUFFER |
| `BUFFER_COMPLETE` | BUFFER → WSCH | Buffering finished | `[ BUFFER -> WSCH-001 ] BUFFER_COMPLETE` | BUFFER |
| `COMPLETE` | WSCH → COORD | Wafer completed | Internal, triggers actor termination | - |

### Message Flow: Robot Request with WAIT

```
Step N:   [ WSCH-001 -> R-1 ] REQUEST_ROBOT_p1
          └─ Wafer scheduler requests robot with priority p1

Step N+1: [ R-1 -> COORD ] REQUEST_PERMISSION
          └─ Robot scheduler requests permission from coordinator

Step N+2: [ COORD -> R-1 ] PERMIT   OR   [ COORD -> R-1 ] WAIT (owned by W-002)
          └─ Coordinator grants permission or signals wait

Step N+3: [ R-1 -> WSCH-001 ] R1AVAILABLE_PICK_P1   OR   [ R-1 -> WSCH-001 ] WAIT (retry in 50ms)
          └─ Robot notifies wafer scheduler of availability or wait

(If WAIT, automatic retry after 50ms)

Step N+4: [ R-1 -> COORD ] REQUEST_PERMISSION
          └─ Automatic retry (no manual intervention)

Step N+5: [ COORD -> R-1 ] PERMIT
          └─ Resource now available

Step N+6: [ R-1 -> WSCH-001 ] R1AVAILABLE_PICK_P1
          └─ Wafer scheduler proceeds
```

---

## Performance Metrics

### Parallel vs Sequential Comparison

#### Sequential Processing (Theoretical)
```
W-001: ████████████████████████████ (530ms)
W-002:                              ████████████████████████████ (530ms)
W-003:                                                          ████████ (530ms)
...
Total: 25 × 530ms = 13,250ms (~13 seconds minimum)
```

#### Parallel Processing (With 3-Way Parallelism)
```
W-001: ████████████████████████████ (530ms)
W-002:       ████████████████████████████ (530ms)
W-003:              ████████████████████████████ (530ms)
W-004:                      ████████████████████████████ (530ms)
...
Total: ~10 seconds (with 3-way parallelism and queuing overhead)
```

**Speedup**: ~30% improvement

### Actual Test Results

```
[W-001] ✓ COMPLETED - cycle time: 970ms
[W-002] ✓ COMPLETED - cycle time: 985ms
[W-003] ✓ COMPLETED - cycle time: 953ms
...
[W-023] ✓ COMPLETED - cycle time: 966ms
[W-024] ✓ COMPLETED - cycle time: 967ms
[W-025] ✓ COMPLETED - cycle time: 938ms
```

**Average Cycle Time**: ~960ms per wafer
**Parallel Efficiency**: 3 wafers processing → ~320ms effective time per wafer slot

---

## Concurrency Control

### Max Concurrent Wafers: 3

The SystemCoordinator enforces a limit:

```csharp
private const int TotalWafers = 25;
private const int MaxActiveWafers = 3;

private void HandleWaferCompleted(WaferCompleted msg)
{
    _completedWafers++;
    TableLogger.Log($"[COORD] Wafer {msg.WaferId} completed ({_completedWafers}/{TotalWafers})");

    // Remove stopped actor from active wafers
    if (_waferSchedulers.Remove(msg.WaferId))
    {
        TableLogger.Log($"[COORD] Wafer scheduler for {msg.WaferId} terminated " +
                      $"(active: {_waferSchedulers.Count}/{MaxActiveWafers})");
    }

    // Spawn next wafer if more to process AND we're under max active limit
    if (_waferCounter < TotalWafers && _waferSchedulers.Count < MaxActiveWafers)
    {
        Self.Tell(new SpawnWafer($"W-{++_waferCounter:D3}"));
    }
    else if (_completedWafers >= TotalWafers)
    {
        TableLogger.Log($"[COORD] All {TotalWafers} wafers completed!");
        Context.System.Terminate();
    }
}
```

### Pipeline Trigger: Spawn on Platen Arrival

```csharp
private void HandleWaferAtPlaten(WaferAtPlaten msg)
{
    TableLogger.Log($"[COORD] Wafer {msg.WaferId} at platen - pipeline trigger " +
                  $"(active: {_waferSchedulers.Count}/{MaxActiveWafers})");

    // Pipeline mode: Spawn next wafer when current wafer reaches platen
    // This creates pipeline spacing while respecting max active limit
    if (_waferSchedulers.Count < MaxActiveWafers && _waferCounter < TotalWafers)
    {
        Self.Tell(new SpawnNextWafer());
    }
}
```

**Why limit to 3?**
- Prevents resource starvation
- Balances throughput vs. queue depth
- Matches physical system constraints (3 robots, 3 equipment stations)
- Creates natural pipeline spacing

**Why pipeline trigger on platen?**
- First wafer has completed first critical step (loading)
- R-1 is now free for next wafer
- Natural spacing prevents queue buildup
- Optimizes resource utilization

---

## Advantages of Current Architecture

### 1. **Safety through One-to-One Rule**
- No resource collisions
- FIFO queues ensure fairness
- Clear ownership tracking
- Automatic collision detection

### 2. **Visibility through 8-Column Logging**
- Complete system state visible at each step
- Easy debugging of communication flows
- Physical layout intuition
- Step numbering for timeline tracking

### 3. **Reliability through WAIT Mechanism**
- No hard denials that could deadlock
- Automatic retry with agreed delay
- Cooperative resource sharing
- Full transparency of wait reasons

### 4. **Efficiency through Guard Conditions**
- Fast bitwise operations (O(1))
- Compact state representation
- Easy condition combinations
- Type-safe with C# enums

### 5. **Performance through Priority Scheduling**
- p4 priority minimizes buffer utilization
- Completed wafers don't wait unnecessarily
- Prevents backpressure and deadlocks
- Optimizes overall throughput

### 6. **Clarity through SYSTEM_READY Protocol**
- Mutual knowledge of system state
- Clear initialization boundary
- Prevents startup race conditions
- Everyone knows everyone is ready

### 7. **Scalability through Layered Architecture**
- Easy to add more robots/equipment
- Easy to increase concurrent wafers
- Wafer logic separated from resource management
- Independent optimization of each layer

---

## Conclusion

The 3-layer parallel scheduling architecture with **SYSTEM_READY protocol**, **8-column event logging**, **guard condition bitmasking**, **WAIT/retry mechanism**, **One-to-One resource rule**, and **priority-based scheduling** enables **safe, efficient, and visible wafer processing** through:

- ✅ **Mutual knowledge initialization** (SYSTEM_READY broadcast)
- ✅ **Complete visibility** (8-column physical layout)
- ✅ **Collision prevention** (One-to-One Rule with FIFO)
- ✅ **Cooperative waiting** (WAIT with 50ms retry)
- ✅ **Efficient state checking** (bitmasked guard conditions)
- ✅ **Priority optimization** (p4 > p3 > p2 > p1)
- ✅ **True parallel pipelining** (3 concurrent wafers)
- ✅ **~30% performance improvement** over sequential processing
- ✅ **Scalable, maintainable, and flexible** design

### Complete Message Flow Summary

```
Initialization (Steps 1-4):
  ROBOTS → COORD → EQUIPMENT → COORD → WSCH-001 → COORD → [ COORD → ALL ]
  └─ Everyone knows everyone is ready

Resource Request (Steps 5+):
  WSCH → Robot → COORD → (PERMIT/WAIT) → Robot → WSCH
  └─ Complete 4-step protocol with retry

Equipment Processing:
  WSCH → Equipment → (PROCESSING) → Equipment → WSCH
  └─ 3-step protocol with duration timeout

Resource Release:
  WSCH → COORD → (Auto-grant to next in queue)
  └─ Automatic FIFO fairness
```

This architecture mirrors real-world semiconductor fabrication systems where **safety**, **visibility**, **efficiency**, and **reliability** are critical for production goals.

---

**Document Version**: 2.0
**Last Updated**: 2025-11-17
**Related**: ARCHITECTURE.md, EXAMPLE_OUTPUT.md, GuardConditions.cs, TableLogger.cs, SystemCoordinator.cs
