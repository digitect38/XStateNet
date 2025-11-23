# CMPSimXS2.Parallel Architecture Documentation

## Overview

CMPSimXS2.Parallel is a Chemical Mechanical Planarization (CMP) wafer processing simulation implementing a sophisticated 3-layer actor architecture using Akka.NET. The system orchestrates parallel wafer processing through a distributed state machine approach with collision prevention and FIFO ordering.

## Table of Contents

1. [System Architecture](#system-architecture)
2. [State Machine Design](#state-machine-design)
3. [Communication Protocols](#communication-protocols)
4. [Guard Conditions System](#guard-conditions-system)
5. [WAIT/Retry Mechanism](#waitretry-mechanism)
6. [Resource Management](#resource-management)
7. [Event Logging Architecture](#event-logging-architecture)

---

## System Architecture

### 3-Layer Actor Hierarchy

```
┌─────────────────────────────────────────────────────┐
│           Layer 1: SystemCoordinator                │
│  - Resource allocation (One-to-One Rule)            │
│  - FIFO queue management for locations              │
│  - Collision prevention                             │
│  - Wafer lifecycle management                       │
└─────────────────┬───────────────────────────────────┘
                  │
        ┌─────────┴─────────┐
        │                   │
┌───────▼────────┐  ┌──────▼──────────────────────────┐
│ Layer 2:       │  │ Layer 3:                        │
│ Wafer          │  │ RobotSchedulersActor            │
│ Schedulers     │◄─┤  - Robot1 (R-1)                 │
│ (One per wafer)│  │  - Robot2 (R-2)                 │
│                │  │  - Robot3 (R-3)                 │
│ W-001          │  │  - Platen (POLISHER)            │
│ W-002          │  │  - Cleaner (CLEANER)            │
│ W-003          │  │  - Buffer (BUFFER)              │
│ ...            │  │                                 │
└────────────────┘  └─────────────────────────────────┘
```

### Layer Responsibilities

#### Layer 1: SystemCoordinator
- **Purpose**: Top-level orchestration and resource management
- **Key Responsibilities**:
  - Resource permission granting/denying (One-to-One Rule)
  - FIFO queue management for physical locations
  - Wafer spawning and lifecycle management
  - Collision detection and prevention
- **Actor Lifetime**: Exists for entire simulation

#### Layer 2: WaferSchedulerActor
- **Purpose**: Individual wafer lifecycle management
- **Key Responsibilities**:
  - State machine execution for single wafer
  - Guard condition evaluation using bitmasking
  - Robot and equipment resource requests
  - Station-to-station wafer movement orchestration
- **Actor Lifetime**: Created when wafer spawned, terminated when wafer completed
- **Concurrency**: Up to `MaxActiveWafers` (default: 3) active simultaneously

#### Layer 3: RobotSchedulersActor
- **Purpose**: Shared resource management (robots and equipment)
- **Key Responsibilities**:
  - Robot task execution (pick, place, move)
  - Equipment operation (polishing, cleaning, buffering)
  - Priority-based task queuing (p1-p4)
  - Permission request to Coordinator
  - WAIT/Retry mechanism implementation
- **Actor Lifetime**: Exists for entire simulation

---

## State Machine Design

### Wafer Scheduler State Machine

The wafer follows a linear progression through 16 states:

```
created
  ↓
waiting_for_r1_pickup
  ↓
r1_moving_to_platen
  ↓
waiting_platen_location
  ↓
r1_placing_to_platen
  ↓
r1_returning_to_carrier_from_platen
  ↓
waiting_for_polisher
  ↓
waiting_for_r2_pickup
  ↓
r2_moving_to_cleaner
  ↓
waiting_cleaner_location
  ↓
r2_placing_to_cleaner
  ↓
r2_returning_to_platen
  ↓
waiting_for_cleaner
  ↓
waiting_for_r3_pickup
  ↓
r3_moving_to_buffer
  ↓
waiting_buffer_location
  ↓
r3_placing_to_buffer
  ↓
r3_returning_to_cleaner
  ↓
waiting_for_buffer
  ↓
waiting_for_r1_return
  ↓
r1_returning_from_buffer
  ↓
r1_placing_to_carrier
  ↓
completed (final)
```

### Robot Scheduler State Machine (Parallel)

Each robot and equipment operates independently in parallel:

```
┌─── robot1 ───┐  ┌─── robot2 ───┐  ┌─── robot3 ───┐
│ idle          │  │ idle          │  │ idle          │
│ requesting    │  │ requesting    │  │ requesting    │
│ waiting       │  │ waiting       │  │ waiting       │
│ working       │  │ working       │  │ working       │
│ releasing     │  │ releasing     │  │ releasing     │
└───────────────┘  └───────────────┘  └───────────────┘

┌─── platen ───┐  ┌─── cleaner ──┐  ┌─── buffer ───┐
│ idle          │  │ idle          │  │ idle          │
│ requesting    │  │ requesting    │  │ requesting    │
│ polishing     │  │ cleaning      │  │ buffering     │
└───────────────┘  └───────────────┘  └───────────────┘
```

**XState JSON Definitions**:
- `WaferSchedulerStateMachine.json`: Complete wafer lifecycle
- `RobotSchedulerStateMachine.json`: Parallel robot/equipment regions

---

## Communication Protocols

### 8-Column Physical Layout

The system visualizes communication using an 8-column layout representing physical stations:

| Column | Station | Purpose |
|--------|---------|---------|
| COORD | Coordinator | Non-positional communications (permissions, notifications) |
| R1(→) | Robot1 Forward | R1 moving carrier → platen (p1, p4 priorities) |
| POLISHER | Platen Station | Polishing operations |
| R2 | Robot2 | R2 moving platen → cleaner (p2 priority) |
| CLEANER | Cleaner Station | Cleaning operations |
| R3 | Robot3 | R3 moving cleaner → buffer (p3 priority) |
| BUFFER | Buffer Station | Buffering operations |
| R1(←) | Robot1 Return | R1 returning buffer → carrier |

### Message Flow Patterns

#### 0. System Initialization Flow (Startup Protocol)

Before any wafer processing begins, all subsystems must report ready and the coordinator must broadcast confirmation:

```
Step 1:   [ ROBOTS -> COORD ] R-1:READY,R-2:READY,R-3:READY
          └─ Robot schedulers report all robots ready

Step 2:   [ EQUIPMENT -> COORD ] PLATEN:READY,CLEANER:READY,BUFFER:READY
          └─ Equipment schedulers report all equipment ready

Step 3:   [ WSCH-001 -> COORD ] READY
          └─ First wafer scheduler reports ready

Step 4:   [ COORD -> ALL ] ALL SYSTEMS READY
          └─ Coordinator broadcasts confirmation that all systems are ready
          └─ Everyone now knows everyone is ready

Step 5+:  (Wafer processing begins)
```

**Key Protocol Rules**:
1. Subsystems (ROBOTS, EQUIPMENT) report ready on startup
2. Each wafer scheduler reports ready on creation
3. After first wafer reports ready, coordinator broadcasts SYSTEM_READY
4. All actors receive confirmation before processing begins
5. This ensures everyone knows everyone is ready (mutual knowledge)

#### 1. Robot Request Flow (4 steps)

```
Step N:   [ WSCH-001 -> R-1 ] REQUEST_ROBOT_p1
          └─ Wafer scheduler requests robot with priority

Step N+1: [ R-1 -> COORD ] REQUEST_PERMISSION
          └─ Robot scheduler requests permission from coordinator

Step N+2: [ COORD -> R-1 ] PERMIT   OR   [ COORD -> R-1 ] WAIT (reason)
          └─ Coordinator grants permission or signals wait

Step N+3: [ R-1 -> WSCH-001 ] R1AVAILABLE_PICK_P1   OR   [ R-1 -> WSCH-001 ] WAIT (retry in 50ms)
          └─ Robot notifies wafer scheduler of availability or wait
```

#### 2. Location Permission Flow

```
Step N:   [ WSCH-001 -> R-1 ] move to platen
          └─ Wafer requests robot to move to station

Step N+1: [ R-1 -> COORD ] REQUEST_PERMISSION (for PLATEN_LOCATION)
          └─ Robot requests location permission

Step N+2: [ COORD -> R-1 ] PERMIT   OR   WAIT (queued position X)
          └─ Coordinator grants if free, otherwise adds to FIFO queue

Step N+3: [ R-1 -> WSCH-001 ] place on platen   OR   WAIT notification
          └─ Robot executes or notifies wait
```

#### 3. Equipment Processing Flow

```
Step N:   [ WSCH-001 -> PLATEN ] REQUEST_POLISH
          └─ Wafer requests processing

Step N+1: [ PLATEN -> WSCH-001 ] POLISHING
          └─ Equipment confirms processing started

Step N+2: (After POLISH_DURATION timeout)
          [ PLATEN -> WSCH-001 ] POLISH_COMPLETE
          └─ Equipment notifies completion
```

#### 4. Resource Release Flow

```
Step N:   [ WSCH-001 -> COORD ] FREE_R-1
          └─ Wafer releases robot permission

Step N+1: (If queue exists)
          [ COORD -> R-1 ] PERMIT (auto-granted to next in queue)
          └─ Coordinator auto-grants to next wafer in FIFO queue
```

### Complete Event Type Reference

| Event Type | Direction | Purpose | Example |
|------------|-----------|---------|---------|
| `INIT_STATUS` | Subsystem → COORD | Initial readiness report | `[ ROBOTS -> COORD ] R-1:READY,R-2:READY,R-3:READY` |
| `SYSTEM_READY` | COORD → ALL | Coordinator broadcasts all systems ready | `[ COORD -> ALL ] ALL SYSTEMS READY` |
| `SPAWN` | Internal | Wafer creation (implicit) | - |
| `REQUEST_ROBOT` | WSCH → Robot | Request robot with priority | `[ WSCH-001 -> R-1 ] REQUEST_ROBOT_p1` |
| `REQUEST_PERMISSION` | Robot → COORD | Request resource permission | `[ R-1 -> COORD ] REQUEST_PERMISSION` |
| `PERMIT_RESOURCE` | COORD → Robot | Grant permission | `[ COORD -> R-1 ] PERMIT` |
| `WAIT_RESOURCE` | COORD → Robot | Signal wait (resource busy) | `[ COORD -> R-1 ] WAIT (owned by W-002)` |
| `NOTIFY_WAIT` | Robot → WSCH | Notify wafer of wait | `[ R-1 -> WSCH-001 ] WAIT (retry in 50ms)` |
| `FREE_ROBOT` | WSCH → COORD | Release robot permission | `[ WSCH-001 -> COORD ] FREE_R-1` |
| `R1_ACTION` | R-1 → WSCH | Robot action detail | `[ R-1 -> WSCH-001 ] pick from carrier` |
| `R2_ACTION` | R-2 → WSCH | Robot action detail | `[ R-2 -> WSCH-001 ] pick from platen` |
| `R3_ACTION` | R-3 → WSCH | Robot action detail | `[ R-3 -> WSCH-001 ] pick from cleaner` |
| `START_TASK` | Robot → WSCH | Robot available notification | `[ R-1 -> WSCH-001 ] R1AVAILABLE_PICK_P1` |
| `REQUEST_POLISH` | WSCH → PLATEN | Request polishing | `[ WSCH-001 -> PLATEN ] REQUEST_POLISH` |
| `POLISHING` | PLATEN → WSCH | Polishing started | `[ PLATEN -> WSCH-001 ] POLISHING` |
| `POLISH_COMPLETE` | PLATEN → WSCH | Polishing finished | `[ PLATEN -> WSCH-001 ] POLISH_COMPLETE` |
| `REQUEST_CLEAN` | WSCH → CLEANER | Request cleaning | `[ WSCH-001 -> CLEANER ] REQUEST_CLEAN` |
| `CLEANING` | CLEANER → WSCH | Cleaning started | `[ CLEANER -> WSCH-001 ] CLEANING` |
| `CLEAN_COMPLETE` | CLEANER → WSCH | Cleaning finished | `[ CLEANER -> WSCH-001 ] CLEAN_COMPLETE` |
| `REQUEST_BUFFER` | WSCH → BUFFER | Request buffering | `[ WSCH-001 -> BUFFER ] REQUEST_BUFFER` |
| `BUFFERING` | BUFFER → WSCH | Buffering started | `[ BUFFER -> WSCH-001 ] BUFFERING` |
| `BUFFER_COMPLETE` | BUFFER → WSCH | Buffering finished | `[ BUFFER -> WSCH-001 ] BUFFER_COMPLETE` |
| `COMPLETE` | WSCH → COORD | Wafer completed | Internal, triggers actor termination |

---

## Guard Conditions System

### Bitmasking Architecture

Guard conditions use a `[Flags]` enum with 32-bit bitmasking for efficient multi-condition checking:

```csharp
[Flags]
public enum GuardConditions : uint
{
    None = 0,

    // Resource Availability (0x00001 - 0x00020)
    Robot1Free        = 1 << 0,   // 0x000001
    Robot2Free        = 1 << 1,   // 0x000002
    Robot3Free        = 1 << 2,   // 0x000004
    PlatenFree        = 1 << 3,   // 0x000008
    CleanerFree       = 1 << 4,   // 0x000010
    BufferFree        = 1 << 5,   // 0x000020

    // Location Availability (0x00040 - 0x00100)
    PlatenLocationFree  = 1 << 6,   // 0x000040
    CleanerLocationFree = 1 << 7,   // 0x000080
    BufferLocationFree  = 1 << 8,   // 0x000100

    // Robot Permissions (0x00200 - 0x00800)
    HasRobot1Permission = 1 << 9,   // 0x000200
    HasRobot2Permission = 1 << 10,  // 0x000400
    HasRobot3Permission = 1 << 11,  // 0x000800

    // Location Permissions (0x01000 - 0x04000)
    HasPlatenPermission  = 1 << 12,  // 0x001000
    HasCleanerPermission = 1 << 13,  // 0x002000
    HasBufferPermission  = 1 << 14,  // 0x004000

    // Wafer State (0x08000 - 0x40000)
    WaferOnRobot   = 1 << 15,  // 0x008000
    WaferAtPlaten  = 1 << 16,  // 0x010000
    WaferAtCleaner = 1 << 17,  // 0x020000
    WaferAtBuffer  = 1 << 18,  // 0x040000

    // Process State (0x80000 - 0x200000)
    PolishComplete = 1 << 19,  // 0x080000
    CleanComplete  = 1 << 20,  // 0x100000
    BufferComplete = 1 << 21,  // 0x200000
}
```

### Complex Condition Combinations

The system defines 7 pre-computed complex conditions:

```csharp
// Stage 1: Pickup from carrier
CanPickFromCarrier = HasRobot1Permission  // 0x000200

// Stage 2: Move to platen
CanMoveToPlaten = WaferOnRobot | HasRobot1Permission  // 0x008200

// Stage 3: Place on platen
CanPlaceOnPlaten = WaferOnRobot | HasPlatenPermission  // 0x009000

// Stage 4: Start polishing
CanStartPolish = WaferAtPlaten | PlatenFree  // 0x010008

// Stage 5: Pick from platen
CanPickFromPlaten = PolishComplete | HasRobot2Permission  // 0x080400

// Stage 6: Move to cleaner
CanMoveToCleaner = WaferOnRobot | HasRobot2Permission  // 0x008400

// Stage 7: Place on cleaner
CanPlaceOnCleaner = WaferOnRobot | HasCleanerPermission  // 0x00A000
```

### Extension Methods

```csharp
// Check if all required conditions are met
bool HasAll(GuardConditions required)

// Check if any of the conditions are met
bool HasAny(GuardConditions check)

// Set (add) conditions
GuardConditions Set(GuardConditions toSet)

// Clear (remove) conditions
GuardConditions Clear(GuardConditions toClear)

// Toggle conditions
GuardConditions Toggle(GuardConditions toToggle)

// Format as hex string for debugging
string ToHexString()  // Returns "0xXXXXXX"
```

### Usage Example

```csharp
// Initialize
private GuardConditions _conditions = GuardConditions.None;

// Robot1 permission granted
_conditions = _conditions.Set(GuardConditions.HasRobot1Permission);
// _conditions = 0x000200

// Check if can pick from carrier
if (_conditions.HasAll(GuardConditions.CanPickFromCarrier))
{
    // Pickup wafer
    _conditions = _conditions.Set(GuardConditions.WaferOnRobot);
    // _conditions = 0x008200
}

// Check if can move to platen
if (_conditions.HasAll(GuardConditions.CanMoveToPlaten))
{
    // Request platen location
    // ...
}
```

---

## WAIT/Retry Mechanism

### Overview

Instead of outright DENY messages, the system implements a cooperative WAIT mechanism with automatic retry. This ensures eventual progress while maintaining resource safety.

### Configuration

```csharp
private const int RetryDelayMs = 50;  // Agreed constant retry delay
```

### Flow Diagram

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
    │                 │ (after delay)
    ▼                 ▼
 ┌──────────────────────────────┐
 │ Robot Available / Retry      │
 │ Wafer Scheduler Proceeds     │
 └──────────────────────────────┘
```

### Implementation Details

#### 1. Permission Denied → WAIT

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
```

#### 2. Automatic Retry

```csharp
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

#### 3. Wafer Scheduler Notification

**WaferSchedulerActor.cs:**
```csharp
Receive<WaitNotification>(msg =>
{
    TableLogger.Log($"[{_waferId}] Wait notification: {msg.Reason}");
    // Wafer scheduler logs wait status but doesn't need to take action
    // Automatic retry is handled by RobotSchedulersActor
});
```

### Key Properties

1. **Non-blocking**: Wafer scheduler doesn't block, system continues processing
2. **Automatic**: Retry happens automatically after agreed delay
3. **Transparent**: All WAIT notifications visible in 8-column output
4. **Fair**: FIFO queues for locations ensure fair ordering
5. **Constant Delay**: 50ms agreed constant prevents retry storms

### Example Output

```
Step 4:  [ WSCH-001 -> R-1 ] REQUEST_ROBOT_p1
Step 5:  [ R-1 -> COORD ] REQUEST_PERMISSION
Step 6:  [ COORD -> R-1 ] WAIT (owned by W-002)
Step 7:  [ R-1 -> WSCH-001 ] WAIT (retry in 50ms)
...
(50ms delay)
...
Step 8:  [ R-1 -> COORD ] REQUEST_PERMISSION
Step 9:  [ COORD -> R-1 ] PERMIT
Step 10: [ R-1 -> WSCH-001 ] R1AVAILABLE_PICK_P1
```

---

## Resource Management

### One-to-One Rule

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

### Collision Prevention

**SystemCoordinator.cs:**
```csharp
private void HandleResourcePermissionRequest(RequestResourcePermission msg)
{
    var resource = msg.ResourceType;
    var waferId = msg.WaferId;

    // Check if resource already allocated
    if (_resourceOwnership.ContainsKey(resource))
    {
        var currentOwner = _resourceOwnership[resource];
        if (currentOwner != waferId)
        {
            // COLLISION DETECTED
            Console.WriteLine($"❌ COLLISION PREVENTED: {resource} " +
                            $"requested by {waferId} but owned by {currentOwner}");
            Sender.Tell(new ResourcePermissionDenied(
                resource, waferId, $"Resource owned by {currentOwner}"));
            return;
        }
    }

    // Grant permission
    _resourceOwnership[resource] = waferId;
    Sender.Tell(new ResourcePermissionGranted(resource, waferId));
}
```

### FIFO Queue Management

Location resources maintain FIFO queues to ensure wafer ordering:

```csharp
private readonly Dictionary<string, Queue<(IActorRef requester, string waferId)>> _locationQueues;

private void HandleLocationPermissionRequest(string location, string waferId, IActorRef requester)
{
    var queue = _locationQueues[location];

    // If resource busy, add to queue
    if (_resourceOwnership.ContainsKey(location))
    {
        if (!queue.Any(entry => entry.waferId == waferId))
        {
            queue.Enqueue((requester, waferId));
            Console.WriteLine($"📋 {waferId} queued for {location} (position {queue.Count})");
        }
        requester.Tell(new ResourcePermissionDenied(location, waferId, $"Queued"));
        return;
    }

    // If queue exists, grant only to first in queue
    if (queue.Count > 0)
    {
        var (firstRequester, firstWaferId) = queue.Peek();
        if (firstWaferId == waferId)
        {
            queue.Dequeue();
            _resourceOwnership[location] = waferId;
            Console.WriteLine($"✓ {waferId} granted {location} (was first in queue)");
            requester.Tell(new ResourcePermissionGranted(location, waferId));
        }
        else
        {
            // Not first - add to queue
            if (!queue.Any(entry => entry.waferId == waferId))
            {
                queue.Enqueue((requester, waferId));
            }
            requester.Tell(new ResourcePermissionDenied(location, waferId, $"Queued"));
        }
    }
    else
    {
        // No queue - grant immediately
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

        // Auto-grant to next in queue
        if (_locationQueues.TryGetValue(msg.ResourceType, out var queue) && queue.Count > 0)
        {
            var (nextRequester, nextWaferId) = queue.Dequeue();
            _resourceOwnership[msg.ResourceType] = nextWaferId;
            Console.WriteLine($"✓ {nextWaferId} auto-granted {msg.ResourceType}");
            nextRequester.Tell(new ResourcePermissionGranted(msg.ResourceType, nextWaferId));
        }
    }
}
```

---

## Event Logging Architecture

### TableLogger Design

**Purpose**: Visualize parallel wafer progression through 8-column physical layout

**Key Features**:
1. **8-Column Layout**: COORD, R1(→), POLISHER, R2, CLEANER, R3, BUFFER, R1(←)
2. **Step Numbering**: Continuous numbering (Step 1, Step 2, ...) without gaps
3. **Column Assignment Tracking**: Wafer positions tracked per column
4. **Action Filtering**: Only prints steps with actual column actions

### Column Determination Logic

**TableLogger.cs:**
```csharp
private static string DetermineColumn(string action)
{
    // COORD: Coordination communications
    if (action.Contains("COORD") || action.Contains("PERMIT_") || action.Contains("FREE_"))
        return "COORD";

    // POLISHER: At polisher station
    if (action.Contains("place on platen") || action.Contains("POLISHING"))
        return "POLISHER";

    // CLEANER: At cleaner station
    if (action.Contains("place on cleaner") || action.Contains("CLEANING"))
        return "CLEANER";

    // BUFFER: At buffer station
    if (action.Contains("place on buffer") || action.Contains("BUFFERING"))
        return "BUFFER";

    // R1(→): R1 forward (carrier → platen)
    if (action.Contains("R-1") &&
        (action.Contains("pick from carrier") || action.Contains("move to platen") ||
         action.Contains("REQUEST_ROBOT_p1") || action.Contains("REQUEST_ROBOT_p4")))
        return "R1_FWD";

    // R2: R2 between platen and cleaner
    if (action.Contains("R-2") &&
        (action.Contains("pick from platen") || action.Contains("move to cleaner") ||
         action.Contains("REQUEST_ROBOT_p2")))
        return "R2";

    // R3: R3 between cleaner and buffer
    if (action.Contains("R-3") &&
        (action.Contains("pick from cleaner") || action.Contains("move to buffer") ||
         action.Contains("REQUEST_ROBOT_p3")))
        return "R3";

    // R1(←): R1 return (buffer → carrier)
    if (action.Contains("R-1") &&
        (action.Contains("pick from buffer") || action.Contains("move to carrier")))
        return "R1_RET";

    return "";
}
```

### Output Example with Column Headers

The system prints column headers once at startup, followed by the initialization sequence with SYSTEM_READY broadcast:

```
Step      COORD                              R1_FWD                             POLISHER                           R2                                 CLEANER                            R3                                 BUFFER                             R1_RET
----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
Step 1    [ ROBOTS -> COORD ] R-1:READY,R-2:READY,R-3:READY
Step 2    [ EQUIPMENT -> COORD ] PLATEN:READY,CLEANER:READY,BUFFER:READY
Step 3    [ WSCH-001 -> COORD ] READY
Step 4    [ COORD -> ALL ] ALL SYSTEMS READY
Step 5                                       [ WSCH-001 -> R-1 ] REQUEST_ROBOT_p1
Step 6    [ R-1 -> COORD ] REQUEST_PERMISSION
Step 7    [ COORD -> R-1 ] PERMIT
Step 8    [ R-1 -> WSCH-001 ] R1AVAILABLE_PICK_P1
Step 9                                       [ R-1 -> WSCH-001 ] pick from carrier
Step 10                                      [ R-1 -> WSCH-001 ] move to platen
Step 11                                                                         [ PLATEN -> WSCH-001 ] POLISHING
```

**Key Observations**:
- **Column Header**: Printed once before Step 1 showing all 8 columns
- **Separator Line**: Dashes separate header from steps
- **Steps 1-3**: Subsystems report ready to COORD (initialization)
- **Step 4**: **COORD broadcasts SYSTEM_READY to ALL** (everyone knows everyone is ready)
- **Step 5+**: Wafer processing begins after system ready confirmation
- **Column Alignment**: Actions appear in appropriate physical station columns

### Configuration

```csharp
// Enable/disable verbose logging
TableLogger.EnableVerboseLogging = true;  // Default: false

// Initialize at simulation start - prints column headers and resets state
TableLogger.Initialize();
// This calls PrintColumnHeader() which outputs:
// - Column header row: Step, COORD, R1_FWD, POLISHER, R2, CLEANER, R3, BUFFER, R1_RET
// - Separator line with dashes
// Headers are printed ONLY ONCE at initialization
```

### Column Header Implementation

**TableLogger.cs:**
```csharp
public static void Initialize()
{
    _waferActions.Clear();
    _activeWafers.Clear();
    _completedWafers.Clear();
    _previousActiveWafers.Clear();
    _currentStepActions.Clear();
    _waferCurrentStation.Clear();
    _globalStepCounter = 0;
    _stepHasActions = false;

    // Print column header once before Step 1
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

---

## Performance Characteristics

### Parallelism

- **Max Active Wafers**: Configurable (default: 3)
- **Total Wafers**: Configurable (default: 25)
- **Pipeline Mode**: New wafer spawns when previous reaches platen

### Timing

- **WAIT Retry Delay**: 50ms (constant)
- **Polishing Duration**: Configurable via state machine
- **Cleaning Duration**: Configurable via state machine
- **Buffering Duration**: Configurable via state machine

### Actor Lifecycle

| Actor Type | Creation | Termination | Count |
|------------|----------|-------------|-------|
| SystemCoordinator | Startup | Shutdown | 1 |
| RobotSchedulersActor | Startup | Shutdown | 1 |
| WaferSchedulerActor | On wafer spawn | On wafer complete | MaxActiveWafers concurrent |

---

## Testing

### Unit Test Coverage

1. **GuardConditionsTests.cs**:
   - All 22 individual flag tests
   - All 7 complex condition combination tests
   - Extension method tests (HasAll, Set, Clear, Toggle, ToHexString)
   - Complete wafer lifecycle scenario tests

2. **WaitMechanismTests.cs**:
   - Permission request/deny flows
   - WAIT notification delivery
   - Automatic retry scheduling
   - FIFO queue ordering
   - Resource collision prevention
   - Re-entrant request handling

### Running Tests

```bash
cd XStateNet2/CMPSimXS2.Tests
dotnet test
```

---

## Future Enhancements

1. **Dynamic Priority Adjustment**: Adjust robot priorities based on wafer urgency
2. **Deadlock Detection**: Automated detection and resolution of circular waits
3. **Performance Metrics**: Throughput, utilization, cycle time tracking
4. **Multi-Platen Support**: Scale to multiple polishing/cleaning/buffer stations
5. **Adaptive WAIT Delay**: Dynamic retry delay based on queue depth
6. **Fault Injection**: Simulate equipment failures and recovery

---

## References

- **XState Documentation**: https://xstate.js.org/
- **Akka.NET Documentation**: https://getakka.net/
- **CMP Process Overview**: Chemical Mechanical Planarization for semiconductor manufacturing

---

**Document Version**: 1.0
**Last Updated**: 2025-11-16
**Authors**: CMPSimXS2.Parallel Development Team
