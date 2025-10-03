# Global Singleton Orchestrator Usage Guide

## Overview

XStateNet now provides a **global singleton orchestrator** that eliminates the need for managing multiple orchestrator instances. This guide shows how to use `GlobalOrchestratorManager` for both production and testing scenarios.

## Core Concept: Channel Group Isolation

Instead of creating separate orchestrator instances, we use **channel group tokens** to provide isolation:

```
┌─────────────────────────────────────────┐
│   GlobalOrchestratorManager (Singleton)  │
│                                          │
│  ┌────────────────────────────────────┐ │
│  │  EventBusOrchestrator (16 channels)│ │
│  └────────────────────────────────────┘ │
│                                          │
│  Channel Groups:                         │
│  ├─ Group 1: Test_OrderTests            │
│  │   ├─ order#1#abc123                  │
│  │   └─ inventory#1#def456              │
│  │                                       │
│  ├─ Group 2: Prod_OrderService          │
│  │   ├─ order#2#ghi789                  │
│  │   └─ payment#2#jkl012                │
│  │                                       │
│  └─ Group 3: Prod_InventoryService      │
│      └─ stock#3#mno345                  │
└─────────────────────────────────────────┘
```

## Production Usage

### Basic Production Pattern

```csharp
using XStateNet.Orchestration;

public class OrderProcessingService : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly ChannelGroupToken _channelGroup;

    public OrderProcessingService()
    {
        // Get global singleton orchestrator
        _orchestrator = GlobalOrchestratorManager.Instance.Orchestrator;

        // Create channel group for this service
        _channelGroup = GlobalOrchestratorManager.Instance.CreateChannelGroup("OrderService");
    }

    public async Task ProcessOrder(string orderId, OrderData data)
    {
        var json = @"{
            id: 'orderMachine',
            initial: 'validating',
            states: {
                validating: {
                    invoke: {
                        src: 'validateOrder',
                        onDone: 'processing',
                        onError: 'failed'
                    }
                },
                processing: {
                    entry: ['processPayment', 'updateInventory'],
                    on: {
                        COMPLETE: 'completed'
                    }
                },
                completed: { type: 'final' },
                failed: { type: 'final' }
            }
        }";

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["processPayment"] = ctx => ctx.RequestSend("PaymentService", "CHARGE", data.Amount),
            ["updateInventory"] = ctx => ctx.RequestSend("InventoryService", "RESERVE", data.Items)
        };

        var services = new Dictionary<string, Func<StateMachine, CancellationToken, Task<object>>>
        {
            ["validateOrder"] = async (sm, ct) =>
            {
                var isValid = await ValidateOrderAsync(data, ct);
                return isValid ? "success" : throw new Exception("Invalid order");
            }
        };

        // Create machine with channel group isolation
        var machine = ExtendedPureStateMachineFactory.CreateWithChannelGroup(
            id: $"order_{orderId}",
            json: json,
            orchestrator: _orchestrator,
            channelGroupToken: _channelGroup,
            orchestratedActions: actions,
            services: services);

        await _orchestrator.StartMachineAsync(machine.Id);
    }

    public void Dispose()
    {
        // Cleanup: Disposes channel group and unregisters all machines
        _channelGroup?.Dispose();
    }

    private async Task<bool> ValidateOrderAsync(OrderData data, CancellationToken ct)
    {
        // Validation logic
        await Task.Delay(100, ct);
        return data.Items.Count > 0;
    }
}
```

### Multi-Tenant Production Pattern

```csharp
public class TenantOrchestrationManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ChannelGroupToken> _tenantGroups = new();
    private readonly EventBusOrchestrator _orchestrator;

    public TenantOrchestrationManager()
    {
        _orchestrator = GlobalOrchestratorManager.Instance.Orchestrator;
    }

    public ChannelGroupToken GetTenantGroup(string tenantId)
    {
        return _tenantGroups.GetOrAdd(tenantId, id =>
            GlobalOrchestratorManager.Instance.CreateChannelGroup($"Tenant_{id}"));
    }

    public async Task ProcessTenantWorkflow(string tenantId, string workflowId, string json)
    {
        var tenantGroup = GetTenantGroup(tenantId);

        var machine = ExtendedPureStateMachineFactory.CreateWithChannelGroup(
            id: workflowId,
            json: json,
            orchestrator: _orchestrator,
            channelGroupToken: tenantGroup);

        await _orchestrator.StartMachineAsync(machine.Id);
    }

    public void ReleaseTenant(string tenantId)
    {
        if (_tenantGroups.TryRemove(tenantId, out var token))
        {
            token.Dispose(); // Cleans up all tenant machines
        }
    }

    public void Dispose()
    {
        foreach (var token in _tenantGroups.Values)
        {
            token.Dispose();
        }
        _tenantGroups.Clear();
    }
}
```

### ASP.NET Core Integration

