# CMP Pipeline Train Architecture
## 3-Level Hierarchical Scheduler System

### Overview
This document describes a **pipelined wafer train processing system** with a 3-level hierarchical scheduler architecture for continuous flow manufacturing.

---

## Architecture

### 3-Level Scheduler Hierarchy

```
┌─────────────────────────────────────────────────────────────┐
│                  Level 1: Coordinator                        │
│              (System Orchestration)                          │
│  - Dispatches wafers into pipeline                          │
│  - Monitors pipeline state                                   │
│  - Controls overall throughput                               │
└──────────────────┬──────────────────────────────────────────┘
                   │
         ┌─────────┴──────────┐
         │                    │
         ▼                    ▼
┌─────────────────┐  ┌─────────────────┐
│   Level 2:      │  │   Level 2:      │
│  Wafer N        │  │  Wafer N+1      │ ... (Multiple Instances)
│  (SemiFlow)     │  │  (SemiFlow)     │
│                 │  │                 │
│ FOUP → P1 →     │  │ FOUP → P1 →     │
│   P2 → FOUP     │  │   P2 → FOUP     │
└────────┬────────┘  └────────┬────────┘
         │                    │
         └────────┬───────────┘
                  │
    ┌─────────────┼─────────────┬──────────────┐
    │             │             │              │
    ▼             ▼             ▼              ▼
┌────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐
│ Level 3│  │ Level 3 │  │ Level 3 │  │ Level 3 │
│ Robot  │  │ Platen1 │  │ Platen2 │  │  FOUP   │
│Scheduler│ │Scheduler│  │Scheduler│  │Scheduler│
└────────┘  └─────────┘  └─────────┘  └─────────┘
```

---

## Level 1: Coordinator Scheduler

### Responsibilities
- **Wafer Dispatching**: Controls when new wafers enter the pipeline
- **Pipeline Management**: Monitors pipeline capacity and state
- **Throughput Control**: Ensures optimal system utilization
- **Resource Coordination**: Oversees all Level 3 resource schedulers

### Key Features
- **Pipeline Capacity Control**: Prevents overloading the system
- **Wafer Train Formation**: Creates continuous flow of wafers
- **Backpressure Handling**: Responds to downstream bottlenecks

### State Machine Flow
```
system_init
    ↓
check_system_ready ←──┐
    ↓ [ready]         │ [not ready]
start_coordinator     │
    ↓                 │
coordinator_loop ─────┘
    ├─ dispatch_next_wafer
    ├─ update_pipeline_state
    └─ check_pipeline_capacity
         ├─ [full] → wait_pipeline_space
         └─ [space] → continue_dispatch
    ↓
wait_pipeline_complete
    ↓
finalize_coordinator
    ↓
coordinator_complete
```

---

## Level 2: Wafer Scheduler (SemiFlow)

### Responsibilities
- **Individual Wafer Workflow**: Manages single wafer lifecycle
- **Resource Requests**: Requests robot and station resources
- **Stage Progression**: Coordinates movement through pipeline stages
- **Conflict Resolution**: Handles resource contention

### Pipeline Stages

#### Stage 1: Load from FOUP
```
request_robot_for_load
    ↓
wait_robot_available_load ←──┐
    ↓ [available]             │ [busy]
acquire_robot_load            │
    ↓                         │
pick_from_foup                │
    ↓                         │
move_to_platen1               │
    ↓                         │
wait_platen1_ready ───────────┘
    ↓ [ready]
place_on_platen1
    ↓
release_robot_after_load
```

#### Stage 2: Process on Platen1
```
start_platen1_process
    ↓
wait_platen1_complete ←──┐
    ↓ [complete]         │ [processing]
platen1_done             │
    │                    │
    └────────────────────┘
```

#### Stage 3: Transfer to Platen2
```
request_robot_for_transfer
    ↓
wait_robot_available_transfer ←──┐
    ↓ [available]                 │ [busy]
acquire_robot_transfer            │
    ↓                             │
pick_from_platen1                 │
    ↓                             │
move_to_platen2                   │
    ↓                             │
wait_platen2_ready ───────────────┘
    ↓ [ready]
place_on_platen2
    ↓
release_robot_after_transfer
```

#### Stage 4: Process on Platen2
```
start_platen2_process
    ↓
wait_platen2_complete ←──┐
    ↓ [complete]         │ [processing]
platen2_done             │
    │                    │
    └────────────────────┘
```

#### Stage 5: Unload to FOUP
```
request_robot_for_unload
    ↓
wait_robot_available_unload ←──┐
    ↓ [available]               │ [busy]
acquire_robot_unload            │
    ↓                           │
pick_from_platen2               │
    ↓                           │
move_to_foup_unload             │
    ↓                           │
place_in_foup                   │
    ↓                           │
release_robot_after_unload      │
    ↓                           │
wafer_complete                  │
```

