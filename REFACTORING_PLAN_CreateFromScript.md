# Refactoring Plan: StateMachineFactory.CreateFromScript → Orchestrated Pattern

## Executive Summary

This document outlines the comprehensive plan to refactor all usages of the now-obsolete `StateMachineFactory.CreateFromScript` to use the orchestrated pattern with `ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices`.

**Total Files to Refactor:** ~70+ C# files
**Status:** Planning Phase
**Priority:** High (Production code), Medium (Tests), Low (Demos/Examples)

---

## 1. Why Refactor?

### Problems with Current Approach
- **Deadlock Risk**: Direct `StateMachine` creation allows machines to send to each other directly, creating circular wait conditions
- **No Central Coordination**: Without orchestrator, message ordering and delivery are not guaranteed
- **Race Conditions**: Async message sends can arrive in unpredictable order
- **Difficult to Debug**: No central point to monitor or trace inter-machine communication

### Benefits of Orchestrated Pattern
- **Deadlock-Free**: All communication goes through `EventBusOrchestrator` which manages message queues
- **Deterministic**: Messages are processed in order through the orchestrator
- **Observable**: Single point to monitor all machine communications
- **Testable**: `OrchestratorTestBase` provides clean test infrastructure
- **Scalable**: Can distribute machines across processes/nodes

---

## 2. File Categories

### Category 1: Infrastructure (COMPLETED ✅)
**Status:** Already suppressed with `#pragma warning disable CS0618`

- `XStateNet5Impl/StateMachineFactory.cs` - Obsolete method definition
- `XStateNet5Impl/Orchestration/PureStateMachine.cs` - Internal factory usage
- `XStateNet5Impl/Orchestration/ExtendedPureStateMachineFactory.cs` - Internal factory usage
- `XStateNet5Impl/StateMachineBuilder.cs` - Builder pattern

**Action:** None required - these are infrastructure files that must use the old API internally.

---

### Category 2: Test Files (HIGH PRIORITY)
**Count:** 29 files
**Complexity:** Low-Medium
**Refactoring Pattern:** Use `OrchestratorTestBase`

#### Files:
```
Test/BenchmarkGetActiveStateString.cs
Test/OrchestratorTestBase.cs (already uses orchestrator!)
Test/OrchestratorTests.cs (already uses orchestrator!)
Test/OrderedEventTests.cs
Test/PubSubTimelineIntegrationTests.cs
Test/SendAsyncCompletionTests.cs
Test/SendMethodVariationsTests.cs
Test/SimpleAsyncTest.cs
Test/StateMachineFactoryTests.cs
Test/TestEventQueueSolution.cs
Test/TimingSensitiveStateMachineTests.cs
Test/UnitTest_Activities.cs
Test/UnitTest_ActorAdvanced.cs
Test/UnitTest_ActorModel.cs
Test/UnitTest_Delays.cs
Test/UnitTest_DiagramFramework.cs
Test/UnitTest_ErrorHandling.cs
Test/UnitTest_ErrorHandling_Debug.cs
Test/UnitTest_InvokeDebug.cs
Test/UnitTest_InvokeOnError.cs
Test/UnitTest_LogAction.cs
Test/UnitTest_LoggerDemo.cs
Test/UnitTest_MultipleTargets.cs
Test/unitTest_PingPongInAMachine.cs
Test/UnitTest_Ping_and_Pong_Machines.cs
Test/UnitTest_SuperComplex.cs
Test/UnitTest_TrafficLight.cs
Test/UnitTest_TransientTransition.cs
Test/UnitTest_VideoPlayer.cs
```

#### Refactoring Strategy:

**Before:**
```csharp
[Fact]
public async Task MyTest()
{
    var machine = StateMachineFactory.CreateFromScript(json, false, false, actions);
    await machine.StartAsync();
    await machine.SendAsync("EVENT");
    // assertions
}
```

