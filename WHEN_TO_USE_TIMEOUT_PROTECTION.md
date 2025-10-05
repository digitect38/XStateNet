# When Is TimeoutProtectedStateMachine Needed?

## Quick Answer

Use `TimeoutProtectedStateMachine` when your state machine:
1. **Calls external services** (APIs, databases, message queues)
2. **Performs long-running operations** (file processing, batch jobs)
3. **Can get stuck** in states due to external failures
4. **Needs resilience** in production environments

**Don't use it** for:
- Simple, fast in-memory state machines
- State machines that already have timeout logic
- Prototype/development code (use in production only)

---

## Real-World Scenarios Where You NEED It

### ✅ Scenario 1: E-Commerce Order Processing

**Problem**: Orders get stuck in "Processing Payment" state when payment gateway is slow/down.

```csharp
// WITHOUT TimeoutProtectedStateMachine
var orderMachine = CreateOrderStateMachine();
await orderMachine.SendAsync("PROCESS_PAYMENT", order);
// ❌ If payment gateway hangs, order stuck forever
// ❌ No automatic recovery
// ❌ Customer sees "Processing..." forever
```

**Solution**:
```csharp
// WITH TimeoutProtectedStateMachine
var timeoutProtection = new TimeoutProtection(...);
var protectedMachine = new TimeoutProtectedStateMachine(
    orderMachine,
    timeoutProtection,
    dlq,
    new TimeoutProtectedStateMachineOptions
    {
        DefaultStateTimeout = TimeSpan.FromMinutes(2),      // Max time in any state
        DefaultTransitionTimeout = TimeSpan.FromSeconds(30), // Max time for transitions
        EnableTimeoutRecovery = true,
        SendTimeoutEventsToDLQ = true
    },
    logger
);

// Configure critical state timeouts
protectedMachine.SetStateTimeout("ProcessingPayment", TimeSpan.FromSeconds(30));
protectedMachine.SetStateTimeout("ConfirmingInventory", TimeSpan.FromSeconds(10));

await protectedMachine.StartAsync();
await protectedMachine.SendAsync("PROCESS_PAYMENT", order);

// ✅ If payment gateway hangs > 30s, timeout fires
// ✅ Order sent to Dead Letter Queue for manual review
// ✅ Customer notified of payment timeout
// ✅ Statistics tracked for SLA monitoring
```

**Benefits**:
- ✅ Orders don't get stuck indefinitely
- ✅ Failed orders captured in DLQ for retry/investigation
- ✅ SLA compliance (99% orders processed within 2 minutes)

---

### ✅ Scenario 2: Manufacturing Equipment Control (SEMI E87)

**Problem**: Equipment state machines wait for hardware responses that may never come.

```csharp
// Equipment waits for wafer carrier to be ready
var carrierMachine = new E87CarrierMachine("CARRIER001");

// WITHOUT TimeoutProtectedStateMachine
await carrierMachine.SendAsync("START_MAPPING");
// ❌ If mapping sensor fails, machine waits forever
// ❌ Production line halted
// ❌ No automatic failsafe
```

**Solution**:
```csharp
var protectedCarrier = new TimeoutProtectedStateMachine(
    carrierMachine,
    timeoutProtection,
    equipmentDLQ,
    new TimeoutProtectedStateMachineOptions
    {
        DefaultStateTimeout = TimeSpan.FromSeconds(60),
        EnableTimeoutRecovery = false,  // ❌ Don't auto-retry equipment operations
        SendStateTimeoutsToDLQ = true,
        SendTimeoutEventsToDLQ = true
    },
    logger
);

// Critical safety timeouts
protectedCarrier.SetStateTimeout("Mapping", TimeSpan.FromSeconds(30));
protectedCarrier.SetStateTimeout("Loading", TimeSpan.FromSeconds(45));
protectedCarrier.SetStateTimeout("Unloading", TimeSpan.FromSeconds(45));
protectedCarrier.SetTransitionTimeout("Idle", "START_LOAD", TimeSpan.FromSeconds(5));

await protectedCarrier.StartAsync();

try
{
    await protectedCarrier.SendAsync("START_MAPPING", carrierData);
}
catch (TimeoutException ex)
{
    // ✅ Timeout detected - enter safety state
    await equipmentController.EmergencyStopAsync();
    await alarmSystem.RaiseAlarmAsync("CARRIER_TIMEOUT", carrierData);

    // ✅ DLQ contains full context for maintenance
    var dlqEntry = await equipmentDLQ.GetEntriesAsync(1);
    logger.LogCritical("Carrier {Id} timeout: {Context}",
        carrierData.Id, dlqEntry.First().Metadata);
}
```

