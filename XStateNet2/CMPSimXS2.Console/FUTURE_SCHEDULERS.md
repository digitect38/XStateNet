# 🚧 Future Scheduler Implementations

## Overview

Two additional schedulers have been requested but require significant implementation effort:

1. **XS1-Legacy (event)** - Legacy XStateNet V1 for historical comparison
2. **XS2-Sync-Pipeline (batch)** - XStateNet2 version of synchronized pipeline

---

## 1. XS1-Legacy (event) - Legacy XStateNet V1

### Goal
Add legacy XStateNet (V1/V5) to demonstrate performance evolution from V1 → V2.

### Current Status
- ❌ Not implemented
- ✅ Project reference added: `<ProjectReference Include="..\..\XStateNet5Impl\XStateNet.csproj" />`
- ✅ XS2 naming complete (ready for XS1 comparison)

### Implementation Requirements

#### Complexity: **HIGH**
The main challenge is bridging two different actor systems:

**XStateNet V1:**
- Uses its own internal actor system (channel-based)
- API: `IStateMachine.SendAsync(string eventName, object? eventData)`
- Namespace: `XStateNet`

**Current Test Harness:**
- Uses Akka.NET actors (`IActorRef`)
- All robots and scheduler communicate via Akka.NET messages

#### Required Components

**1. Actor System Adapter**
```csharp
public class AkkaToXS1Adapter
{
    private readonly Akka.Actor.IActorRef _akkaActor;
    private readonly XStateNet.IStateMachine _xs1Machine;

    // Convert Akka.NET messages → XStateNet V1 events
    // Convert XStateNet V1 events → Akka.NET messages
}
```

**2. Scheduler Implementation**
```csharp
public class RobotSchedulerXS1Legacy : IRobotScheduler
{
    private readonly XStateNet.IStateMachine _machine;

    public RobotSchedulerXS1Legacy(...)
    {
        // Create XStateNet V1 machine
        _machine = XStateNet.StateMachineFactory.CreateFromScript(...);
    }

    public void RegisterRobot(string robotId, IActorRef robotActor)
    {
        // Wrap Akka.NET actor in adapter
        // Register with XS1 machine
    }
}
```

**3. Message Translation**
- `TransferRequest` → XStateNet V1 event format
- Robot state updates → XStateNet V1 compatible
- Response handling → Akka.NET message format

#### Estimated Effort
- **Time**: 4-6 hours
- **Complexity**: High (two actor system integration)
- **Risk**: Medium (API compatibility issues)

#### Expected Results

Once implemented, the test would show:

```
Testing: Lock (polling)           15.73s  ← Traditional
Testing: Actor (event)            15.69s  ← Pure Akka.NET

Testing: XS1-Legacy (event)       ??.??s  ← V1 (Channel-based)  🆕
Testing: XS2-Dict (event)         15.70s  ← V2 baseline
Testing: XS2-Frozen (event)       15.68s  ← V2 optimized
Testing: XS2-Array (event)        15.69s  ← V2 max performance
```

**Value**:
- ✅ Demonstrates V1 → V2 improvement
- ✅ Shows channel-based vs Akka.NET performance
- ✅ Validates migration benefits

#### Implementation Plan

See `ADD_LEGACY_XSTATENET_PLAN.md` for detailed implementation steps.

---

## 2. XS2-Sync-Pipeline (batch) - XStateNet2 Sync-Pipeline

### Goal
Demonstrate XStateNet2 can handle synchronized batch execution patterns.

### Current Status
- ❌ Not implemented
- ✅ Pure actor version exists: `SynchronizedPipelineScheduler.cs` (449 lines)
- ✅ XS2 template available: `RobotSchedulerXState.cs`

### Implementation Requirements

#### Complexity: **MEDIUM**
Convert existing actor-based Sync-Pipeline to use XStateNet2 state machine.

**Current Architecture** (Pure Actor):
```csharp
public class SynchronizedPipelineScheduler
{
    private readonly IActorRef _schedulerActor; // Pure actor

    // Batch coordination logic in actor
    private class SyncSchedulerActor : ReceiveActor
    {
        // Wait for all robots idle
        // Execute batch transfers
        // Synchronize pipeline stages
    }
}
```

**Target Architecture** (XStateNet2):
```csharp
public class RobotSchedulerXS2SyncPipeline
{
    private readonly IActorRef _machine; // XState machine

    // State machine JSON definition
    private const string MachineJson = @"{
        'id': 'syncPipeline',
        'initial': 'collecting',
        'states': {
            'collecting': {
                'on': {
                    'REQUEST_TRANSFER': { 'actions': ['queueTransfer'] },
                    'ALL_ROBOTS_IDLE': { 'target': 'synchronizing' }
                }
            },
            'synchronizing': {
                'entry': ['executeBatchTransfers'],
                'on': {
                    'BATCH_COMPLETE': { 'target': 'collecting' }
                }
            }
        }
    }";
}
```

#### Required Components