**After:**
```csharp
public class MyTests : OrchestratorTestBase
{
    [Fact]
    public async Task MyTest()
    {
        var machine = CreateMachine("machine1", json, orchestratedActions);
        await machine.StartAsync();
        await _orchestrator.SendEventAsync("test", "machine1", "EVENT");
        await WaitForStateAsync(machine, "#machine1.expectedState");
        // assertions
    }
}
```

**Key Changes:**
1. Inherit from `OrchestratorTestBase` (or `XStateNet.Tests.OrchestratorTestBase`)
2. Use `CreateMachine()` helper instead of `StateMachineFactory.CreateFromScript()`
3. Convert actions to use `OrchestratedContext` with `ctx.RequestSend()` instead of `machine.SendAsync()`
4. Use `_orchestrator.SendEventAsync()` for external events
5. Use `WaitForStateAsync()` instead of `Task.Delay()` or `TaskCompletionSource`

---

### Category 3: SEMI Standard Machines (CRITICAL PRIORITY ⚠️)
**Count:** 25 files
**Complexity:** High
**Impact:** Production code

#### Files:
```
SemiStandard/Machines/CleaningMachine.cs
SemiStandard/Machines/DryerMachine.cs
SemiStandard/Machines/InspectionMachine.cs
SemiStandard/Machines/LoadPortMachine.cs
SemiStandard/Machines/PreAlignerMachine.cs
SemiStandard/Machines/UnloadPortMachine.cs
SemiStandard/Machines/WaferTransferRobotMachine.cs
SemiStandard/Schedulers/CMPMasterScheduler.cs
SemiStandard/Schedulers/CMPToolScheduler.cs
SemiStandard/Standards/E142WaferMapMachine.cs
SemiStandard/Standards/E157ModuleProcessTrackingMachine.cs
SemiStandard/Standards/E30GemMachine.cs
SemiStandard/Standards/E37HSMSSessionMachine.cs
SemiStandard/Standards/E39E116E10EquipmentMetricsMachine.cs
SemiStandard/Standards/E40ProcessJobMachine.cs
SemiStandard/Standards/E42RecipeManagementMachine.cs
SemiStandard/Standards/E84HandoffMachine.cs
SemiStandard/Standards/E87CarrierManagementMachine.cs
SemiStandard/Standards/E90SubstrateTrackingMachine.cs
SemiStandard/Standards/E94ControlJobMachine.cs
SemiStandard/Standards/SemiEquipmentMachine.cs
SemiStandard/StateMachineAdapter.cs
SemiStandard/Testing/XStateEquipmentController.cs
SemiStandard/Transport/XStateNetHsmsConnection.cs
```

#### Refactoring Strategy:

These are production machines that communicate with each other. They MUST use orchestrator.

**Example: E40ProcessJobMachine.cs**

**Before:**
```csharp
public class E40ProcessJobMachine
{
    private readonly IStateMachine _machine;

    public E40ProcessJobMachine(string jobId)
    {
        var actions = new ActionMap { ... };
        _machine = StateMachineFactory.CreateFromScript(json, false, false, actions);
    }

    public void ProcessMaterial(string materialId)
    {
        _machine.SendAsync("PROCESS_MATERIAL", materialId);
    }
}
```

**After:**
```csharp
public class E40ProcessJobMachine
{
    private readonly IPureStateMachine _machine;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly string _machineId;

    public E40ProcessJobMachine(string jobId, EventBusOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        _machineId = $"E40_{jobId}";

        var orchestratedActions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["onProcessStart"] = ctx =>
            {
                // Use ctx.RequestSend instead of machine.SendAsync
                ctx.RequestSend("E94_ControlJob", "JOB_STARTED", jobId);
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: _machineId,
            json: json,
            orchestrator: orchestrator,
            orchestratedActions: orchestratedActions
        );
    }

    public async Task ProcessMaterial(string materialId)
    {
        await _orchestrator.SendEventAsync("controller", _machineId, "PROCESS_MATERIAL", materialId);
    }
}
```