**Benefits**:
- ✅ Equipment never waits indefinitely (safety requirement)
- ✅ Production halts gracefully on timeout
- ✅ Maintenance team notified with full diagnostic data
- ✅ Compliance with SEMI standards (E10, E30)

---

### ✅ Scenario 3: Distributed Workflow with External Dependencies

**Problem**: Multi-step workflow calls multiple microservices - any can fail or hang.

```csharp
// Workflow: User signup → Email verification → Account activation → Welcome email
var signupWorkflow = CreateSignupWorkflow();

// WITHOUT TimeoutProtectedStateMachine
await signupWorkflow.SendAsync("VERIFY_EMAIL", user);
// ❌ If email service is down, workflow stuck at "WaitingForVerification"
// ❌ User can't complete signup
// ❌ No retry mechanism
```

**Solution**:
```csharp
var protectedWorkflow = new TimeoutProtectedStateMachine(
    signupWorkflow,
    timeoutProtection,
    workflowDLQ,
    new TimeoutProtectedStateMachineOptions
    {
        DefaultStateTimeout = TimeSpan.FromMinutes(10),
        DefaultTransitionTimeout = TimeSpan.FromSeconds(30),
        EnableTimeoutRecovery = true,  // ✅ Auto-retry transient failures
        SendTimeoutEventsToDLQ = true
    },
    logger
);

// Per-state timeout configuration
protectedWorkflow.SetStateTimeout("SendingVerificationEmail", TimeSpan.FromSeconds(15));
protectedWorkflow.SetStateTimeout("WaitingForVerification", TimeSpan.FromHours(24));  // User has 24h
protectedWorkflow.SetStateTimeout("ActivatingAccount", TimeSpan.FromSeconds(10));
protectedWorkflow.SetStateTimeout("SendingWelcomeEmail", TimeSpan.FromSeconds(15));

// Per-transition timeout (service calls)
protectedWorkflow.SetTransitionTimeout("PendingVerification", "SEND_EMAIL", TimeSpan.FromSeconds(10));
protectedWorkflow.SetTransitionTimeout("EmailSent", "VERIFY", TimeSpan.FromSeconds(5));

await protectedWorkflow.StartAsync();
await protectedWorkflow.SendAsync("START_SIGNUP", user);

// Monitoring
var stats = protectedWorkflow.GetStatistics();
if (stats.TimeoutRate > 0.05)  // > 5% timeout rate
{
    alerting.NotifyOpsTeam($"Signup workflow timeout rate: {stats.TimeoutRate:P}");
}
```

**Benefits**:
- ✅ Failed signups captured in DLQ for retry
- ✅ Email service outages don't permanently fail signups
- ✅ User experience: "Signup is taking longer than usual, we'll email you when ready"
- ✅ Ops team alerted when external services degrade

---

### ✅ Scenario 4: IoT Device State Management

**Problem**: IoT devices may disconnect, lose power, or enter unknown states.

```csharp
// Smart thermostat state machine
var thermostatMachine = CreateThermostatMachine(deviceId);

// WITHOUT TimeoutProtectedStateMachine
await thermostatMachine.SendAsync("SET_TEMPERATURE", 72);
// ❌ If device offline, command sent but never acknowledged
// ❌ UI shows "Setting temperature..." forever
// ❌ No detection of device failure
```

**Solution**:
```csharp
var protectedThermostat = new TimeoutProtectedStateMachine(
    thermostatMachine,
    timeoutProtection,
    deviceDLQ,
    new TimeoutProtectedStateMachineOptions
    {
        DefaultStateTimeout = TimeSpan.FromSeconds(30),
        DefaultTransitionTimeout = TimeSpan.FromSeconds(10),
        EnableTimeoutRecovery = true,
        SendStateTimeoutsToDLQ = true
    },
    logger
);

// Device communication timeouts
protectedThermostat.SetTransitionTimeout("Idle", "SET_TEMP", TimeSpan.FromSeconds(5));
protectedThermostat.SetTransitionTimeout("SettingTemp", "CONFIRM", TimeSpan.FromSeconds(10));
protectedThermostat.SetStateTimeout("WaitingForAck", TimeSpan.FromSeconds(15));

try
{
    await protectedThermostat.SendAsync("SET_TEMPERATURE", new { Temp = 72 });
}
catch (TimeoutException)
{
    // ✅ Device offline detected
    await deviceRegistry.MarkAsOfflineAsync(deviceId);
    await notificationService.NotifyUserAsync(userId,
        "Your thermostat appears to be offline. Last seen 5 minutes ago.");

    // ✅ Queue command for retry when device comes back online
    await deviceCommandQueue.EnqueueAsync(deviceId, "SET_TEMPERATURE", 72);
}

// Adaptive learning for unreliable networks
var adaptiveStats = protectedThermostat.GetStatistics();
var recommendedTimeout = adaptiveStats.AdaptiveTimeouts
    .FirstOrDefault(x => x.OperationName.Contains("SET_TEMP"))
    ?.RecommendedTimeout ?? TimeSpan.FromSeconds(5);

logger.LogInformation("Learned optimal timeout for device {Device}: {Timeout}",
    deviceId, recommendedTimeout);
```

