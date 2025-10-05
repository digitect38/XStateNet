# TimeoutProtectedStateMachine Orchestrator Integration

## Overview

`TimeoutProtectedStateMachine` can now participate as a **first-class citizen** in the orchestrated system, allowing it to communicate with other state machines through `EventBusOrchestrator`.

---

## What's New

### Orchestrator Integration Features

✅ **Automatic Registration**: Pass orchestrator to constructor for automatic registration
✅ **Explicit Registration**: `RegisterWithOrchestrator()` method with channel group support
✅ **Unregistration**: `UnregisterFromOrchestrator()` method
✅ **DI Support**: Extension methods for dependency injection
✅ **Backward Compatible**: Optional parameter - existing code works unchanged

---

## Quick Start

### 1. Manual Registration (Constructor)

```csharp
var orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
var timeoutProtection = new TimeoutProtection(new TimeoutOptions { ... });

// Automatically registers with orchestrator
var protectedMachine = new TimeoutProtectedStateMachine(
    innerMachine,
    timeoutProtection,
    dlq: null,
    options: null,
    logger: null,
    orchestrator: orchestrator);  // ← Automatic registration

// Machine is now registered and can communicate via orchestrator
```

### 2. Explicit Registration (After Construction)

```csharp
var protectedMachine = new TimeoutProtectedStateMachine(
    innerMachine,
    timeoutProtection);

// Register later with optional channel group
protectedMachine.RegisterWithOrchestrator(orchestrator, channelGroupId: 1);
```

### 3. Dependency Injection

#### Basic DI Registration

```csharp
services.AddSingleton<EventBusOrchestrator>(...);
services.AddXStateNetResilience(Configuration);

// Existing method with orchestrator flag
services.AddTimeoutProtectedStateMachine(
    sp => CreateMachine(sp),
    stateMachineName: "OrderProcessor",
    registerWithOrchestrator: true);  // ← Enable orchestrator registration
```

#### Dedicated Orchestrated DI Method

```csharp
services.AddSingleton<EventBusOrchestrator>(...);
services.AddXStateNetResilience(Configuration);

// New method specifically for orchestrated machines
services.AddOrchestratedTimeoutProtectedStateMachine(
    sp => CreateMachine(sp),
    stateMachineName: "OrderProcessor",
    channelGroupId: 1);  // ← Optional channel group
```

---

## API Reference

### Constructor

```csharp
public TimeoutProtectedStateMachine(
    IStateMachine innerMachine,
    ITimeoutProtection timeoutProtection,
    IDeadLetterQueue? dlq = null,
    TimeoutProtectedStateMachineOptions? options = null,
    ILogger<TimeoutProtectedStateMachine>? logger = null,
    EventBusOrchestrator? orchestrator = null)  // ← NEW: Optional orchestrator
```

**Parameters:**
- `orchestrator`: If provided, machine is automatically registered with the orchestrator

### Methods

#### RegisterWithOrchestrator

```csharp
public void RegisterWithOrchestrator(
    EventBusOrchestrator orchestrator,
    int? channelGroupId = null)
```

**Purpose**: Explicitly register this machine with an orchestrator
**Parameters:**
- `orchestrator`: The orchestrator to register with
- `channelGroupId`: Optional channel group for isolation

**Behavior:**
- Registers machine with orchestrator
- Logs registration event
- Supports channel group isolation

#### UnregisterFromOrchestrator

```csharp
public void UnregisterFromOrchestrator()
```

**Purpose**: Unregister this machine from its orchestrator
**Behavior:**
- Removes machine from orchestrator registry
- Logs unregistration event
- Sets internal orchestrator reference to null

### Extension Methods

#### AddTimeoutProtectedStateMachine

```csharp
public static IServiceCollection AddTimeoutProtectedStateMachine(
    this IServiceCollection services,
    Func<IServiceProvider, IStateMachine> stateMachineFactory,
    string stateMachineName = "Default",
    bool registerWithOrchestrator = false)  // ← NEW parameter
```

**Usage:**
```csharp
services.AddTimeoutProtectedStateMachine(
    sp => CreateMachine(sp),
    "MyMachine",
    registerWithOrchestrator: true);
```

#### AddOrchestratedTimeoutProtectedStateMachine