**Key Changes:**
1. Accept `EventBusOrchestrator` in constructor
2. Generate unique machine ID
3. Change from `ActionMap` to `Dictionary<string, Action<OrchestratedContext>>`
4. Use `ctx.RequestSend()` inside actions instead of `machine.SendAsync()`
5. Use `_orchestrator.SendEventAsync()` for public API methods
6. Return `IPureStateMachine` instead of `IStateMachine`

**Note:** These machines often communicate with each other (E40 → E94, E87 → E90, etc.). The orchestrator ensures deadlock-free communication.

---

### Category 4: Demo/Example Apps (MEDIUM PRIORITY)
**Count:** 10 files
**Complexity:** Low
**Purpose:** Show best practices

#### Files:
```
app/ForestOfParallel/Program.cs
OrchestratorTestApp/AdvancedFeaturesDemo.cs
OrchestratorTestApp/DebugTest.cs
OrchestratorTestApp/HarshTests.cs
OrchestratorTestApp/InteractiveMenu.cs
OrchestratorTestApp/MonitoringDemo.cs
OrchestratorTestApp/SimpleTest.cs
OrchestratorTestApp/Test1MEvents.cs
OrchestratorTestApp/TestRunner.cs
XStateNet.Distributed.Example/PubSubExample.cs
```

#### Refactoring Strategy:

Update these to demonstrate the orchestrated pattern as best practice.

**Example: SimpleTest.cs**

**Before:**
```csharp
class Program
{
    static void Main()
    {
        var machine = StateMachineFactory.CreateFromScript(json, false, false, actions);
        machine.Start();
        machine.SendAsync("START");
        Console.ReadLine();
    }
}
```

**After:**
```csharp
class Program
{
    static async Task Main()
    {
        var orchestrator = new EventBusOrchestrator(new OrchestratorConfig());

        var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: "demo",
            json: json,
            orchestrator: orchestrator,
            orchestratedActions: orchestratedActions
        );

        await machine.StartAsync();
        await orchestrator.SendEventAsync("app", "demo", "START");

        Console.WriteLine("Machine started. Press Enter to exit.");
        Console.ReadLine();

        orchestrator.Dispose();
    }
}
```

---

### Category 5: XStateNet.Distributed (MEDIUM PRIORITY)
**Count:** 11 files
**Complexity:** Low-Medium
**Note:** Most already use orchestrator, just need to update factory call

#### Files:
```
XStateNet.Distributed/Resilience/CircuitBreakerStateMachineBuilder.cs
XStateNet.Distributed/StateMachine/XStateNetTimeoutProtectedStateMachine.cs
XStateNet.Distributed.Tests/Benchmarks/EventBusBenchmarks.cs
XStateNet.Distributed.Tests/DistributedCommunicationTests.cs
XStateNet.Distributed.Tests/DistributedStateMachineTests.cs
XStateNet.Distributed.Tests/OrchestratorTestBase.cs (already correct!)
XStateNet.Distributed.Tests/PubSub/ComprehensivePubSubTests.cs
XStateNet.Distributed.Tests/PubSub/EventNotificationServiceTests.cs
XStateNet.Distributed.Tests/PubSub/PerformanceValidationTests.cs
XStateNet.Distributed.Tests/PubSub/SimplePingPongEventBusTests.cs
```

#### Refactoring Strategy:

Most of these files already have orchestrator infrastructure, just need to change the factory call.

**Simple replacement:**
```csharp
// Before
var machine = StateMachineFactory.CreateFromScript(json, false, false, actions);

// After
var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
    id: machineId,
    json: json,
    orchestrator: orchestrator,
    orchestratedActions: ConvertToOrchestratedActions(actions)
);
```

---

### Category 6: Other (LOW PRIORITY)
**Count:** 5 files
**Complexity:** Low
**Note:** Specialized use cases

#### Files:
```
TimelineWPF/DemoWindow.xaml.cs
TimelineWPF.Demo/DemoWindow.xaml.cs
XStateNet.GPU/GPUAcceleratedStateMachine.cs
XStateNet.GPU/Integration/XStateNetGPUBridge.cs
XStateNet5Impl/Benchmarking/BenchmarkFramework.cs
```

