# Real-Time Monitoring Test Issue

## Problem

`RealTimeMonitoringTests.Monitor_CapturesActions` is failing with timeout.

## Root Cause

**Architecture mismatch:**

1. Test uses `ExtendedPureStateMachineFactory.CreateWithChannelGroup()` to create **orchestrated** machines
2. Actions are executed **asynchronously** through the `EventBusOrchestrator`
3. `StateMachineMonitor` expects **synchronous** action execution on the underlying `StateMachine`
4. Monitor never receives the `ActionExecuted` events because actions run through orchestrator, not directly on the machine

## Code Analysis

### Test Setup (RealTimeMonitoringTests.cs:180-223)

```csharp
// Creates ORCHESTRATED machine
var (pureMachine, machineId) = CreateTestMachine("test-actions_2");

// Gets underlying StateMachine (synchronous)
var underlying = GetUnderlying(pureMachine);

// Monitors the underlying machine (expects synchronous events)
var monitor = new StateMachineMonitor(underlying!);
monitor.ActionExecuted += (sender, e) => { /* Never fires! */ };

// Sends events through ORCHESTRATOR (async)
await SendToMachineAsync(machineId, "START"); // Orchestrated!
await SendToMachineAsync(machineId, "STOP");  // Orchestrated!
```

### The CreateMachine Method (OrchestratorTestBase.cs:32-52)

```csharp
protected IPureStateMachine CreateMachine(...)
{
    // Uses ORCHESTRATED factory
    var machine = ExtendedPureStateMachineFactory.CreateWithChannelGroup(
        orchestrator: _orchestrator,
        orchestratedActions: actions,  // Actions run ASYNC through orchestrator
        ...
    );
    return machine;
}
```

### Event Flow

```
Test sends "START" event
         ↓
  EventBusOrchestrator receives event
         ↓
  Orchestrator queues event for machine
         ↓
  Machine processes event asynchronously
         ↓
  Action executed in orchestrator context
         ↓
  ❌ StateMachineMonitor never notified
     (monitoring synchronous machine, not orchestrator)
```

## Why It Fails

1. **Actions are orchestrated** - They run through `EventBusOrchestrator`, not directly on the `StateMachine`
2. **Monitor watches wrong layer** - It monitors the underlying `StateMachine`, but actions execute at the orchestrator level
3. **Timing issue** - Even if actions fired, they're async so might not complete before the 5-second timeout
4. **Event not propagated** - `StateMachine.ActionExecuted` event doesn't fire for orchestrated actions

## Solutions

### Option 1: Monitor the Orchestrator (Recommended)

Create `OrchestratedStateMachineMonitor` that listens to orchestrator events:

```csharp
public class OrchestratedStateMachineMonitor
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly string _machineId;

    public event EventHandler<ActionExecutedEventArgs>? ActionExecuted;
    public event EventHandler<StateTransitionEventArgs>? StateTransitioned;

    public OrchestratedStateMachineMonitor(
        EventBusOrchestrator orchestrator,
        string machineId)
    {
        _orchestrator = orchestrator;
        _machineId = machineId;
    }

    public void StartMonitoring()
    {
        // Listen to orchestrator events for this machine
        _orchestrator.MachineEventProcessed += OnMachineEvent;
    }

    private void OnMachineEvent(object? sender, MachineEventArgs e)
    {
        if (e.MachineId != _machineId) return;

        // Extract action/transition info from event
        // Raise appropriate events
    }
}
```

### Option 2: Use Non-Orchestrated Machines

Change test to use `StateMachineFactory.CreateFromScript()` instead:

```csharp
private (StateMachine machine, string machineId) CreateTestMachine(string id)
{
    var machine = StateMachineFactory.CreateFromScript(json);

    // Register synchronous actions
    machine.RegisterAction("logStart", (sm) => { });
    machine.RegisterAction("startProcess", (sm) => { });
    // ...

    machine.Start();
    return (machine, machine.Id);
}
```

Then use synchronous event sending:

```csharp
machine.SendEvent("START"); // Synchronous!
```

### Option 3: Add Delay for Async Completion

Quick workaround (not recommended):

```csharp
await SendToMachineAsync(machineId, "START");
await Task.Delay(100); // Wait for orchestrator to process
await SendToMachineAsync(machineId, "STOP");
await Task.Delay(100);
```

**Problem**: Violates event-driven principle, adds timing dependencies.

## Recommended Fix

**Implement Option 1** - Create `OrchestratedStateMachineMonitor`:

1. Monitor orchestrator events instead of machine events
2. Filter by machine ID
3. Extract action/transition information
4. Provide same API as `StateMachineMonitor`

This preserves the orchestrated pattern while enabling monitoring.

## Impact

- **HSMS implementation**: ❌ Not affected
- **Monitoring tests**: ⚠️ Currently failing
- **Other tests**: ✅ Not affected

## Priority

**Medium** - This is a pre-existing architectural issue with monitoring orchestrated machines. It doesn't block HSMS protocol implementation.

## Workaround for Now

Skip the failing monitoring test until proper orchestrated monitoring is implemented:

```csharp
[Fact(Skip = "Monitoring orchestrated machines not yet supported")]
public async Task Monitor_CapturesActions()
{
    // ...
}
```

---

*Issue discovered: 2025-10-04*
*Status: Documented, not yet fixed*
*Related to: Orchestrated pattern migration*
