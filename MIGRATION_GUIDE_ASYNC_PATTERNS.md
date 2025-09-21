# Migration Guide: Async Patterns and Event-Driven State Tracking

## Overview
This guide helps migrate XStateNet code from synchronous patterns to async/await and event-driven state tracking.

## Key Changes

### 1. Replace `GetActiveStateString()` with `StateChanged` Event
**Deprecated:** `GetActiveStateString()` is marked obsolete and will be removed in the next major version.

#### Old Pattern:
```csharp
machine.Send("START");
await Task.Delay(100); // Wait for state transition
var state = machine.GetActiveStateString();
Assert.Contains("running", state);
```

#### New Pattern:
```csharp
var currentState = "";
machine.StateChanged += (state) => currentState = state;

await machine.SendAsync("START");
Assert.Contains("running", currentState);
```

#### For Test Assertions with Multiple Transitions:
```csharp
var stateHistory = new List<string>();
var expectedStates = new[] { "running", "paused", "idle" };

machine.StateChanged += (state) => stateHistory.Add(state);

await machine.SendAsync("START");
await machine.SendAsync("PAUSE");
await machine.SendAsync("STOP");

Assert.Equal(expectedStates, stateHistory);
```

### 2. Replace `Send()` with `SendAsync()` or `SendAsyncWithState()`
**Recommended:** Use `SendAsyncWithState()` when you need the resulting state.

#### Old Pattern:
```csharp
machine.Send("EVENT");
Thread.Sleep(100); // Or Task.Delay
var state = machine.GetActiveStateString();
```

#### New Pattern (Simple):
```csharp
await machine.SendAsync("EVENT");
// State transition is complete when SendAsync returns
```

#### New Pattern (With State Return):
```csharp
// SendAsyncWithState returns the new state after transition
var newState = await machine.SendAsyncWithState("EVENT");
// No need to call GetActiveStateString()
```

#### Chaining Transitions with State Verification:
```csharp
var state1 = await machine.SendAsyncWithState("START");
Assert.Equal("running", state1);

var state2 = await machine.SendAsyncWithState("PAUSE");
Assert.Equal("paused", state2);

var finalState = await machine.SendAsyncWithState("STOP");
Assert.Equal("idle", finalState);
```

### 3. Deterministic Testing
When using `DeterministicTestMode`, both patterns work synchronously:

```csharp
using (DeterministicTestMode.Enable())
{
    var stateHistory = new List<string>();
    machine.StateChanged += (state) => stateHistory.Add(state);

    // These complete synchronously in deterministic mode
    await machine.SendAsync("START");
    await machine.SendAsync("STOP");

    // No delays needed
    Assert.Equal(["running", "idle"], stateHistory);
}
```

## Migration Steps

### For Test Files:

1. **Add StateChanged subscription at test setup:**
```csharp
[Fact]
public async Task TestMethod()
{
    var machine = CreateMachine();
    var currentState = "";
    machine.StateChanged += (state) => currentState = state;
    // ... rest of test
}
```

2. **Replace Send with SendAsync:**
```csharp
// Before
machine.Send("EVENT");
await Task.Delay(100);

// After
await machine.SendAsync("EVENT");
```

3. **Remove GetActiveStateString calls:**
```csharp
// Before
Assert.Contains("expected", machine.GetActiveStateString());

// After (using StateChanged subscription)
Assert.Contains("expected", currentState);
```

### For Production Code:

1. **Subscribe to StateChanged for monitoring:**
```csharp
public class StateMachineService
{
    private readonly IStateMachine _machine;
    private string _currentState = "";

    public StateMachineService()
    {
        _machine = CreateMachine();
        _machine.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(string newState)
    {
        _currentState = newState;
        // Log, update UI, trigger other actions
    }

    public async Task ProcessEventAsync(string eventName)
    {
        await _machine.SendAsync(eventName);
    }
}
```

2. **Use async/await throughout:**
```csharp
// Update method signatures
public async Task ProcessWorkflowAsync()
{
    await _machine.SendAsync("START");
    // ... other async operations
    await _machine.SendAsync("COMPLETE");
}
```

## Common Patterns

### Waiting for Specific State
```csharp
var tcs = new TaskCompletionSource<bool>();
machine.StateChanged += (state) =>
{
    if (state.Contains("target"))
        tcs.TrySetResult(true);
};

await machine.SendAsync("TRIGGER");
await tcs.Task; // Wait for target state
```

### Tracking State History
```csharp
var stateHistory = new ConcurrentQueue<string>();
machine.StateChanged += (state) => stateHistory.Enqueue(state);
```

### Reactive UI Updates
```csharp
machine.StateChanged += (state) =>
{
    Dispatcher.Invoke(() =>
    {
        StatusLabel.Text = $"Current State: {state}";
    });
};
```

## Benefits of Migration

1. **No Race Conditions:** Event-driven pattern eliminates timing issues
2. **Better Performance:** No arbitrary delays needed
3. **Cleaner Code:** Async/await makes flow more readable
4. **Reactive Programming:** Natural fit for state machines
5. **Testability:** Deterministic mode ensures predictable test execution

## Suppressing Warnings During Migration

If you need to temporarily suppress obsolete warnings during migration:

```csharp
#pragma warning disable CS0618 // Type or member is obsolete
var state = machine.GetActiveStateString();
#pragma warning restore CS0618
```

## Tools and Scripts

A PowerShell script is available to help automate some migrations:
- `fix_nullable_warnings.ps1` - Includes patterns for updating GetActiveStateString usage

## Next Steps

1. Update test files first (they're isolated)
2. Update example/demo code
3. Update production code with proper async patterns
4. Remove obsolete method usage suppressions
5. Consider enabling deterministic mode for all tests