#### Refactoring Strategy:

**Option 1:** Suppress warnings if these are specialized internal implementations
**Option 2:** Refactor to use orchestrator if they interact with other machines

For benchmarking code, suppression is acceptable as performance is the goal.

---

## 3. Refactoring Priorities

### Phase 1: Critical (Week 1-2)
1. **SemiStandard Production Code** (Category 3)
   - E40ProcessJobMachine
   - E94ControlJobMachine
   - E87CarrierManagement
   - E90SubstrateTracking
   - Related schedulers

**Why:** These are production machines that communicate with each other. Deadlock risk is highest here.

### Phase 2: Tests (Week 3-4)
2. **Core Test Files** (Category 2)
   - Start with simple tests (UnitTest_TrafficLight, UnitTest_VideoPlayer)
   - Move to inter-machine tests (UnitTest_Ping_and_Pong_Machines, UnitTest_ActorModel)
   - Complex tests last (UnitTest_SuperComplex, TimingSensitiveStateMachineTests)

**Why:** Tests validate the refactoring. Getting them passing ensures the pattern works.

### Phase 3: Distributed (Week 5)
3. **XStateNet.Distributed** (Category 5)
   - Test files first
   - Production code (CircuitBreakerStateMachineBuilder, etc.)

**Why:** These already use orchestrator infrastructure, just need factory updates.

### Phase 4: Examples & Demos (Week 6)
4. **Demo Applications** (Category 4)
   - Update to show best practices
   - Add comments explaining orchestrated pattern

**Why:** These teach users. Should demonstrate the recommended approach.

### Phase 5: Specialized (Week 7)
5. **GPU & Other** (Category 6)
   - Evaluate each on case-by-case basis
   - Suppress warnings where appropriate

---

## 4. Conversion Helper Functions

### ActionMap to OrchestratedActions Converter

```csharp
public static Dictionary<string, Action<OrchestratedContext>> ConvertToOrchestratedActions(
    ActionMap actionMap,
    IStateMachine? oldMachine = null)
{
    var orchestratedActions = new Dictionary<string, Action<OrchestratedContext>>();

    foreach (var (actionName, namedActions) in actionMap)
    {
        orchestratedActions[actionName] = ctx =>
        {
            foreach (var action in namedActions)
            {
                // Call the original action
                // Note: Original actions can't send to other machines directly anymore
                action.Action?.Invoke(oldMachine);
            }
        };
    }

    return orchestratedActions;
}
```

**Note:** This is a starting point. Many actions will need manual refactoring to use `ctx.RequestSend()`.

---

## 5. Common Refactoring Patterns

### Pattern 1: Replace Direct Send with RequestSend

**Before:**
```csharp
["onComplete"] = (sm) =>
{
    otherMachine.SendAsync("NOTIFY", data);
}
```

**After:**
```csharp
["onComplete"] = ctx =>
{
    ctx.RequestSend("otherMachineId", "NOTIFY", data);
}
```

### Pattern 2: Replace TaskCompletionSource with WaitForStateAsync

**Before:**
```csharp
var tcs = new TaskCompletionSource<bool>();
["onDone"] = (sm) => tcs.SetResult(true);
// ...
await tcs.Task;
```

**After:**
```csharp
["onDone"] = ctx => { /* just transition */ };
// ...
await WaitForStateAsync(machine, "#machine.done");
```

### Pattern 3: Constructor Injection of Orchestrator

**Before:**
```csharp
public class MyMachine
{
    public MyMachine(string id)
    {
        _machine = StateMachineFactory.CreateFromScript(...);
    }
}
```

**After:**
```csharp
public class MyMachine
{
    public MyMachine(string id, EventBusOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        _machineId = $"MyMachine_{id}";
        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: _machineId,
            json: ...,
            orchestrator: orchestrator,
            orchestratedActions: ...
        );
    }
}
```

