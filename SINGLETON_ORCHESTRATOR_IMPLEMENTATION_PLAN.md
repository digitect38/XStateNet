# True Singleton Orchestrator Implementation Plan

## Overview

This document outlines the implementation plan for a true singleton orchestrator pattern that serves both production and testing scenarios with channel group token isolation.

## Design Philosophy

**Key Principle**: Production environments experience the same level of concurrency harshness as parallel tests. Therefore, we need a single, battle-tested orchestrator implementation that handles both scenarios identically.

**Solution**: True singleton orchestrator with channel group token-based isolation.

## Architecture

### 1. GlobalOrchestratorManager (✅ COMPLETED)

**Location**: `XStateNet5Impl/Orchestration/GlobalOrchestratorManager.cs`

**Features**:
- Thread-safe singleton using `Lazy<T>` with `ExecutionAndPublication` mode
- Auto-growing channel pool (16 → 512 channels)
- Channel group token management for isolation
- Scoped machine ID generation
- Metrics and monitoring support

**Configuration**:
```csharp
PoolSize = 16           // Initial channel count
MaxPoolSize = 512       // Maximum capacity
AllowGrowth = true      // Auto-grow under load
EnableMetrics = true    // Performance tracking
GrowthFactor = 2.0      // Double when growing
ShrinkThreshold = 0.25  // Shrink when utilization < 25%
```

### 2. ChannelGroupToken (✅ COMPLETED)

**Purpose**: Provides isolation between different execution contexts (tests, production features, etc.)

**Features**:
- Unique group ID for scoping
- IDisposable pattern for automatic cleanup
- Timestamp tracking
- Released state tracking

**Machine ID Format**: `{baseName}#{GroupId}#{UniqueGuid}`
- Example: `counter#42#a1b2c3d4e5f6...`

## Implementation Tasks

### Phase 1: Core Infrastructure (IN PROGRESS)

#### Task 1.1: Extend EventBusOrchestrator (🔄 IN PROGRESS)

**File**: `XStateNet5Impl/Orchestration/EventBusOrchestrator.cs`

**Changes Required**:

1. **Add ChannelGroupId to ManagedStateMachine** (✅ DONE):
```csharp
private class ManagedStateMachine
{
    public string Id { get; set; } = "";
    public IStateMachine Machine { get; set; } = null!;
    public int EventBusIndex { get; set; }
    public int? ChannelGroupId { get; set; } // NEW
}
```

2. **Add UnregisterMachinesInGroup method** (⏳ TODO):
```csharp
public void UnregisterMachinesInGroup(int channelGroupId)
{
    var machinesToRemove = _machines
        .Where(kvp => kvp.Value.ChannelGroupId == channelGroupId)
        .Select(kvp => kvp.Key)
        .ToList();

    foreach (var machineId in machinesToRemove)
    {
        UnregisterMachine(machineId);
    }
}
```

3. **Add RegisterMachine overload** (⏳ TODO):
```csharp
public void RegisterMachine(string machineId, IStateMachine machine, int? channelGroupId)
{
    if (string.IsNullOrEmpty(machineId))
        throw new ArgumentNullException(nameof(machineId));
    if (machine == null)
        throw new ArgumentNullException(nameof(machine));

    var managed = new ManagedStateMachine
    {
        Id = machineId,
        Machine = machine,
        EventBusIndex = GetEventBusIndex(machineId),
        ChannelGroupId = channelGroupId
    };

    _machines[machineId] = managed;

    if (_config.EnableMetrics)
    {
        _metrics.RecordMachineRegistered(machineId, machine.GetType().Name);
        _logger.LogMachineRegistered(machineId, machine.GetType().Name);
    }

    if (_config.EnableLogging)
    {
        Console.WriteLine($"[Orchestrator] Registered machine: {machineId} " +
            $"(Group: {channelGroupId?.ToString() ?? "none"}) on bus {managed.EventBusIndex}");
    }
}
```

4. **Update existing RegisterMachine** (⏳ TODO):
```csharp
public void RegisterMachine(string machineId, IStateMachine machine)
{
    // Extract channel group from machine ID if present (format: name#groupId#guid)
    int? channelGroupId = null;
    var parts = machineId.Split('#');
    if (parts.Length >= 2 && int.TryParse(parts[1], out var groupId))
    {
        channelGroupId = groupId;
    }

    RegisterMachine(machineId, machine, channelGroupId);
}
```

#### Task 1.2: Update ExtendedPureStateMachineFactory (⏳ TODO)

**File**: `XStateNet5Impl/Orchestration/ExtendedPureStateMachineFactory.cs`

**Changes Required**:

