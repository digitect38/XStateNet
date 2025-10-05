using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace XStateNet.Orchestration
{
    /// <summary>
    /// Central orchestrator that manages all state machine communication
    /// State machines have NO send methods - all communication goes through the orchestrator
    /// </summary>
    public class EventBusOrchestrator : IDisposable
    {
        private readonly ConcurrentDictionary<string, ManagedStateMachine> _machines = new();
        private readonly ConcurrentDictionary<Guid, PendingRequest> _pendingRequests = new();
        private readonly ConcurrentDictionary<string, OrchestratedContext> _machineContexts = new();

        // Pool of event buses for load distribution
        private readonly EventBusPool _eventBusPool;
        private readonly CancellationTokenSource _cancellationSource;
        private bool _disposed = false;

        // Configuration
        private readonly OrchestratorConfig _config;

        // Monitoring and observability
        private readonly OrchestratorMetrics _metrics;
        private readonly OrchestratorLogger _logger;
        public OrchestratorMetrics Metrics => _metrics;

        // Monitoring events for external observers
        public event EventHandler<MachineEventProcessedEventArgs>? MachineEventProcessed;
        public event EventHandler<MachineEventFailedEventArgs>? MachineEventFailed;

        public EventBusOrchestrator(OrchestratorConfig? config = null)
        {
            _config = config ?? new OrchestratorConfig();
            _cancellationSource = new CancellationTokenSource();
            _eventBusPool = new EventBusPool(_config.PoolSize, _cancellationSource.Token, ProcessEventAsync, _config);

            // Initialize monitoring
            _metrics = new OrchestratorMetrics();
            _logger = new OrchestratorLogger(_config.LogLevel, _config.EnableStructuredLogging);

            // Start periodic cleanup of metrics
            if (_config.EnableMetrics)
            {
                _ = Task.Run(async () =>
                {
                    while (!_cancellationSource.Token.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromMinutes(5), _cancellationSource.Token);
                            _metrics.Cleanup(TimeSpan.FromHours(1)); // Keep 1 hour of detailed metrics
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                });
            }
        }

        /// <summary>
        /// Register a state machine with the orchestrator
        /// The state machine should have NO send methods
        /// </summary>
        public void RegisterMachine(string machineId, IStateMachine machine)
        {
            // Extract channel group from machine ID if present
            // Supports both formats:
            // - Old: name#groupId#guid
            // - New: name_groupId_guid
            int? channelGroupId = null;
            var parts = machineId.Split('#', '_');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var groupId))
            {
                channelGroupId = groupId;
            }

            RegisterMachine(machineId, machine, channelGroupId);
        }

        /// <summary>
        /// Register a state machine with the orchestrator with explicit channel group
        /// </summary>
        public void RegisterMachine(string machineId, IStateMachine machine, int? channelGroupId)
        {
            if (string.IsNullOrEmpty(machineId))
                throw new ArgumentNullException(nameof(machineId));
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));

            var managed = new ManagedStateMachine
            {
                Id = machineId,
                Machine = machine,
                EventBusIndex = GetEventBusIndex(machineId),
                ChannelGroupId = channelGroupId
            };

            _machines[machineId] = managed;

            // Record metrics
            if (_config.EnableMetrics)
            {
                _metrics.RecordMachineRegistered(machineId, machine.GetType().Name);
                _logger.LogMachineRegistered(machineId, machine.GetType().Name);
            }

            if (_config.EnableLogging)
            {
                Console.WriteLine($"[Orchestrator] Registered machine: {machineId} " +
                    $"(Group: {channelGroupId?.ToString() ?? "none"}) on bus {managed.EventBusIndex}");
            }
        }

        /// <summary>
        /// Register a state machine with orchestrated context support
        /// </summary>
        public void RegisterMachineWithContext(string machineId, IStateMachine machine, OrchestratedContext? context = null)
        {
            RegisterMachine(machineId, machine);

            // Store the context if provided
            if (context != null)
            {
                _machineContexts[machineId] = context;
            }
        }

        /// <summary>
        /// Unregister a state machine from the orchestrator
        /// </summary>
        public void UnregisterMachine(string machineId)
        {
            if (_machines.TryRemove(machineId, out var removed))
            {
                _machineContexts.TryRemove(machineId, out _);

                if (_config.EnableLogging)
                {
                    Console.WriteLine($"[Orchestrator] Unregistered machine: {machineId} " +
                        $"(Group: {removed.ChannelGroupId?.ToString() ?? "none"})");
                }
            }
        }

        /// <summary>
        /// Unregister all machines in a channel group
        /// Used for cleanup when a channel group token is disposed
        /// </summary>
        public void UnregisterMachinesInGroup(int channelGroupId)
        {
            var machinesToRemove = _machines
                .Where(kvp => kvp.Value.ChannelGroupId == channelGroupId)
                .Select(kvp => kvp.Key)
                .ToList();

            if (_config.EnableLogging && machinesToRemove.Count > 0)
            {
                Console.WriteLine($"[Orchestrator] Unregistering {machinesToRemove.Count} machines from channel group {channelGroupId}");
            }

            foreach (var machineId in machinesToRemove)
            {
                UnregisterMachine(machineId);
            }
        }

        /// <summary>
        /// Get or create an orchestrated context for a machine
        /// </summary>
        public OrchestratedContext GetOrCreateContext(string machineId)
        {
            return _machineContexts.GetOrAdd(machineId, id => new OrchestratedContext(this, id));
        }

        /// <summary>
        /// Start a machine and process any deferred sends from entry actions
        /// </summary>
        public async Task<string> StartMachineAsync(string machineId)
        {
            if (!_machines.TryGetValue(machineId, out var machine))
            {
                throw new InvalidOperationException($"Machine '{machineId}' not registered");
            }

            var result = await machine.Machine.StartAsync();

            // Check if this machine has an orchestrated context with deferred sends
            if (_machineContexts.TryGetValue(machineId, out var context))
            {
                // Execute any deferred sends that were queued during the entry action
                await context.ExecuteDeferredSends();
            }

            return result;
        }

        /// <summary>
        /// Send an event from one machine to another (or to itself)
        /// This is the ONLY way machines can communicate
        /// </summary>
        public async Task<EventResult> SendEventAsync(
            string fromMachineId,
            string toMachineId,
            string eventName,
            object? eventData = null,
            int timeoutMs = 5000)
        {
            var stopwatch = Stopwatch.StartNew();
            // Create the event
            var evt = new OrchestratedEvent
            {
                Id = Guid.NewGuid(),
                FromMachineId = fromMachineId,
                ToMachineId = toMachineId,
                EventName = eventName,
                EventData = eventData,
                Timestamp = DateTime.UtcNow,
                IsSelfSend = fromMachineId == toMachineId
            };

            // Record event start
            if (_config.EnableMetrics)
            {
                _metrics.RecordEventStart(evt.Id, toMachineId, eventName);
            }

            // Validate target exists
            if (!_machines.ContainsKey(toMachineId))
            {
                return new EventResult
                {
                    Success = false,
                    ErrorMessage = $"Target machine '{toMachineId}' not registered",
                    EventId = evt.Id
                };
            }

            // Create pending request
            var pendingRequest = new PendingRequest
            {
                Event = evt,
                CompletionSource = new TaskCompletionSource<EventResult>()
            };

            if (!_pendingRequests.TryAdd(evt.Id, pendingRequest))
            {
                return new EventResult
                {
                    Success = false,
                    ErrorMessage = "Failed to register request",
                    EventId = evt.Id
                };
            }

            try
            {
                // Route to appropriate event bus
                var targetMachine = _machines[toMachineId];
                var enqueued = await _eventBusPool.EnqueueEventAsync(targetMachine.EventBusIndex, evt, _config.ThrottleDelay);

                if (!enqueued)
                {
                    return new EventResult
                    {
                        Success = false,
                        ErrorMessage = "Event queue full - throttled",
                        EventId = evt.Id
                    };
                }

                // Wait for result with timeout
                using var cts = new CancellationTokenSource(timeoutMs);
                try
                {
                    var result = await pendingRequest.CompletionSource.Task.WaitAsync(cts.Token);

                    // Record metrics
                    if (_config.EnableMetrics)
                    {
                        if (result.Success)
                        {
                            _metrics.RecordEventSuccess(evt.Id, stopwatch.Elapsed);
                            _logger.LogEventProcessed(toMachineId, eventName, stopwatch.Elapsed, true);
                        }
                        else
                        {
                            _metrics.RecordEventFailure(evt.Id, result.ErrorMessage ?? "Unknown error", stopwatch.Elapsed);
                            _logger.LogEventProcessed(toMachineId, eventName, stopwatch.Elapsed, false);
                        }
                    }

                    return result;
                }
                catch (OperationCanceledException)
                {
                    var timeoutResult = new EventResult
                    {
                        Success = false,
                        ErrorMessage = $"Request timed out after {timeoutMs}ms",
                        EventId = evt.Id
                    };

                    // Record timeout
                    if (_config.EnableMetrics)
                    {
                        _metrics.RecordEventTimeout(evt.Id, stopwatch.Elapsed);
                        _logger.LogEventProcessed(toMachineId, eventName, stopwatch.Elapsed, false);
                    }

                    return timeoutResult;
                }
            }
            finally
            {
                _pendingRequests.TryRemove(evt.Id, out _);
            }
        }

        /// <summary>
        /// Send event without waiting for result (fire-and-forget)
        /// </summary>
        public async Task SendEventFireAndForgetAsync(
            string fromMachineId,
            string toMachineId,
            string eventName,
            object? eventData = null)
        {
            if (!_machines.ContainsKey(toMachineId))
            {
                Console.WriteLine($"[Orchestrator] Warning: Target machine '{toMachineId}' not registered");
                return;
            }

            var evt = new OrchestratedEvent
            {
                Id = Guid.NewGuid(),
                FromMachineId = fromMachineId,
                ToMachineId = toMachineId,
                EventName = eventName,
                EventData = eventData,
                Timestamp = DateTime.UtcNow,
                IsSelfSend = fromMachineId == toMachineId,
                IsFireAndForget = true
            };

            var targetMachine = _machines[toMachineId];
            await _eventBusPool.EnqueueEventAsync(targetMachine.EventBusIndex, evt, _config.ThrottleDelay);
        }

        /// <summary>
        /// Process an event from the event bus
        /// </summary>
        private async Task ProcessEventAsync(OrchestratedEvent evt)
        {
            EventResult result;

            try
            {
                if (!_machines.TryGetValue(evt.ToMachineId, out var targetMachine))
                {
                    result = new EventResult
                    {
                        Success = false,
                        ErrorMessage = $"Machine '{evt.ToMachineId}' not found",
                        EventId = evt.Id
                    };
                }
                else
                {
                    // Log self-sends for debugging
                    if (evt.IsSelfSend)
                    {
                        Console.WriteLine($"[Orchestrator] Processing self-send: {evt.EventName} for {evt.ToMachineId}");
                    }

                    // Send event to the state machine
                    var newState = await targetMachine.Machine.SendAsync(evt.EventName, evt.EventData);

                    // Check if this machine has an orchestrated context with deferred sends
                    if (_machineContexts.TryGetValue(evt.ToMachineId, out var context))
                    {
                        // Execute any deferred sends that were queued during the action
                        await context.ExecuteDeferredSends();
                    }

                    result = new EventResult
                    {
                        Success = true,
                        NewState = newState,
                        ProcessedBy = evt.ToMachineId,
                        EventId = evt.Id,
                        Timestamp = DateTime.UtcNow
                    };

                    if (_config.EnableLogging)
                    {
                        Console.WriteLine($"[Orchestrator] Processed {evt.EventName} for {evt.ToMachineId}, new state: {newState}");
                    }

                    // Fire monitoring event
                    MachineEventProcessed?.Invoke(this, new MachineEventProcessedEventArgs
                    {
                        MachineId = evt.ToMachineId,
                        EventName = evt.EventName,
                        EventData = evt.EventData,
                        OldState = "", // TODO: Capture old state if needed
                        NewState = newState,
                        Timestamp = DateTime.UtcNow,
                        Duration = TimeSpan.Zero // TODO: Add duration tracking if needed
                    });
                }
            }
            catch (Exception ex)
            {
                result = new EventResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    EventId = evt.Id
                };
                Console.WriteLine($"[Orchestrator] Error processing event: {ex.Message}");

                // Fire monitoring event for failure
                MachineEventFailed?.Invoke(this, new MachineEventFailedEventArgs
                {
                    MachineId = evt.ToMachineId,
                    EventName = evt.EventName,
                    EventData = evt.EventData,
                    Exception = ex,
                    Timestamp = DateTime.UtcNow
                });
            }

            // Complete pending request if not fire-and-forget
            if (!evt.IsFireAndForget && _pendingRequests.TryRemove(evt.Id, out var pending))
            {
                pending.CompletionSource.TrySetResult(result);
            }
        }

        /// <summary>
        /// Get statistics about the orchestrator
        /// </summary>
        public OrchestratorStats GetStats()
        {
            // Update real-time metrics
            if (_config.EnableMetrics)
            {
                _metrics.UpdatePendingRequests(_pendingRequests.Count);
                _metrics.UpdateQueuedEvents(_eventBusPool.GetStats().Sum(s => s.QueuedEvents));
            }

            return new OrchestratorStats
            {
                RegisteredMachines = _machines.Count,
                PendingRequests = _pendingRequests.Count,
                EventBusStats = _eventBusPool.GetStats(),
                IsRunning = !_cancellationSource.IsCancellationRequested,
                HealthStatus = _config.EnableMetrics ? _metrics.GetHealthStatus() : null,
                Metrics = _config.EnableMetrics ? _metrics.GetCurrentMetrics() : null
            };
        }

        /// <summary>
        /// Get health status of the orchestrator
        /// </summary>
        public HealthStatus GetHealthStatus()
        {
            if (!_config.EnableMetrics)
            {
                return new HealthStatus
                {
                    Level = HealthLevel.Healthy,
                    Timestamp = DateTime.UtcNow,
                    Issues = new List<string> { "Monitoring disabled" }
                };
            }

            return _metrics.GetHealthStatus();
        }

        /// <summary>
        /// Create a monitoring dashboard for real-time visualization
        /// </summary>
        public MonitoringDashboard CreateDashboard()
        {
            if (!_config.EnableMetrics)
            {
                throw new InvalidOperationException("Metrics must be enabled to create a dashboard");
            }

            return new MonitoringDashboard(_metrics);
        }

        /// <summary>
        /// Determine which event bus to use for a machine (consistent hashing)
        /// </summary>
        private int GetEventBusIndex(string machineId)
        {
            return Math.Abs(machineId.GetHashCode()) % _config.PoolSize;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cancellationSource.Cancel();
            _eventBusPool.Dispose();

            // Cancel all pending requests
            foreach (var pending in _pendingRequests.Values)
            {
                pending.CompletionSource.TrySetCanceled();
            }
            _pendingRequests.Clear();

            _cancellationSource.Dispose();
            Console.WriteLine("[Orchestrator] Disposed");
        }

        #region Internal Types

        private class ManagedStateMachine
        {
            public string Id { get; set; } = "";
            public IStateMachine Machine { get; set; } = null!;
            public int EventBusIndex { get; set; }
            public int? ChannelGroupId { get; set; } // For isolation via channel groups
        }

        private class PendingRequest
        {
            public OrchestratedEvent Event { get; set; } = null!;
            public TaskCompletionSource<EventResult> CompletionSource { get; set; } = new();
        }

        #endregion
    }

    /// <summary>
    /// Pool of event buses for load distribution
    /// </summary>
    public class EventBusPool : IDisposable
    {
        private readonly EventBus[] _buses;
        private readonly Task[] _processors;

        public EventBusPool(int poolSize, CancellationToken cancellationToken, Func<OrchestratedEvent, Task> processor, OrchestratorConfig config)
        {
            _buses = new EventBus[poolSize];
            _processors = new Task[poolSize];

            for (int i = 0; i < poolSize; i++)
            {
                _buses[i] = new EventBus(config);
                var bus = _buses[i];
                _processors[i] = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var evt in bus.Channel.Reader.ReadAllAsync(cancellationToken))
                        {
                            try
                            {
                                await processor(evt);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[EventBusPool] Error processing event: {ex.Message}");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancellation token is triggered
                    }
                });
            }
        }

        public async Task<bool> EnqueueEventAsync(int busIndex, OrchestratedEvent evt, TimeSpan throttleDelay = default)
        {
            if (throttleDelay == default)
                throttleDelay = TimeSpan.Zero;

            return await _buses[busIndex].TryEnqueueWithThrottleAsync(evt, throttleDelay);
        }

        public List<EventBusStats> GetStats()
        {
            return _buses.Select((bus, index) => new EventBusStats
            {
                BusIndex = index,
                QueuedEvents = bus.Channel.Reader.TryPeek(out _) ? -1 : 0, // -1 means "has items", 0 means empty
                TotalProcessed = bus.EventCount
            }).ToList();
        }

        public void Dispose()
        {
            // First, complete all channel writers to signal no more items
            foreach (var bus in _buses)
            {
                bus.Channel.Writer.TryComplete();
            }

            // Then wait for processors to finish processing remaining items
            try
            {
                Task.WaitAll(_processors, TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Ignore cancellation exceptions during disposal
            }
        }

        private class EventBus
        {
            public Channel<OrchestratedEvent> Channel { get; }
            public long EventCount { get; set; }
            private readonly SemaphoreSlim _throttle;

            public EventBus(OrchestratorConfig config)
            {
                if (config.EnableBackpressure && config.MaxQueueDepth > 0)
                {
                    // Use bounded channel with backpressure for high-throughput scenarios
                    var boundedOptions = new BoundedChannelOptions(config.MaxQueueDepth)
                    {
                        FullMode = BoundedChannelFullMode.Wait,
                        SingleReader = true,
                        SingleWriter = false
                    };
                    Channel = System.Threading.Channels.Channel.CreateBounded<OrchestratedEvent>(boundedOptions);
                }
                else
                {
                    // Use unbounded channel for normal scenarios
                    var unboundedOptions = new UnboundedChannelOptions
                    {
                        SingleReader = true,
                        SingleWriter = false
                    };
                    Channel = System.Threading.Channels.Channel.CreateUnbounded<OrchestratedEvent>(unboundedOptions);
                }

                // Throttling semaphore for flow control
                _throttle = new SemaphoreSlim(config.MaxQueueDepth, config.MaxQueueDepth);
            }

            public async Task<bool> TryEnqueueWithThrottleAsync(OrchestratedEvent evt, TimeSpan throttleDelay)
            {
                if (throttleDelay > TimeSpan.Zero)
                {
                    if (!await _throttle.WaitAsync(throttleDelay))
                        return false; // Throttled
                }

                try
                {
                    await Channel.Writer.WriteAsync(evt);
                    EventCount++;

                    if (throttleDelay > TimeSpan.Zero)
                    {
                        // Release throttle after a delay to control flow
                        Task.Delay(throttleDelay).ContinueWith(_ => _throttle.Release());
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    #region Public Types

    public class OrchestratorConfig
    {
        public int PoolSize { get; set; } = 4;  // Number of event buses in the pool
        public bool EnableLogging { get; set; } = true;
        public int DefaultTimeoutMs { get; set; } = 5000;

        // High-throughput optimizations
        public int MaxQueueDepth { get; set; } = 10000;  // Max events per channel
        public int BatchSize { get; set; } = 100;        // Events per batch
        public bool EnableBackpressure { get; set; } = false;  // Bounded channels
        public TimeSpan ThrottleDelay { get; set; } = TimeSpan.Zero;  // Adaptive throttling

        // Monitoring and observability
        public bool EnableMetrics { get; set; } = true;
        public bool EnableStructuredLogging { get; set; } = true;
        public string LogLevel { get; set; } = "INFO";
        public bool EnableHealthChecks { get; set; } = true;
        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan MetricsRetentionPeriod { get; set; } = TimeSpan.FromHours(24);
    }

    public class OrchestratedEvent
    {
        public Guid Id { get; set; }
        public string FromMachineId { get; set; } = "";
        public string ToMachineId { get; set; } = "";
        public string EventName { get; set; } = "";
        public object? EventData { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsSelfSend { get; set; }
        public bool IsFireAndForget { get; set; }
    }

    public class EventResult
    {
        public bool Success { get; set; }
        public string? NewState { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ProcessedBy { get; set; }
        public Guid EventId { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class OrchestratorStats
    {
        public int RegisteredMachines { get; set; }
        public int PendingRequests { get; set; }
        public List<EventBusStats> EventBusStats { get; set; } = new();
        public bool IsRunning { get; set; }
        public HealthStatus? HealthStatus { get; set; }
        public MetricsSnapshot? Metrics { get; set; }
    }

    public class EventBusStats
    {
        public int BusIndex { get; set; }
        public int QueuedEvents { get; set; }
        public long TotalProcessed { get; set; }
    }

    /// <summary>
    /// Event args for machine event processing
    /// </summary>
    public class MachineEventProcessedEventArgs : EventArgs
    {
        public string MachineId { get; set; } = "";
        public string EventName { get; set; } = "";
        public object? EventData { get; set; }
        public string OldState { get; set; } = "";
        public string NewState { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Event args for machine event failures
    /// </summary>
    public class MachineEventFailedEventArgs : EventArgs
    {
        public string MachineId { get; set; } = "";
        public string EventName { get; set; } = "";
        public object? EventData { get; set; }
        public Exception Exception { get; set; } = null!;
        public DateTime Timestamp { get; set; }
    }

    #endregion
}