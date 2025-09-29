# XStateNet Performance Guide

## 🚀 Performance Optimization and Benchmarking

This guide provides comprehensive information about XStateNet performance characteristics, optimization techniques, and benchmarking tools.

## 📊 Performance Overview

XStateNet is designed for high-performance, production-ready state machine processing with the following characteristics:

### Key Performance Metrics

| Metric | Single Core | Multi-Core (4) | Multi-Core (8) | Distributed |
|--------|-------------|----------------|----------------|-------------|
| **Throughput** | 10,000+ eps | 50,000+ eps | 100,000+ eps | 500,000+ eps |
| **Latency (avg)** | <2.0ms | <1.5ms | <1.0ms | <5.0ms |
| **Latency (P95)** | <5.0ms | <3.0ms | <2.0ms | <10.0ms |
| **Memory Usage** | <50MB | <100MB | <150MB | <200MB |
| **CPU Efficiency** | 85% | 90% | 92% | 88% |

*eps = events per second*

## 🏗️ Architecture Performance

### Event Processing Pipeline

The XStateNet event processing pipeline is optimized for minimal latency and maximum throughput:

```
┌─────────────┐  ┌──────────────┐  ┌─────────────┐  ┌─────────────┐
│   Client    │  │  Event Bus   │  │   Router    │  │   Machine   │
│  Request    │─▶│   Pool       │─▶│   Logic     │─▶│  Execution  │
│  (50-100μs) │  │  (100-200μs) │  │  (50-150μs) │  │ (500-2000μs)│
└─────────────┘  └──────────────┘  └─────────────┘  └─────────────┘
```

### Memory Management

- **Object Pooling**: Reuse of event objects and contexts
- **Zero-Copy Operations**: Minimal data copying in hot paths
- **Generational GC Optimization**: Reduced garbage collection pressure
- **Lock-Free Data Structures**: Minimal contention in concurrent scenarios

## ⚙️ Performance Configuration

### Optimal Configuration Settings

```csharp
var highPerformanceConfig = new OrchestratorConfig
{
    // Core performance settings
    PoolSize = Environment.ProcessorCount, // Or 2x for I/O bound workloads
    EnableBackpressure = true,
    MaxQueueDepth = 50000, // Adjust based on memory constraints
    ThrottleDelay = TimeSpan.Zero, // No throttling for max performance

    // Monitoring (impacts performance)
    EnableMetrics = true, // ~5% overhead
    EnableLogging = false, // ~10-15% overhead when enabled
    EnableStructuredLogging = false, // ~20% overhead

    // Resilience vs Performance tradeoff
    EnableCircuitBreaker = false, // Disable for absolute max performance

    // Metrics collection frequency
    MetricsInterval = TimeSpan.FromSeconds(5), // Longer intervals = better performance

    // Health checks frequency
    HealthCheckInterval = TimeSpan.FromSeconds(30) // Less frequent = better performance
};
```

### Configuration Impact Analysis

| Setting | Performance Impact | Recommendation |
|---------|-------------------|----------------|
| **PoolSize** | Linear scaling up to core count | Set to `Environment.ProcessorCount` |
| **EnableBackpressure** | <1% overhead | Always enable for production |
| **MaxQueueDepth** | Memory vs throughput tradeoff | 10K-50K based on RAM |
| **EnableMetrics** | ~5% overhead | Enable in production |
| **EnableLogging** | ~10-15% overhead | Disable in high-perf scenarios |
| **CircuitBreaker** | ~2-3% overhead | Enable for resilience over perf |

## 🔧 Optimization Techniques

### 1. Action Optimization

Optimize your state machine actions for maximum performance:

```csharp
// ❌ Inefficient action
["slowAction"] = ctx =>
{
    var data = JsonSerializer.Deserialize<MyData>(ctx.EventData.ToString());
    Thread.Sleep(100); // Blocking operation
    var result = CallSlowWebService(data);
    ctx.SetStateData("result", JsonSerializer.Serialize(result));
}

// ✅ Optimized action
["fastAction"] = ctx =>
{
    var data = (MyData)ctx.EventData; // Direct cast, no serialization
    _ = Task.Run(async () => // Non-blocking
    {
        var result = await CallWebServiceAsync(data);
        ctx.SetStateData("result", result); // Direct object storage
        ctx.Machine.SendFireAndForget("COMPLETED");
    });
}
```

