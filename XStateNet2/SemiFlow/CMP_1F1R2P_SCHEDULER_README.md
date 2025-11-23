# CMP 1F1R2P Scheduler - SemiFlow Implementation

## System Overview

**Equipment Configuration:**
- 1 FOUP (Front Opening Unified Pod) - 25 wafer capacity
- 1 Robot with dual arms
- 2 Platens (CMP processing stations)

**Processing Modes:**
1. **1-Step Process**: FOUP → Robot → Platen (1 or 2) → Robot → FOUP
2. **2-Step Process**: FOUP → Robot → Platen 1 → Robot → Platen 2 → Robot → FOUP

## Architecture

### Multi-Lane Design

The scheduler uses **4 parallel lanes** for coordinated operation:

1. **wafer_scheduler** (Priority 1) - Main wafer processing workflow
2. **platen1_manager** (Priority 2) - Platen 1 operations and monitoring
3. **platen2_manager** (Priority 2) - Platen 2 operations and monitoring
4. **robot_manager** (Priority 3) - Robot operations and utilization tracking

### Workflow States

**XState Machine Generated:**
- **Total States**: 111
- **Type**: Parallel (multi-lane)
- **Machine ID**: CMP_1F1R2P_Scheduler

## Processing Workflows

### 2-Step Processing Flow

```
┌─────────┐     ┌──────┐     ┌─────────┐     ┌──────┐     ┌─────────┐     ┌──────┐     ┌─────────┐
│  FOUP   │────▶│Robot │────▶│Platen 1 │────▶│Robot │────▶│Platen 2 │────▶│Robot │────▶│  FOUP   │
│ (Pick)  │     │(Move)│     │(Process)│     │(Move)│     │(Process)│     │(Move)│     │(Place)  │
└─────────┘     └──────┘     └─────────┘     └──────┘     └─────────┘     └──────┘     └─────────┘
```

**Steps:**
1. Robot picks wafer from FOUP
2. Robot moves to first available platen
3. Place wafer on Platen 1
4. Robot returns home (available for other tasks)
5. Platen 1 processes wafer (~60 seconds)
6. Robot picks wafer from Platen 1
7. Robot moves to Platen 2
8. Place wafer on Platen 2
9. Robot returns home
10. Platen 2 processes wafer (~60 seconds)
11. Robot picks wafer from Platen 2
12. Robot returns wafer to FOUP
13. Mark wafer complete

### 1-Step Processing Flow

```
┌─────────┐     ┌──────┐     ┌─────────────┐     ┌──────┐     ┌─────────┐
│  FOUP   │────▶│Robot │────▶│Platen (1/2) │────▶│Robot │────▶│  FOUP   │
│ (Pick)  │     │(Move)│     │  (Process)  │     │(Move)│     │(Place)  │
└─────────┘     └──────┘     └─────────────┘     └──────┘     └─────────┘
```

**Steps:**
1. Robot picks wafer from FOUP
2. Robot moves to first available platen (1 or 2)
3. Place wafer on selected platen
4. Robot returns home
5. Platen processes wafer (~60 seconds)
6. Robot picks wafer from platen
7. Robot returns wafer to FOUP
8. Mark wafer complete

## Key Features

### Dynamic Process Selection

```json
{
  "id": "process_decision",
  "type": "branch",
  "cases": [
    {
      "when": "isTwoStepProcess",
      "steps": [/* 2-step workflow */]
    }
  ],
  "otherwise": [/* 1-step workflow */]
}
```

The system determines processing type per wafer based on `isTwoStepProcess` guard condition.

### Platen Selection Strategy

**2-Step Process:**
- Step 1: Select first available platen (`selectAvailablePlaten`)
- Step 2: Select the other platen (`selectOtherPlaten`)

**1-Step Process:**
- Select first available platen (Platen 1 or Platen 2)

### Resource Management

**Robot Acquisition:**
```json
{
  "type": "useStation",
  "role": "robot",
  "waitForAvailable": true,
  "maxWaitTime": 30000
}
```

**Benefits:**
- Prevents robot contention
- Ensures sequential robot operations
- Automatic retry with 30-second timeout

### Error Handling

**Process Timeouts:**
- Platen processing: 90 seconds timeout
- Robot movements: 10 seconds timeout
- Wafer placement: 5 seconds timeout

**Retry Strategy (1-Step Process):**
```json
{
  "retry": {
    "count": 2,
    "delay": 5000,
    "strategy": "fixed",
    "retryOn": ["ProcessError", "TimeoutError"]
  }
}
```

**Global Error Handlers:**
- Critical error detection
- System pause on error
- Operator notification
- Error logging

