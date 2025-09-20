using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using XStateNet;

namespace XStateNet.Distributed.EventBus.Optimized
{
    /// <summary>
    /// High-performance lock-free in-memory event bus with enhanced safety and monitoring
    /// </summary>
    public sealed class EnhancedOptimizedInMemoryEventBus : IStateMachineEventBus, IDisposable
    {
        private readonly ILogger<EnhancedOptimizedInMemoryEventBus>? _logger;
        private readonly EventBusOptions _options;

        // Lock-free data structures
        private readonly ConcurrentDictionary<string, SubscriptionSet> _topicSubscriptions = new();
        private readonly ConcurrentDictionary<string, PatternSubscriptionSet> _patternSubscriptions = new();
        private readonly ConcurrentDictionary<string, RequestHandler> _requestHandlers = new();
        private readonly ConcurrentDictionary<string, PendingRequest> _pendingRequests = new();

        // Object pools with monitoring
        private readonly MonitoredExpandableObjectPool<StateMachineEvent> _eventPool;
        private readonly Microsoft.Extensions.ObjectPool.ObjectPool<List<ISubscription>> _listPool;
        private readonly ArrayPool<byte> _byteArrayPool = ArrayPool<byte>.Shared;

        // Channels with bounded options for safety
        private readonly Channel<PublishWorkItem> _publishChannel;
        private readonly Channel<BroadcastWorkItem> _broadcastChannel;
        private readonly Channel<PublishWorkItem> _criticalChannel; // High priority channel

        // Circuit breaker for fault tolerance
        private readonly CircuitBreaker _circuitBreaker;

        // Rate limiting
        private readonly RateLimiter _rateLimiter;

        // Dead letter queue for failed messages
        private readonly DeadLetterQueue _deadLetterQueue;

        // Performance metrics with detailed tracking
        private readonly PerformanceMetrics _metrics;
        private readonly Meter _meter;
        private readonly Counter<long> _eventsPublishedCounter;
        private readonly Counter<long> _eventsDeliveredCounter;
        private readonly Counter<long> _eventsFailedCounter;
        private readonly Histogram<double> _eventProcessingLatency;
        private readonly ObservableGauge<long> _activeSubscriptionsGauge;
        private readonly ObservableGauge<long> _queueDepthGauge;

        // Health monitoring
        private readonly HealthMonitor _healthMonitor;

        // Background processing
        private readonly CancellationTokenSource _cts = new();
        private readonly Task[] _processingTasks;
        private readonly Task _monitoringTask;
        private readonly Task _cleanupTask;

        private volatile int _isConnected;
        private volatile int _disposed;

        public bool IsConnected => _isConnected == 1;

        public event EventHandler<EventBusConnectedEventArgs>? Connected;
        public event EventHandler<EventBusDisconnectedEventArgs>? Disconnected;
        public event EventHandler<EventBusErrorEventArgs>? ErrorOccurred;

        public EnhancedOptimizedInMemoryEventBus(
            ILogger<EnhancedOptimizedInMemoryEventBus>? logger = null,
            EventBusOptions? options = null)
        {
            _logger = logger;
            _options = options ?? EventBusOptions.Default;

            // Initialize metrics
            _meter = new Meter("XStateNet.EventBus", "1.0.0");
            _eventsPublishedCounter = _meter.CreateCounter<long>("events_published_total");
            _eventsDeliveredCounter = _meter.CreateCounter<long>("events_delivered_total");
            _eventsFailedCounter = _meter.CreateCounter<long>("events_failed_total");
            _eventProcessingLatency = _meter.CreateHistogram<double>("event_processing_latency_ms");
            _activeSubscriptionsGauge = _meter.CreateObservableGauge("active_subscriptions", 
                () => _topicSubscriptions.Sum(kvp => kvp.Value.Count) + 
                      _patternSubscriptions.Sum(kvp => kvp.Value.Count));
            _queueDepthGauge = _meter.CreateObservableGauge("queue_depth", 
                () => _publishChannel.Reader.Count + _broadcastChannel.Reader.Count);

            _metrics = new PerformanceMetrics();
            _healthMonitor = new HealthMonitor(_logger);
            _circuitBreaker = new CircuitBreaker(_options.CircuitBreakerThreshold, _options.CircuitBreakerTimeout);
            _rateLimiter = new RateLimiter(_options.RateLimitPerSecond);
            _deadLetterQueue = new DeadLetterQueue(_options.MaxDeadLetterQueueSize, _logger);

            // Initialize monitored object pool
            _eventPool = new MonitoredExpandableObjectPool<StateMachineEvent>(
                () => new StateMachineEvent(),
                evt =>
                {
                    evt.EventId = Guid.NewGuid().ToString();
                    evt.EventName = string.Empty;
                    evt.SourceMachineId = string.Empty;
                    evt.TargetMachineId = null;
                    evt.Payload = null;
                    evt.Headers.Clear();
                    evt.Timestamp = default;
                    evt.CorrelationId = null;
                    evt.CausationId = null;
                    return true;
                },
                _metrics,
                logger: _logger,
                initialSize: _options.InitialPoolSize,
                maxSize: _options.MaxPoolSize);

            var poolProvider = new DefaultObjectPoolProvider();
            _listPool = poolProvider.Create(new ListPoolPolicy());

            // Create channels with bounded options for safety
            var publishChannelOptions = new BoundedChannelOptions(_options.MaxQueueSize)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            };

