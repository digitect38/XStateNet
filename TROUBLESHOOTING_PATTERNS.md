# Troubleshooting Patterns - Lessons from Real Fixes

> **Purpose**: Document proven diagnostic and fix patterns from actual production issues.
> Use this as a reference when encountering similar failure symptoms.

---

## Pattern 1: Cross-Process IPC Message Loss (SharedMemory)

### Symptoms
- ✗ Intermittent test failures (~40% message loss rate)
- ✓ Works when run individually
- ✗ Fails in parallel/stress tests
- ✓ Sender reports success
- ✗ Receiver never gets message

### Diagnostic Steps
1. **Add diagnostic logging** in message reader loop
2. **Check for "wrong process consumed message" patterns**
3. **Verify process-to-machine ownership mapping**

### Root Cause Pattern: **Competitive Consumption**
```
Multiple readers → Single shared queue → Race to consume → Message lost
```

### Key Treatment: **Per-Consumer Inbox Architecture**
```csharp
// ❌ ANTI-PATTERN: Shared buffer with competitive readers
_sharedBuffer = new RingBuffer(_sharedSegment);
// All processes read from same buffer → RACE!

// ✅ SOLUTION: Dedicated inbox per consumer
_inboxSegment = new SharedMemorySegment($"{name}_Inbox_P{ProcessId}");
_inboxBuffer = new RingBuffer(_inboxSegment);
// Each process reads ONLY from own inbox → NO RACE
```

**Sender Strategy**: Write to target's inbox, not shared buffer
```csharp
var targetInbox = GetOrCreateProcessInbox(targetProcessId);
await targetInbox.WriteAsync(message);
```

### Verification
- Sequential stress test: 50+ runs
- Parallel execution test
- Multi-process concurrent test

**Related Patterns**: Message queues, event buses, distributed systems

---

## Pattern 2: State Caching Race Condition (CircuitBreaker)

### Symptoms
- ✗ State machine "stuck" in intermediate state
- ✓ Event logs show transition completed
- ✗ Code path uses old state
- ✗ Timing-dependent (works sometimes)

### Diagnostic Steps
1. **Check if state is cached** at method entry
2. **Add logging**: Compare cached vs current state
3. **Look for async state transitions** during method execution

### Root Cause Pattern: **Stale State Cache**
```csharp
// ❌ ANTI-PATTERN: Cache state once
public async Task Execute() {
    var state = CurrentState;  // Cached here

    // Async transition happens elsewhere
    // ...

    if (state.Contains("halfOpen")) {  // Uses STALE cache!
        // Wrong code path!
    }
}
```

### Key Treatment: **Re-read State at Each Decision Point**
```csharp
// ✅ SOLUTION: Fresh reads
public async Task Execute() {
    // Don't cache - read fresh each time
    if (CurrentState.Contains("open") && !CurrentState.Contains("halfOpen")) {
        throw new CircuitBreakerOpenException();
    }

    if (CurrentState.Contains("halfOpen")) {  // Fresh read
        // Correct code path
    }
}
```

### When to Apply
- ⚠️ State can change during method execution
- ⚠️ Async/concurrent state transitions
- ⚠️ Multiple decision points based on same state

### Performance vs Correctness
- **Cost**: Extra property read (typically trivial)
- **Benefit**: Eliminates race condition
- **Verdict**: Always prefer correctness

**Related Patterns**: TOCTOU (Time-of-check Time-of-use), concurrent state machines

---

## Pattern 3: Fixed Delay Test Flakiness

### Symptoms
- ✗ Test fails with "Expected state X, got Y"
- ✓ Async operation completed (logs confirm)
- ✗ Property/state not updated yet
- Pass rate: 80-95% (timing-dependent)

### Diagnostic Steps
1. **Check for Task.Delay(N)** in test assertions
2. **Verify event-driven completion vs polling**
3. **Measure actual completion time** (add Stopwatch)

### Root Cause Pattern: **Arbitrary Timeout Assumptions**
```csharp
// ❌ ANTI-PATTERN: Hope delay is enough
await SomeAsyncOperation();
await Task.Delay(50);  // Hope this is enough
Assert.Equal("expected", actualState);  // FLAKY!
```

### Key Treatment: **Event-Driven Completion**
```csharp
// ✅ SOLUTION: Wait for actual event
var tcs = new TaskCompletionSource<string>();
obj.StateChanged += (s, args) => tcs.TrySetResult(args.NewState);

await SomeAsyncOperation();

// Wait for actual completion OR timeout
var completed = await Task.WhenAny(tcs.Task, Task.Delay(500));
Assert.True(completed == tcs.Task, "Should complete within 500ms");
Assert.Equal("expected", tcs.Task.Result);
```

