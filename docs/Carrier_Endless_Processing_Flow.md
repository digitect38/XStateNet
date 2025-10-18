# Carrier Endless Processing Flow

## Overview

The CMP Simulator implements **endless carrier processing** using SEMI E87 Carrier Management standard. After completing all wafers in one carrier, the system automatically swaps to the next carrier and continues processing indefinitely.

This document describes the complete lifecycle of N successive carriers (CARRIER_001 → CARRIER_002 → ... → CARRIER_N).

---

## Architecture Components

### Key Components

1. **CarrierManager** - SEMI E87/E90 carrier lifecycle management
2. **CarrierMachine** - State machine for each carrier (E87 states)
3. **DeclarativeSchedulerMachine** - Rule-based wafer scheduling
4. **LoadPortMachine** - LoadPort state management
5. **Station Machines** - Polisher, Cleaner, Buffer
6. **Robot Machines** - R1, R2, R3 for wafer transfers
7. **WaferMachine** - E90 substrate tracking for each wafer

### State Machine Communication

- **Pub/Sub Pattern**: All machines communicate via EventBusOrchestrator
- **Event-Driven**: No polling - state changes trigger immediate notifications
- **Deferred Sends**: Events are queued and executed via `ExecuteDeferredSends()`

---

## CARRIER_001: Initial Carrier Flow

### Phase 1: Initialization (Constructor)

```
OrchestratedForwardPriorityController()
├─ Load settings (TOTAL_WAFERS, timing configuration)
├─ Create EventBusOrchestrator
├─ InitializeStations()
├─ InitializeWafers()
│  ├─ Create 25 Wafer objects with distinct colors
│  ├─ Create WaferMachine (E90) for each wafer
│  ├─ Create CARRIER_001 carrier object
│  ├─ Add all 25 wafers to CARRIER_001
│  └─ 📦 Log: "Initial carrier CARRIER_001 created with 25 wafers"
└─ InitializeStateMachines()
   ├─ Create LoadPortMachine
   ├─ Create CarrierMachine for CARRIER_001
   ├─ Create DeclarativeSchedulerMachine
   ├─ Create Station Machines (Polisher, Cleaner, Buffer)
   └─ Create Robot Machines (R1, R2, R3)
```

**Key Log Messages:**
```
📦 Initial carrier CARRIER_001 created with 25 wafers
✓ Using DeclarativeSchedulerMachine with rules from: ...
✓ State machines created (LoadPort + 1 Carriers + Scheduler)
✓ Subscribed to state update events (Direct Pub/Sub pattern)
```

### Phase 2: Start Simulation

```
StartSimulation()
├─ Initialize E87/E90 Load Port and Carrier
│  ├─ InitializeLoadPortsAsync("LoadPort")
│  ├─ 📦 Log: "Registering Carrier CARRIER_001 at LoadPort with 25 wafers"
│  └─ CreateAndPlaceCarrierAsync(CARRIER_001)
├─ Start Scheduler (FIRST - ready to receive events)
├─ Start all state machines in parallel
│  ├─ LoadPort, Polisher, Cleaner, Buffer
│  ├─ R1, R2, R3 robots
│  ├─ CARRIER_001 machine
│  └─ All 25 WaferMachines (E90)
└─ Initialize all wafers to InCarrier state (E90: Acquired)
```

**Key Log Messages:**
```
🔧 Initializing SEMI E87/E90 Load Port and Carrier...
📦 Registering Carrier CARRIER_001 at LoadPort with 25 wafers
✓ E87/E90 Carrier initialized
✓ DeclarativeScheduler started and ready to receive events
✓ All state machines started (including E90 substrate tracking)
```

### Phase 3: CARRIER_001 E87 Lifecycle

```
E87 Carrier Workflow for CARRIER_001:
├─ ▶ Log: "Starting processing for CARRIER_001"
├─ NotPresent → [CARRIER_DETECTED] → WaitingForHost
├─ WaitingForHost → [HOST_PROCEED] → Mapping
├─ Mapping → [MAPPING_COMPLETE] → MappingVerification
├─ MappingVerification → [Auto-transition 600ms] → ReadyToAccess
├─ ReadyToAccess → [START_ACCESS] → InAccess ✓
└─ InAccess state: Carrier is now active for processing
```