### Pattern 4: Test Class Inheritance

**Before:**
```csharp
public class MyTests
{
    [Fact]
    public async Task Test() { ... }
}
```

**After:**
```csharp
public class MyTests : OrchestratorTestBase
{
    [Fact]
    public async Task Test() { ... }
}
```

---

## 6. Migration Checklist

For each file being refactored:

- [ ] Read the file and understand current machine interactions
- [ ] Identify all `StateMachineFactory.CreateFromScript` calls
- [ ] Identify all inter-machine `SendAsync` calls
- [ ] Create orchestrated action dictionary
- [ ] Convert actions to use `ctx.RequestSend()`
- [ ] Update constructor to accept `EventBusOrchestrator`
- [ ] Update factory call to `ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices`
- [ ] Update all external event sends to use `orchestrator.SendEventAsync()`
- [ ] For tests: inherit from `OrchestratorTestBase`
- [ ] For tests: use `WaitForStateAsync()` instead of delays
- [ ] Build and fix compilation errors
- [ ] Run tests and verify behavior unchanged
- [ ] Remove obsolete warning (should be gone)

---

## 7. Testing Strategy

### Unit Tests
- Each refactored class should have passing tests
- Tests should use `OrchestratorTestBase` pattern
- Verify no deadlocks under stress

### Integration Tests
- Test multi-machine scenarios (E40 + E94 + E87 + E90)
- Verify message ordering is correct
- Verify no race conditions

### Stress Tests
- Run 1000+ iterations of critical tests
- Verify no flaky tests due to race conditions
- Monitor for deadlocks

---

## 8. Documentation Updates

After refactoring:

1. **Update README.md** - Show orchestrated pattern in main examples
2. **Update docs/EXAMPLES.md** - Replace all examples with orchestrated pattern
3. **Update docs/API_REFERENCE.md** - Mark old factory as obsolete, document new pattern
4. **Create MIGRATION_GUIDE.md** - Step-by-step guide for users
5. **Update inline comments** - Explain why orchestrator is needed

---

## 9. Rollback Plan

If critical issues arise:

1. **Keep obsolete methods** - Don't remove old API, just mark obsolete
2. **Suppress warnings** - Use `#pragma warning disable CS0618` if needed
3. **Incremental rollout** - Refactor one category at a time
4. **Feature flags** - Allow both patterns temporarily
5. **Comprehensive testing** - Test each phase before moving to next

---

## 10. Success Criteria

✅ **Zero** obsolete warnings in production code
✅ **All** tests passing with orchestrated pattern
✅ **No** deadlocks in stress tests (10,000+ iterations)
✅ **No** race conditions in multi-machine scenarios
✅ **Documentation** updated with best practices
✅ **Performance** maintained or improved

---

## 11. Next Steps

1. **Review this plan** with team
2. **Start with Phase 1** (SEMI Standard machines)
3. **Create tracking issues** for each category
4. **Set up CI/CD** to catch new usages of obsolete API
5. **Begin refactoring** following the priority order

---

## Appendix A: Example Refactoring - E40ProcessJobMachine

See `SemiStandard/Standards/E40ProcessJobMachine.cs` for detailed before/after example.

## Appendix B: OrchestratorTestBase Template

```csharp
public class MyTests : XStateNet.Tests.OrchestratorTestBase
{
    private readonly ITestOutputHelper _output;

    public MyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task MyTest()
    {
        // Arrange
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["myAction"] = ctx => ctx.RequestSend("other", "EVENT")
        };

        var machine = CreateMachine("myMachine", json, actions);

        // Act
        await machine.StartAsync();
        await _orchestrator.SendEventAsync("test", "myMachine", "START");

        // Assert
        await WaitForStateAsync(machine, "#myMachine.done", timeoutMs: 5000);
        Assert.Equal("#myMachine.done", machine.CurrentState);
    }
}
```

---

**Last Updated:** 2025-10-03
**Document Version:** 1.0
**Status:** Draft - Awaiting Review