**1. XState Machine Definition**
- States: `collecting`, `synchronizing`, `executing`
- Events: `REQUEST_TRANSFER`, `ALL_ROBOTS_IDLE`, `BATCH_COMPLETE`
- Actions: `queueTransfer`, `executeBatchTransfers`, `resetSync`

**2. Batch Coordination Logic**
```csharp
private void ExecuteBatchTransfersAction()
{
    // Group transfers by robot
    var batchR1 = _context.PendingRequests.Where(r => CanRobotHandle("Robot 1", r));
    var batchR2 = _context.PendingRequests.Where(r => CanRobotHandle("Robot 2", r));
    var batchR3 = _context.PendingRequests.Where(r => CanRobotHandle("Robot 3", r));

    // Execute all in parallel
    foreach (var batch in new[] { batchR1, batchR2, batchR3 })
    {
        if (batch.Any())
        {
            ExecuteTransfer(batch.First(), robotId);
        }
    }
}
```

**3. Synchronization Detection**
```csharp
private void CheckAllRobotsIdle()
{
    if (_context.RobotStates.Values.All(r => r.State == "idle"))
    {
        _machine.Tell(new SendEvent("ALL_ROBOTS_IDLE"));
    }
}
```

#### Estimated Effort
- **Time**: 2-3 hours
- **Complexity**: Medium (adapt existing logic to XState)
- **Risk**: Low (well-understood pattern)

#### Expected Results

```
Testing: Sync-Pipeline (batch)       15.70s  ← Pure actor
Testing: XS2-Sync-Pipeline (batch)   15.xx s  ← XStateNet2  🆕
```

**Value**:
- ✅ Shows XStateNet2 handles batch coordination
- ✅ Demonstrates declarative sync-pipeline logic
- ✅ Completes XS2 architecture coverage

#### Benefits of XS2 Version

1. **Declarative State Machine**
   ```json
   {
     "collecting": { "on": { "ALL_ROBOTS_IDLE": "synchronizing" } },
     "synchronizing": { "entry": ["executeBatchTransfers"] }
   }
   ```

2. **Visual State Chart Possible**
   - See synchronization flow clearly
   - Debug batch execution states

3. **Better Maintainability**
   - State transitions explicit
   - Easier to add new sync strategies

---

## 3. Implementation Priority

### High Priority (Immediate Value)
1. **XS2-Sync-Pipeline**
   - Medium effort, low risk
   - Completes XS2 coverage
   - Shows batch coordination capability

### Medium Priority (Historical Value)
2. **XS1-Legacy**
   - High effort, medium risk
   - Demonstrates V1 → V2 evolution
   - Requires actor system adapter

---

## 4. Current Test Coverage

### Implemented (14 schedulers):
```
✅ Lock (polling)
✅ Actor (event)
✅ XS2-Dict (event)
✅ XS2-Frozen (event)
✅ XS2-Array (event)
✅ Autonomous (polling)
✅ Autonomous-Array (polling)
✅ Autonomous-Event (event)
✅ Actor-Mailbox (event)
✅ Ant-Colony (event)
✅ XS2-PubSub-Dedicated (multi)
✅ PubSub-Single (one)
✅ XS2-PubSub-Array (one)
✅ Sync-Pipeline (batch) ← Pure actor
```

### Planned (2 schedulers):
```
🚧 XS1-Legacy (event) ← V1 comparison
🚧 XS2-Sync-Pipeline (batch) ← XS2 batch coordination
```

---

## 5. Next Steps

### Option A: Quick Value (XS2-Sync-Pipeline Only)
1. Create `RobotSchedulerXS2SyncPipeline.cs`
2. Adapt Sync-Pipeline logic to XStateNet2
3. Add to test harness
4. Run benchmark

**Result**: 15 schedulers, complete XS2 coverage

### Option B: Complete Comparison (Both)
1. Create `RobotSchedulerXS1Legacy.cs` with actor adapter
2. Create `RobotSchedulerXS2SyncPipeline.cs`
3. Add both to test harness
4. Run comprehensive benchmark

**Result**: 16 schedulers, V1 vs V2 comparison

### Option C: Documentation Only (Current)
- Document implementation requirements
- Provide clear specifications
- Ready for future implementation

**Result**: Clear roadmap for future work

---

## 6. Recommendation

**Implement XS2-Sync-Pipeline first** (Option A):
- ✅ Immediate value (completes XS2 coverage)
- ✅ Lower risk (medium complexity)
- ✅ Demonstrates XStateNet2 batch capability
- ✅ Can add XS1-Legacy later

**Defer XS1-Legacy** until:
- Actor adapter framework is built
- More time available for integration work
- Clear use case for V1 vs V2 comparison data

---

## 7. Summary

**Current Status**: 14 schedulers implemented and tested
**XS2 Coverage**: 5/6 patterns (missing: batch coordination)
**V1 vs V2 Comparison**: Not yet implemented

**Next Action**: Choose implementation priority based on project goals.

---

**Last Updated**: 2025-11-02
**Status**: Documentation complete, awaiting implementation decision