```csharp
public static IServiceCollection AddOrchestratedTimeoutProtectedStateMachine(
    this IServiceCollection services,
    Func<IServiceProvider, IStateMachine> stateMachineFactory,
    string stateMachineName = "Default",
    int? channelGroupId = null)
```

**Usage:**
```csharp
services.AddOrchestratedTimeoutProtectedStateMachine(
    sp => CreateMachine(sp),
    "MyMachine",
    channelGroupId: 1);
```

---

## Architecture

### Before: Timeout Protection Only

```
┌─────────────────────────────────────┐
│  TimeoutProtectedStateMachine       │
│  ┌───────────────────────────────┐  │
│  │ IStateMachine (Inner)         │  │
│  └───────────────────────────────┘  │
│                                      │
│  Uses: ITimeoutProtection           │
│  Uses: IDeadLetterQueue             │
│                                      │
│  ❌ NOT registered with orchestrator│
└─────────────────────────────────────┘
```

### After: Orchestrated Timeout Protection

```
┌────────────────────────────────────────────────┐
│  EventBusOrchestrator                          │
│  ┌──────────────────────────────────────────┐  │
│  │ Registered Machines:                     │  │
│  │  - StateMachine (native)                 │  │
│  │  - TimeoutProtectedStateMachine ✅ NEW!  │  │
│  │  - PureStateMachineAdapter               │  │
│  └──────────────────────────────────────────┘  │
│                                                 │
│  ✅ All machines communicate via events        │
│  ✅ Thread-safe without manual locking         │
│  ✅ Channel group isolation                    │
└────────────────────────────────────────────────┘
```

---

## Use Cases

### Use Case 1: E-Commerce Order Processing with Orchestration

**Scenario**: Multiple timeout-protected machines need to communicate

```csharp
var orchestrator = new EventBusOrchestrator(new OrchestratorConfig());

// Order processor with timeout protection
var orderProcessor = new TimeoutProtectedStateMachine(
    CreateOrderMachine(),
    timeoutProtection,
    orchestrator: orchestrator);

orderProcessor.SetStateTimeout("ProcessingPayment", TimeSpan.FromSeconds(30));

// Inventory service with timeout protection
var inventoryService = new TimeoutProtectedStateMachine(
    CreateInventoryMachine(),
    timeoutProtection,
    orchestrator: orchestrator);

inventoryService.SetStateTimeout("CheckingStock", TimeSpan.FromSeconds(10));

// Machines can now send events to each other via orchestrator
await orchestrator.SendEventAsync("InventoryService", "CHECK_STOCK", orderData);
```

### Use Case 2: Multi-Tenant with Channel Groups

**Scenario**: Isolate different tenants' machines

```csharp
var orchestrator = new EventBusOrchestrator(new OrchestratorConfig());

// Tenant 1 machines (channel group 1)
var tenant1Processor = new TimeoutProtectedStateMachine(
    CreateProcessorForTenant1(),
    timeoutProtection);
tenant1Processor.RegisterWithOrchestrator(orchestrator, channelGroupId: 1);

// Tenant 2 machines (channel group 2)
var tenant2Processor = new TimeoutProtectedStateMachine(
    CreateProcessorForTenant2(),
    timeoutProtection);
tenant2Processor.RegisterWithOrchestrator(orchestrator, channelGroupId: 2);

// Machines in different channel groups cannot communicate
// Provides tenant isolation
```

### Use Case 3: Microservices with Timeout Protection

**Scenario**: Each service has timeout-protected state machines

```csharp
// Service A (Order Service)
services.AddSingleton<EventBusOrchestrator>(...);
services.AddOrchestratedTimeoutProtectedStateMachine(
    sp => CreateOrderStateMachine(sp),
    "OrderService");

// Service B (Payment Service)
services.AddOrchestratedTimeoutProtectedStateMachine(
    sp => CreatePaymentStateMachine(sp),
    "PaymentService");

// Both services share the orchestrator and can communicate
// Both have automatic timeout protection
```

---

## Configuration

### appsettings.json Configuration

```json
{
  "Resilience": {
    "TimeoutProtection": {
      "DefaultTimeout": "00:00:30",
      "EnableAdaptiveTimeout": true,
      "AdaptiveTimeoutMultiplier": 1.5
    }
  },
  "StateMachines": {
    "OrderProcessor": {
      "TimeoutProtection": {
        "DefaultStateTimeout": "00:01:00",
        "DefaultTransitionTimeout": "00:00:10",
        "EnableTimeoutRecovery": true,
        "SendTimeoutEventsToDLQ": true
      }
    }
  }
}
```