```csharp
// Startup.cs or Program.cs
services.AddSingleton(sp => GlobalOrchestratorManager.Instance.Orchestrator);

// Per-request scoped service
services.AddScoped<RequestOrchestrationContext>();

// RequestOrchestrationContext.cs
public class RequestOrchestrationContext : IDisposable
{
    private readonly ChannelGroupToken _channelGroup;
    private readonly EventBusOrchestrator _orchestrator;

    public RequestOrchestrationContext(EventBusOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        _channelGroup = GlobalOrchestratorManager.Instance.CreateChannelGroup(
            $"Request_{Guid.NewGuid():N}");
    }

    public IPureStateMachine CreateMachine(string id, string json,
        Dictionary<string, Action<OrchestratedContext>>? actions = null)
    {
        return ExtendedPureStateMachineFactory.CreateWithChannelGroup(
            id, json, _orchestrator, _channelGroup, actions);
    }

    public void Dispose()
    {
        _channelGroup?.Dispose();
    }
}

// Controller
[ApiController]
[Route("api/[controller]")]
public class OrderController : ControllerBase
{
    private readonly RequestOrchestrationContext _context;

    public OrderController(RequestOrchestrationContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] OrderRequest request)
    {
        var machine = _context.CreateMachine("orderMachine", OrderMachineJson);
        await machine.StartAsync();
        // Process order...
        return Ok();
    }
}
```

## Testing Usage

### Basic Test Pattern

```csharp
using Xunit;
using XStateNet.Orchestration;

public class OrderProcessingTests : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly ChannelGroupToken _channelGroup;

    public OrderProcessingTests()
    {
        _orchestrator = GlobalOrchestratorManager.Instance.Orchestrator;
        _channelGroup = GlobalOrchestratorManager.Instance.CreateChannelGroup("OrderTests");
    }

    [Fact]
    public async Task Order_ShouldProcessSuccessfully()
    {
        // Arrange
        var json = @"{
            id: 'order',
            initial: 'processing',
            states: {
                processing: {
                    on: { COMPLETE: 'completed' }
                },
                completed: { type: 'final' }
            }
        }";

        var machine = ExtendedPureStateMachineFactory.CreateWithChannelGroup(
            "order", json, _orchestrator, _channelGroup);

        // Act
        await _orchestrator.StartMachineAsync(machine.Id);
        await _orchestrator.SendEventFireAndForgetAsync("test", machine.Id, "COMPLETE");
        await Task.Delay(50);

        // Assert
        Assert.Contains("completed", machine.CurrentState);
    }

    public void Dispose()
    {
        _channelGroup?.Dispose();
    }
}
```

### Using OrchestratorTestBase

XStateNet provides `OrchestratorTestBase` for simpler test writing:

```csharp
public class MyStateMachineTests : OrchestratorTestBase
{
    [Fact]
    public async Task StateMachine_ShouldTransition()
    {
        // Arrange - CreateMachine automatically uses channel group
        var machine = CreateMachine(
            id: "testMachine",
            json: @"{
                id: 'test',
                initial: 'idle',
                states: {
                    idle: { on: { START: 'running' } },
                    running: {}
                }
            }");

        // Act
        await _orchestrator.StartMachineAsync(machine.Id);
        await SendEventAsync("test", machine.Id, "START");

        // Assert
        await WaitForStateAsync(machine, "running");
        Assert.Contains("running", machine.CurrentState);
    }

    // Cleanup handled automatically by base class
}
```

### Parallel Test Execution

Tests run in parallel without interference thanks to channel group isolation:

```csharp
[Collection("Orchestrator")]
public class ParallelTestA : OrchestratorTestBase
{
    [Fact]
    public async Task Test1()
    {
        var machine = CreateMachine("counter", CounterJson);
        // Isolated from other tests
    }
}

[Collection("Orchestrator")]
public class ParallelTestB : OrchestratorTestBase
{
    [Fact]
    public async Task Test1()
    {
        var machine = CreateMachine("counter", CounterJson);
        // Same ID but different channel group - no conflict!
    }
}
```

## Best Practices

### 1. Always Use Channel Groups

❌ **Don't** create machines without channel groups in production:
```csharp
// NO - No isolation, potential conflicts
var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
    "order", json, orchestrator);
```

✅ **Do** use channel groups for isolation:
```csharp
// YES - Proper isolation
var machine = ExtendedPureStateMachineFactory.CreateWithChannelGroup(
    "order", json, orchestrator, channelGroup);
```

### 2. Dispose Channel Groups

Always dispose channel groups when done:

```csharp
public class MyService : IDisposable
{
    private readonly ChannelGroupToken _channelGroup;

    public MyService()
    {
        _channelGroup = GlobalOrchestratorManager.Instance.CreateChannelGroup("MyService");
    }

    public void Dispose()
    {
        _channelGroup?.Dispose(); // Cleans up all machines
    }
}
```

### 3. Scope Channel Groups Appropriately

- **Per-Request**: ASP.NET Core scoped services
- **Per-Tenant**: Multi-tenant applications
- **Per-Feature**: Long-lived feature services
- **Per-Test**: Test classes

### 4. Monitor Metrics