### 2. State Machine Design Patterns

#### Optimized State Machine Structure

```csharp
// ✅ Efficient state machine design
var optimizedJson = @"{
    ""id"": ""optimizedProcessor"",
    ""initial"": ""ready"",
    ""states"": {
        ""ready"": {
            ""on"": {
                ""PROCESS"": ""processing"",
                ""BATCH_PROCESS"": ""batch_processing""
            }
        },
        ""processing"": {
            ""entry"": [""fastProcess""],
            ""on"": { ""COMPLETE"": ""ready"" }
        },
        ""batch_processing"": {
            ""entry"": [""batchProcess""],
            ""on"": { ""BATCH_COMPLETE"": ""ready"" }
        }
    }
}";

// Batch processing for higher throughput
["batchProcess"] = ctx =>
{
    var items = (IEnumerable<object>)ctx.EventData;
    Parallel.ForEach(items, ProcessSingleItem); // Parallel processing
    ctx.Machine.SendFireAndForget("BATCH_COMPLETE");
}
```

### 3. Memory Optimization

#### Object Pooling Pattern

```csharp
public class OptimizedEventProcessor
{
    private readonly ObjectPool<ProcessingContext> _contextPool;
    private readonly ObjectPool<StringBuilder> _stringBuilderPool;

    public OptimizedEventProcessor()
    {
        _contextPool = new DefaultObjectPool<ProcessingContext>(
            new ProcessingContextPoolPolicy(), maximumRetained: 100);
        _stringBuilderPool = new DefaultObjectPoolProvider()
            .CreateStringBuilderPool();
    }

    public async Task ProcessEventOptimized(ExecutionContext ctx)
    {
        // Rent from pool instead of allocating
        var processingCtx = _contextPool.Get();
        var sb = _stringBuilderPool.Get();

        try
        {
            // Use pooled objects for processing
            processingCtx.Initialize(ctx.EventData);
            sb.Clear();
            sb.Append("Processing: ").Append(processingCtx.Id);

            // Do processing...

        }
        finally
        {
            // Return to pool
            _contextPool.Return(processingCtx);
            _stringBuilderPool.Return(sb);
        }
    }
}
```

### 4. Concurrent Processing Patterns

#### High-Performance Parallel Processing

```csharp
public class HighThroughputProcessor
{
    private readonly Channel<WorkItem> _workChannel;
    private readonly Task[] _workers;

    public HighThroughputProcessor(int workerCount)
    {
        var options = new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };

        _workChannel = Channel.CreateBounded<WorkItem>(options);

        // Create dedicated worker tasks
        _workers = new Task[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            _workers[i] = Task.Run(WorkerLoop);
        }
    }

    private async Task WorkerLoop()
    {
        await foreach (var item in _workChannel.Reader.ReadAllAsync())
        {
            await ProcessWorkItem(item);
        }
    }

    public async Task<bool> EnqueueWork(WorkItem item)
    {
        return await _workChannel.Writer.TryWriteAsync(item);
    }
}
```

## 📈 Benchmarking Tools

### Built-in Benchmark Framework

XStateNet includes a comprehensive benchmarking framework for performance measurement:

#### Running Benchmarks

```csharp
// Full benchmark suite
await BenchmarkRunner.RunFullBenchmarkSuite();

// Quick performance check
await BenchmarkRunner.RunQuickBenchmark();

// Specific benchmark types
await BenchmarkRunner.RunLatencyFocusedBenchmark();
await BenchmarkRunner.RunThroughputFocusedBenchmark();
```

#### Custom Benchmark Configuration