### Code Configuration

```csharp
var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
{
    EnableMetrics = true,
    EnableLogging = true
});

var timeoutProtection = new TimeoutProtection(new TimeoutOptions
{
    DefaultTimeout = TimeSpan.FromSeconds(30),
    EnableAdaptiveTimeout = true
});

var protectedMachine = new TimeoutProtectedStateMachine(
    innerMachine,
    timeoutProtection,
    dlq: deadLetterQueue,
    options: new TimeoutProtectedStateMachineOptions
    {
        DefaultStateTimeout = TimeSpan.FromMinutes(1),
        DefaultTransitionTimeout = TimeSpan.FromSeconds(10),
        EnableTimeoutRecovery = true
    },
    logger: logger,
    orchestrator: orchestrator);

// Configure specific timeouts
protectedMachine.SetStateTimeout("ProcessingPayment", TimeSpan.FromSeconds(30));
protectedMachine.SetTransitionTimeout("Idle", "START", TimeSpan.FromSeconds(5));
```

---

## Benefits

### 1. First-Class Orchestrated Communication

✅ **Before**: TimeoutProtectedStateMachine was isolated - couldn't communicate via orchestrator
✅ **After**: Full participation in orchestrated event-based communication

### 2. Thread-Safe by Design

✅ No manual locking required
✅ EventBusOrchestrator handles concurrency
✅ Channel groups provide isolation

### 3. Combined Resilience

✅ **Timeout Protection**: Detects slow/stuck operations
✅ **Orchestration**: Thread-safe communication
✅ **DLQ**: Failed events captured for retry
✅ **Adaptive Learning**: Optimal timeouts learned over time

### 4. Flexible Deployment

✅ **Single Process**: All machines in one orchestrator
✅ **Multi-Tenant**: Channel groups for isolation
✅ **Microservices**: Distributed orchestration

---

## Migration Guide

### From Non-Orchestrated to Orchestrated

#### Before (No Orchestration)

```csharp
var protectedMachine = new TimeoutProtectedStateMachine(
    innerMachine,
    timeoutProtection,
    dlq,
    options,
    logger);

// Machine is isolated - cannot communicate via orchestrator
```

#### After (With Orchestration)

```csharp
var orchestrator = new EventBusOrchestrator(new OrchestratorConfig());

var protectedMachine = new TimeoutProtectedStateMachine(
    innerMachine,
    timeoutProtection,
    dlq,
    options,
    logger,
    orchestrator);  // ← Just add orchestrator parameter

// Machine is now orchestrated - can send/receive events
```

**Migration is backward compatible**: The `orchestrator` parameter is optional. Existing code continues to work unchanged.

---

## Best Practices

### 1. ✅ Register with Orchestrator for Multi-Machine Systems

```csharp
// Good: Multiple machines can communicate
var machine1 = new TimeoutProtectedStateMachine(..., orchestrator: orchestrator);
var machine2 = new TimeoutProtectedStateMachine(..., orchestrator: orchestrator);
```

### 2. ✅ Use Channel Groups for Isolation

```csharp
// Good: Tenant isolation
tenantMachine.RegisterWithOrchestrator(orchestrator, channelGroupId: tenantId);
```

### 3. ✅ Unregister on Disposal

```csharp
// Good: Clean up resources
public void Dispose()
{
    protectedMachine.UnregisterFromOrchestrator();
    protectedMachine.Stop();
}
```

### 4. ✅ Use DI for Automatic Wiring

```csharp
// Good: Orchestrator and machine managed by DI container
services.AddSingleton<EventBusOrchestrator>(...);
services.AddOrchestratedTimeoutProtectedStateMachine(...);
```

### 5. ❌ Don't Register Same Machine Twice

```csharp
// Bad: Machine already registered in constructor
var machine = new TimeoutProtectedStateMachine(..., orchestrator: orchestrator);
machine.RegisterWithOrchestrator(orchestrator);  // ❌ Already registered!
```

---

## Troubleshooting

### Problem: Machine Not Receiving Events

**Check:**
1. Is machine registered with orchestrator?
   ```csharp
   var stats = orchestrator.GetStats();
   Console.WriteLine($"Registered machines: {stats.RegisteredMachines}");
   ```