### Phase 4: LoadPort Integration

```
LoadPort Workflow:
├─ SendCarrierArrive(CARRIER_001)
├─ SendDock() → 📦 Log: "Carrier CARRIER_001 docked"
└─ SendStartProcessing() → Processing begins
```

### Phase 5: Wafer Processing (25 wafers)

```
For each wafer (1 to 25):
├─ Scheduler evaluates rules from JSON configuration
├─ Rule matches: LoadPort.Pending > 0 AND Robot idle AND Station empty
├─ Issue TRANSFER command: LoadPort → R1 → Polisher
├─ Wafer processing flow:
│  ├─ LoadPort → R1 (300ms transfer)
│  ├─ R1 → Polisher (300ms transfer)
│  ├─ Polish (3000ms processing)
│  ├─ Polisher → R2 (300ms transfer)
│  ├─ R2 → Cleaner (300ms transfer)
│  ├─ Clean (3000ms processing)
│  ├─ Cleaner → R3 (300ms transfer)
│  ├─ R3 → Buffer (300ms transfer)
│  ├─ Buffer → R3 (300ms transfer)
│  └─ R3 → LoadPort (300ms transfer) ✅ Wafer complete
└─ E90 State transitions: Acquired → NeedsProcessing → InProcess.Polishing
   → InProcess.Cleaning → Processed → Complete
```

**Parallel Processing:**
- Polisher and Cleaner can process different wafers simultaneously
- Up to 3 robots can be in motion at the same time
- Forward Priority: P1 (Polisher empty) → P2 (Cleaner empty) → P3 (Buffer return) → P4 (LoadPort pickup)

### Phase 6: CARRIER_001 Completion

```
AllWafersCompleted Event:
├─ ✅ Log: "All 25 wafers completed!"
├─ Log Timing Statistics (efficiency, throughput)
├─ ⏸ Pause Scheduler (prevents premature rule execution)
├─ Flush Pending Events (ExecuteDeferredSends)
└─ Wait 200ms for pending transfers to complete
```

### Phase 7: CARRIER_001 Undocking

```
E87 Carrier Completion:
├─ CARRIER_001: SendAccessComplete() → InAccess → Complete
├─ LoadPort: SendComplete()
├─ LoadPort: SendUndock() → 📤 Log: "Carrier CARRIER_001 undocked"
├─ CARRIER_001: SendCarrierRemoved() → Complete → CarrierOut ✓
└─ CARRIER_001 lifecycle complete - ready for swap
```

---

## Carrier Swap Process

### Overview

The carrier swap process transitions from CARRIER_001 (or any carrier N) to the next carrier (CARRIER_002 or CARRIER_N+1). This happens automatically after all wafers complete.

### Swap Flow

```
SwapToNextCarrierAsync():
├─ 🔄 Log: "Swapping to next carrier..."
├─ Increment carrier counter: _nextCarrierNumber++ (2, 3, 4, ...)
├─ Generate new carrier ID: "CARRIER_002", "CARRIER_003", etc.
├─ 📦 Log: "Creating new carrier: CARRIER_002"
│
├─ Reset Scheduler for next batch
│  └─ DeclarativeScheduler.Reset(CARRIER_002)
│     ├─ Clear LoadPort.Completed queue
│     ├─ Reset LoadPort.Pending queue (1-25)
│     ├─ Clear station states (Polisher, Cleaner, Buffer)
│     ├─ Clear robot states (R1, R2, R3)
│     └─ 🔄 Log: "Reset starting for CARRIER_002"
│
├─ Regenerate wafer colors for new batch
│  └─ Each carrier gets unique color scheme
│
├─ Reset all 25 wafers (on UI thread)
│  ├─ IsCompleted = false
│  ├─ CurrentStation = "LoadPort"
│  ├─ Apply new color (Brush = new SolidColorBrush)
│  └─ Reset position to original LoadPort slot
│
├─ Reset E90 State Machines
│  └─ For each wafer: WaferMachine.AcquireAsync()
│     └─ E90: NotAcquired → Acquired (InCarrier state)
│
├─ Create new CarrierMachine for CARRIER_002
│  ├─ CarrierMachine(CARRIER_002, waferIds=[1-25])
│  ├─ Subscribe to StateChanged events
│  └─ StartAsync() → Initialize state machine
│
├─ Add to _carrierMachines list (keeps all for event handling)
├─ Register with E87/E90 Manager
│  ├─ Create Carrier object with 25 wafers
│  └─ CreateAndPlaceCarrierAsync(CARRIER_002)
│
└─ ✓ Log: "Carrier CARRIER_002 created with 25 wafers"
```