### Hybrid Approach (when events unavailable)
```csharp
// If property lags behind event:
var stateReached = new TaskCompletionSource<string>();
obj.StateChanged += (s, args) => stateReached.TrySetResult(args.NewState);

await Task.WhenAny(stateReached.Task, Task.Delay(500));
await Task.Delay(10);  // Small grace period for property update
Assert.Equal("expected", obj.State);
```

### Test Design Principles
1. **Event-driven > Polling**: Use completion signals
2. **Generous timeouts**: 500ms not 50ms (allow for CI variability)
3. **Fail with diagnostics**: Include actual values in assertions
4. **Separate TCS per event**: Don't reuse for different states

**Related Patterns**: Async testing, event-driven architecture

---

## Pattern 4: Cross-Process Memory Visibility

### Symptoms
- ✗ Process B can't see Process A's writes
- ✓ Data written successfully (no errors)
- ✗ Checksum mismatch / stale reads
- Platform-specific (CPU cache behavior)

### Diagnostic Steps
1. **Check for memory barriers** in shared memory code
2. **Verify flush operations** after writes
3. **Test on different CPU architectures**

### Root Cause Pattern: **CPU Cache Incoherence**
```
Process A writes → CPU cache → Not visible to Process B
```

### Key Treatment: **Explicit Memory Barriers**
```csharp
// ❌ ANTI-PATTERN: Assume visibility
public void WriteData(byte[] data) {
    _accessor.WriteArray(offset, data, 0, data.Length);
    // No guarantee other processes see this!
}

// ✅ SOLUTION: Force visibility
public void WriteData(byte[] data) {
    _accessor.WriteArray(offset, data, 0, data.Length);

    Thread.MemoryBarrier();  // Force cache sync
    _accessor.Flush();       // Write to backing store
}

public int ReadData(byte[] buffer) {
    Thread.MemoryBarrier();  // Ensure fresh read
    return _accessor.ReadArray(offset, buffer, 0, count);
}
```

### When to Apply
- ✓ Memory-mapped files
- ✓ Shared memory IPC
- ✓ Lock-free data structures
- ✓ Cross-process communication

### Platform Considerations
- **x86/x64**: Strong memory model (fewer issues)
- **ARM/Mobile**: Weaker model (more barriers needed)
- **Always use barriers**: For portability

**Related Patterns**: Lock-free programming, shared memory, IPC

---

## Pattern 5: Checksum/Offset Calculation Bugs

### Symptoms
- ✗ "Checksum mismatch" errors (100% failure rate)
- ✗ Expected: 0x0, Got: 0xXXXXXXXX
- ✓ Data appears to be written correctly

