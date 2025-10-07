# Deterministic Wait Pattern

## Problem
Tests using `Task.Delay()` with fixed timeouts are non-deterministic:
- ❌ Too short → tests fail randomly on slower machines
- ❌ Too long → tests waste time waiting unnecessarily
- ❌ No way to detect if queue has drained

## Solution: ResilienceTestBase
A base class providing deterministic wait helpers that:
- ✅ Poll until condition is met (fast on fast machines)
- ✅ Detect "no progress" to break early (queue drained)
- ✅ Provide reasonable timeout as safety net
- ✅ Eliminate code duplication across tests

## API

### WaitForCountAsync
Wait for a counter to reach a target value:
```csharp
// Wait for 50 events to be processed
await WaitForCountAsync(() => processedCount, targetValue: 50, timeoutSeconds: 5);
```

### WaitForConditionAsync
Wait for any boolean condition:
```csharp
// Wait for complex condition
await WaitForConditionAsync(
    condition: () => successCount > 40 && failureCount < 10,
    getProgress: () => successCount + failureCount,
    timeoutSeconds: 5);
```

### WaitUntilQuiescentAsync
Wait until no progress detected (queue drained):
```csharp
// Wait for queue to drain
await WaitUntilQuiescentAsync(
    getProgress: () => processedCount,
    noProgressTimeoutMs: 1000);
```

### WaitWithProgressAsync
Minimum wait + quiescent detection:
```csharp
// Wait at least 500ms, then until quiescent
await WaitWithProgressAsync(
    getProgress: () => processedCount,
    minimumWaitMs: 500,
    additionalQuiescentMs: 500);
```

## Before vs After

### ❌ Before (Non-deterministic)
```csharp
await Task.WhenAll(tasks);
await Task.Delay(1000); // Fixed delay - might be too short or too long

Assert.True(processedCount >= 40); // Might fail on slow machines
```

### ✅ After (Deterministic)
```csharp
await Task.WhenAll(tasks);
await WaitForCountAsync(() => processedCount, targetValue: 50);

Assert.True(processedCount >= 40); // Reliable on all machines
```

## Implementation Details

### Progress Detection
The helper tracks progress between polls:
```csharp
while (!condition() && DateTime.UtcNow < deadline)
{
    await Task.Delay(50); // Poll every 50ms

    if (getProgress() == lastProgress)
    {
        noProgressCount++;
        if (noProgressCount >= 20) // 1 second with no progress
            break; // Queue likely drained
    }
    else
    {
        noProgressCount = 0; // Reset on progress
        lastProgress = getProgress();
    }
}
```

### Benefits
1. **Fast**: Completes as soon as condition is met
2. **Reliable**: Doesn't fail on slow machines
3. **Early exit**: Detects queue drainage to avoid wasting time
4. **Configurable**: Timeout and thresholds can be adjusted
5. **Reusable**: One implementation used across all tests

## Usage Pattern

1. **Inherit from ResilienceTestBase**:
```csharp
public class MyTests : ResilienceTestBase
{
    public MyTests(ITestOutputHelper output) : base(output) { }
}
```

2. **Replace Task.Delay with deterministic wait**:
```csharp
// Old:
await Task.Delay(1000);

// New:
await WaitUntilQuiescentAsync(() => processedCount);
```

3. **Assert with confidence**:
```csharp
// The wait ensures condition is met or timeout reached
Assert.True(processedCount >= expectedCount);
```

## Migration Status

### ✅ Migrated
- QuickContinuousTests.cs
  - QuickContinuous_ConcurrentEvents_1000Times
  - QuickContinuous_RaceConditions_1000Times
  - QuickContinuous_MemoryStress_1000Times

### 🔄 To Migrate
- NetworkChaosTests.cs (partially done)
- ResourceLimitTests.cs (2 tests done)
- SoakStabilityTests.cs
- All other resilience tests

## Recommendation

**All test classes** in the Resilience namespace should:
1. Inherit from `ResilienceTestBase`
2. Replace all `Task.Delay()` with appropriate deterministic wait helpers
3. Use progress tracking (counter increments) to detect completion

This will make the entire test suite **reliable**, **fast**, and **maintainable**.