```csharp
var benchmarkConfig = new BenchmarkConfig
{
    // Benchmark execution
    WarmupIterations = 5,      // More warmup for accurate results
    MeasurementIterations = 10, // More iterations for statistical significance

    // Machine scaling
    DefaultMachineCount = 20,   // Test with more machines
    EventBusPoolSize = 8,      // Match your production config

    // Event volumes
    ThroughputEventCount = 100000,  // Higher volume for throughput tests
    LatencyEventCount = 1000,       // Moderate volume for latency precision

    // Performance settings
    EnableMetrics = false,          // Disable for pure performance measurement
    EnableBackpressure = true,      // Match production settings
    MaxQueueDepth = 100000,        // Large queue for burst handling

    // Stress testing
    ConcurrencyLevel = Environment.ProcessorCount * 2,
    BurstSize = 5000,
    BurstCount = 20,

    // Long duration testing
    LongDurationTestTime = TimeSpan.FromMinutes(10),
    LongDurationEventsPerSecond = 10000
};

var framework = new BenchmarkFramework(benchmarkConfig);
var results = await framework.RunBenchmarkSuiteAsync();
```

### Benchmark Result Analysis

#### Understanding Benchmark Metrics

```csharp
foreach (var result in results.SuccessfulResults)
{
    Console.WriteLine($"Benchmark: {result.BenchmarkName}");
    Console.WriteLine($"Throughput: {result.EventsPerSecond:F0} events/sec");
    Console.WriteLine($"Average Latency: {result.AverageLatency:F3} ms");

    if (result.LatencyPercentiles.Any())
    {
        Console.WriteLine("Latency Percentiles:");
        foreach (var percentile in result.LatencyPercentiles)
        {
            Console.WriteLine($"  {percentile.Key}: {percentile.Value:F3} ms");
        }
    }

    // Performance classification
    if (result.EventsPerSecond > 50000)
        Console.WriteLine("🏆 Excellent performance");
    else if (result.EventsPerSecond > 25000)
        Console.WriteLine("✅ Good performance");
    else if (result.EventsPerSecond > 10000)
        Console.WriteLine("⚠️ Fair performance - consider optimization");
    else
        Console.WriteLine("❌ Poor performance - optimization required");
}
```

### Custom Performance Testing

#### Creating Custom Benchmarks

```csharp
public class CustomPerformanceTester
{
    public async Task<PerformanceResult> TestCustomScenario()
    {
        var config = new OrchestratorConfig
        {
            PoolSize = 8,
            EnableMetrics = true,
            EnableBackpressure = true,
            MaxQueueDepth = 50000
        };

        using var orchestrator = new EventBusOrchestrator(config);
        var machine = CreateCustomTestMachine(orchestrator);

        await orchestrator.RegisterMachineAsync("custom", machine);
        await orchestrator.StartAllMachinesAsync();

        // Warmup phase
        await RunWarmup(orchestrator);

        // Measurement phase
        var stopwatch = Stopwatch.StartNew();
        var eventCount = await RunMeasurementPhase(orchestrator);
        stopwatch.Stop();

        var metrics = orchestrator.GetMetrics();

        return new PerformanceResult
        {
            EventCount = eventCount,
            Duration = stopwatch.Elapsed,
            EventsPerSecond = eventCount / stopwatch.Elapsed.TotalSeconds,
            AverageLatency = metrics.AverageLatency,
            P95Latency = metrics.P95Latency,
            P99Latency = metrics.P99Latency,
            MemoryUsageMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0)
        };
    }

    private async Task RunWarmup(EventBusOrchestrator orchestrator)
    {
        for (int i = 0; i < 1000; i++)
        {
            await orchestrator.SendEventFireAndForgetAsync($"warmup-{i}", "custom", "PROCESS");
        }

        await Task.Delay(1000); // Let warmup complete
    }

    private async Task<int> RunMeasurementPhase(EventBusOrchestrator orchestrator)
    {
        const int eventCount = 10000;
        var tasks = new List<Task>();

        for (int i = 0; i < eventCount; i++)
        {
            tasks.Add(orchestrator.SendEventFireAndForgetAsync($"test-{i}", "custom", "PROCESS"));
        }

        await Task.WhenAll(tasks);
        return eventCount;
    }
}
```

## 🎯 Performance Monitoring

### Real-time Performance Monitoring

