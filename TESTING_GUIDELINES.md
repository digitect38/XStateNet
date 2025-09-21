# XStateNet Testing Guidelines

## Purpose
This document establishes best practices for writing reliable, deterministic tests for asynchronous and concurrent code in the XStateNet project.

## Core Principles

### 1. **No Hardcoded Delays**
❌ **Bad:**
```csharp
await Task.Delay(500); // Assumes operation completes in 500ms
Assert.True(operationCompleted);
```

✅ **Good:**
```csharp
var completionSignal = new TaskCompletionSource<bool>();
operation.OnComplete += () => completionSignal.SetResult(true);
await completionSignal.Task;
Assert.True(operationCompleted);
```

### 2. **Use TaskCompletionSource for Synchronization**
❌ **Bad:**
```csharp
// Start async operation
var task = DoSomethingAsync();
await Task.Delay(100); // Hope it's ready
Assert.Equal(expected, result);
```

✅ **Good:**
```csharp
var operationStarted = new TaskCompletionSource<bool>();
var operationCompleted = new TaskCompletionSource<string>();

var task = DoSomethingAsync(
    onStart: () => operationStarted.SetResult(true),
    onComplete: (result) => operationCompleted.SetResult(result)
);

await operationStarted.Task; // Know exactly when started
var result = await operationCompleted.Task; // Know exactly when done
Assert.Equal(expected, result);
```

### 3. **Test Relative Performance, Not Absolute**
❌ **Bad:**
```csharp
[Fact]
public async Task Throughput_ShouldProcess10000EventsPerSecond()
{
    var processed = await ProcessEvents(TimeSpan.FromSeconds(1));
    Assert.True(processed >= 10000); // Fails on slow CI servers
}
```

✅ **Good:**
```csharp
[Fact]
public async Task Throughput_ShouldScaleLinearlyWithWorkers()
{
    var baseline = await MeasureThroughput(workers: 1);
    var scaled = await MeasureThroughput(workers: 4);

    // Test scaling efficiency, not absolute numbers
    var efficiency = scaled / (baseline * 4);
    Assert.True(efficiency >= 0.7); // At least 70% scaling efficiency
}
```

### 4. **Always Await Async Operations**
❌ **Bad:**
```csharp
[Fact]
public void ProcessAsync_ShouldComplete()
{
    _ = service.ProcessAsync(); // Fire and forget - race condition!
    Assert.True(service.IsProcessing);
}
```

✅ **Good:**
```csharp
[Fact]
public async Task ProcessAsync_ShouldComplete()
{
    await service.ProcessAsync();
    Assert.True(service.IsProcessing);
}
```

### 5. **Use CancellationTokens with Timeouts**
❌ **Bad:**
```csharp
[Fact]
public async Task LongOperation_ShouldComplete()
{
    await LongRunningOperation(); // Could hang forever
}
```

✅ **Good:**
```csharp
[Fact]
public async Task LongOperation_ShouldComplete()
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await LongRunningOperation(cts.Token);
}
```

## Testing Patterns

### Pattern 1: Event-Driven Synchronization
```csharp
[Fact]
public async Task CircuitBreaker_ShouldOpenOnFailures()
{
    var stateChanged = new TaskCompletionSource<CircuitState>();
    breaker.StateChanged += (s, e) => {
        if (e.ToState == CircuitState.Open)
            stateChanged.TrySetResult(e.ToState);
    };

    // Trigger failures
    for (int i = 0; i < threshold; i++)
    {
        try { await breaker.ExecuteAsync(FailingOperation); }
        catch { /* expected */ }
    }

    // Wait for state change event
    var newState = await stateChanged.Task;
    Assert.Equal(CircuitState.Open, newState);
}
```

### Pattern 2: Deterministic Time Control
```csharp
public interface ITimeProvider
{
    DateTime UtcNow { get; }
    Task Delay(TimeSpan delay);
}

public class TestTimeProvider : ITimeProvider
{
    private DateTime _currentTime = DateTime.UtcNow;

    public DateTime UtcNow => _currentTime;

    public void Advance(TimeSpan amount) => _currentTime = _currentTime.Add(amount);

    public Task Delay(TimeSpan delay)
    {
        Advance(delay);
        return Task.CompletedTask; // Instant completion in tests
    }
}
```

### Pattern 3: Backpressure Testing
```csharp
[Fact]
public async Task Channel_ShouldHandleBackpressure()
{
    var channel = CreateBoundedChannel(capacity: 10);
    var writeBlocked = new TaskCompletionSource<bool>();

    // Fill channel
    for (int i = 0; i < 10; i++)
    {
        Assert.True(channel.TryWrite(i));
    }

    // This write should block
    var blockedWrite = Task.Run(async () =>
    {
        writeBlocked.SetResult(true);
        await channel.WriteAsync(11); // Will block until space available
    });

    await writeBlocked.Task; // Ensure write has started
    Assert.False(blockedWrite.IsCompleted); // Should be blocked

    // Read one item to make space
    await channel.ReadAsync();

    // Now the blocked write should complete
    await blockedWrite;
}
```

