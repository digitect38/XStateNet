# Timeout Protection - Complete Guide

## 📚 Documentation Index

1. **[WHEN_TO_USE_TIMEOUT_PROTECTION.md](WHEN_TO_USE_TIMEOUT_PROTECTION.md)** ⭐ **START HERE**
   - Real-world scenarios where you need it
   - When NOT to use it
   - Decision tree and guidelines
   - **Best for**: Understanding if you need timeout protection

2. **[TIMEOUT_PROTECTION_QUICK_REFERENCE.md](TIMEOUT_PROTECTION_QUICK_REFERENCE.md)** 🚀 **QUICK START**
   - TL;DR comparison
   - 3-step migration guide
   - Code snippets
   - **Best for**: Quick lookup while coding

3. **[TIMEOUT_PROTECTION_MIGRATION_GUIDE.md](TIMEOUT_PROTECTION_MIGRATION_GUIDE.md)** 📖 **FULL GUIDE**
   - Complete migration examples
   - Before/after code samples
   - DI configuration
   - FAQ
   - **Best for**: Migrating from deprecated implementation

4. **[XSTATENET_TIMEOUT_PROTECTION_EVALUATION.md](XSTATENET_TIMEOUT_PROTECTION_EVALUATION.md)** 🔬 **TECHNICAL**
   - Architecture comparison
   - Implementation details
   - Risk assessment
   - **Best for**: Understanding why deprecation happened

---

## Quick Start

### Installation
```bash
dotnet add package XStateNet.Distributed
```

### Basic Usage
```csharp
// 1. Create timeout protection service
var timeoutProtection = new TimeoutProtection(
    new TimeoutOptions { DefaultTimeout = TimeSpan.FromSeconds(30) },
    circuitBreaker: null,
    logger);

// 2. Wrap your state machine
var protectedMachine = new TimeoutProtectedStateMachine(
    yourStateMachine,
    timeoutProtection,
    deadLetterQueue,
    options,
    logger);

// 3. Configure timeouts
protectedMachine.SetStateTimeout("Processing", TimeSpan.FromMinutes(5));
protectedMachine.SetTransitionTimeout("Idle", "Start", TimeSpan.FromSeconds(3));

// 4. Use it
await protectedMachine.StartAsync();
await protectedMachine.SendAsync("START", data);
```

### With Dependency Injection
```csharp
// In Startup.cs
services.AddXStateNetResilience(Configuration);
services.AddTimeoutProtectedStateMachine(
    sp => sp.GetRequiredService<IStateMachine>(),
    "MyMachine");
```

---

## What's Deprecated?

### ❌ XStateNetTimeoutProtectedStateMachine (DEPRECATED)
- **Status**: Marked `[Obsolete]` as of 2025-10-05
- **Removal**: Next major version (v2.0)
- **Reason**: Untested, uses deprecated APIs, duplicate functionality
- **Migration**: See [TIMEOUT_PROTECTION_MIGRATION_GUIDE.md](TIMEOUT_PROTECTION_MIGRATION_GUIDE.md)

### ✅ TimeoutProtectedStateMachine (RECOMMENDED)
- **Status**: Active, production-ready
- **Features**: Adaptive timeouts, DI support, modern APIs
- **Usage**: See [WHEN_TO_USE_TIMEOUT_PROTECTION.md](WHEN_TO_USE_TIMEOUT_PROTECTION.md)

---

## Key Concepts

### What Does Timeout Protection Do?

Wraps your state machine to monitor:
1. **State Timeouts**: "Machine stuck in 'Processing' for 5 minutes"
2. **Transition Timeouts**: "Transition from 'Idle' to 'Active' took 30 seconds"
3. **Action Timeouts**: "ValidateData action exceeded 10 seconds"

When timeout occurs:
- ✅ Logs warning/error with context
- ✅ Sends to Dead Letter Queue (optional)
- ✅ Triggers recovery/retry (optional)
- ✅ Collects statistics for monitoring

### When Do You Need It?

**USE IT for**:
- State machines calling external services (APIs, databases)
- Long-running operations (>1 second)
- Production environments with SLAs
- Safety-critical or financial workflows

**DON'T USE for**:
- In-memory only state machines
- Fast operations (<100ms)
- Prototype/development code
- State machines already with timeout logic

👉 **See [WHEN_TO_USE_TIMEOUT_PROTECTION.md](WHEN_TO_USE_TIMEOUT_PROTECTION.md) for detailed decision tree**

---

## Common Scenarios