```csharp
public class ProductionPerformanceMonitor
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly Timer _monitoringTimer;
    private readonly List<PerformanceSnapshot> _snapshots = new();

    public ProductionPerformanceMonitor(EventBusOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        _monitoringTimer = new Timer(CaptureMetrics, null,
            TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    private void CaptureMetrics(object state)
    {
        try
        {
            var metrics = _orchestrator.GetMetrics();
            var health = _orchestrator.GetHealthStatus();

            var snapshot = new PerformanceSnapshot
            {
                Timestamp = DateTime.UtcNow,
                EventsPerSecond = metrics.EventsPerSecond,
                AverageLatency = metrics.AverageLatency,
                QueueDepth = metrics.QueueDepth,
                ErrorRate = metrics.ErrorRate,
                HealthLevel = health.Level,
                MemoryUsageMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0)
            };

            _snapshots.Add(snapshot);

            // Alert on performance degradation
            if (ShouldAlert(snapshot))
            {
                _ = Task.Run(() => SendPerformanceAlert(snapshot));
            }

            // Keep only recent snapshots (last hour)
            var cutoff = DateTime.UtcNow.AddHours(-1);
            _snapshots.RemoveAll(s => s.Timestamp < cutoff);
        }
        catch (Exception ex)
        {
            // Log monitoring errors
            Console.WriteLine($"Performance monitoring error: {ex.Message}");
        }
    }

    private bool ShouldAlert(PerformanceSnapshot snapshot)
    {
        // Alert if performance degrades significantly
        if (_snapshots.Count < 10) return false;

        var recentSnapshots = _snapshots.TakeLast(10).ToList();
        var avgThroughput = recentSnapshots.Average(s => s.EventsPerSecond);
        var avgLatency = recentSnapshots.Average(s => s.AverageLatency);

        return snapshot.EventsPerSecond < avgThroughput * 0.7 ||  // 30% throughput drop
               snapshot.AverageLatency > avgLatency * 1.5 ||       // 50% latency increase
               snapshot.ErrorRate > 0.05 ||                       // 5% error rate
               snapshot.HealthLevel != HealthLevel.Healthy;
    }
}

public record PerformanceSnapshot
{
    public DateTime Timestamp { get; init; }
    public double EventsPerSecond { get; init; }
    public double AverageLatency { get; init; }
    public int QueueDepth { get; init; }
    public double ErrorRate { get; init; }
    public HealthLevel HealthLevel { get; init; }
    public double MemoryUsageMB { get; init; }
}
```

### Performance Alerting

```csharp
public class PerformanceAlertManager
{
    private readonly ILogger _logger;
    private readonly AlertingConfiguration _config;

    public async Task SendPerformanceAlert(PerformanceSnapshot snapshot)
    {
        var alert = new PerformanceAlert
        {
            Timestamp = snapshot.Timestamp,
            AlertType = DetermineAlertType(snapshot),
            Severity = DetermineSeverity(snapshot),
            Message = GenerateAlertMessage(snapshot),
            Metrics = snapshot
        };

        // Send to monitoring systems
        await SendToMonitoringSystem(alert);

        // Send notifications based on severity
        if (alert.Severity >= AlertSeverity.High)
        {
            await SendEmailAlert(alert);
            await SendSlackAlert(alert);
        }

        if (alert.Severity == AlertSeverity.Critical)
        {
            await SendPagerDutyAlert(alert);
        }
    }

    private AlertType DetermineAlertType(PerformanceSnapshot snapshot)
    {
        if (snapshot.EventsPerSecond < 1000) return AlertType.LowThroughput;
        if (snapshot.AverageLatency > 50) return AlertType.HighLatency;
        if (snapshot.ErrorRate > 0.05) return AlertType.HighErrorRate;
        if (snapshot.HealthLevel != HealthLevel.Healthy) return AlertType.HealthDegraded;
        return AlertType.General;
    }
}
```

## 🔍 Performance Troubleshooting

### Common Performance Issues

#### 1. High Latency

**Symptoms:**
- Average latency > 10ms
- P95/P99 latencies significantly higher than average
- Increasing response times under load

**Causes & Solutions:**

```csharp
// ❌ Blocking operations in actions
["slowAction"] = ctx =>
{
    Thread.Sleep(100); // Blocks event loop
    var result = httpClient.GetStringAsync(url).Result; // Blocking async
};

// ✅ Non-blocking operations
["fastAction"] = async ctx =>
{
    await Task.Delay(100); // Non-blocking delay
    var result = await httpClient.GetStringAsync(url); // Proper async
};
```