**Benefits**:
- ✅ Device offline detection within seconds
- ✅ Commands queued for retry when device reconnects
- ✅ User notifications instead of silent failures
- ✅ Adaptive timeouts learn device-specific network latency

---

## Scenarios Where You DON'T Need It

### ❌ Scenario A: Simple UI State Machine

```csharp
// Toggle button: enabled ⇄ disabled
var buttonMachine = CreateToggleButtonMachine();
await buttonMachine.SendAsync("TOGGLE");

// ❌ DON'T wrap with TimeoutProtectedStateMachine
// - Executes in microseconds
// - No external dependencies
// - Timeout would add overhead for no benefit
```

### ❌ Scenario B: In-Memory Event Processing

```csharp
// Event aggregator: collect → validate → publish
var eventAggregator = CreateEventAggregatorMachine();
await eventAggregator.SendAsync("ADD_EVENT", evt);

// ❌ DON'T wrap with TimeoutProtectedStateMachine
// - All in-memory operations
// - No I/O, no network calls
// - Fast (<1ms) state transitions
```

### ❌ Scenario C: Already Has Timeout Logic

```csharp
// HttpClient already has timeout
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

var apiMachine = CreateApiCallMachine(httpClient);
// ❌ DON'T wrap - HttpClient already handles timeout
// - Would create duplicate timeout logic
// - Harder to debug (which timeout fired?)
```

---

## Decision Tree: Do I Need TimeoutProtectedStateMachine?

```
┌─────────────────────────────────────────┐
│ Does your state machine call            │
│ external services or I/O?               │
└───────────┬─────────────────────────────┘
            │
       ┌────┴────┐
       │         │
      YES       NO → ❌ Don't use it
       │
       ▼
┌─────────────────────────────────────────┐
│ Can those calls take >1 second           │
│ or fail/hang?                           │
└───────────┬─────────────────────────────┘
            │
       ┌────┴────┐
       │         │
      YES       NO → ❌ Don't use it
       │
       ▼
┌─────────────────────────────────────────┐
│ Do you need resilience in production?   │
│ (retry, DLQ, monitoring)                │
└───────────┬─────────────────────────────┘
            │
       ┌────┴────┐
       │         │
      YES       NO → ⚠️ Maybe use simpler timeout (CancellationToken)
       │
       ▼
✅ USE TimeoutProtectedStateMachine
```

---

## Comparison: TimeoutProtectedStateMachine vs. Alternatives

### vs. Manual CancellationToken

```csharp
// Manual CancellationToken approach
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    await stateMachine.SendAsync("EVENT", data, cts.Token);
}
catch (OperationCanceledException)
{
    // ❌ Have to manually:
    // - Log timeout
    // - Send to DLQ
    // - Track statistics
    // - Implement retry logic
    // - Learn adaptive timeouts
}

// TimeoutProtectedStateMachine approach
var protected = new TimeoutProtectedStateMachine(stateMachine, ...);
await protected.SendAsync("EVENT", data);
// ✅ Automatic:
// - Logging with context
// - DLQ integration
// - Statistics collection
// - Retry via recovery
// - Adaptive timeout learning
```

**Use CancellationToken when**: Single operation, simple timeout needed
**Use TimeoutProtectedStateMachine when**: Multiple operations, need resilience features

---

### vs. Polly (Resilience Library)

```csharp
// Polly approach
var policy = Policy
    .TimeoutAsync(TimeSpan.FromSeconds(30))
    .WrapAsync(Policy
        .Handle<TimeoutException>()
        .RetryAsync(3));

await policy.ExecuteAsync(async () =>
{
    await stateMachine.SendAsync("EVENT", data);
});

// TimeoutProtectedStateMachine approach
var protected = new TimeoutProtectedStateMachine(stateMachine, ...);
await protected.SendAsync("EVENT", data);
```

