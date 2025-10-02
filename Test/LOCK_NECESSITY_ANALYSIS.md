# Why Does Sending an Event Require a Lock?

## The Current Implementation

When you call `SendAsync(event)`, XStateNet:
1. **Acquires a write lock** (`_stateLock.EnterWriteLock()`)
2. Processes the transition
3. Executes actions
4. Releases the lock

## But Why Lock for Sending?

### The Real Question: What If We Didn't Lock?

Let's imagine a **lock-free event queue** approach:

```csharp
// Hypothetical lock-free implementation
public async Task<string> SendAsync(string eventName, object? eventData = null)
{
    // Just queue the event without locking
    _eventQueue.Enqueue(new Event(eventName, eventData));

    // Return immediately
    return GetActiveStateNames();
}

// Background processor
async Task ProcessEvents()
{
    while (true)
    {
        if (_eventQueue.TryDequeue(out var evt))
        {
            // NOW acquire lock for processing
            _stateLock.EnterWriteLock();
            try
            {
                await ProcessEvent(evt);
            }
            finally
            {
                _stateLock.ExitWriteLock();
            }
        }
    }
}
```

## Why This Could Work

With a lock-free queue approach:

```
Action Execution                    Event Queue
================                   ============
│
├─► sm.SendAsync("SENT")
│   │
│   └─► Enqueue("SENT") ✓          ["SENT"]
│       (No lock needed!)
│
├─► Continue with action...
│
└─► Action completes
    └─► Release main lock

                                   Background Thread
                                   ================
                                   │
                                   ├─► Dequeue("SENT")
                                   │
                                   ├─► Acquire lock ✓
                                   │   (Now available!)
                                   │
                                   ├─► Process event
                                   │
                                   └─► Release lock
```

## The Current Design Choice

Looking at the code, XStateNet actually **DOES** support an event queue!

```csharp
// StateMachine.cs line ~537
if (_eventQueue != null)
{
    return await _eventQueue.SendAsync(eventName);
}
```

But the `EventQueue.SendAsync` still waits for completion:

```csharp
// Concurrency.cs
public async Task<string> SendAsync(string eventName)
{
    var completionSource = new TaskCompletionSource<string>();
    await _channel.Writer.WriteAsync(message);

    // Wait for the event to be processed
    return await completionSource.Task;  // <-- Still waits!
}
```

## The Problem: Semantic Expectations

### Current Semantics (Synchronous-like)
```csharp
await machine.SendAsync("GO");
// Event has been fully processed
// State is now updated
var state = machine.GetActiveStateNames(); // Guaranteed to reflect "GO" event
```

### Lock-Free Semantics (True Async)
```csharp
await machine.SendAsync("GO");
// Event is queued but maybe not processed yet!
var state = machine.GetActiveStateNames(); // Might not reflect "GO" event yet
```

## Why The Lock Exists

The lock serves multiple purposes:

### 1. **Atomicity of Transitions**
```
Without Lock:                   With Lock:
Thread 1: State A → B           Thread 1: State A → B (atomic)
Thread 2: State A → C           Thread 2: Waits...then B → C
Result: Undefined state!        Result: Predictable A → B → C
```

### 2. **Consistency of Read-After-Write**
```csharp
// User expectation:
await machine.SendAsync("LOGIN");
Assert(machine.IsInState("logged_in")); // Should be true!
```

### 3. **Action Ordering Guarantees**
```
Event1 → Action1 (modifies shared data)
Event2 → Action2 (reads shared data)

Without lock: Action2 might run before Action1 completes!
With lock: Strict ordering preserved
```

## The Real Solution: Two-Phase Processing

What we actually need is **two-phase event processing**:

### Phase 1: Event Submission (No Lock)
```csharp
public void SendEventAsync(string eventName)
{
    // Just queue it - no lock needed!
    _pendingEvents.Enqueue(eventName);
    _eventSignal.Set(); // Wake up processor
}
```

### Phase 2: Event Processing (With Lock)
```csharp
async Task ProcessPendingEvents()
{
    while (_pendingEvents.TryDequeue(out var evt))
    {
        _stateLock.EnterWriteLock();
        try
        {
            await ProcessEvent(evt);
        }
        finally
        {
            _stateLock.ExitWriteLock();
        }
    }
}
```

## Implementing Fire-and-Forget in XStateNet

The framework actually has this partially implemented:

```csharp
// SendAndForget method (line ~600)
public void SendAndForget(string eventName, object? eventData = null)
{
    if (_eventQueue != null)
    {
        _ = Task.Run(async () =>
        {
            await _eventQueue.SendAsync(eventName);
        });
        return; // <-- Returns immediately!
    }

    // Direct processing in background
    _ = Task.Run(() =>
    {
        _stateLock.EnterWriteLock();
        try
        {
            Transit(eventName, eventData);
        }
        finally
        {
            _stateLock.ExitWriteLock();
        }
    });
}
```

## The Fix: A True Fire-and-Forget Queue

We need a method that:
1. **Never blocks** on enqueue
2. **Never waits** for processing
3. **Processes events** after current transition completes

```csharp
public class StateMachine
{
    private readonly Channel<string> _deferredEvents =
        Channel.CreateUnbounded<string>();

    // New method: truly fire-and-forget
    public void DeferEvent(string eventName)
    {
        // Never blocks, never waits
        _deferredEvents.Writer.TryWrite(eventName);
    }

    // Modified transition completion
    private async Task CompleteTransition()
    {
        // ... normal transition logic ...

        // After releasing lock, process deferred events
        _stateLock.ExitWriteLock();

        // Now process any deferred events
        while (_deferredEvents.Reader.TryRead(out var evt))
        {
            await SendAsync(evt); // Now safe!
        }
    }
}
```

## Conclusion

The lock exists because XStateNet provides **synchronous-like semantics** for async operations:
- `SendAsync` means "send and wait for processing"
- The lock ensures atomicity and ordering
- Actions run inside the locked transition

To fix the deadlock, we need:
1. **A true fire-and-forget API** that just queues events
2. **Deferred processing** after the current transition completes
3. **Clear semantics** about when events are processed

The framework has the building blocks (EventQueue, SendAndForget) but they still maintain synchronous semantics. The solution is to add a truly asynchronous event deferral mechanism that processes events after the current transition completes.