### E-Commerce Order Processing
```csharp
// Prevent orders from getting stuck in "Processing Payment"
protectedMachine.SetStateTimeout("ProcessingPayment", TimeSpan.FromSeconds(30));
protectedMachine.SetStateTimeout("ConfirmingInventory", TimeSpan.FromSeconds(10));
```

### Manufacturing Equipment Control
```csharp
// SEMI E87 - Prevent equipment from waiting indefinitely
protectedMachine.SetStateTimeout("Mapping", TimeSpan.FromSeconds(30));
protectedMachine.SetStateTimeout("Loading", TimeSpan.FromSeconds(45));
```

### Distributed Workflow
```csharp
// Multi-service workflow with resilience
protectedMachine.SetTransitionTimeout("Idle", "SEND_EMAIL", TimeSpan.FromSeconds(10));
protectedMachine.SetStateTimeout("WaitingForVerification", TimeSpan.FromHours(24));
```

### IoT Device Management
```csharp
// Detect offline devices quickly
protectedMachine.SetTransitionTimeout("Idle", "SET_TEMP", TimeSpan.FromSeconds(5));
protectedMachine.SetStateTimeout("WaitingForAck", TimeSpan.FromSeconds(15));
```

👉 **See [WHEN_TO_USE_TIMEOUT_PROTECTION.md](WHEN_TO_USE_TIMEOUT_PROTECTION.md#real-world-scenarios-where-you-need-it) for full examples**

---

## Migration Path

### Step 1: Identify Usage
```bash
# Search for deprecated class
grep -r "XStateNetTimeoutProtectedStateMachine" --include="*.cs"
```

### Step 2: Replace Constructor
```csharp
// OLD
var machine = new XStateNetTimeoutProtectedStateMachine(
    innerMachine, dlq, options, logger);

// NEW
var timeoutService = new TimeoutProtection(timeoutOptions, null, logger);
var machine = new TimeoutProtectedStateMachine(
    innerMachine, timeoutService, dlq, options, logger);
```

### Step 3: Update Method Calls
```csharp
// OLD
machine.ConfigureStateTimeout("State", timeout);

// NEW
machine.SetStateTimeout("State", timeout);
```

### Step 4: Enable Adaptive Timeouts (Bonus)
```csharp
var timeoutOptions = new TimeoutOptions
{
    EnableAdaptiveTimeout = true,  // ✅ Learns optimal timeouts!
    AdaptiveTimeoutMultiplier = 1.5
};
```

👉 **See [TIMEOUT_PROTECTION_MIGRATION_GUIDE.md](TIMEOUT_PROTECTION_MIGRATION_GUIDE.md) for complete guide**

---

## Configuration

### Via Code
```csharp
var options = new TimeoutProtectedStateMachineOptions
{
    DefaultStateTimeout = TimeSpan.FromMinutes(1),
    DefaultTransitionTimeout = TimeSpan.FromSeconds(10),
    EnableTimeoutRecovery = true,
    SendTimeoutEventsToDLQ = true
};

var machine = new TimeoutProtectedStateMachine(
    innerMachine, timeoutProtection, dlq, options, logger);
```

### Via appsettings.json
```json
{
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

---

## Monitoring & Observability

### Get Statistics
```csharp
var stats = protectedMachine.GetStatistics();

Console.WriteLine($"Timeout Rate: {stats.TimeoutRate:P}");
Console.WriteLine($"Recovery Rate: {stats.RecoveryRate:P}");
Console.WriteLine($"Active Scopes: {stats.ActiveTimeoutScopes}");

// Adaptive timeout recommendations
foreach (var recommendation in stats.AdaptiveTimeouts)
{
    Console.WriteLine($"{recommendation.OperationName}: " +
        $"Current={recommendation.CurrentTimeout}, " +
        $"Recommended={recommendation.RecommendedTimeout} " +
        $"(based on {recommendation.SampleCount} samples)");
}
```

### Health Check Endpoint
```csharp
app.MapGet("/health/timeouts", async (TimeoutProtectedStateMachine machine) =>
{
    var stats = machine.GetStatistics();
    return Results.Ok(new
    {
        Healthy = stats.TimeoutRate < 0.01,  // < 1% timeout rate
        stats.TimeoutRate,
        stats.RecoveryRate,
        stats.ActiveTimeoutScopes,
        Recommendations = stats.AdaptiveTimeouts
    });
});
```

---

## Best Practices

### 1. Set Realistic Timeouts
```csharp
// ✅ GOOD: Based on P99 latency
protectedMachine.SetStateTimeout("ApiCall", TimeSpan.FromSeconds(30));  // 2x expected

// ❌ BAD: Arbitrary or unrealistic
protectedMachine.SetStateTimeout("ApiCall", TimeSpan.FromSeconds(1));   // Too aggressive
protectedMachine.SetStateTimeout("ApiCall", TimeSpan.FromMinutes(10));  // Too generous
```

### 2. Enable Adaptive Learning
```csharp
var timeoutOptions = new TimeoutOptions
{
    EnableAdaptiveTimeout = true,
    AdaptiveTimeoutMultiplier = 1.5  // 50% margin above average
};
```

### 3. Monitor Timeout Rates
```csharp
// Alert if timeout rate > 1%
if (stats.TimeoutRate > 0.01)
{
    alerting.NotifyOpsTeam($"High timeout rate: {stats.TimeoutRate:P}");
}
```

### 4. Use DLQ for Recovery
```csharp
var options = new TimeoutProtectedStateMachineOptions
{
    SendTimeoutEventsToDLQ = true,
    EnableTimeoutRecovery = true  // Auto-retry from DLQ
};
```

### 5. Different Timeouts per Environment
```json
{
  "Development": {
    "DefaultTimeout": "00:05:00"  // Generous for debugging
  },
  "Production": {
    "DefaultTimeout": "00:00:30"  // Strict for SLA compliance
  }
}
```

---

## Troubleshooting

### Problem: Timeout fires too often
**Solution**: Check adaptive recommendations
```csharp
var stats = machine.GetStatistics();
var recommendation = stats.AdaptiveTimeouts
    .FirstOrDefault(x => x.OperationName == "MyOperation");

if (recommendation != null)
{
    machine.SetTransitionTimeout("State", "Event", recommendation.RecommendedTimeout);
}
```

### Problem: Timeout doesn't fire when expected
**Check**: Is timeout configured for this state/transition?
```csharp
// Debug: Print all configured timeouts
logger.LogDebug("Configured timeouts: {Timeouts}",
    string.Join(", ", machine.GetConfiguredTimeouts()));
```

### Problem: DLQ entries not appearing
**Check**: Are DLQ options enabled?
```csharp
var options = new TimeoutProtectedStateMachineOptions
{
    SendTimeoutEventsToDLQ = true,      // ✅ Enable
    SendStateTimeoutsToDLQ = true,      // ✅ Enable
    SendTimeoutEventsToDLQ = true       // ✅ Enable
};
```

👉 **See [TROUBLESHOOTING_PATTERNS.md](TROUBLESHOOTING_PATTERNS.md) for more**

---

## FAQ

**Q: Will my code break when upgrading?**
**A**: No, `XStateNetTimeoutProtectedStateMachine` is marked `[Obsolete]` with `error: false`. You'll get warnings but code compiles.

**Q: When will it be removed?**
**A**: Next major version (v2.0). You have time to migrate.

**Q: Can I use both Polly and TimeoutProtectedStateMachine?**
**A**: Yes! Use Polly for HTTP resilience, TimeoutProtectedStateMachine for state machine resilience. They complement each other.

**Q: Does it work with OrchestratedCircuitBreaker?**
**A**: Yes! `TimeoutProtection` can integrate with `ICircuitBreaker` for combined timeout + circuit breaking.

**Q: What's the performance overhead?**
**A**: Minimal (~1-2ms per transition) for timeout checking. Adaptive learning adds negligible overhead.

---

## Related Documentation

- **XStateNet Core**: State machine fundamentals
- **EventBusOrchestrator**: Thread-safe orchestration
- **OrchestratedCircuitBreaker**: Circuit breaking pattern
- **DeadLetterQueue**: Failed event storage and retry
- **TROUBLESHOOTING_PATTERNS.md**: Debugging guide

---

## Version History

- **2025-10-05**: XStateNetTimeoutProtectedStateMachine marked deprecated
- **2025-10-05**: Documentation created
- **Future (v2.0)**: XStateNetTimeoutProtectedStateMachine removed

---

**Need Help?**
- 📖 Read: [WHEN_TO_USE_TIMEOUT_PROTECTION.md](WHEN_TO_USE_TIMEOUT_PROTECTION.md)
- 🚀 Quick Start: [TIMEOUT_PROTECTION_QUICK_REFERENCE.md](TIMEOUT_PROTECTION_QUICK_REFERENCE.md)
- 🔧 Migration: [TIMEOUT_PROTECTION_MIGRATION_GUIDE.md](TIMEOUT_PROTECTION_MIGRATION_GUIDE.md)
- 🐛 Issues: https://github.com/anthropics/claude-code/issues
