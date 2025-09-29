# XStateNet API Reference

## 📚 Core API Reference

This document provides comprehensive API documentation for XStateNet, covering all major classes, interfaces, and methods.

## 🎯 Table of Contents

- [Core Classes](#core-classes)
- [Configuration Classes](#configuration-classes)
- [Event Orchestration](#event-orchestration)
- [Inter-Machine Communication](#inter-machine-communication)
- [Monitoring & Metrics](#monitoring--metrics)
- [Benchmarking](#benchmarking)
- [Extension Points](#extension-points)
- [Examples](#examples)

## 🏗️ Core Classes

### StateMachine

The primary state machine implementation.

```csharp
public class StateMachine : IDisposable
{
    // Properties
    public string MachineId { get; }
    public string CurrentState { get; }
    public bool IsRunning { get; }
    public StateMachineConfiguration Configuration { get; }

    // Events
    public event EventHandler<StateChangedEventArgs> StateChanged;
    public event EventHandler<EventProcessedEventArgs> EventProcessed;
    public event EventHandler<ErrorEventArgs> ErrorOccurred;

    // Methods
    public Task StartAsync();
    public Task StopAsync();
    public Task<string> SendAsync(string eventName, object data = null);
    public Task<string> SendAsync(string eventName, object data, TimeSpan timeout);
    public void SendFireAndForget(string eventName, object data = null);

    // State management
    public bool IsInState(string stateName);
    public string[] GetActiveStates();
    public StateMachineSnapshot GetSnapshot();
    public Task RestoreFromSnapshot(StateMachineSnapshot snapshot);
}
```

#### Usage Example

```csharp
var machine = StateMachineFactory.CreateFromScript("myMachine", jsonScript);

// Subscribe to events
machine.StateChanged += (sender, args) =>
    Console.WriteLine($"State: {args.From} -> {args.To}");

// Start and use the machine
await machine.StartAsync();
var result = await machine.SendAsync("START_PROCESS", new { id = 123 });
```

### StateMachineFactory

Factory for creating state machines from various sources.

```csharp
public static class StateMachineFactory
{
    // Create from JSON script
    public static StateMachine CreateFromScript(string machineId, string jsonScript);
    public static StateMachine CreateFromScript(string machineId, string jsonScript,
        Dictionary<string, Action<ExecutionContext>> actions);

    // Create from configuration objects
    public static StateMachine CreateFromConfiguration(StateMachineConfiguration config);

    // Create with orchestrator integration
    public static StateMachine CreateFromScript(string machineId, string jsonScript,
        EventBusOrchestrator orchestrator, Dictionary<string, Action<ExecutionContext>> actions);

    // Advanced creation methods
    public static PriorityStateMachine CreatePriorityMachine(string machineId, string jsonScript);
    public static TimingSensitiveStateMachine CreateTimingSensitiveMachine(string machineId, string jsonScript);
}
```

### ExecutionContext

Context object passed to actions and guards.

```csharp
public class ExecutionContext
{
    // Current execution state
    public string MachineId { get; }
    public string CurrentState { get; }
    public string PreviousState { get; }
    public string EventName { get; }
    public object EventData { get; }
    public DateTime Timestamp { get; }

    // Machine interaction
    public StateMachine Machine { get; }
    public Task<string> RequestSend(string targetMachine, string eventName, object data = null);
    public void Log(string message, LogLevel level = LogLevel.Information);

    // State data management
    public T GetStateData<T>(string key);
    public void SetStateData(string key, object value);
    public void ClearStateData(string key);

    // Conditional execution
    public bool EvaluateGuard(string guardName);
    public Task ExecuteActionAsync(string actionName);
}
```

## ⚙️ Configuration Classes

### OrchestratorConfig

Configuration for the event bus orchestrator.

```csharp
public class OrchestratorConfig
{
    // Core settings
    public int PoolSize { get; set; } = 4;
    public bool EnableMetrics { get; set; } = true;
    public bool EnableLogging { get; set; } = true;
    public bool EnableStructuredLogging { get; set; } = false;
    public string LogLevel { get; set; } = "Information";

    // Performance settings
    public bool EnableBackpressure { get; set; } = true;
    public int MaxQueueDepth { get; set; } = 10000;
    public TimeSpan ThrottleDelay { get; set; } = TimeSpan.Zero;

    // Resilience settings
    public bool EnableCircuitBreaker { get; set; } = false;
    public CircuitBreakerConfig CircuitBreakerConfig { get; set; }

    // Monitoring settings
    public TimeSpan MetricsInterval { get; set; } = TimeSpan.FromSeconds(5);
    public bool EnableHealthChecks { get; set; } = true;
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);
}
```

### BenchmarkConfig

Configuration for performance benchmarking.

```csharp
public class BenchmarkConfig
{
    // Benchmark execution settings
    public int WarmupIterations { get; set; } = 3;
    public int MeasurementIterations { get; set; } = 5;
    public int DefaultMachineCount { get; set; } = 10;
    public int EventBusPoolSize { get; set; } = 4;

    // Event count settings for different benchmark types
    public int ThroughputEventCount { get; set; } = 50000;
    public int LatencyEventCount { get; set; } = 1000;
    public int ScalabilityEventCount { get; set; } = 10000;
    public int MemoryTestEventCount { get; set; } = 100000;
    public int StressEventCount { get; set; } = 200000;

    // Performance optimization settings
    public bool EnableMetrics { get; set; } = false;
    public bool EnableBackpressure { get; set; } = true;
    public int MaxQueueDepth { get; set; } = 50000;

    // Stress testing settings
    public int ConcurrencyLevel { get; set; } = Environment.ProcessorCount;
    public int BurstSize { get; set; } = 1000;
    public int BurstCount { get; set; } = 10;

    // Long duration test settings
    public TimeSpan LongDurationTestTime { get; set; } = TimeSpan.FromMinutes(2);
    public int LongDurationEventsPerSecond { get; set; } = 1000;
}
```

## 🎼 Event Orchestration

### EventBusOrchestrator

Central orchestrator for managing multiple state machines.

```csharp
public class EventBusOrchestrator : IDisposable
{
    // Constructor
    public EventBusOrchestrator(OrchestratorConfig config);

    // Machine management
    public Task RegisterMachineAsync(string machineId, StateMachine machine);
    public Task UnregisterMachineAsync(string machineId);
    public Task StartMachineAsync(string machineId);
    public Task StopMachineAsync(string machineId);
    public Task StartAllMachinesAsync();
    public Task StopAllMachinesAsync();

    // Event sending
    public Task<string> SendEventAsync(string requestId, string machineId, string eventName, object data = null);
    public Task<string> SendEventAsync(string requestId, string machineId, string eventName, object data, TimeSpan timeout);
    public Task SendEventFireAndForgetAsync(string requestId, string machineId, string eventName, object data = null);

    // Monitoring
    public OrchestratorMetrics GetMetrics();
    public HealthStatus GetHealthStatus();
    public MonitoringDashboard CreateDashboard();

    // Statistics
    public OrchestratorStatistics GetStatistics();
    public Task<string> GetMachineStatusAsync(string machineId);
    public Dictionary<string, string> GetAllMachineStatuses();
}
```

### Usage Example

```csharp
var config = new OrchestratorConfig
{
    PoolSize = 8,
    EnableMetrics = true,
    EnableBackpressure = true,
    MaxQueueDepth = 50000
};

using var orchestrator = new EventBusOrchestrator(config);

// Register machines
await orchestrator.RegisterMachineAsync("processor1", machine1);
await orchestrator.RegisterMachineAsync("processor2", machine2);

// Start all machines
await orchestrator.StartAllMachinesAsync();

// Send events
var result = await orchestrator.SendEventAsync("req1", "processor1", "PROCESS", data);
```

### MonitoringDashboard

Real-time monitoring and dashboard functionality.

```csharp
public class MonitoringDashboard : IDisposable
{
    // Dashboard control
    public void StartMonitoring(TimeSpan updateInterval);
    public void StopMonitoring();
    public bool IsMonitoring { get; }

    // Display methods
    public void DisplayCurrentMetrics();
    public void DisplaySummaryReport();
    public void DisplayHealthStatus();

    // Export methods
    public void ExportMetrics(string filePath, ExportFormat format);
    public void ExportSummary(string filePath);

    // Configuration
    public void SetUpdateInterval(TimeSpan interval);
    public void EnableDetailedView(bool enabled);
    public void SetMetricFilters(params string[] metricNames);
}
```

## 🌐 Inter-Machine Communication

### InterMachineConnector

Facilitates communication between state machines.

```csharp
public class InterMachineConnector : IDisposable
{
    // Machine registration
    public void RegisterMachine(string machineId, StateMachine machine);
    public void UnregisterMachine(string machineId);
    public bool IsMachineRegistered(string machineId);

    // Connection management
    public void ConnectMachines(string fromMachine, string toMachine, string eventName);
    public void DisconnectMachines(string fromMachine, string toMachine, string eventName);

    // Event routing
    public Task SendToMachineAsync(string targetMachine, string eventName, object data = null);
    public Task SendToMachineAsync(string targetMachine, string eventName, object data, TimeSpan timeout);
    public Task BroadcastEventAsync(string eventName, object data = null);

    // Machine queries
    public Task<string> GetMachineStateAsync(string machineId);
    public Task<bool> IsMachineInStateAsync(string machineId, string stateName);
    public Task<MachineInfo[]> GetAllMachineInfoAsync();
}
```

### MachineRegistrator

Registry and discovery service for distributed machines.

```csharp
public class MachineRegistrator
{
    // Registration
    public Task RegisterMachineAsync(string machineId, string machineType, StateMachine machine);
    public Task UnregisterMachineAsync(string machineId);
    public Task UpdateMachineStatusAsync(string machineId, string status);

    // Discovery
    public Task<RegisteredMachine[]> GetRegisteredMachinesAsync();
    public Task<RegisteredMachine> GetMachineAsync(string machineId);
    public Task<RegisteredMachine[]> GetMachinesByTypeAsync(string machineType);

    // Health monitoring
    public Task<bool> IsMachineHealthyAsync(string machineId);
    public Task<HealthReport> GetMachineHealthAsync(string machineId);
    public Task<HealthSummary> GetOverallHealthAsync();
}
```

## 📊 Monitoring & Metrics

### OrchestratorMetrics

Comprehensive metrics collection and reporting.

```csharp
public class OrchestratorMetrics
{
    // Performance metrics
    public long TotalEventsProcessed { get; }
    public double EventsPerSecond { get; }
    public double AverageLatency { get; }
    public double P95Latency { get; }
    public double P99Latency { get; }

    // System metrics
    public int ActiveMachines { get; }
    public int QueueDepth { get; }
    public double CpuUsage { get; }
    public long MemoryUsage { get; }

    // Error metrics
    public long ErrorCount { get; }
    public double ErrorRate { get; }
    public Dictionary<string, int> ErrorsByType { get; }

    // Methods
    public MetricsSnapshot GetSnapshot();
    public void ResetCounters();
    public void ExportToJson(string filePath);
    public void ExportToCsv(string filePath);
}
```

### HealthStatus

System health monitoring and reporting.

```csharp
public class HealthStatus
{
    public HealthLevel Level { get; }  // Healthy, Degraded, Unhealthy, Critical
    public DateTime Timestamp { get; }
    public List<string> Issues { get; }
    public Dictionary<string, object> Details { get; }

    // Component health
    public ComponentHealth[] ComponentStatuses { get; }

    // Methods
    public bool IsHealthy { get; }
    public string GetHealthSummary();
    public void ExportHealthReport(string filePath);
}
```

## 🏁 Benchmarking

### BenchmarkFramework

Comprehensive performance testing framework.

```csharp
public class BenchmarkFramework : IDisposable
{
    public BenchmarkFramework(BenchmarkConfig config);

    // Benchmark execution
    public Task<BenchmarkSuiteResult> RunBenchmarkSuiteAsync();
    public Task<BenchmarkResult> BenchmarkSequentialThroughputAsync();
    public Task<BenchmarkResult> BenchmarkParallelThroughputAsync();
    public Task<BenchmarkResult> BenchmarkSingleEventLatencyAsync();
    public Task<BenchmarkResult> BenchmarkRequestResponseLatencyAsync();
    public Task<BenchmarkResult> BenchmarkMachineScalabilityAsync();
    public Task<BenchmarkResult> BenchmarkEventBusScalabilityAsync();
    public Task<BenchmarkResult> BenchmarkMemoryUsageAsync();
    public Task<BenchmarkResult> BenchmarkHighConcurrencyAsync();
    public Task<BenchmarkResult> BenchmarkBurstTrafficAsync();
    public Task<BenchmarkResult> BenchmarkLongDurationAsync();

    // Configuration
    public void UpdateConfig(BenchmarkConfig config);
    public BenchmarkConfig GetCurrentConfig();
}
```

### BenchmarkResult

Results from benchmark execution.

```csharp
public class BenchmarkResult
{
    public string BenchmarkName { get; }
    public bool Success { get; }
    public string ErrorMessage { get; }

    // Performance metrics
    public int EventCount { get; }
    public TimeSpan Duration { get; }
    public double EventsPerSecond { get; }
    public double AverageLatency { get; }
    public Dictionary<string, double> LatencyPercentiles { get; }
    public List<double> Measurements { get; }

    // Scalability data
    public List<ScalabilityDataPoint> ScalabilityData { get; }

    // Export methods
    public void ExportToJson(string filePath);
    public void ExportToCsv(string filePath);
    public string GenerateReport();
}
```

### BenchmarkRunner

High-level benchmark execution utilities.

```csharp
public static class BenchmarkRunner
{
    // Pre-configured benchmark suites
    public static Task RunFullBenchmarkSuite();
    public static Task RunQuickBenchmark();
    public static Task RunLatencyFocusedBenchmark();
    public static Task RunThroughputFocusedBenchmark();

    // Custom benchmark execution
    public static Task<BenchmarkResult[]> RunCoreBenchmarks(BenchmarkFramework framework);
    public static Task RunCustomBenchmarkSuite(BenchmarkConfig config);
}
```

## 🔌 Extension Points

### IActionProvider

Interface for providing custom actions.

```csharp
public interface IActionProvider
{
    Action<ExecutionContext> GetAction(string actionName);
    bool HasAction(string actionName);
    IEnumerable<string> GetActionNames();
}
```

### IEventInterceptor

Interface for intercepting and modifying events.

```csharp
public interface IEventInterceptor
{
    Task<bool> OnEventReceived(EventContext context);
    Task OnEventProcessed(EventContext context);
    Task OnEventFailed(EventContext context, Exception exception);
}
```

### IMetricsProvider

Interface for custom metrics providers.

```csharp
public interface IMetricsProvider
{
    void RecordEventProcessed(string machineId, TimeSpan duration);
    void RecordStateTransition(string machineId, string fromState, string toState);
    void RecordError(string machineId, string errorType, Exception exception);
    void RecordMetric(string name, double value, Dictionary<string, string> tags = null);
}
```

### IStateStorage

Interface for custom state persistence.

```csharp
public interface IStateStorage
{
    Task<StateMachineSnapshot> LoadSnapshotAsync(string machineId);
    Task SaveSnapshotAsync(string machineId, StateMachineSnapshot snapshot);
    Task DeleteSnapshotAsync(string machineId);
    Task<bool> ExistsAsync(string machineId);
}
```

## 💡 Usage Examples

### Complete Application Example

```csharp
public class OrderProcessingService
{
    private EventBusOrchestrator _orchestrator;
    private InterMachineConnector _connector;

    public async Task InitializeAsync()
    {
        // Configure orchestrator
        var config = new OrchestratorConfig
        {
            PoolSize = 8,
            EnableMetrics = true,
            EnableBackpressure = true,
            MaxQueueDepth = 50000,
            EnableCircuitBreaker = true
        };

        _orchestrator = new EventBusOrchestrator(config);
        _connector = new InterMachineConnector();

        // Create machines
        var orderMachine = CreateOrderMachine();
        var paymentMachine = CreatePaymentMachine();
        var inventoryMachine = CreateInventoryMachine();

        // Register machines
        await _orchestrator.RegisterMachineAsync("orders", orderMachine);
        await _orchestrator.RegisterMachineAsync("payments", paymentMachine);
        await _orchestrator.RegisterMachineAsync("inventory", inventoryMachine);

        // Setup inter-machine communication
        _connector.RegisterMachine("orders", orderMachine);
        _connector.RegisterMachine("payments", paymentMachine);
        _connector.RegisterMachine("inventory", inventoryMachine);

        _connector.ConnectMachines("orders", "payments", "PAYMENT_REQUIRED");
        _connector.ConnectMachines("orders", "inventory", "RESERVE_ITEMS");

        // Start monitoring
        var dashboard = _orchestrator.CreateDashboard();
        dashboard.StartMonitoring(TimeSpan.FromSeconds(5));

        await _orchestrator.StartAllMachinesAsync();
    }

    private StateMachine CreateOrderMachine()
    {
        var actions = new Dictionary<string, Action<ExecutionContext>>
        {
            ["validateOrder"] = ctx => ValidateOrder((Order)ctx.EventData),
            ["requestPayment"] = ctx => ctx.RequestSend("payments", "PROCESS_PAYMENT", ctx.EventData),
            ["reserveItems"] = ctx => ctx.RequestSend("inventory", "RESERVE", ctx.EventData),
            ["completeOrder"] = ctx => CompleteOrder((Order)ctx.EventData)
        };

        var json = @"{
            ""id"": ""orderProcessor"",
            ""initial"": ""idle"",
            ""states"": {
                ""idle"": {
                    ""on"": { ""NEW_ORDER"": ""validating"" }
                },
                ""validating"": {
                    ""entry"": [""validateOrder""],
                    ""on"": {
                        ""VALID"": ""processing"",
                        ""INVALID"": ""rejected""
                    }
                },
                ""processing"": {
                    ""entry"": [""requestPayment"", ""reserveItems""],
                    ""on"": {
                        ""PAYMENT_SUCCESS"": ""completing"",
                        ""PAYMENT_FAILED"": ""cancelled""
                    }
                },
                ""completing"": {
                    ""entry"": [""completeOrder""],
                    ""on"": { ""COMPLETED"": ""idle"" }
                },
                ""rejected"": { ""type"": ""final"" },
                ""cancelled"": { ""type"": ""final"" }
            }
        }";

        return StateMachineFactory.CreateFromScript("orders", json, _orchestrator, actions);
    }

    public async Task ProcessOrderAsync(Order order)
    {
        var result = await _orchestrator.SendEventAsync(
            Guid.NewGuid().ToString(),
            "orders",
            "NEW_ORDER",
            order
        );

        Console.WriteLine($"Order processing result: {result}");
    }
}
```

### Custom Extension Example

```csharp
public class DatabaseActionProvider : IActionProvider
{
    private readonly IDbConnection _connection;

    public DatabaseActionProvider(IDbConnection connection)
    {
        _connection = connection;
    }

    public Action<ExecutionContext> GetAction(string actionName)
    {
        return actionName switch
        {
            "saveToDatabase" => SaveToDatabase,
            "loadFromDatabase" => LoadFromDatabase,
            "deleteFromDatabase" => DeleteFromDatabase,
            _ => throw new UnknownActionException($"Unknown action: {actionName}")
        };
    }

    private void SaveToDatabase(ExecutionContext ctx)
    {
        var data = ctx.EventData;
        var sql = "INSERT INTO state_data (machine_id, state, data) VALUES (@MachineId, @State, @Data)";
        _connection.Execute(sql, new { ctx.MachineId, ctx.CurrentState, Data = JsonSerializer.Serialize(data) });
        ctx.Log($"Saved data for machine {ctx.MachineId} in state {ctx.CurrentState}");
    }

    // ... other methods
}
```

---

This API reference provides comprehensive coverage of the XStateNet framework. For additional examples and advanced usage patterns, see the [Examples](EXAMPLES.md) documentation.