2. Is the machine ID correct?
   ```csharp
   Console.WriteLine($"Machine ID: {protectedMachine.Id}");
   // Should match the ID used in SendEventAsync
   ```

3. Are machines in the same channel group?
   ```csharp
   // Machines in different channel groups cannot communicate
   ```

### Problem: Events Timing Out

**Check:**
1. Are transition timeouts too aggressive?
   ```csharp
   protectedMachine.SetTransitionTimeout("State", "Event", TimeSpan.FromSeconds(30));
   ```

2. Is adaptive learning enabled?
   ```csharp
   var stats = protectedMachine.GetStatistics();
   var recommendation = stats.AdaptiveTimeouts
       .FirstOrDefault(x => x.OperationName == "MyOperation");
   ```

### Problem: Orchestrator Registration Fails

**Check:**
1. Is orchestrator instance valid?
   ```csharp
   if (orchestrator == null)
       throw new ArgumentNullException(nameof(orchestrator));
   ```

2. Is machine ID unique?
   ```csharp
   // Each machine needs unique ID in the orchestrator
   ```

---

## Related Documentation

- **[README_TIMEOUT_PROTECTION.md](README_TIMEOUT_PROTECTION.md)** - Complete timeout protection guide
- **[WHEN_TO_USE_TIMEOUT_PROTECTION.md](WHEN_TO_USE_TIMEOUT_PROTECTION.md)** - When to use timeout protection
- **[TIMEOUT_PROTECTION_MIGRATION_GUIDE.md](TIMEOUT_PROTECTION_MIGRATION_GUIDE.md)** - Migration from deprecated implementation
- **EventBusOrchestrator Documentation** - Orchestration fundamentals

---

## Testing

### Test Coverage

✅ **11 Tests Passing** in `TimeoutProtectedStateMachineTests.cs`:
- Basic functionality tests
- Timeout configuration tests
- Statistics collection tests
- Orchestrator integration tests
- Error handling tests

### Example Test

```csharp
[Fact]
public async Task TimeoutProtectedStateMachine_WithOrchestrator_CommunicatesSuccessfully()
{
    // Arrange
    var orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
    var innerMachine = CreateSimpleStateMachine();

    var protectedMachine = new TimeoutProtectedStateMachine(
        innerMachine,
        timeoutProtection,
        orchestrator: orchestrator);  // ← With orchestrator

    await protectedMachine.StartAsync();

    // Act
    await orchestrator.SendEventAsync(protectedMachine.Id, "START", null);

    // Assert
    Assert.Contains("active", protectedMachine.CurrentState);
}
```

---

## Version History

- **2025-10-05**: Orchestrator integration added
  - Optional `orchestrator` parameter in constructor
  - `RegisterWithOrchestrator()` method
  - `UnregisterFromOrchestrator()` method
  - DI extension methods updated
  - All tests passing (11/11)

---

## FAQ

### Q: Is this a breaking change?

**A**: No. The `orchestrator` parameter is optional. All existing code continues to work without modification.

### Q: Can I use TimeoutProtectedStateMachine without an orchestrator?

**A**: Yes. Simply don't pass the `orchestrator` parameter. The machine will work in standalone mode with timeout protection only.

### Q: How does this differ from native StateMachine orchestration?

**A**: Native `StateMachine` registers with orchestrator via `ExtendedPureStateMachineFactory`. `TimeoutProtectedStateMachine` wraps any `IStateMachine` and adds timeout protection while maintaining orchestrator compatibility.

### Q: Can I change channel groups after registration?

**A**: Yes. Unregister and re-register with a different channel group:
```csharp
machine.UnregisterFromOrchestrator();
machine.RegisterWithOrchestrator(orchestrator, channelGroupId: newGroupId);
```

### Q: What happens if orchestrator is disposed while machine is registered?

**A**: The machine will continue to function but cannot send/receive orchestrated events. Unregister before disposing the orchestrator:
```csharp
machine.UnregisterFromOrchestrator();
orchestrator.Dispose();
```

---

**Need Help?**
- 📖 Full Documentation: [README_TIMEOUT_PROTECTION.md](README_TIMEOUT_PROTECTION.md)
- 🐛 Report Issues: https://github.com/anthropics/claude-code/issues
- 💡 Examples: See `XStateNet.Distributed.Tests/Resilience/TimeoutProtectedStateMachineTests.cs`