## Performance Metrics

### Collected Metrics

1. **cycle_time** - Total time per wafer (timer)
2. **throughput** - Wafers processed per hour (counter)
3. **platen1_utilization** - Platen 1 usage percentage (gauge)
4. **platen2_utilization** - Platen 2 usage percentage (gauge)
5. **robot_utilization** - Robot usage percentage (gauge)

### Metric Collection Points

- After each platen process completion
- On wafer completion
- Continuous utilization tracking in manager lanes

## Station Properties

### FOUP1
```json
{
  "capacity": 25,
  "position": "loadPort1",
  "waferCount": 25
}
```

### ROBOT1
```json
{
  "capacity": 2,
  "arm1": null,
  "arm2": null,
  "position": "home"
}
```

### PLATEN1 & PLATEN2
```json
{
  "capacity": 1,
  "temperature": 25,
  "pressure": 0,
  "rpm": 0,
  "processStep": "polish"
}
```

## System Events

### Automated Events
- **WAFER_READY** - Wafer available for processing
- **ROBOT_AVAILABLE** - Robot ready for next task
- **PLATEN_AVAILABLE** - Platen ready for wafer
- **PROCESS_COMPLETE** - Platen finished processing
- **WAFER_PROCESSED** - Wafer completed all steps
- **LOT_COMPLETE** - All 25 wafers processed

### Error Events
- **ERROR_OCCURRED** - General error condition
- **EMERGENCY_STOP** - Emergency shutdown trigger

## Configuration Constants

```json
{
  "MAX_WAFERS": 25,
  "ROBOT_MOVE_TIME": 2000,
  "PLATEN_PROCESS_TIME": 60000,
  "WAFER_LOAD_TIME": 3000,
  "WAFER_UNLOAD_TIME": 3000,
  "BUFFER_SIZE": 1
}
```

## State Variables

```json
{
  "processedWafers": 0,
  "totalWafers": 25,
  "activeWafers": [],
  "completedWafers": [],
  "errorCount": 0,
  "cycleStartTime": 0,
  "currentLot": "LOT001"
}
```

## Execution Flow

### Initialization Phase
1. `initializeSystem` - Check all stations
2. `startLotProcessing` - Begin lot (25 wafers)

### Main Processing Loop
```
while (hasMoreWafers) {
  1. getNextWaferFromFoup
  2. determineProcessType
  3. if (isTwoStepProcess) {
       2-step workflow
     } else {
       1-step workflow
     }
  4. incrementProcessedCount
  5. emitEvent(WAFER_PROCESSED)
}
```

### Finalization Phase
1. `finalizeLotProcessing` - Complete lot
2. `emitEvent(LOT_COMPLETE)` - Notify completion
3. `cleanupSystem` - Reset for next lot

## Parallel Lane Coordination

### Lane Interactions

**wafer_scheduler** (main lane):
- Controls overall wafer flow
- Requests robot via `useStation`
- Emits events for platen/robot managers

**platen1_manager / platen2_manager**:
- Listen for `WAFER_ON_PLATEN1/2` events
- Track platen state and utilization
- Independent monitoring loops

**robot_manager**:
- Listens for `ROBOT_TASK` events
- Tracks robot utilization
- Manages robot state

### Event-Driven Synchronization

```json
{
  "id": "wait_for_wafer_p1",
  "type": "onEvent",
  "event": "WAFER_ON_PLATEN1",
  "once": false,
  "steps": [
    {
      "id": "update_platen1_state",
      "type": "action",
      "action": "updatePlatenState"
    }
  ]
}
```

## Optimization Strategies

### Throughput Maximization

**2-Step Process:**
- While Platen 1 processes, robot can:
  - Return home (idle)
  - Pick next wafer (pipelining)
- While Platen 2 processes, robot can:
  - Unload from Platen 1
  - Start next wafer cycle

**1-Step Process:**
- Use both platens independently
- Load balancing between Platen 1 and 2
- Parallel processing of multiple wafers

### Expected Throughput

**1-Step Process:**
- Process time: ~60 seconds per wafer per platen
- Robot transfer time: ~10 seconds total
- **Throughput**: ~2 wafers/minute (120 wafers/hour) with 2 platens

**2-Step Process:**
- Process time: ~120 seconds per wafer (both platens)
- Robot transfer time: ~20 seconds total
- **Throughput**: ~1 wafer/2.5 minutes (24 wafers/hour)

**Mixed Mode:**
- Can interleave 1-step and 2-step processes
- Dynamic optimization based on wafer requirements

## Implementation Actions

The following actions need to be implemented in the execution engine:

