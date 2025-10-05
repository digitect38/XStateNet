# Timeout Protection Quick Reference

## TL;DR - What Should I Use?

```csharp
// ❌ DON'T USE (Deprecated)
var old = new XStateNetTimeoutProtectedStateMachine(machine, dlq, options, logger);

// ✅ USE THIS INSTEAD
var timeoutService = new TimeoutProtection(timeoutOptions, null, logger);
var wrapper = new TimeoutProtectedStateMachine(machine, timeoutService, dlq, options, logger);
```

---

## Quick Comparison

| Question | XStateNetTimeoutProtectedStateMachine | TimeoutProtectedStateMachine |
|----------|---------------------------------------|------------------------------|
| **Should I use this?** | ❌ No (deprecated) | ✅ Yes (recommended) |
| **Why not/why?** | Untested, deprecated APIs, no DI | Production-ready, DI support, adaptive learning |
| **Test coverage?** | 0% | Minimal (via service mocks) |
| **Production usage?** | None | Used in DI extensions |
| **Adaptive timeouts?** | ❌ No | ✅ Yes |
| **DI support?** | ❌ No | ✅ Yes |

---

## What Does Timeout Protection Do?

Both wrappers monitor your state machine for:

1. **State Timeouts**: "Machine stuck in 'Processing' for 5 minutes"
2. **Transition Timeouts**: "Transition from 'Idle' to 'Active' took 30 seconds"
3. **Action Timeouts**: "ValidateData action exceeded 10 seconds"

When timeout occurs:
- ✅ Send to Dead Letter Queue
- ✅ Trigger recovery (optional)
- ✅ Log warning/error
- ✅ Collect statistics

---

## Migration in 3 Steps

### 1. Replace Constructor
```csharp
// OLD
var protected = new XStateNetTimeoutProtectedStateMachine(
    innerMachine, dlq, options, logger);

// NEW
var timeoutProtection = new TimeoutProtection(...);  // Add this
var protected = new TimeoutProtectedStateMachine(
    innerMachine, timeoutProtection, dlq, options, logger);  // Add timeoutProtection param
```

### 2. Rename Configuration Methods
```csharp
// OLD
protected.ConfigureStateTimeout("Processing", TimeSpan.FromMinutes(5));

// NEW
protected.SetStateTimeout("Processing", TimeSpan.FromMinutes(5));
```

### 3. Done!
Same API, same behavior, better implementation.

---

## Bonus: Use Dependency Injection

```csharp
// In Startup.cs or Program.cs
services.AddXStateNetResilience(Configuration);

services.AddTimeoutProtectedStateMachine(
    sp => sp.GetRequiredService<IStateMachine>(),
    stateMachineName: "MyMachine"
);
```

Then in appsettings.json:
```json
{
  "StateMachines": {
    "MyMachine": {
      "TimeoutProtection": {
        "DefaultStateTimeout": "00:01:00",
        "EnableTimeoutRecovery": true
      }
    }
  }
}
```

---

## Why Two Implementations Existed?

**XStateNetTimeoutProtectedStateMachine**: Experimental proof-of-concept using an internal state machine (400+ lines of JSON) to monitor timeouts. Never finished, never tested.

**TimeoutProtectedStateMachine**: Production implementation using service interfaces (`ITimeoutProtection`). Testable, maintainable, supports DI.

**Decision**: Keep the one that works, deprecate the experiment.

---

For full migration guide, see `TIMEOUT_PROTECTION_MIGRATION_GUIDE.md`