### Key Differences from CARRIER_001

**CARRIER_001 (First Carrier):**
- Created during initialization
- Scheduler starts fresh (not paused)
- No need to broadcast states (scheduler is clean)

**CARRIER_002+ (Subsequent Carriers):**
- Created dynamically during carrier swap
- Scheduler must be resumed after reset
- **Critical**: Must broadcast station/robot states to scheduler after resume

---

## CARRIER_002: Second Carrier Flow

### Phase 1: E87 Lifecycle (Same as CARRIER_001)

```
E87 Carrier Workflow for CARRIER_002:
├─ ▶ Log: "Starting processing for CARRIER_002"
├─ NotPresent → [CARRIER_DETECTED] → WaitingForHost
├─ WaitingForHost → [HOST_PROCEED] → Mapping
├─ Mapping → [MAPPING_COMPLETE] → MappingVerification
├─ MappingVerification → [Auto-transition 600ms] → ReadyToAccess
├─ ReadyToAccess → [START_ACCESS] → InAccess ✓
└─ InAccess state: Carrier is now active for processing
```

### Phase 2: LoadPort Integration (Same as CARRIER_001)

```
LoadPort Workflow:
├─ SendCarrierArrive(CARRIER_002)
├─ SendDock() → 📦 Log: "Carrier CARRIER_002 docked"
└─ SendStartProcessing()
```

### Phase 3: Resume Scheduler ⚠️ **CRITICAL**

This step is **UNIQUE to CARRIER_002+** and was NOT needed for CARRIER_001.

```
Resume Scheduler:
├─ Reset simulation start time (for new batch statistics)
├─ DeclarativeScheduler.Resume(CARRIER_002)
│  ├─ Set _isPaused = false
│  ├─ ▶ Log: "Scheduler resumed for CARRIER_002"
│  ├─ 📊 Log: "Queue Status: Pending=25, Completed=0/25"
│  └─ 📊 Log: "Next wafers: 1, 2, 3, 4, 5..."
│
└─ 📡 Broadcast station/robot states to scheduler
   ├─ Why? After reset, scheduler has no cached state information
   ├─ Polisher.BroadcastStatus() → STATION_STATUS: "polisher" = "empty"
   ├─ Cleaner.BroadcastStatus() → STATION_STATUS: "cleaner" = "empty"
   ├─ Buffer.BroadcastStatus() → STATION_STATUS: "buffer" = "empty"
   ├─ R1.BroadcastStatus() → ROBOT_STATUS: "R1" = "idle"
   ├─ R2.BroadcastStatus() → ROBOT_STATUS: "R2" = "idle"
   ├─ R3.BroadcastStatus() → ROBOT_STATUS: "R3" = "idle"
   └─ ExecuteDeferredSends() → Deliver all status updates to scheduler
```

**Why Status Broadcast is Critical:**

After scheduler reset, all internal state caches are cleared:
```csharp
// SchedulingRuleEngine.cs - Reset() method
_stationStates.Clear();    // Scheduler doesn't know station states
_stationWafers.Clear();    // Scheduler doesn't know what wafers are where
_robotStates.Clear();      // Scheduler doesn't know robot states
_robotWafers.Clear();      // Scheduler doesn't know what robots hold
```

Without the status broadcast, the scheduler would wait forever for status updates that never come, and no wafers would be processed.

**Status Broadcast Implementation:**

```csharp
// OrchestratedForwardPriorityController.cs - SwapToNextCarrierAsync()
var schedulerContext = _orchestrator.GetOrCreateContext("scheduler");

// Extract leaf state names (e.g., "#polisher.empty" → "empty")
_polisher?.BroadcastStatus(schedulerContext);  // Sends STATION_STATUS event
_cleaner?.BroadcastStatus(schedulerContext);
_buffer?.BroadcastStatus(schedulerContext);
_r1?.BroadcastStatus(schedulerContext);        // Sends ROBOT_STATUS event
_r2?.BroadcastStatus(schedulerContext);
_r3?.BroadcastStatus(schedulerContext);

await schedulerContext.ExecuteDeferredSends(); // Deliver all events
```