### System Actions
- `initializeSystem()`
- `startLotProcessing()`
- `finalizeLotProcessing()`
- `cleanupSystem()`

### Wafer Management
- `getNextWaferFromFoup()`
- `determineProcessType()` → boolean (1-step vs 2-step)
- `markWaferComplete()`
- `incrementProcessedCount()`

### Robot Operations
- `pickWaferFromFoup()`
- `pickWaferFromPlaten()`
- `placeWaferOnPlaten()`
- `placeWaferInFoup()`
- `moveRobotToPlaten()`
- `moveRobotToFoup()`
- `robotReturnHome()`
- `executeRobotMovement()`

### Platen Operations
- `selectAvailablePlaten()` → platen ID
- `selectOtherPlaten()` → platen ID (for 2-step)
- `processWaferOnPlaten()` → async operation
- `initializePlaten1()`
- `initializePlaten2()`
- `updatePlatenState()`

### Monitoring & Metrics
- `updateUtilization(station)` → percentage
- `trackMetric(name, value)`
- `logError(error)`
- `handleProcessTimeout()`
- `handleGlobalError()`
- `handleGlobalTimeout()`
- `pauseSystem()`

### Guards
- `hasMoreWafers()` → boolean
- `isTwoStepProcess()` → boolean
- `platenProcessComplete()` → boolean
- `systemRunning()` → boolean
- `isCriticalError()` → boolean

## Usage Example

### Converting to XState

```bash
cd SemiFlow/SemiFlow.CLI
dotnet run ../../cmp_1f1r2p_semiflow.json ../../cmp_1f1r2p_xstate.json
```

### Running with XStateNet2

```csharp
using XStateNet2.Core;
using System.Text.Json;

// Load XState machine
var json = File.ReadAllText("cmp_1f1r2p_xstate.json");
var machine = JsonSerializer.Deserialize<XStateMachineScript>(json);

// Create interpreter
var interpreter = new Interpreter(machine);

// Register action implementations
interpreter.RegisterAction("initializeSystem", (context, event) => {
    // Initialize FOUP, Robot, Platens
});

interpreter.RegisterAction("getNextWaferFromFoup", (context, event) => {
    // Pick next wafer
});

// Register guards
interpreter.RegisterGuard("hasMoreWafers", (context, event) => {
    return context["processedWafers"] < 25;
});

interpreter.RegisterGuard("isTwoStepProcess", (context, event) => {
    // Determine based on wafer properties
    return DetermineProcessType();
});

// Start processing
interpreter.Start();
```

## Advantages of This Design

### 1. Flexibility
- Supports both 1-step and 2-step processes
- Dynamic platen selection
- Configurable per wafer

### 2. Efficiency
- Parallel platen usage
- Robot pipelining opportunities
- Minimized idle time

### 3. Robustness
- Comprehensive error handling
- Timeout protection
- Retry mechanisms
- Global error handlers

### 4. Observability
- Real-time utilization metrics
- Process time tracking
- Throughput monitoring
- Event-driven status updates

### 5. Maintainability
- Declarative SemiFlow DSL
- Clear separation of concerns
- Modular lane design
- Well-documented workflow

## Future Enhancements

### Potential Optimizations

1. **Pre-fetching**: Robot picks next wafer while current wafer processes
2. **Buffering**: Add buffer station between platens for 2-step process
3. **Smart Scheduling**: Prioritize 1-step wafers to maximize throughput
4. **Predictive Maintenance**: Track platen usage for maintenance scheduling
5. **Dynamic Timing**: Adjust process times based on wafer type

### Additional Features

1. **Recipe Management**: Different platen settings per wafer type
2. **Lot Tracking**: Multiple lots with different priorities
3. **Quality Control**: Post-process inspection stations
4. **Cleaning Cycles**: Periodic platen cleaning workflows
5. **Advanced Metrics**: OEE (Overall Equipment Effectiveness), MTBF, MTTR

## File Locations

- **SemiFlow Definition**: `cmp_1f1r2p_semiflow.json`
- **XState Output**: `cmp_1f1r2p_xstate.json`
- **This Documentation**: `CMP_1F1R2P_SCHEDULER_README.md`

## Related Documentation

- [SemiFlow Schema 1.0](SemiFlow_Schema_1_0.json)
- [Conversion Examples](CONVERSION_EXAMPLE.md)
- [Test Suite](SemiFlow.Tests/TEST_SUMMARY.md)
- [Original 1F1R1P Example](cmp_1f1r1p_semiflow.json)

---

**Generated with**: Claude Code + SemiFlow to XState Converter
**Version**: 1.0.0
**Last Updated**: 2025-11-19