**Polly advantages**:
- ✅ More policy types (retry, circuit breaker, bulkhead)
- ✅ More granular control
- ✅ Industry standard library

**TimeoutProtectedStateMachine advantages**:
- ✅ State-aware timeouts (different per state)
- ✅ Transition-specific timeouts
- ✅ Adaptive timeout learning
- ✅ Integrated with XStateNet ecosystem

**Best choice**: Use **both** - Polly for HTTP resilience, TimeoutProtectedStateMachine for state machine resilience

---

### vs. OrchestratedCircuitBreaker

```csharp
// OrchestratedCircuitBreaker - failure rate threshold
var breaker = new OrchestratedCircuitBreaker("api", orchestrator,
    failureThreshold: 5,
    openDuration: TimeSpan.FromMinutes(1));

await breaker.ExecuteAsync(async ct =>
{
    await apiClient.CallAsync(data);
});
// ✅ Prevents cascading failures
// ✅ Fast-fail when service degraded
// ❌ Doesn't detect slow operations (only failures)

// TimeoutProtectedStateMachine - timeout detection
var protected = new TimeoutProtectedStateMachine(stateMachine, ...);
await protected.SendAsync("CALL_API", data);
// ✅ Detects slow operations
// ✅ Per-state/transition timeouts
// ❌ Doesn't prevent cascading failures
```

**Best practice**: Use **both together**:
```csharp
services.AddSingleton<ICircuitBreaker>(...);  // For circuit breaking
services.AddSingleton<ITimeoutProtection>(sp =>
{
    var breaker = sp.GetRequiredService<ICircuitBreaker>();
    return new TimeoutProtection(options, breaker, logger);
});
services.AddTimeoutProtectedStateMachine(...);  // Wraps state machine with both
```

---

## Practical Guidelines

### When to Add TimeoutProtectedStateMachine

**Add it immediately if**:
- ✅ State machine in production
- ✅ Calls 2+ external services
- ✅ SLA requirements (e.g., "99% complete within 5 minutes")
- ✅ Money/safety involved (payments, equipment control)

**Consider adding if**:
- ⚠️ Single external service call
- ⚠️ Operations take >5 seconds
- ⚠️ Have seen production hangs before

**Skip it if**:
- ❌ Prototype/demo code
- ❌ All operations <100ms
- ❌ No external dependencies
- ❌ Already has comprehensive timeout logic

---

### Configuration Best Practices

```csharp
// Good timeout values
protectedMachine.SetStateTimeout("ApiCall", TimeSpan.FromSeconds(30));      // ✅ 2x expected time
protectedMachine.SetStateTimeout("DatabaseQuery", TimeSpan.FromSeconds(10)); // ✅ 3x P99 latency
protectedMachine.SetStateTimeout("UserInput", TimeSpan.FromMinutes(5));     // ✅ Based on UX requirements

// Bad timeout values
protectedMachine.SetStateTimeout("ApiCall", TimeSpan.FromSeconds(1));       // ❌ Too aggressive
protectedMachine.SetStateTimeout("DatabaseQuery", TimeSpan.FromMinutes(10)); // ❌ Too generous
protectedMachine.SetStateTimeout("UserInput", TimeSpan.FromSeconds(10));    // ❌ Unrealistic for user
```

**Timeout tuning guidelines**:
1. Start with **2x expected duration**
2. Enable **adaptive learning** (`EnableAdaptiveTimeout = true`)
3. Monitor timeout rate (target: <1%)
4. Adjust based on **P99 latency** from metrics
5. Re-evaluate **quarterly** as dependencies change

---

## Summary

### Use TimeoutProtectedStateMachine When:
- ✅ Calling **external services** (APIs, databases, queues)
- ✅ **Long-running operations** (>1 second)
- ✅ Need **resilience** (retry, DLQ, monitoring)
- ✅ **Production environment** with SLAs
- ✅ **Safety-critical** or **financial** workflows

### Don't Use It When:
- ❌ **In-memory only** state machines
- ❌ **Fast operations** (<100ms)
- ❌ Already has **timeout logic**
- ❌ **Prototype/development** code
- ❌ No **external dependencies**

### The Rule of Thumb:
> "If your state machine can get stuck waiting for something outside your process, wrap it with TimeoutProtectedStateMachine."

---

**See also**:
- `TIMEOUT_PROTECTION_MIGRATION_GUIDE.md` - How to implement
- `XSTATENET_TIMEOUT_PROTECTION_EVALUATION.md` - Technical details
- `TROUBLESHOOTING_PATTERNS.md` - Debugging timeout issues