            _publishChannel = Channel.CreateBounded<PublishWorkItem>(publishChannelOptions);
            _broadcastChannel = Channel.CreateBounded<BroadcastWorkItem>(publishChannelOptions);
            
            // Critical channel with smaller buffer for high priority events
            var criticalChannelOptions = new BoundedChannelOptions(_options.MaxQueueSize / 10)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = true // Allow immediate processing
            };
            _criticalChannel = Channel.CreateBounded<PublishWorkItem>(criticalChannelOptions);

            // Start worker tasks
            int workerCount = _options.WorkerCount;
            _processingTasks = new Task[workerCount * 3]; // Include critical channel workers
            for (int i = 0; i < workerCount; i++)
            {
                _processingTasks[i * 3] = Task.Run(() => ProcessPublishQueueAsync(_cts.Token));
                _processingTasks[i * 3 + 1] = Task.Run(() => ProcessBroadcastQueueAsync(_cts.Token));
                _processingTasks[i * 3 + 2] = Task.Run(() => ProcessCriticalQueueAsync(_cts.Token));
            }

            // Start monitoring and cleanup tasks
            _monitoringTask = Task.Run(() => MonitorHealthAsync(_cts.Token));
            _cleanupTask = Task.Run(() => CleanupExpiredRequestsAsync(_cts.Token));
        }

        #region Connection Management

        public async Task ConnectAsync()
        {
            if (Interlocked.CompareExchange(ref _isConnected, 1, 0) == 0)
            {
                _healthMonitor.RecordConnectionEstablished();
                
                Connected?.Invoke(this, new EventBusConnectedEventArgs
                {
                    Endpoint = "memory://enhanced-optimized",
                    ConnectedAt = DateTime.UtcNow
                });

                _logger?.LogInformation("EnhancedOptimizedInMemoryEventBus connected with enhanced safety features");

                // Warm up the pools
                await WarmupPoolsAsync();
            }
        }

        private async Task WarmupPoolsAsync()
        {
            var warmupTasks = new List<Task>();
            
            // Pre-allocate some objects in the pool
            for (int i = 0; i < _options.InitialPoolSize / 2; i++)
            {
                warmupTasks.Add(Task.Run(() =>
                {
                    var evt = _eventPool.Get();
                    _eventPool.Return(evt);
                }));
            }

            await Task.WhenAll(warmupTasks);
            _logger?.LogDebug("Object pools warmed up successfully");
        }

        #endregion

        #region Publishing with Safety Features

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task PublishStateChangeAsync(string machineId, StateChangeEvent evt)
        {
            if (_isConnected == 0) return;

            // Check circuit breaker
            if (!_circuitBreaker.IsOpen)
            {
                try
                {
                    evt.SourceMachineId = machineId;
                    await PublishInternalAsync($"state.{machineId}", evt, PublishMode.Topic, EventPriority.Normal);
                    _circuitBreaker.RecordSuccess();
                }
                catch (Exception ex)
                {
                    _circuitBreaker.RecordFailure();
                    await HandlePublishFailureAsync(evt, ex);
                }
            }
            else
            {
                await _deadLetterQueue.EnqueueAsync(evt);
                _logger?.LogWarning("Circuit breaker is open, event sent to dead letter queue");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task PublishEventAsync(string targetMachineId, string eventName, object? payload = null)
        {
            if (_isConnected == 0) return;

            // Apply rate limiting
            if (!await _rateLimiter.TryAcquireAsync())
            {
                _metrics.RecordRateLimitHit();
                _logger?.LogWarning("Rate limit exceeded for event {EventName}", eventName);
                return;
            }

            var evt = _eventPool.Get();
            try
            {
                evt.EventName = eventName;
                evt.TargetMachineId = targetMachineId;
                evt.Payload = payload;
                evt.Timestamp = DateTime.UtcNow;

                await PublishInternalAsync($"machine.{targetMachineId}", evt, PublishMode.Topic, EventPriority.Normal);
            }
            catch (Exception ex)
            {
                _eventPool.Return(evt);
                await HandlePublishFailureAsync(evt, ex);
                throw;
            }
        }

        public async Task PublishCriticalEventAsync(string targetMachineId, string eventName, object? payload = null)
        {
            if (_isConnected == 0) return;

            var evt = _eventPool.Get();
            evt.EventName = eventName;
            evt.TargetMachineId = targetMachineId;
            evt.Payload = payload;
            evt.Timestamp = DateTime.UtcNow;
            evt.Headers["Priority"] = "Critical";

            await PublishInternalAsync($"machine.{targetMachineId}", evt, PublishMode.Topic, EventPriority.Critical);
        }

        private async Task HandlePublishFailureAsync(object evt, Exception ex)
        {
            _eventsFailedCounter.Add(1);
            _metrics.RecordFailure();
            
            ErrorOccurred?.Invoke(this, new EventBusErrorEventArgs
            {
                Error = ex,
                Message = $"Failed to publish event: {ex.Message}",
                Timestamp = DateTime.UtcNow
            });

            await _deadLetterQueue.EnqueueAsync(evt);
            _logger?.LogError(ex, "Failed to publish event, moved to dead letter queue");
        }

        #endregion

        #region Enhanced Processing with Safety

        private async Task ProcessPublishQueueAsync(CancellationToken cancellationToken)
        {
            var buffer = new List<PublishWorkItem>(100);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await foreach (var item in _publishChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    buffer.Add(item);

                    // Batch processing with timeout protection
                    if (buffer.Count >= _options.BatchSize || 
                        stopwatch.ElapsedMilliseconds > _options.BatchTimeoutMs ||
                        !_publishChannel.Reader.TryPeek(out _))
                    {
                        await ProcessPublishBatchSafelyAsync(buffer);
                        buffer.Clear();
                        stopwatch.Restart();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unexpected error in publish queue processing");
                _healthMonitor.RecordError(ex);
            }
            finally
            {
                if (buffer.Count > 0)
                {
                    await ProcessPublishBatchSafelyAsync(buffer);
                }
            }
        }

        private async Task ProcessCriticalQueueAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var item in _criticalChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    // Process critical events immediately, one by one
                    await ProcessCriticalEventAsync(item);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unexpected error in critical queue processing");
                _healthMonitor.RecordError(ex);
            }
        }

        private async Task ProcessCriticalEventAsync(PublishWorkItem item)
        {
            var stopwatch = Stopwatch.StartNew();
            var subscriptionsToNotify = _listPool.Get();

            try
            {
                GatherSubscriptions(item.Topic, subscriptionsToNotify);
                
                // Process with timeout protection
                using var cts = new CancellationTokenSource(_options.CriticalEventTimeoutMs);
                await NotifySubscriptionsAsync(subscriptionsToNotify, item.Event, cts.Token);

                _metrics.RecordEventProcessed(stopwatch.ElapsedMilliseconds, true);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to process critical event");
                _metrics.RecordFailure();
                await _deadLetterQueue.EnqueueAsync(item.Event);
            }
            finally
            {
                _listPool.Return(subscriptionsToNotify);
                if (item.ReturnToPool)
                {
                    _eventPool.Return(item.Event);
                }
            }
        }

        private async Task ProcessPublishBatchSafelyAsync(List<PublishWorkItem> items)
        {
            var subscriptionsToNotify = _listPool.Get();
            var eventsToReturn = new List<StateMachineEvent>();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                foreach (var item in items)
                {
                    try
                    {
                        subscriptionsToNotify.Clear();
                        GatherSubscriptions(item.Topic, subscriptionsToNotify);

                        // Notify with timeout protection
                        using var cts = new CancellationTokenSource(_options.EventProcessingTimeoutMs);
                        await NotifySubscriptionsAsync(subscriptionsToNotify, item.Event, cts.Token);

                        if (item.ReturnToPool)
                        {
                            eventsToReturn.Add(item.Event);
                        }

                        _eventsPublishedCounter.Add(1);
                        _metrics.RecordEventProcessed(stopwatch.ElapsedMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to process event in batch");
                        _eventsFailedCounter.Add(1);
                        await _deadLetterQueue.EnqueueAsync(item.Event);
                    }
                }

                // Return events to pool
                foreach (var evt in eventsToReturn)
                {
                    _eventPool.Return(evt);
                }

                _eventProcessingLatency.Record(stopwatch.ElapsedMilliseconds);
            }
            finally
            {
                _listPool.Return(subscriptionsToNotify);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void GatherSubscriptions(string topic, List<ISubscription> subscriptions)
        {
            // Direct topic match
            if (_topicSubscriptions.TryGetValue(topic, out var topicSubs))
            {
                topicSubs.CopyTo(subscriptions);
            }

            // Pattern matches
            foreach (var kvp in _patternSubscriptions)
            {
                if (kvp.Value.Matches(topic))
                {
                    kvp.Value.CopyTo(subscriptions);
                }
            }
        }

        private async Task NotifySubscriptionsAsync(
            List<ISubscription> subscriptions, 
            StateMachineEvent evt, 
            CancellationToken cancellationToken)
        {
            var tasks = new List<Task>(subscriptions.Count);

            foreach (var sub in subscriptions)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await sub.NotifyAsync(evt, cancellationToken);
                        _eventsDeliveredCounter.Add(1);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger?.LogWarning("Subscription notification timed out");
                        _metrics.RecordTimeout();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error notifying subscription");
                        _eventsFailedCounter.Add(1);
                        _healthMonitor.RecordSubscriptionError(sub.Id, ex);
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
        }

        #endregion

        #region Health Monitoring

        private async Task MonitorHealthAsync(CancellationToken cancellationToken)
        {
            var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.HealthCheckIntervalSeconds));

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await timer.WaitForNextTickAsync(cancellationToken);

                    var health = await PerformHealthCheckAsync();
                    _healthMonitor.UpdateHealth(health);

                    if (health.Status == HealthStatus.Critical)
                    {
                        _logger?.LogError("Event bus health is critical: {Reasons}", 
                            string.Join(", ", health.Issues));
                        
                        // Trigger self-healing if configured
                        if (_options.EnableSelfHealing)
                        {
                            await AttemptSelfHealingAsync(health);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during health monitoring");
                }
            }

            timer.Dispose();
        }

        private async Task<HealthCheckResult> PerformHealthCheckAsync()
        {
            var result = new HealthCheckResult();

            // Check queue depths
            var publishQueueDepth = _publishChannel.Reader.Count;
            var broadcastQueueDepth = _broadcastChannel.Reader.Count;
            var criticalQueueDepth = _criticalChannel.Reader.Count;

            if (publishQueueDepth > _options.MaxQueueSize * 0.9)
            {
                result.Issues.Add($"Publish queue near capacity: {publishQueueDepth}/{_options.MaxQueueSize}");
                result.Status = HealthStatus.Warning;
            }

            if (criticalQueueDepth > _options.MaxQueueSize / 10 * 0.5)
            {
                result.Issues.Add($"Critical queue backlog: {criticalQueueDepth}");
                result.Status = HealthStatus.Critical;
            }

            // Check circuit breaker
            if (_circuitBreaker.IsOpen)
            {
                result.Issues.Add("Circuit breaker is open");
                result.Status = HealthStatus.Critical;
            }

            // Check memory pressure
            var memoryUsage = GC.GetTotalMemory(false);
            if (memoryUsage > _options.MaxMemoryBytes)
            {
                result.Issues.Add($"Memory usage exceeded: {memoryUsage / 1_000_000}MB");
                result.Status = HealthStatus.Warning;
                
                // Force GC if critical
                if (memoryUsage > _options.MaxMemoryBytes * 1.5)
                {
                    GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                    result.Status = HealthStatus.Critical;
                }
            }

            // Check dead letter queue
            if (_deadLetterQueue.Count > _options.MaxDeadLetterQueueSize * 0.8)
            {
                result.Issues.Add($"Dead letter queue filling up: {_deadLetterQueue.Count}");
                result.Status = HealthStatus.Warning;
            }

            // Check pool health
            var poolHealth = _eventPool.GetHealth();
            if (poolHealth.UtilizationPercent > 90)
            {
                result.Issues.Add($"Object pool near exhaustion: {poolHealth.UtilizationPercent}%");
                result.Status = HealthStatus.Warning;
            }

            result.Timestamp = DateTime.UtcNow;
            result.Metrics = _metrics.GetSnapshot();

            return result;
        }

        private async Task AttemptSelfHealingAsync(HealthCheckResult health)
        {
            _logger?.LogInformation("Attempting self-healing for issues: {Issues}", 
                string.Join(", ", health.Issues));

            foreach (var issue in health.Issues)
            {
                if (issue.Contains("queue near capacity"))
                {
                    // Temporarily increase worker threads
                    await AddTemporaryWorkersAsync(2, TimeSpan.FromMinutes(5));
                }
                else if (issue.Contains("Memory usage"))
                {
                    // Force cleanup
                    GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                    _eventPool.Compact();
                }
                else if (issue.Contains("Dead letter queue"))
                {
                    // Try to reprocess dead letters
                    await ReprocessDeadLettersAsync(10);
                }
                else if (issue.Contains("Circuit breaker"))
                {
                    // Wait and retry
                    await Task.Delay(TimeSpan.FromSeconds(30));
                    _circuitBreaker.Reset();
                }
            }
        }

        private async Task AddTemporaryWorkersAsync(int count, TimeSpan duration)
        {
            var temporaryTasks = new Task[count];
            using var cts = new CancellationTokenSource(duration);

            for (int i = 0; i < count; i++)
            {
                temporaryTasks[i] = Task.Run(() => ProcessPublishQueueAsync(cts.Token));
            }

            _logger?.LogInformation("Added {Count} temporary workers for {Duration}", count, duration);
            await Task.WhenAll(temporaryTasks);
            _logger?.LogInformation("Temporary workers completed");
        }

        private async Task ReprocessDeadLettersAsync(int maxCount)
        {
            var reprocessed = 0;
            
            while (reprocessed < maxCount && _deadLetterQueue.TryDequeue(out var item))
            {
                try
                {
                    if (item is StateMachineEvent evt)
                    {
                        await PublishInternalAsync($"reprocess.{evt.EventName}", evt, 
                            PublishMode.Topic, EventPriority.Normal);
                        reprocessed++;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to reprocess dead letter");
                    _deadLetterQueue.Enqueue(item);
                    break;
                }
            }

            if (reprocessed > 0)
            {
                _logger?.LogInformation("Reprocessed {Count} dead letter items", reprocessed);
            }
        }

        #endregion

        #region Cleanup and Maintenance

        private async Task CleanupExpiredRequestsAsync(CancellationToken cancellationToken)
        {
            var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await timer.WaitForNextTickAsync(cancellationToken);

                    var now = DateTime.UtcNow;
                    var expiredRequests = _pendingRequests
                        .Where(kvp => kvp.Value.Timeout < now)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var correlationId in expiredRequests)
                    {
                        if (_pendingRequests.TryRemove(correlationId, out var request))
                        {
                            request.Completion.TrySetCanceled();
                            _metrics.RecordTimeout();
                            _logger?.LogDebug("Cleaned up expired request: {CorrelationId}", correlationId);
                        }
                    }

                    // Compact pools if needed
                    if (_metrics.GetSnapshot().MemoryUsageBytes > _options.MaxMemoryBytes * 0.8)
                    {
                        _eventPool.Compact();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during cleanup");
                }
            }

            timer.Dispose();
        }

        #endregion

        #region Internal Methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task PublishInternalAsync(
            string topic, 
            StateMachineEvent evt, 
            PublishMode mode, 
            EventPriority priority)
        {
            var workItem = new PublishWorkItem
            {
                Topic = topic,
                Event = evt,
                Mode = mode,
                Priority = priority,
                ReturnToPool = mode != PublishMode.External,
                Timestamp = DateTime.UtcNow
            };

            var channel = priority == EventPriority.Critical ? _criticalChannel : _publishChannel;
            
            if (!channel.Writer.TryWrite(workItem))
            {
                // Use timeout to prevent indefinite blocking
                using var cts = new CancellationTokenSource(_options.PublishTimeoutMs);
                try
                {
                    await channel.Writer.WriteAsync(workItem, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    _metrics.RecordTimeout();
                    throw new TimeoutException($"Failed to publish event within {_options.PublishTimeoutMs}ms");
                }
            }
        }

        #endregion

        #region Metrics and Diagnostics

        public EventBusMetrics GetMetrics()
        {
            return _metrics.GetSnapshot();
        }

        public async Task<DiagnosticReport> GenerateDiagnosticReportAsync()
        {
            var report = new DiagnosticReport
            {
                Timestamp = DateTime.UtcNow,
                Metrics = GetMetrics(),
                Health = await PerformHealthCheckAsync(),
                PoolStatistics = _eventPool.GetHealth(),
                DeadLetterQueueSize = _deadLetterQueue.Count,
                CircuitBreakerState = _circuitBreaker.IsOpen ? "Open" : "Closed",
                TopicStatistics = GenerateTopicStatistics(),
                WorkerThreads = _processingTasks.Count(t => !t.IsCompleted)
            };

            return report;
        }

        private Dictionary<string, TopicStatistics> GenerateTopicStatistics()
        {
            var stats = new Dictionary<string, TopicStatistics>();

            foreach (var kvp in _topicSubscriptions)
            {
                stats[kvp.Key] = new TopicStatistics
                {
                    SubscriberCount = kvp.Value.Count,
                    LastPublishTime = _metrics.GetTopicLastPublishTime(kvp.Key)
                };
            }

            return stats;
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            _cts.Cancel();
            DisconnectAsync().GetAwaiter().GetResult();

            try
            {
                Task.WaitAll(_processingTasks.Concat(new[] { _monitoringTask, _cleanupTask }).ToArray(), 
                    TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Timeout or cancellation during shutdown
            }

            _cts.Dispose();
            _meter.Dispose();

            var finalMetrics = GetMetrics();
            _logger?.LogInformation(
                "EnhancedOptimizedInMemoryEventBus disposed. " +
                "Total events: {Published}, Delivered: {Delivered}, Failed: {Failed}, " +
                "Uptime: {Uptime}, Peak throughput: {PeakThroughput}/s",
                finalMetrics.TotalEventsPublished, 
                finalMetrics.TotalEventsDelivered, 
                finalMetrics.TotalEventsFailed,
                finalMetrics.Uptime, 
                finalMetrics.PeakThroughputPerSecond);
        }

        #endregion
    }

    #region Supporting Types

    public class EventBusOptions
    {
        public int WorkerCount { get; set; } = 4;
        public int MaxQueueSize { get; set; } = 100_000;
        public int BatchSize { get; set; } = 100;
        public int BatchTimeoutMs { get; set; } = 10;
        public int InitialPoolSize { get; set; } = 100;
        public int MaxPoolSize { get; set; } = 10_000;
        public int CircuitBreakerThreshold { get; set; } = 50;
        public TimeSpan CircuitBreakerTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public int RateLimitPerSecond { get; set; } = 100_000;
        public int MaxDeadLetterQueueSize { get; set; } = 10_000;
        public int EventProcessingTimeoutMs { get; set; } = 5000;
        public int CriticalEventTimeoutMs { get; set; } = 1000;
        public int PublishTimeoutMs { get; set; } = 1000;
        public long MaxMemoryBytes { get; set; } = 500_000_000; // 500MB
        public int HealthCheckIntervalSeconds { get; set; } = 10;
        public bool EnableSelfHealing { get; set; } = true;

        public static EventBusOptions Default => new();
    }

    public enum EventPriority
    {
        Normal,
        High,
        Critical
    }

    public enum PublishMode
    {
        Topic,
        Group,
        Request,
        External
    }

    public struct PublishWorkItem
    {
        public string Topic;
        public StateMachineEvent Event;
        public PublishMode Mode;
        public EventPriority Priority;
        public bool ReturnToPool;
        public DateTime Timestamp;
    }

    public struct BroadcastWorkItem
    {
        public string EventName;
        public object? Payload;
        public string? Filter;
        public DateTime Timestamp;
    }

    #endregion

    #region Circuit Breaker

    public class CircuitBreaker
    {
        private readonly int _threshold;
        private readonly TimeSpan _timeout;
        private int _failureCount;
        private DateTime _lastFailureTime;
        private volatile bool _isOpen;

        public bool IsOpen => _isOpen;

        public CircuitBreaker(int threshold, TimeSpan timeout)
        {
            _threshold = threshold;
            _timeout = timeout;
        }

        public void RecordSuccess()
        {
            Interlocked.Exchange(ref _failureCount, 0);
        }

        public void RecordFailure()
        {
            var count = Interlocked.Increment(ref _failureCount);
            _lastFailureTime = DateTime.UtcNow;

            if (count >= _threshold)
            {
                _isOpen = true;
                Task.Delay(_timeout).ContinueWith(_ => Reset());
            }
        }

        public void Reset()
        {
            _isOpen = false;
            Interlocked.Exchange(ref _failureCount, 0);
        }
    }

    #endregion

    #region Rate Limiter

    public class RateLimiter
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly int _limit;
        private readonly Timer _resetTimer;

        public RateLimiter(int limitPerSecond)
        {
            _limit = limitPerSecond;
            _semaphore = new SemaphoreSlim(limitPerSecond, limitPerSecond);
            _resetTimer = new Timer(Reset, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public async Task<bool> TryAcquireAsync()
        {
            return await _semaphore.WaitAsync(0);
        }

        private void Reset(object? state)
        {
            var currentCount = _semaphore.CurrentCount;
            if (currentCount < _limit)
            {
                _semaphore.Release(Math.Min(_limit - currentCount, _limit));
            }
        }
    }

    #endregion

    #region Dead Letter Queue

    public class DeadLetterQueue
    {
        private readonly ConcurrentQueue<object> _queue = new();
        private readonly int _maxSize;
        private readonly ILogger? _logger;
        private int _count;

        public int Count => _count;

        public DeadLetterQueue(int maxSize, ILogger? logger)
        {
            _maxSize = maxSize;
            _logger = logger;
        }

        public async Task EnqueueAsync(object item)
        {
            if (Interlocked.Increment(ref _count) <= _maxSize)
            {
                _queue.Enqueue(item);
            }
            else
            {
                Interlocked.Decrement(ref _count);
                _logger?.LogError("Dead letter queue is full, dropping message");
            }
        }

        public void Enqueue(object item)
        {
            if (Interlocked.Increment(ref _count) <= _maxSize)
            {
                _queue.Enqueue(item);
            }
            else
            {
                Interlocked.Decrement(ref _count);
                _logger?.LogError("Dead letter queue is full, dropping message");
            }
        }

        public bool TryDequeue(out object? item)
        {
            if (_queue.TryDequeue(out item))
            {
                Interlocked.Decrement(ref _count);
                return true;
            }
            return false;
        }
    }

    #endregion

    #region Health Monitoring

    public class HealthMonitor
    {
        private readonly ILogger? _logger;
        private readonly ConcurrentDictionary<string, int> _subscriptionErrors = new();
        private volatile HealthCheckResult _lastHealth = new();
        private DateTime _startTime = DateTime.UtcNow;
        private long _totalErrors;

        public HealthMonitor(ILogger? logger)
        {
            _logger = logger;
        }

        public void RecordConnectionEstablished()
        {
            _startTime = DateTime.UtcNow;
        }

        public void RecordError(Exception ex)
        {
            Interlocked.Increment(ref _totalErrors);
            _logger?.LogError(ex, "Health monitor recorded error");
        }

        public void RecordSubscriptionError(string subscriptionId, Exception ex)
        {
            _subscriptionErrors.AddOrUpdate(subscriptionId, 1, (_, count) => count + 1);
        }

        public void UpdateHealth(HealthCheckResult health)
        {
            _lastHealth = health;
        }

        public HealthCheckResult GetLastHealth() => _lastHealth;
    }

    public class HealthCheckResult
    {
        public HealthStatus Status { get; set; } = HealthStatus.Healthy;
        public List<string> Issues { get; set; } = new();
        public DateTime Timestamp { get; set; }
        public EventBusMetrics Metrics { get; set; } = new();
    }

    public enum HealthStatus
    {
        Healthy,
        Warning,
        Critical
    }

    #endregion

    #region Performance Metrics

    public class PerformanceMetrics
    {
        private long _totalEventsPublished;
        private long _totalEventsDelivered;
        private long _totalEventsFailed;
        private long _totalTimeouts;
        private long _rateLimitHits;
        private long _peakThroughput;
        private readonly ConcurrentDictionary<string, DateTime> _topicLastPublish = new();
        private readonly DateTime _startTime = DateTime.UtcNow;

        public void RecordEventProcessed(double latencyMs, bool isCritical = false)
        {
            Interlocked.Increment(ref _totalEventsPublished);
            UpdatePeakThroughput();
        }

        public void RecordFailure()
        {
            Interlocked.Increment(ref _totalEventsFailed);
        }

        public void RecordTimeout()
        {
            Interlocked.Increment(ref _totalTimeouts);
        }

        public void RecordRateLimitHit()
        {
            Interlocked.Increment(ref _rateLimitHits);
        }

        public DateTime GetTopicLastPublishTime(string topic)
        {
            return _topicLastPublish.TryGetValue(topic, out var time) ? time : DateTime.MinValue;
        }

        private void UpdatePeakThroughput()
        {
            // Simple approximation - would need sliding window for accuracy
            var elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;
            if (elapsed > 0)
            {
                var throughput = (long)(_totalEventsPublished / elapsed);
                if (throughput > _peakThroughput)
                {
                    Interlocked.Exchange(ref _peakThroughput, throughput);
                }
            }
        }

        public EventBusMetrics GetSnapshot()
        {
            return new EventBusMetrics
            {
                TotalEventsPublished = _totalEventsPublished,
                TotalEventsDelivered = _totalEventsDelivered,
                TotalEventsFailed = _totalEventsFailed,
                TotalTimeouts = _totalTimeouts,
                RateLimitHits = _rateLimitHits,
                PeakThroughputPerSecond = _peakThroughput,
                MemoryUsageBytes = GC.GetTotalMemory(false),
                Uptime = DateTime.UtcNow - _startTime
            };
        }
    }

    public class EventBusMetrics
    {
        public long TotalEventsPublished { get; set; }
        public long TotalEventsDelivered { get; set; }
        public long TotalEventsFailed { get; set; }
        public long TotalTimeouts { get; set; }
        public long RateLimitHits { get; set; }
        public long PeakThroughputPerSecond { get; set; }
        public long MemoryUsageBytes { get; set; }
        public TimeSpan Uptime { get; set; }
    }

    #endregion

    #region Diagnostic Report

    public class DiagnosticReport
    {
        public DateTime Timestamp { get; set; }
        public EventBusMetrics Metrics { get; set; } = new();
        public HealthCheckResult Health { get; set; } = new();
        public PoolHealth PoolStatistics { get; set; } = new();
        public int DeadLetterQueueSize { get; set; }
        public string CircuitBreakerState { get; set; } = string.Empty;
        public Dictionary<string, TopicStatistics> TopicStatistics { get; set; } = new();
        public int WorkerThreads { get; set; }
    }

    public class TopicStatistics
    {
        public int SubscriberCount { get; set; }
        public DateTime LastPublishTime { get; set; }
    }

    #endregion

    #region Enhanced Object Pool

    public sealed class MonitoredExpandableObjectPool<T> where T : class
    {
        private readonly ConcurrentBag<T> _objects = new();
        private readonly Func<T> _objectGenerator;
        private readonly Func<T, bool> _resetFunc;
        private readonly PerformanceMetrics _metrics;
        private readonly ILogger? _logger;
        private int _currentSize;
        private int _rentedCount;
        private int _totalCreated;
        private readonly int _maxSize;

        public MonitoredExpandableObjectPool(
            Func<T> objectGenerator,
            Func<T, bool> resetFunc,
            PerformanceMetrics metrics,
            ILogger? logger = null,
            int initialSize = 16,
            int maxSize = 10000)
        {
            _objectGenerator = objectGenerator;
            _resetFunc = resetFunc;
            _metrics = metrics;
            _logger = logger;
            _currentSize = initialSize;
            _maxSize = maxSize;

            // Pre-populate
            for (int i = 0; i < initialSize; i++)
            {
                _objects.Add(_objectGenerator());
                Interlocked.Increment(ref _totalCreated);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get()
        {
            var rentedCount = Interlocked.Increment(ref _rentedCount);

            if (_objects.TryTake(out T? item))
            {
                return item;
            }

            // Expand if needed
            if (rentedCount > _currentSize && _currentSize < _maxSize)
            {
                ExpandPool();
                if (_objects.TryTake(out item))
                {
                    return item;
                }
            }

            // Create new if pool exhausted
            Interlocked.Increment(ref _totalCreated);
            return _objectGenerator();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Return(T item)
        {
            if (_resetFunc(item))
            {
                _objects.Add(item);
                Interlocked.Decrement(ref _rentedCount);
            }
        }

        private void ExpandPool()
        {
            lock (_objects)
            {
                if (_currentSize >= _maxSize) return;

                var oldSize = _currentSize;
                var newSize = Math.Min(_currentSize * 2, _maxSize);
                var itemsToAdd = newSize - _currentSize;

                for (int i = 0; i < itemsToAdd; i++)
                {
                    _objects.Add(_objectGenerator());
                    Interlocked.Increment(ref _totalCreated);
                }

                _currentSize = newSize;
                _logger?.LogInformation("Pool expanded from {Old} to {New}", oldSize, newSize);
            }
        }

        public void Compact()
        {
            // Remove excess objects if pool is oversized
            while (_objects.Count > _currentSize / 2 && _objects.TryTake(out var item))
            {
                // Object is removed and will be GC'd
            }
        }

        public PoolHealth GetHealth()
        {
            return new PoolHealth
            {
                CurrentSize = _currentSize,
                RentedCount = _rentedCount,
                TotalCreated = _totalCreated,
                AvailableCount = _objects.Count,
                UtilizationPercent = (_rentedCount * 100) / Math.Max(_currentSize, 1)
            };
        }
    }

    public class PoolHealth
    {
        public int CurrentSize { get; set; }
        public int RentedCount { get; set; }
        public int TotalCreated { get; set; }
        public int AvailableCount { get; set; }
        public int UtilizationPercent { get; set; }
    }

    #endregion

    #region Enhanced Subscription

    public interface ISubscription
    {
        string Id { get; }
        Task NotifyAsync(StateMachineEvent evt, CancellationToken cancellationToken);
    }

    public sealed class SafeSubscription : ISubscription
    {
        private readonly Action<StateMachineEvent> _handler;
        public string Id { get; }

        public SafeSubscription(string topic, Action<StateMachineEvent> handler)
        {
            Id = Guid.NewGuid().ToString();
            _handler = handler;
        }

        public Task NotifyAsync(StateMachineEvent evt, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _handler(evt);
            }, cancellationToken);
        }
    }

    #endregion
}