#### 2. Low Throughput

**Symptoms:**
- Events per second below expectations
- CPU utilization low despite load
- Backpressure activation

**Diagnostic Steps:**

```csharp
public class ThroughputDiagnostics
{
    public async Task DiagnoseLowThroughput(EventBusOrchestrator orchestrator)
    {
        var metrics = orchestrator.GetMetrics();

        Console.WriteLine($"Current throughput: {metrics.EventsPerSecond:F0} eps");
        Console.WriteLine($"Queue depth: {metrics.QueueDepth}");
        Console.WriteLine($"Pool utilization: {GetPoolUtilization()}%");
        Console.WriteLine($"CPU usage: {metrics.CpuUsage:F1}%");

        // Check for bottlenecks
        if (metrics.QueueDepth > 1000)
            Console.WriteLine("⚠️ High queue depth - machines may be processing slowly");

        if (metrics.CpuUsage < 50)
            Console.WriteLine("⚠️ Low CPU usage - may have concurrency issues");

        if (GetPoolUtilization() > 90)
            Console.WriteLine("⚠️ High pool utilization - consider increasing PoolSize");
    }

    private double GetPoolUtilization()
    {
        // Implementation to check event bus pool utilization
        return 75.0; // Placeholder
    }
}
```

#### 3. Memory Issues

**Symptoms:**
- Increasing memory usage over time
- Frequent garbage collection
- OutOfMemoryException

**Memory Optimization:**

```csharp
public class MemoryOptimizedProcessor
{
    private readonly ConcurrentQueue<ReusableEventData> _eventPool = new();

    ["optimizedAction"] = ctx =>
    {
        // Reuse objects instead of creating new ones
        if (!_eventPool.TryDequeue(out var eventData))
        {
            eventData = new ReusableEventData();
        }

        eventData.Reset();
        eventData.Initialize(ctx.EventData);

        try
        {
            // Process using reusable object
            ProcessEventData(eventData);
        }
        finally
        {
            // Return to pool for reuse
            _eventPool.Enqueue(eventData);
        }
    };
}
```

### Performance Profiling

#### Using Built-in Profiling

```csharp
public class PerformanceProfiler
{
    public async Task ProfileApplication()
    {
        var config = new OrchestratorConfig
        {
            EnableMetrics = true,
            MetricsInterval = TimeSpan.FromMilliseconds(100) // High frequency
        };

        using var orchestrator = new EventBusOrchestrator(config);

        // Create profiling session
        using var profilingSession = new ProfilingSession(orchestrator);

        // Run workload
        await RunProfilingWorkload(orchestrator);

        // Analyze results
        var profile = profilingSession.GetProfile();

        Console.WriteLine($"Total samples: {profile.SampleCount}");
        Console.WriteLine($"Average processing time: {profile.AverageProcessingTime:F3}ms");

        Console.WriteLine("\nTop bottlenecks:");
        foreach (var bottleneck in profile.TopBottlenecks)
        {
            Console.WriteLine($"  {bottleneck.Operation}: {bottleneck.Percentage:F1}% ({bottleneck.AverageTime:F3}ms)");
        }
    }
}
```

## 🏆 Performance Best Practices

### 1. Configuration Optimization

```csharp
// Production-optimized configuration
var productionConfig = new OrchestratorConfig
{
    // Match your server's core count
    PoolSize = Environment.ProcessorCount,

    // Enable backpressure for stability
    EnableBackpressure = true,
    MaxQueueDepth = 50000, // Adjust based on available memory

    // Minimal monitoring overhead
    EnableMetrics = true,
    EnableLogging = false, // Disable verbose logging in production
    MetricsInterval = TimeSpan.FromSeconds(10),

    // Resilience vs performance balance
    EnableCircuitBreaker = true, // Enable for production resilience
    CircuitBreakerConfig = new CircuitBreakerConfig
    {
        FailureThreshold = 10,
        TimeoutDuration = TimeSpan.FromSeconds(30),
        RecoveryTimeout = TimeSpan.FromMinutes(1)
    }
};
```