```csharp
// Get orchestrator metrics
var metrics = GlobalOrchestratorManager.Instance.GetMetrics();
Console.WriteLine($"Total events: {metrics.TotalEventsProcessed}");
Console.WriteLine($"Active groups: {GlobalOrchestratorManager.Instance.ActiveChannelGroupCount}");

// Get orchestrator stats
var stats = orchestrator.GetStats();
Console.WriteLine($"Registered machines: {stats.RegisteredMachines}");
Console.WriteLine($"Pending requests: {stats.PendingRequests}");
```

### 5. Machine ID Format

Machines created with channel groups have scoped IDs:

```
Format: {baseName}#{groupId}#{uniqueGuid}
Example: counter#42#a1b2c3d4e5f67890...
```

This ensures:
- ✅ No ID conflicts between channel groups
- ✅ Easy identification of machine's group
- ✅ Automatic cleanup when group is disposed

## Advanced Patterns

### Long-Running Workflows

```csharp
public class WorkflowEngine
{
    private readonly ConcurrentDictionary<string, ChannelGroupToken> _workflows = new();
    private readonly EventBusOrchestrator _orchestrator;

    public WorkflowEngine()
    {
        _orchestrator = GlobalOrchestratorManager.Instance.Orchestrator;
    }

    public async Task<string> StartWorkflow(string workflowJson)
    {
        var workflowId = Guid.NewGuid().ToString();
        var channelGroup = GlobalOrchestratorManager.Instance.CreateChannelGroup($"Workflow_{workflowId}");
        _workflows[workflowId] = channelGroup;

        var machine = ExtendedPureStateMachineFactory.CreateWithChannelGroup(
            "workflow", workflowJson, _orchestrator, channelGroup);

        await _orchestrator.StartMachineAsync(machine.Id);
        return workflowId;
    }

    public void CancelWorkflow(string workflowId)
    {
        if (_workflows.TryRemove(workflowId, out var group))
        {
            group.Dispose(); // Stops all workflow machines
        }
    }
}
```

### Feature Flag Integration

```csharp
public class FeatureOrchestrationService
{
    private readonly Dictionary<string, ChannelGroupToken> _featureGroups = new();

    public void EnableFeature(string featureName)
    {
        if (!_featureGroups.ContainsKey(featureName))
        {
            var group = GlobalOrchestratorManager.Instance.CreateChannelGroup($"Feature_{featureName}");
            _featureGroups[featureName] = group;
        }
    }

    public void DisableFeature(string featureName)
    {
        if (_featureGroups.TryGetValue(featureName, out var group))
        {
            group.Dispose();
            _featureGroups.Remove(featureName);
        }
    }

    public ChannelGroupToken GetFeatureGroup(string featureName)
    {
        return _featureGroups[featureName];
    }
}
```

## Troubleshooting

### Problem: Channel group released but machines still active

**Cause**: Holding reference to machines after channel group disposal

**Solution**: Don't cache machine references; let channel group manage lifecycle

```csharp
// ❌ DON'T
var machines = new List<IPureStateMachine>();
machines.Add(CreateMachine(...));
_channelGroup.Dispose(); // Machines still in list

// ✅ DO
CreateMachine(...); // No need to store reference
_channelGroup.Dispose(); // Properly cleans up
```

### Problem: Machine ID conflicts

**Cause**: Not using channel groups

**Solution**: Always use `CreateWithChannelGroup`:

```csharp
// ✅ Correct
ExtendedPureStateMachineFactory.CreateWithChannelGroup(
    "order", json, orchestrator, channelGroup);
```

### Problem: Tests interfering with each other

**Cause**: Sharing channel groups or not inheriting from `OrchestratorTestBase`

**Solution**: Each test class gets its own channel group:

```csharp
public class MyTests : OrchestratorTestBase // ✅
{
    // Automatic isolation
}
```

## Performance Characteristics

- **Channel Pool**: 16 channels (configurable)
- **Concurrent Channel Groups**: Unlimited
- **Machines per Group**: Unlimited
- **Overhead**: Minimal (scoped machine IDs only)
- **Cleanup**: O(n) where n = machines in group

## Migration from Per-Instance Orchestrators

### Before (Old Pattern)
```csharp
public class MyService : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;

    public MyService()
    {
        _orchestrator = new EventBusOrchestrator(config);
    }

    public void Dispose()
    {
        _orchestrator?.Dispose();
    }
}
```

### After (New Pattern)
```csharp
public class MyService : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly ChannelGroupToken _channelGroup;

    public MyService()
    {
        _orchestrator = GlobalOrchestratorManager.Instance.Orchestrator;
        _channelGroup = GlobalOrchestratorManager.Instance.CreateChannelGroup("MyService");
    }

    public void Dispose()
    {
        _channelGroup?.Dispose();
    }
}
```

## Summary

✅ **Use global singleton** - One orchestrator for entire application
✅ **Channel groups for isolation** - Prevent cross-contamination
✅ **Dispose channel groups** - Automatic cleanup
✅ **Scope appropriately** - Per-request, per-tenant, per-feature
✅ **Monitor metrics** - Track performance and health

The global singleton orchestrator with channel group isolation provides the best of both worlds: **centralized management** with **complete isolation**.