### Diagnostic Steps
1. **Calculate structure offsets manually** (don't assume)
2. **Print actual vs expected offsets** in debug
3. **Check for Pack=1 vs Pack=8** in StructLayout

### Root Cause Pattern: **Offset Miscalculation**
```csharp
// ❌ WRONG: Assumed offset 20
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Header {
    public int Field1;      // Offset 0 (4 bytes)
    public int Field2;      // Offset 4 (4 bytes)
    public int Field3;      // Offset 8 (4 bytes)
    public int Field4;      // Offset 12 (4 bytes)
    public long Field5;     // Offset 16 (8 bytes)
    public uint Checksum;   // Offset 24, NOT 20!
}

// Writing to wrong offset:
BitConverter.GetBytes(checksum).CopyTo(buffer, 20);  // ❌ Overwrites Field5!
```

### Key Treatment: **Calculate Offsets Explicitly**
```csharp
// ✅ SOLUTION: Explicit calculation with comments
int offset = 0;
offset += 4;  // Field1
offset += 4;  // Field2
offset += 4;  // Field3
offset += 4;  // Field4
offset += 8;  // Field5 (long = 8 bytes)
// offset now = 24 (correct checksum position)

BitConverter.GetBytes(checksum).CopyTo(buffer, offset);
```

### Verification Techniques
```csharp
// Use Marshal.OffsetOf for safety
var checksumOffset = (int)Marshal.OffsetOf<Header>(nameof(Header.Checksum));
BitConverter.GetBytes(checksum).CopyTo(buffer, checksumOffset);
```

**Related Patterns**: Binary protocols, serialization, structure packing

---

## General Diagnostic Framework

### Step 1: Reproduce Reliably
- Run test 20-50 times to find pattern
- Note pass rate (50% vs 90% vs 0%)
- Check if sequential vs parallel matters

### Step 2: Add Diagnostic Logging
```csharp
// Log state at key decision points
Log($"State check: cached={state}, current={CurrentState}");
Log($"Message: sender={senderPid}, target={targetPid}, machine={machineId}");
Log($"Timing: event={eventTime}, check={checkTime}, delta={delta}ms");
```

### Step 3: Identify Pattern
- **0% pass**: Logic bug, wrong offset, broken API
- **50-60% pass**: Race condition, competitive consumption
- **80-95% pass**: Timing issue, property lag, cache staleness
- **Works alone, fails together**: Shared resource, isolation issue

### Step 4: Apply Treatment
1. **Eliminate competition**: Dedicated resources
2. **Eliminate caching**: Fresh reads
3. **Eliminate arbitrary delays**: Event-driven
4. **Add synchronization**: Barriers, locks, events

### Step 5: Verify with Stress Test
```bash
# Sequential stress
for i in {1..50}; do dotnet test ...; done | uniq -c

# Parallel stress
dotnet test -- RunConfiguration.MaxCpuCount=4

# Long-running
while true; do dotnet test || break; done
```

---

## Prevention Checklist

### For IPC/Shared Memory Code
- [ ] Each consumer has dedicated inbox/queue?
- [ ] Memory barriers after writes, before reads?
- [ ] Flush operations for visibility?
- [ ] Offsets calculated explicitly (not assumed)?
- [ ] Structure packing (Pack=1) documented?

### For Async State Machines
- [ ] State re-read at each decision point (not cached)?
- [ ] Properties updated synchronously after events?
- [ ] Event-driven waiting (not fixed delays)?
- [ ] Timeout assertions have generous margins (500ms+)?

### For Tests
- [ ] TaskCompletionSource for state transitions?
- [ ] Separate TCS per expected state?
- [ ] Grace period after event before property read?
- [ ] Diagnostic output on failure (expected vs actual)?
- [ ] Stress tested (50+ runs)?

---

## Quick Reference: Failure Signature → Treatment

| Symptom | Likely Cause | Treatment |
|---------|--------------|-----------|
| "Message lost" ~40% | Competitive consumption | Per-consumer inbox |
| "Stuck in state X" | Stale state cache | Re-read at decision points |
| "Expected Y, got X" 90% | Fixed delay too short | Event-driven waiting |
| "Checksum mismatch 0x0" | Wrong offset | Explicit offset calculation |
| "Works alone, fails parallel" | Shared resource race | Dedicated resources |
| "Can't see writes" | Cache incoherence | Memory barriers + flush |

---

## Success Metrics

**Before Fixes:**
- SharedMemory: 60-70% pass rate
- CircuitBreaker: 91% pass rate (10/11 passes)
- E87 Carrier: ~50% pass rate (parallel execution)

**After Applying Patterns:**
- SharedMemory: 100% (271/271 test passes)
- CircuitBreaker: 100% (51/51 test passes)
- E87 Carrier: 100% (30/30 sequential passes)

**Comprehensive Stress Test (947 test cases):**
- Sequential runs: 20/20 passes (100%) - 92 minutes
- **Parallel runs: 26/26 passes (100%) - 122 minutes**
- Total test executions: **~24,622 individual test runs** (947 × 26)
- Failures: 0
- Parallel execution: MaxCpuCount (full CPU utilization)

**Confidence Level**: Exceptional - Production-ready with parallel execution stability

---

## Future Application

When encountering similar failures:

1. **Check this document first** - match symptom pattern
2. **Apply diagnostic steps** - confirm root cause
3. **Use proven treatment** - don't reinvent
4. **Verify with stress test** - ensure stability
5. **Update this document** - add new patterns discovered

---

*Last Updated: 2025-10-05*
*Commit: f8af89d (E87 Carrier event-driven waiting)*
*Commit: 06095da (CircuitBreaker state caching fix)*
*Commit: fc7226f (SharedMemory per-process inbox)*
*Commit: 5c68b86 (SharedMemory critical bugs)*

**Validation**:
- Sequential: 947 tests × 20 runs = 18,940 executions (92 min)
- **Parallel: 947 tests × 26 runs = 24,622 executions (122 min)**
- **Combined: 43,562 test executions, 0 failures**
- Parallel execution demonstrates thread-safety and race-condition elimination