### Phase 4: Wafer Processing (Same as CARRIER_001)

```
For each wafer (1 to 25):
├─ Scheduler evaluates rules based on broadcast states
├─ First rule matches: Polisher empty, R1 idle, wafers pending
├─ Issue TRANSFER command: LoadPort → R1 → Polisher
└─ [Same processing flow as CARRIER_001]
```

### Phase 5: CARRIER_002 Completion & Swap to CARRIER_003

```
CARRIER_002 Complete:
├─ ✅ Log: "All 25 wafers completed!"
├─ Log Timing Statistics
├─ ⏸ Pause Scheduler
├─ Complete E87 lifecycle (InAccess → Complete → CarrierOut)
└─ SwapToNextCarrierAsync() → Create CARRIER_003
```

---

## CARRIER_N: Endless Loop

The pattern repeats indefinitely for CARRIER_003, CARRIER_004, ..., CARRIER_999, CARRIER_1000, etc.

### Carrier Counter

```csharp
private int _nextCarrierNumber = 1; // Starts at 1, increments forever

// During swap:
_nextCarrierNumber++;  // 2, 3, 4, 5, ...
string newCarrierId = $"CARRIER_{_nextCarrierNumber:D3}";  // CARRIER_002, CARRIER_003, ...
```

**Important**: The counter **never resets**. Even after 1000 carriers, it continues incrementing.

### Endless Processing Loop

```
CARRIER_001 → [Swap] → CARRIER_002 → [Swap] → CARRIER_003 → ... → CARRIER_N
     ↑                                                                    |
     └────────────────────────────────────────────────────────────────────┘
                              Endless Loop
```

Each carrier is completely independent:
- Unique carrier ID (CARRIER_001, CARRIER_002, ...)
- Fresh color scheme for 25 wafers
- Independent E87 lifecycle
- Independent timing statistics
- Independent E90 wafer tracking

---

## Key Synchronization Points

### 1. Pause Scheduler Before Swap

**Why**: Prevents scheduler from issuing new TRANSFER commands during carrier transition.

**Implementation**:
```csharp
_declarativeScheduler?.Pause();  // Set _isPaused = true
```

**Effect**: All rule evaluation is blocked until `Resume()` is called.

### 2. Flush Pending Events

**Why**: Ensures all in-flight transfers complete before scheduler reset.

**Implementation**:
```csharp
var schedulerContext = _orchestrator.GetOrCreateContext("scheduler");
await schedulerContext.ExecuteDeferredSends();
await Task.Delay(200);  // Wait for pending transfers
```

**Effect**: Any queued TRANSFER commands are executed before reset, preventing orphaned wafers.

### 3. Reset Scheduler

**Why**: Clears all state caches and resets wafer queues for new carrier.

**Implementation**:
```csharp
_declarativeScheduler?.Reset(newCarrierId);
```

**Effect**:
- LoadPort.Pending = [1, 2, 3, ..., 25]
- LoadPort.Completed = []
- All station/robot state caches cleared

### 4. Resume Scheduler

**Why**: Allows scheduler to begin processing new carrier.

**Implementation**:
```csharp
_declarativeScheduler?.Resume(newCarrierId);
```

**Effect**: `_isPaused = false`, scheduler can evaluate rules again.

### 5. Broadcast States

**Why**: Informs scheduler of current station/robot availability after reset.

**Implementation**:
```csharp
_polisher?.BroadcastStatus(schedulerContext);
_cleaner?.BroadcastStatus(schedulerContext);
_buffer?.BroadcastStatus(schedulerContext);
_r1?.BroadcastStatus(schedulerContext);
_r2?.BroadcastStatus(schedulerContext);
_r3?.BroadcastStatus(schedulerContext);
await schedulerContext.ExecuteDeferredSends();
```

**Effect**: Scheduler receives 6 status events and can immediately evaluate rules.