### Key Features
- **Asynchronous Execution**: Each wafer instance runs independently
- **Resource Locking**: Acquire/Release pattern for shared resources
- **Retry Mechanisms**: Handles busy resources gracefully
- **Stage Isolation**: Each stage is independent for pipelining

---

## Level 3: Resource Schedulers

### Robot Scheduler

#### Responsibilities
- **Task Queue Management**: Queues robot requests from multiple wafers
- **Priority Handling**: Processes requests in optimal order
- **Movement Coordination**: Executes physical movements
- **Utilization Tracking**: Monitors robot efficiency

#### State Machine
```
robot_init
    ↓
robot_idle (loop while systemRunning)
    ├─ check_robot_request
    │    ├─ [has request] → process_robot_request
    │    │                      ↓
    │    │                  execute_robot_task
    │    │                      ↓
    │    │                  update_robot_state
    │    │                      ↓
    │    │                  (back to check)
    │    │
    │    └─ [no request] → robot_wait → (back to check)
    ↓
robot_shutdown
    ↓
robot_final
```

### Platen1 Scheduler

#### Responsibilities
- **Processing Queue**: Manages wafers waiting for Platen1
- **Process Execution**: Controls CMP processing on Platen1
- **Status Updates**: Signals completion to waiting wafers
- **Utilization Tracking**: Monitors Platen1 efficiency

#### State Machine
```
platen1_init
    ↓
platen1_idle (loop while systemRunning)
    ├─ check_platen1_wafer
    │    ├─ [has wafer] → platen1_processing
    │    │                      ↓
    │    │                  platen1_process_delay (5s)
    │    │                      ↓
    │    │                  platen1_complete_wafer
    │    │                      ↓
    │    │                  update_platen1_state
    │    │                      ↓
    │    │                  (back to check)
    │    │
    │    └─ [no wafer] → platen1_wait → (back to check)
    ↓
platen1_shutdown
    ↓
platen1_final
```

### Platen2 Scheduler

Same structure as Platen1, managing Platen2 processing.

---

## Pipelined Execution Model

### Time-based Pipeline View

```
Time →

T0:  W1: FOUP→P1
T1:  W1: ──P1──    W2: FOUP→P1
T2:  W1: P1→P2     W2: ──P1──    W3: FOUP→P1
T3:  W1: ──P2──    W2: P1→P2     W3: ──P1──    W4: FOUP→P1
T4:  W1: P2→FOUP   W2: ──P2──    W3: P1→P2     W4: ──P1──    W5: FOUP→P1
T5:                W2: P2→FOUP   W3: ──P2──    W4: P1→P2     W5: ──P1──
T6:                               W3: P2→FOUP   W4: ──P2──    W5: P1→P2
...

Legend:
FOUP→P1: Loading from FOUP to Platen1
──P1──:  Processing on Platen1
P1→P2:   Transfer from Platen1 to Platen2
──P2──:  Processing on Platen2
P2→FOUP: Unloading from Platen2 to FOUP
```

### Pipeline Characteristics

- **Maximum Throughput**: Limited by slowest stage (typically processing)
- **Pipeline Depth**: Up to 5 stages occupied simultaneously
- **Latency**: ~4 stages (Load + P1 + Transfer + P2 + Unload)
- **Resource Utilization**: Overlapped operations maximize efficiency

---

## Resource Contention & Scheduling

### Robot Contention
Multiple wafers may request robot simultaneously:
1. Wafer finishing Platen1 (needs transfer to Platen2)
2. Wafer finishing Platen2 (needs unload to FOUP)
3. New wafer (needs load from FOUP)

**Solution**: Robot scheduler maintains FIFO queue or priority-based dispatch

### Platen Contention
- Platen1: Only one wafer can process at a time
- Platen2: Only one wafer can process at a time

**Solution**: Wafers wait (retry loop) until platen becomes available

---

## Key Actions & Guards

### Coordinator Actions
- `initializeCoordinator()` - Initialize system
- `startCoordinator()` - Begin operation
- `dispatchNextWafer()` - Create new wafer instance
- `updatePipelineState()` - Update metrics
- `monitorPipeline()` - Check pipeline status
- `finalizeCoordinator()` - Cleanup

### Coordinator Guards
- `allResourcesReady` - All resources initialized
- `hasMoreWafersToDispatch` - More wafers to process
- `pipelineFull` - Pipeline at capacity
- `pipelineNotEmpty` - Wafers still in pipeline

### Wafer Actions
- `waferInitialized()` - Mark wafer ready
- `requestRobotForLoad/Transfer/Unload()` - Request robot
- `acquireRobot()` - Lock robot resource
- `releaseRobot()` - Unlock robot resource
- `robotPickFromFoup/Platen1/Platen2()` - Pick operations
- `robotPlaceOnPlaten1/Platen2/InFoup()` - Place operations
- `robotMoveToPlaten1/Platen2/Foup()` - Movement
- `startPlaten1/2Processing()` - Begin processing
- `logPlaten1/2Complete()` - Log completion
- `markWaferComplete()` - Finalize wafer