1. **Add overload with ChannelGroupToken**:
```csharp
public static IPureStateMachine CreateFromScriptWithGuardsAndServices(
    string id,
    string json,
    EventBusOrchestrator orchestrator,
    ChannelGroupToken? channelGroupToken = null,
    Dictionary<string, Action<OrchestratedContext>>? orchestratedActions = null,
    Dictionary<string, Func<OrchestratedContext, bool>>? guards = null,
    Dictionary<string, Func<OrchestratedContext, CancellationToken, Task>>? services = null,
    Dictionary<string, TimeSpan>? delays = null,
    Dictionary<string, Func<Task>>? activities = null)
{
    // Generate scoped machine ID if channel group provided
    var machineId = channelGroupToken != null
        ? GlobalOrchestratorManager.Instance.CreateScopedMachineId(channelGroupToken, id)
        : id;

    var machine = CreateFromScriptWithGuardsAndServices(
        machineId, json, orchestrator,
        orchestratedActions, guards, services, delays, activities);

    return machine;
}
```

### Phase 2: Test Infrastructure (⏳ TODO)

#### Task 2.1: Update OrchestratorTestBase

**File**: `Test/OrchestratorTestBase.cs`

**Changes Required**:

```csharp
public abstract class OrchestratorTestBase : IDisposable
{
    protected readonly EventBusOrchestrator _orchestrator;
    protected readonly ChannelGroupToken _channelGroup;
    protected readonly List<IPureStateMachine> _machines = new();

    protected OrchestratorTestBase()
    {
        // Use global singleton orchestrator
        _orchestrator = GlobalOrchestratorManager.Instance.Orchestrator;

        // Create isolated channel group for this test
        _channelGroup = GlobalOrchestratorManager.Instance.CreateChannelGroup(
            $"Test_{GetType().Name}");
    }

    protected IPureStateMachine CreateMachine(
        string id,
        string json,
        Dictionary<string, Action<OrchestratedContext>>? actions = null,
        Dictionary<string, Func<OrchestratedContext, bool>>? guards = null,
        Dictionary<string, Func<OrchestratedContext, CancellationToken, Task>>? services = null,
        Dictionary<string, TimeSpan>? delays = null,
        Dictionary<string, Func<Task>>? activities = null)
    {
        // Pass channel group token to factory
        var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: id,
            json: json,
            orchestrator: _orchestrator,
            channelGroupToken: _channelGroup,
            orchestratedActions: actions,
            guards: guards,
            services: services,
            delays: delays,
            activities: activities);

        _machines.Add(machine);
        return machine;
    }

    public virtual void Dispose()
    {
        // Cleanup machines
        foreach (var machine in _machines)
        {
            try
            {
                machine?.Dispose();
            }
            catch { }
        }

        // Release channel group (unregisters all machines in group)
        _channelGroup?.Dispose();
    }
}
```

#### Task 2.2: Create xUnit Collection Fixture (⏳ TODO)

**File**: `Test/OrchestratorCollectionFixture.cs` (NEW)

**Purpose**: Ensure proper orchestrator lifecycle in parallel test execution

```csharp
using Xunit;

namespace XStateNet.Tests
{
    [CollectionDefinition("Orchestrator")]
    public class OrchestratorCollection : ICollectionFixture<OrchestratorFixture>
    {
    }

    public class OrchestratorFixture : IDisposable
    {
        public OrchestratorFixture()
        {
            // Orchestrator is singleton, no initialization needed
        }

        public void Dispose()
        {
            // Optional: Force cleanup for test run
            // GlobalOrchestratorManager.Instance.ForceCleanup();
        }
    }
}
```

**Usage in Tests**:
```csharp
[Collection("Orchestrator")]
public class MyTests : OrchestratorTestBase
{
    // Tests automatically isolated via channel groups
}
```

### Phase 3: Production Examples (⏳ TODO)

#### Task 3.1: Create Production Usage Guide

**File**: `SINGLETON_ORCHESTRATOR_USAGE.md` (NEW)

**Content**: Production patterns and examples

#### Task 3.2: Create Example Applications

1. **Simple Production Example**:
```csharp
// Production code
public class OrderProcessingService
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly ChannelGroupToken _channelGroup;

    public OrderProcessingService()
    {
        _orchestrator = GlobalOrchestratorManager.Instance.Orchestrator;
        _channelGroup = GlobalOrchestratorManager.Instance.CreateChannelGroup("OrderProcessing");
    }

    public async Task ProcessOrder(Order order)
    {
        var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: $"order_{order.Id}",
            json: OrderStateMachineJson,
            orchestrator: _orchestrator,
            channelGroupToken: _channelGroup,
            orchestratedActions: GetOrderActions(order));

        await _orchestrator.StartMachineAsync(machine.Id);
        // Process order through state machine...
    }
}
```

2. **Multi-Tenant Production Example**:
```csharp
// Each tenant gets isolated channel group
public class TenantOrchestrationManager
{
    private readonly ConcurrentDictionary<string, ChannelGroupToken> _tenantGroups = new();
    private readonly EventBusOrchestrator _orchestrator;

    public TenantOrchestrationManager()
    {
        _orchestrator = GlobalOrchestratorManager.Instance.Orchestrator;
    }

    public ChannelGroupToken GetOrCreateTenantGroup(string tenantId)
    {
        return _tenantGroups.GetOrAdd(tenantId, id =>
            GlobalOrchestratorManager.Instance.CreateChannelGroup($"Tenant_{id}"));
    }

    public void ReleaseTenant(string tenantId)
    {
        if (_tenantGroups.TryRemove(tenantId, out var token))
        {
            token.Dispose();
        }
    }
}
```