## Common Pitfalls and Solutions

### Pitfall 1: Testing Implementation Details
❌ **Bad:** Testing private methods or internal state
✅ **Good:** Test observable behavior through public APIs

### Pitfall 2: Shared State Between Tests
❌ **Bad:** Static variables that persist between test runs
✅ **Good:** Fresh instances for each test, proper cleanup in Dispose()

### Pitfall 3: Non-Deterministic Assertions
❌ **Bad:** `Assert.True(value > random.Next())`
✅ **Good:** Use fixed seeds for randomness in tests

### Pitfall 4: Ignoring Test Warnings
❌ **Bad:** Suppressing compiler warnings about unawaited tasks
✅ **Good:** Address the root cause - await or explicitly handle all tasks

## Performance Testing Guidelines

1. **Use BenchmarkDotNet for Micro-benchmarks**
   - Provides statistical analysis
   - Handles warmup and measurement iterations
   - Accounts for JIT and GC effects

2. **Test Scaling Characteristics**
   - Linear scaling with threads/cores
   - Contention under high concurrency
   - Memory usage patterns

3. **Use Relative Metrics**
   - "2x faster than baseline" not "processes 10,000 items/sec"
   - "O(n) complexity" not "completes in 100ms"

## Integration Testing Guidelines

1. **Use TestContainers for External Dependencies**
   ```csharp
   [Fact]
   public async Task Redis_Integration()
   {
       await using var redis = new TestcontainersBuilder<RedisTestcontainer>()
           .WithDatabase(new RedisTestcontainerConfiguration())
           .Build();

       await redis.StartAsync();
       // Test with real Redis instance
   }
   ```

2. **Mock Time-Dependent Operations**
   - Use ISystemClock abstraction
   - Control time progression in tests

3. **Test Failure Scenarios**
   - Network failures
   - Timeout scenarios
   - Resource exhaustion

## Continuous Integration Considerations

1. **Account for CI Environment Constraints**
   - Limited CPU cores
   - Shared resources
   - Variable performance

2. **Use Test Categories**
   ```csharp
   [Trait("Category", "Fast")]     // < 100ms
   [Trait("Category", "Slow")]     // > 1s
   [Trait("Category", "Integration")] // Requires external services
   ```

3. **Implement Retry Logic for Flaky Tests**
   ```csharp
   [Retry(3)] // Retry up to 3 times on failure
   public async Task PotentiallyFlakyTest() { }
   ```

## Review Checklist

Before submitting a PR, ensure:
- [ ] No `Task.Delay()` used for synchronization
- [ ] All async methods are awaited
- [ ] Tests use TaskCompletionSource for coordination
- [ ] Performance tests use relative metrics
- [ ] Timeouts are specified for long operations
- [ ] Tests are deterministic and repeatable
- [ ] No shared state between tests
- [ ] External dependencies are mocked/containerized
- [ ] Tests pass on both fast and slow machines

## Example: Refactoring a Flaky Test

### Before (Flaky):
```csharp
[Fact]
public async Task EventBus_ShouldDeliverAllMessages()
{
    var bus = new EventBus();
    var received = 0;

    bus.Subscribe("topic", msg => received++);

    for (int i = 0; i < 1000; i++)
    {
        await bus.PublishAsync("topic", $"Message {i}");
    }

    await Task.Delay(500); // Hope all messages are processed
    Assert.Equal(1000, received); // May fail if processing is slow
}
```

### After (Deterministic):
```csharp
[Fact]
public async Task EventBus_ShouldDeliverAllMessages()
{
    var bus = new EventBus();
    var messageCount = 1000;
    var countdown = new CountdownEvent(messageCount);
    var received = 0;

    bus.Subscribe("topic", msg =>
    {
        Interlocked.Increment(ref received);
        countdown.Signal();
    });

    for (int i = 0; i < messageCount; i++)
    {
        await bus.PublishAsync("topic", $"Message {i}");
    }

    // Wait for all messages with timeout
    Assert.True(countdown.Wait(TimeSpan.FromSeconds(10)));
    Assert.Equal(messageCount, received);
}
```

## Conclusion

Following these guidelines will result in:
- **Reliable tests** that pass consistently
- **Fast feedback** from CI/CD pipelines
- **Maintainable code** that's easy to understand
- **Confident deployments** backed by trustworthy tests

Remember: A flaky test is worse than no test - it erodes confidence and wastes developer time.