### Wafer Guards
- `robotAvailable` - Robot is idle
- `platen1Available` - Platen1 is idle
- `platen2Available` - Platen2 is idle
- `platen1ProcessComplete` - Platen1 done processing
- `platen2ProcessComplete` - Platen2 done processing

### Resource Actions
- `initializeRobot/Platen1/Platen2()` - Initialize resource
- `processRobotRequest()` - Handle robot request
- `executeRobotTask()` - Execute robot movement
- `updateRobotUtilization()` - Update metrics
- `processOnPlaten1/2()` - Execute CMP processing
- `completePlaten1/2Processing()` - Signal done
- `updatePlaten1/2Utilization()` - Update metrics
- `shutdownRobot/Platen1/Platen2()` - Cleanup

### Resource Guards
- `hasRobotRequest` - Robot request pending
- `platen1HasWafer` - Wafer on Platen1
- `platen2HasWafer` - Wafer on Platen2
- `systemRunning` - System still active

---

## Benefits of This Architecture

### 1. **True Pipelining**
- Multiple wafers in different stages simultaneously
- Maximum resource utilization
- Continuous flow manufacturing

### 2. **Scalability**
- Easy to add more platens (just add resource scheduler)
- Wafer scheduler template can be instantiated N times
- Coordinator handles any number of wafers

### 3. **Modularity**
- Each level has clear responsibilities
- Levels communicate through well-defined interfaces
- Easy to modify individual schedulers

### 4. **Flexibility**
- Can change processing times without affecting flow
- Can add priorities to wafers
- Can optimize robot scheduling algorithm

### 5. **Observability**
- Each level reports its own metrics
- Pipeline state visible at coordinator level
- Easy to diagnose bottlenecks

---

## Implementation Considerations

### Wafer Instantiation
The coordinator dynamically creates wafer scheduler instances:
```csharp
// Pseudo-code
foreach (var waferToDispatch in lotWafers)
{
    var waferInstance = CreateWaferSchedulerInstance(waferToDispatch);
    waferInstance.Start(); // Async execution
    activePipeline.Add(waferInstance);
}
```

### Resource Synchronization
Resources use locks or semaphores:
```csharp
// Pseudo-code
class RobotScheduler
{
    private SemaphoreSlim robotLock = new(1, 1);
    private Queue<RobotRequest> requests = new();

    public async Task<bool> RequestRobot(WaferId wafer)
    {
        requests.Enqueue(new RobotRequest(wafer));
        return await robotLock.WaitAsync(timeout);
    }

    public void ReleaseRobot()
    {
        robotLock.Release();
    }
}
```

### Pipeline Monitoring
Coordinator tracks pipeline state:
```csharp
// Pseudo-code
class PipelineState
{
    public int WafersInFoup { get; set; }
    public int WafersOnPlaten1 { get; set; }
    public int WafersInTransit { get; set; }
    public int WafersOnPlaten2 { get; set; }
    public int WafersCompleted { get; set; }

    public int TotalInPipeline =>
        WafersOnPlaten1 + WafersInTransit + WafersOnPlaten2;

    public bool CanDispatch => TotalInPipeline < MaxPipelineDepth;
}
```

---

## Performance Metrics

### Throughput
```
Throughput = WafersCompleted / TotalTime
Ideal = 1 / MaxStageTime (if fully pipelined)
```

### Utilization
```
RobotUtilization = RobotBusyTime / TotalTime
Platen1Utilization = Platen1ProcessTime / TotalTime
Platen2Utilization = Platen2ProcessTime / TotalTime
```

### Pipeline Efficiency
```
PipelineEfficiency = ActualThroughput / IdealThroughput
Ideal = 100% when pipeline is always full
```

### Cycle Time
```
WaferCycleTime = TimeExitFoup - TimeEnterFoup
Includes: Load + P1Process + Transfer + P2Process + Unload
```

---

## Next Steps

1. **Implement Simulator**: Create C# simulator supporting this architecture
2. **Add Visualization**: Show pipeline state in real-time
3. **Optimize Robot Scheduling**: Implement smart queueing algorithms
4. **Add Error Handling**: Handle resource failures and recovery
5. **Performance Tuning**: Adjust pipeline depth for optimal throughput

---

## Example Configuration

```json
{
  "lot": {
    "id": "LOT001",
    "totalWafers": 25,
    "pipelineDepth": 5
  },
  "resources": {
    "robot": {
      "pickTime": 2000,
      "moveTime": 3000,
      "placeTime": 2000
    },
    "platen1": {
      "processTime": 45000,
      "setupTime": 5000
    },
    "platen2": {
      "processTime": 45000,
      "setupTime": 5000
    }
  },
  "coordinator": {
    "dispatchInterval": 10000,
    "maxPipelineDepth": 5
  }
}
```

---

This architecture enables **true continuous flow manufacturing** with optimal resource utilization and scalability!