### Phase 4: Testing & Validation (⏳ TODO)

#### Task 4.1: Create Comprehensive Tests

**File**: `Test/GlobalOrchestratorTests.cs` (NEW)

**Test Cases**:
1. ✅ Singleton behavior (same instance across calls)
2. ✅ Channel group isolation (no cross-talk between groups)
3. ✅ Concurrent channel group creation (thread-safety)
4. ✅ Channel group cleanup (proper unregistration)
5. ✅ Scoped machine ID format validation
6. ✅ Auto-growth under load
7. ✅ Metrics tracking
8. ✅ Parallel test execution (no interference)

#### Task 4.2: Performance Testing

**File**: `OrchestratorTestApp/GlobalOrchestratorStressTest.cs` (NEW)

**Scenarios**:
1. 1000 concurrent channel groups
2. 10,000 machines distributed across groups
3. 1M events with channel group isolation
4. Channel group creation/cleanup cycles

#### Task 4.3: Migration Testing

**Strategy**:
1. Run existing tests with new GlobalOrchestratorManager
2. Verify no regressions
3. Validate isolation between parallel tests
4. Compare performance metrics

## Benefits

### Production Benefits

1. **Single Source of Truth**: One orchestrator instance for entire application
2. **Auto-Scaling**: Pool grows from 16 to 512 channels under load
3. **Isolation**: Channel groups prevent cross-contamination between features
4. **Performance**: Shared thread pool, connection pooling, reduced overhead
5. **Monitoring**: Centralized metrics for entire application

### Testing Benefits

1. **True Parallel Execution**: Tests run concurrently without interference
2. **Deterministic Cleanup**: Channel groups ensure complete cleanup
3. **Real Production Conditions**: Tests use same infrastructure as production
4. **No Mocking Needed**: Real orchestrator in all tests
5. **Fast Execution**: Shared pool eliminates startup overhead

## Migration Path

### Step 1: Update Test Infrastructure
- Modify OrchestratorTestBase to use GlobalOrchestratorManager
- Add xUnit collection fixtures
- Run existing test suite

### Step 2: Update Production Code
- Replace per-feature orchestrator instances with global singleton
- Add channel group tokens for logical isolation
- Update factory calls to include channel group tokens

### Step 3: Validate & Monitor
- Compare metrics before/after migration
- Verify no memory leaks in long-running tests
- Validate channel pool growth behavior

## Implementation Checklist

### Core Infrastructure
- [x] Create GlobalOrchestratorManager singleton
- [x] Implement ChannelGroupToken with IDisposable
- [x] Add ChannelGroupId to ManagedStateMachine
- [ ] Implement UnregisterMachinesInGroup
- [ ] Add RegisterMachine overload with channel group
- [ ] Update factory to support channel groups

### Test Infrastructure
- [ ] Update OrchestratorTestBase
- [ ] Create xUnit collection fixtures
- [ ] Add GlobalOrchestratorTests
- [ ] Create stress tests

### Documentation & Examples
- [x] Create implementation plan (this document)
- [ ] Create production usage guide
- [ ] Add example applications
- [ ] Update existing documentation

### Validation
- [ ] Run full test suite
- [ ] Performance benchmarking
- [ ] Memory leak testing
- [ ] Production smoke tests

## Timeline

- **Phase 1 (Core)**: 2-3 hours
- **Phase 2 (Tests)**: 2-3 hours
- **Phase 3 (Examples)**: 1-2 hours
- **Phase 4 (Validation)**: 2-3 hours

**Total Estimated Time**: 7-11 hours

## Success Criteria

1. ✅ All existing tests pass with new infrastructure
2. ✅ No test interference in parallel execution
3. ✅ Channel pool auto-grows under load
4. ✅ Memory usage stable over time
5. ✅ Performance equal or better than current implementation
6. ✅ Production examples demonstrate real-world usage

## Risk Mitigation

### Risk 1: Singleton State Pollution
- **Mitigation**: Channel group token isolation ensures complete isolation
- **Validation**: Parallel test suite with aggressive concurrency

### Risk 2: Resource Exhaustion
- **Mitigation**: Auto-growth with configurable limits (512 max channels)
- **Validation**: Stress tests with 1000+ concurrent channel groups

### Risk 3: Test Flakiness
- **Mitigation**: Deterministic cleanup via IDisposable pattern
- **Validation**: Run test suite 100 times to detect flakiness

### Risk 4: Migration Complexity
- **Mitigation**: Backward-compatible API, gradual rollout
- **Validation**: Keep old OrchestratorTestBase during transition

## Next Steps

1. **Immediate**: Complete EventBusOrchestrator modifications
2. **Next**: Update ExtendedPureStateMachineFactory
3. **Then**: Update OrchestratorTestBase
4. **Finally**: Create comprehensive tests and examples