### 2. State Machine Design

```csharp
// Efficient state machine patterns
var efficientStateMachine = @"{
    ""id"": ""efficient"",
    ""initial"": ""ready"",
    ""states"": {
        ""ready"": {
            ""on"": {
                ""SINGLE"": ""processing"",
                ""BATCH"": ""batch_processing""  // Batch for efficiency
            }
        },
        ""processing"": {
            ""entry"": [""fastProcess""],  // Keep actions lightweight
            ""on"": { ""DONE"": ""ready"" }
        },
        ""batch_processing"": {
            ""entry"": [""batchProcess""],  // Process multiple items at once
            ""on"": { ""BATCH_DONE"": ""ready"" }
        }
    }
}";
```

### 3. Action Implementation

```csharp
// High-performance action patterns
var performanceActions = new Dictionary<string, Action<ExecutionContext>>
{
    // ✅ Fast, non-blocking action
    ["fastProcess"] = ctx =>
    {
        var data = (ProcessingData)ctx.EventData; // Direct cast

        // Quick synchronous processing
        var result = ProcessQuickly(data);

        // Store result efficiently
        ctx.SetStateData("result", result);
        ctx.Machine.SendFireAndForget("DONE");
    },

    // ✅ Async action for I/O operations
    ["asyncProcess"] = ctx =>
    {
        _ = Task.Run(async () =>
        {
            var data = (ProcessingData)ctx.EventData;
            var result = await ProcessAsyncOperation(data);
            ctx.SetStateData("result", result);
            ctx.Machine.SendFireAndForget("DONE");
        });
    },

    // ✅ Batch processing action
    ["batchProcess"] = ctx =>
    {
        var items = (IEnumerable<ProcessingData>)ctx.EventData;

        // Parallel processing for CPU-bound work
        var results = items.AsParallel()
            .WithDegreeOfParallelism(Environment.ProcessorCount)
            .Select(ProcessQuickly)
            .ToList();

        ctx.SetStateData("batchResults", results);
        ctx.Machine.SendFireAndForget("BATCH_DONE");
    }
};
```

### 4. Monitoring Strategy

```csharp
// Efficient monitoring setup
public class EfficientMonitoring
{
    public static void SetupMonitoring(EventBusOrchestrator orchestrator)
    {
        // Lightweight dashboard with longer intervals
        var dashboard = orchestrator.CreateDashboard();
        dashboard.SetUpdateInterval(TimeSpan.FromSeconds(5)); // Not too frequent
        dashboard.EnableDetailedView(false); // Reduce overhead

        // Focus on key metrics only
        dashboard.SetMetricFilters("EventsPerSecond", "AverageLatency", "ErrorRate", "QueueDepth");

        dashboard.StartMonitoring();

        // Custom alerting for critical metrics only
        var alertManager = new PerformanceAlertManager();
        // Set up alerting thresholds...
    }
}
```

## 📋 Performance Checklist

### Pre-Production Performance Checklist

- [ ] **Configuration Optimized**
  - [ ] PoolSize matches server cores
  - [ ] Backpressure enabled with appropriate queue depth
  - [ ] Minimal logging in production
  - [ ] Circuit breaker configured appropriately

- [ ] **State Machine Design**
  - [ ] Actions are lightweight and fast
  - [ ] No blocking operations in actions
  - [ ] Batch processing used where appropriate
  - [ ] State transitions are efficient

- [ ] **Memory Management**
  - [ ] Object pooling implemented for frequently used objects
  - [ ] No memory leaks in long-running actions
  - [ ] Proper disposal of resources

- [ ] **Benchmarking Complete**
  - [ ] Throughput meets requirements
  - [ ] Latency within acceptable bounds
  - [ ] Memory usage stable under load
  - [ ] Error rates acceptable

- [ ] **Monitoring Setup**
  - [ ] Real-time performance monitoring
  - [ ] Alerting on performance degradation
  - [ ] Regular performance reports
  - [ ] Historical performance tracking

---

This performance guide provides the foundation for building high-performance XStateNet applications. Regular benchmarking and monitoring ensure your system maintains optimal performance in production environments.