---

## State Machine Communication Flow

### Event Flow During Processing

```
Station State Change (e.g., Polisher: empty → occupied)
├─ PolisherMachine: Entry action "reportOccupied"
├─ Action: ctx.RequestSend("scheduler", "STATION_STATUS", {...})
├─ Deferred send queued in OrchestratedContext
├─ ExecuteDeferredSends() called after state transition
├─ EventBusOrchestrator delivers event to "scheduler"
├─ DeclarativeSchedulerMachine receives event
├─ Action "onStationStatus" extracts event data
├─ Delegates to SchedulingRuleEngine.OnStationStatus()
├─ Rule engine updates state cache: _stationStates["polisher"] = "occupied"
└─ Rule engine evaluates all rules in priority order
```

### Event Types

**STATION_STATUS**: Polisher, Cleaner, Buffer state changes
- Data: `{ station: "polisher", state: "empty", wafer: null }`

**ROBOT_STATUS**: R1, R2, R3 state changes
- Data: `{ robot: "R1", state: "idle", wafer: null, waitingFor: null }`

**CARRIER_STATUS**: Carrier E87 state changes
- Data: `{ carrierId: "CARRIER_001", state: "InAccess" }`

**LOADPORT_STATUS**: LoadPort state changes
- Data: `{ station: "LoadPort", state: "Processing" }`

**CARRIER_WAFER_COMPLETED**: Wafer completed and returned to carrier
- Data: `{ waferId: 5 }`

---

## E87 State Diagram

```
┌─────────────┐
│ NotPresent  │ ← Initial state
└──────┬──────┘
       │ CARRIER_DETECTED
       ↓
┌─────────────┐
│WaitingForHost│
└──────┬──────┘
       │ HOST_PROCEED
       ↓
┌─────────────┐
│   Mapping   │ ← Carrier slot mapping
└──────┬──────┘
       │ MAPPING_COMPLETE
       ↓
┌─────────────┐
│MappingVerif.│ ← Verify mapping (auto-transition 600ms)
└──────┬──────┘
       │ Auto
       ↓
┌─────────────┐
│ReadyToAccess│ ← Ready for wafer processing
└──────┬──────┘
       │ START_ACCESS
       ↓
┌─────────────┐
│  InAccess   │ ← Active processing state ✓
└──────┬──────┘
       │ ACCESS_COMPLETE (all wafers done)
       ↓
┌─────────────┐
│  Complete   │ ← Processing complete
└──────┬──────┘
       │ CARRIER_REMOVED
       ↓
┌─────────────┐
│ CarrierOut  │ ← Carrier removed from system
└─────────────┘
```

---

## E90 Wafer State Diagram

```
┌──────────────┐
│ NotAcquired  │ ← Initial state
└──────┬───────┘
       │ ACQUIRE (placed in carrier)
       ↓
┌──────────────┐
│  Acquired    │ ← Wafer in carrier (InCarrier)
└──────┬───────┘
       │ SELECT_FOR_PROCESS (robot picks up)
       ↓
┌──────────────┐
│NeedsProcess. │ ← Ready for processing
└──────┬───────┘
       │ PLACED_IN_PROCESS_MODULE (placed in polisher)
       ↓
┌──────────────┐
│ReadyToProcess│
└──────┬───────┘
       │ START_PROCESS
       ↓
┌──────────────┐
│  InProcess   │ ← Processing (Polishing, Cleaning)
│  .Polishing  │
│  .Cleaning   │
└──────┬───────┘
       │ COMPLETE_CLEANING
       ↓
┌──────────────┐
│  Processed   │ ← Processing complete
└──────┬───────┘
       │ PLACED_IN_CARRIER (returned to carrier)
       ↓
┌──────────────┐
│  Complete    │ ← Wafer complete ✓
└──────────────┘
```

---

## Timing Statistics (Per Carrier)

Each carrier batch gets independent timing statistics:

```
═══════════════════════════════════════════════════════════
                   TIMING STATISTICS
═══════════════════════════════════════════════════════════
Total simulation time: 82450.3 ms (82.45 s)

Configured operation times (per wafer):
  • Polishing:  3000 ms (3.0 s)
  • Cleaning:   3000 ms (3.0 s)
  • Transfer:   300 ms (0.3 s)

Theoretical minimum time for 25 wafers:
  (Assuming perfect parallelization and no overhead)
  • First wafer:         7200 ms (7.2 s)
  • Each additional:     3000 ms (3.0 s) (bottleneck)
  • Total (25 wafers):   79200 ms (79.2 s)

Actual overhead:  3250.3 ms (3.25 s)
Efficiency:       96.1%
═══════════════════════════════════════════════════════════
```

**Efficiency Calculation**:
```
Efficiency = (TheoreticalMin / ActualTime) * 100%
           = (79200 / 82450) * 100%
           = 96.1%
```

---

## Troubleshooting

### Problem: CARRIER_002 wafers not processing after swap

**Symptoms**:
- CARRIER_001 completes successfully
- Carrier swap occurs
- CARRIER_002 reaches InAccess state
- LoadPort docks and starts processing
- Scheduler resumes
- **No TRANSFER commands appear in log**

**Root Cause**: Status broadcast missing or failing

**Solution**:
1. Verify `BroadcastStatus()` methods exist in all machines (Polisher, Cleaner, Buffer, R1, R2, R3)
2. Verify state name extraction works correctly (strip "#polisher.empty" → "empty")
3. Verify `ExecuteDeferredSends()` is called after all broadcasts
4. Check scheduler is not paused (`_isPaused = false`)

### Problem: Wafer stuck in "processing" after carrier swap

**Symptoms**:
- Some wafers from CARRIER_001 still in stations during CARRIER_002 processing
- Wafer colors don't change
- Completed count doesn't reset

**Root Cause**: Flush pending events missing before reset

**Solution**:
1. Add `await schedulerContext.ExecuteDeferredSends()` before reset
2. Add `await Task.Delay(200)` to allow pending transfers to complete
3. Ensure scheduler is paused before executing pending events

### Problem: Carrier counter overflow

**Symptoms**:
- After many carriers, counter exceeds expected range
- Carrier IDs become very long

**Root Cause**: Counter never resets (by design)

**Solution**:
- This is expected behavior
- Counter will increment to 999, then 1000, 1001, etc.
- Format string `{_nextCarrierNumber:D3}` pads with zeros: CARRIER_001, CARRIER_099, CARRIER_1000
- If needed, manually reset `_nextCarrierNumber = 1` during `ResetSimulation()`

---

## Code References

### Key Files

**OrchestratedForwardPriorityController.cs**:
- `InitializeWafers()` - Line 201: Creates CARRIER_001
- `StartSimulation()` - Line 552: Starts CARRIER_001 E87 workflow
- `SwapToNextCarrierAsync()` - Line 1168: Handles carrier swap and CARRIER_N creation

**DeclarativeSchedulerMachine.cs**:
- `Reset()` - Line 213: Resets scheduler for new carrier
- `Resume()` - Line 229: Resumes scheduler after pause
- `Pause()` - Line 221: Pauses scheduler during swap

**SchedulingRuleEngine.cs**:
- `Reset()` - Line 684: Clears state caches and wafer queues
- `Resume()` - Line 722: Unpauses and logs queue status
- `OnStationStatus()` - Updates station state cache
- `OnRobotStatus()` - Updates robot state cache

**Station Machines** (Polisher, Cleaner, Buffer):
- `BroadcastStatus()` - Sends current state to scheduler

**Robot Machines** (R1, R2, R3):
- `BroadcastStatus()` - Sends current state to scheduler

---

## Conclusion

The endless carrier processing system demonstrates:

1. **Robust State Management**: E87 carrier lifecycle + E90 wafer tracking
2. **Event-Driven Architecture**: Pub/Sub pattern with no polling
3. **Seamless Transitions**: Automatic carrier swap with no manual intervention
4. **Independent Batches**: Each carrier is completely isolated
5. **Efficient Synchronization**: Pause/Resume/Broadcast pattern ensures correct state

The system can run indefinitely, processing carrier after carrier with 96%+ efficiency, limited only by system resources.

**Next Steps**:
- Monitor log file for carrier swap transitions
- Verify efficiency stays above 95% across multiple carriers
- Test with different wafer counts (10, 25, 50)
- Test with different timing configurations
