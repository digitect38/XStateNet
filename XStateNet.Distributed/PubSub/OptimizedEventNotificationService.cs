using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using XStateNet;
using XStateNet.Distributed.Core;
using XStateNet.Distributed.EventBus;
using XStateNet.Distributed.PubSub;

namespace XStateNet.Distributed.PubSub.Optimized
{
    /// <summary>
    /// Highly optimized event notification service with minimal allocations
    /// Uses object pooling, lock-free collections, and async batching
    /// </summary>
    public sealed class OptimizedEventNotificationService : IDisposable
    {
        private readonly IStateMachine _stateMachine;
        private readonly IStateMachineEventBus _eventBus;
        private readonly ILogger<OptimizedEventNotificationService>? _logger;
        private readonly string _machineId;

        // Object pools to reduce allocations
        private readonly Microsoft.Extensions.ObjectPool.ObjectPool<StateMachineEvent> _eventPool;
        private readonly Microsoft.Extensions.ObjectPool.ObjectPool<StateChangeEvent> _stateChangePool;
        private readonly Microsoft.Extensions.ObjectPool.ObjectPool<List<StateMachineEvent>> _listPool;
        private readonly ArrayPool<byte> _byteArrayPool = ArrayPool<byte>.Shared;

        // Lock-free collections
        private readonly ConcurrentDictionary<string, SubscriptionList> _subscriptions = new();
        private readonly ConcurrentQueue<EventWorkItem> _eventQueue = new();

        // Channels for async processing
        private readonly Channel<EventBatch> _batchChannel;
        private readonly Channel<StateMachineEvent> _publishChannel;

        // Performance counters
        private long _eventsPublished;
        private long _eventsDropped;
        private long _batchesSent;

        // Background processing
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _processingTask;
        private readonly Task _batchingTask;

        // Configuration
        private readonly EventServiceOptions _options;

        private volatile bool _isStarted;
        private volatile bool _disposed;
        private string? _previousState;

        public OptimizedEventNotificationService(
            IStateMachine stateMachine,
            IStateMachineEventBus eventBus,
            string? machineId = null,
            EventServiceOptions? options = null,
            ILogger<OptimizedEventNotificationService>? logger = null)
        {
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _machineId = machineId ?? stateMachine.machineId ?? Guid.NewGuid().ToString();
            _logger = logger;
            _options = options ?? new EventServiceOptions();

            // Initialize object pools
            var poolProvider = new DefaultObjectPoolProvider();
            _eventPool = poolProvider.Create(new StateMachineEventPooledObjectPolicy());
            _stateChangePool = poolProvider.Create(new StateChangeEventPooledObjectPolicy());
            _listPool = poolProvider.Create(new ListPooledObjectPolicy<StateMachineEvent>());

            // Create channels with bounded capacity for backpressure
            _batchChannel = Channel.CreateBounded<EventBatch>(new BoundedChannelOptions(_options.BatchChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = true
            });

            _publishChannel = Channel.CreateBounded<StateMachineEvent>(new BoundedChannelOptions(_options.PublishChannelCapacity)
            {
                FullMode = _options.DropEventsWhenFull ? BoundedChannelFullMode.DropOldest : BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = true
            });

            // Start background processors
            _processingTask = ProcessEventsAsync(_cts.Token);
            _batchingTask = ProcessBatchesAsync(_cts.Token);
        }

        #region Public Methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async ValueTask StartAsync()
        {
            if (_isStarted) return;

            await _eventBus.ConnectAsync();
            WireUpStateMachineEventsOptimized();
            _isStarted = true;

            _logger?.LogInformation("Optimized EventNotificationService started for machine {MachineId}", _machineId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async ValueTask StopAsync()
        {
            if (!_isStarted) return;

            _isStarted = false;
            UnwireStateMachineEvents();

            // Flush remaining events
            await FlushAsync();

            await _eventBus.DisconnectAsync();
            _logger?.LogInformation("Optimized EventNotificationService stopped. Published: {Published}, Dropped: {Dropped}",
                _eventsPublished, _eventsDropped);
        }

        /// <summary>
        /// Publish event with zero allocation for common cases
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask PublishStateChangeAsync(string? oldState, string newState, string? transition)
        {
            if (!_isStarted) return ValueTask.CompletedTask;

            var evt = _stateChangePool.Get();
            try
            {
                evt.SourceMachineId = _machineId;
                evt.OldState = oldState;
                evt.NewState = newState;
                evt.Transition = transition;
                evt.Timestamp = DateTime.UtcNow;

                return PublishEventInternalAsync(evt);
            }
            catch
            {
                _stateChangePool.Return(evt);
                throw;
            }
        }

        /// <summary>
        /// Publish error event
        /// </summary>
        public async ValueTask PublishErrorAsync(Exception exception, string? context = null)
        {
            if (!_isStarted) return;

            var evt = _eventPool.Get();
            try
            {
                evt.EventName = "Error";
                evt.SourceMachineId = _machineId;
                evt.Payload = new
                {
                    ErrorMessage = exception.Message,
                    ErrorType = exception.GetType().Name,
                    StackTrace = exception.StackTrace,
                    Context = context
                };
                evt.Timestamp = DateTime.UtcNow;

                await PublishEventInternalAsync(evt);
            }
            catch
            {
                _eventPool.Return(evt);
                throw;
            }
        }

        /// <summary>
        /// High-performance batch publish
        /// </summary>
        public async ValueTask PublishBatchAsync(IReadOnlyList<StateMachineEvent> events)
        {
            if (!_isStarted || events.Count == 0) return;

            var batch = new EventBatch
            {
                Events = events,
                Timestamp = DateTime.UtcNow
            };

            await _batchChannel.Writer.WriteAsync(batch);
            Interlocked.Increment(ref _batchesSent);
        }

        /// <summary>
        /// Subscribe with minimal overhead
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IDisposable SubscribeFast(string pattern, Action<StateMachineEvent> handler)
        {
            var subscription = new FastSubscription(pattern, handler);

            var list = _subscriptions.GetOrAdd(pattern, _ => new SubscriptionList());
            list.Add(subscription);

            return new SubscriptionDisposable(() =>
            {
                list.Remove(subscription);
                if (list.Count == 0)
                {
                    _subscriptions.TryRemove(pattern, out _);
                }
            });
        }

        /// <summary>
        /// Flush all pending events
        /// </summary>
        public async Task FlushAsync()
        {
            _publishChannel.Writer.TryComplete();
            _batchChannel.Writer.TryComplete();

            await Task.WhenAll(_processingTask, _batchingTask);
        }

        /// <summary>
        /// Create an event aggregator for batch processing
        /// </summary>
        public EventAggregator<T> CreateAggregator<T>(
            TimeSpan window,
            int maxBatchSize,
            Action<List<T>> batchHandler) where T : StateMachineEvent
        {
            return new EventAggregator<T>(window, maxBatchSize, batchHandler);
        }

        #endregion

        #region Background Processing

        private async Task ProcessEventsAsync(CancellationToken cancellationToken)
        {
            var buffer = _listPool.Get();
            try
            {
                await foreach (var evt in _publishChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    buffer.Add(evt);

                    // Batch events for efficient processing
                    if (buffer.Count >= _options.BatchSize ||
                        !_publishChannel.Reader.TryPeek(out _))
                    {
                        await ProcessEventBatch(buffer);
                        buffer.Clear();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            finally
            {
                if (buffer.Count > 0)
                {
                    await ProcessEventBatch(buffer);
                }
                _listPool.Return(buffer);
            }
        }

        private async Task ProcessBatchesAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var batch in _batchChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    await ProcessEventBatch(batch.Events);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        private async ValueTask ProcessEventBatch(IReadOnlyList<StateMachineEvent> events)
        {
            if (events.Count == 0) return;

            // Process local subscriptions in parallel
            var tasks = new List<Task>();

            foreach (var evt in events)
            {
                // Fast path for local subscriptions
                if (_subscriptions.TryGetValue(evt.EventName, out var list))
                {
                    tasks.Add(Task.Run(() => list.Notify(evt)));
                }

                // Check wildcard subscriptions
                if (_subscriptions.TryGetValue("*", out var wildcardList))
                {
                    tasks.Add(Task.Run(() => wildcardList.Notify(evt)));
                }
            }

            // Publish to event bus
            if (_eventBus.IsConnected)
            {
                foreach (var evt in events)
                {
                    tasks.Add(PublishToEventBusAsync(evt));
                }
            }

            await Task.WhenAll(tasks);
            Interlocked.Add(ref _eventsPublished, events.Count);
        }

        private async Task PublishToEventBusAsync(StateMachineEvent evt)
        {
            try
            {
                if (evt is StateChangeEvent stateChange)
                {
                    await _eventBus.PublishStateChangeAsync(_machineId, stateChange);
                }
                else
                {
                    await _eventBus.PublishEventAsync(_machineId, evt.EventName, evt.Payload);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to publish event {EventName}", evt.EventName);
                Interlocked.Increment(ref _eventsDropped);
            }
        }

        #endregion

        #region Optimized Event Wiring

        private void WireUpStateMachineEventsOptimized()
        {
            // Wire up the StateChanged event from IStateMachine
            if (_stateMachine != null)
            {
                // Initialize previous state with current state (may be empty if not started)
                var currentState = _stateMachine.GetActiveStateString();
                _previousState = !string.IsNullOrEmpty(currentState) ? currentState : null;

                _stateMachine.StateChanged += OnStateMachineStateChanged;
                _stateMachine.ErrorOccurred += OnStateMachineError;
            }
        }

        private void UnwireStateMachineEvents()
        {
            if (_stateMachine != null)
            {
                _stateMachine.StateChanged -= OnStateMachineStateChanged;
                _stateMachine.ErrorOccurred -= OnStateMachineError;
            }
        }

        private void OnStateMachineStateChanged(string newState)
        {
            // Fire and forget with optimized processing
            _ = PublishStateChangeAsync(_previousState, newState, null);

            // Update previous state for next transition
            _previousState = newState;
        }

        private void OnStateMachineError(Exception error)
        {
            // Fire and forget
            _ = Task.Run(async () =>
            {
                try
                {
                    await PublishErrorAsync(error);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to publish error event");
                }
            });
        }

        private async void OnTransitionOptimized(CompoundState? source, StateNode? target, string? eventName)
        {
            if (source != null && target != null)
            {
                await PublishStateChangeAsync(source.Name, target.Name ?? "", eventName);
            }
        }

        private async void OnActionExecutedOptimized(string? actionName, string? stateName)
        {
            var evt = _eventPool.Get();
            evt.EventName = "ActionExecuted";
            evt.SourceMachineId = _machineId;
            evt.Payload = new { actionName, stateName };
            evt.Timestamp = DateTime.UtcNow;

            await PublishEventInternalAsync(evt);
        }

        private async void OnGuardEvaluatedOptimized(string? guardName, bool passed)
        {
            var evt = _eventPool.Get();
            evt.EventName = "GuardEvaluated";
            evt.SourceMachineId = _machineId;
            evt.Payload = new { guardName, passed };
            evt.Timestamp = DateTime.UtcNow;

            await PublishEventInternalAsync(evt);
        }

        #endregion

        #region Internal Methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async ValueTask PublishEventInternalAsync(StateMachineEvent evt)
        {
            if (!_publishChannel.Writer.TryWrite(evt))
            {
                await _publishChannel.Writer.WriteAsync(evt);
            }
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _cts.Cancel();

            try
            {
                Task.WaitAll(new[] { _processingTask, _batchingTask }, TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Timeout or cancellation during shutdown
            }

            _cts.Dispose();
            _publishChannel.Writer.TryComplete();
            _batchChannel.Writer.TryComplete();

            _logger?.LogInformation("EventNotificationService disposed. Total events: {Published}, Dropped: {Dropped}, Batches: {Batches}",
                _eventsPublished, _eventsDropped, _batchesSent);
        }

        #endregion

        #region Internal Classes

        private class EventWorkItem
        {
            public StateMachineEvent Event { get; set; } = null!;
            public TaskCompletionSource<bool>? Completion { get; set; }
        }

        private class EventBatch
        {
            public IReadOnlyList<StateMachineEvent> Events { get; set; } = null!;
            public DateTime Timestamp { get; set; }
        }

        private class FastSubscription
        {
            private readonly string _pattern;
            private readonly Action<StateMachineEvent> _handler;

            public FastSubscription(string pattern, Action<StateMachineEvent> handler)
            {
                _pattern = pattern;
                _handler = handler;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Notify(StateMachineEvent evt)
            {
                _handler(evt);
            }
        }

        private class SubscriptionList
        {
            private readonly List<FastSubscription> _subscriptions = new();
            private readonly ReaderWriterLockSlim _lock = new();

            public int Count => _subscriptions.Count;

            public void Add(FastSubscription subscription)
            {
                _lock.EnterWriteLock();
                try
                {
                    _subscriptions.Add(subscription);
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }

            public void Remove(FastSubscription subscription)
            {
                _lock.EnterWriteLock();
                try
                {
                    _subscriptions.Remove(subscription);
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Notify(StateMachineEvent evt)
            {
                _lock.EnterReadLock();
                try
                {
                    foreach (var sub in _subscriptions)
                    {
                        sub.Notify(evt);
                    }
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }

        private class SubscriptionDisposable : IDisposable
        {
            private Action? _disposeAction;

            public SubscriptionDisposable(Action disposeAction)
            {
                _disposeAction = disposeAction;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _disposeAction, null)?.Invoke();
            }
        }

        #endregion
    }

    #region Configuration

    public class EventServiceOptions
    {
        /// <summary>
        /// Size of event batches for processing (default: 100)
        /// </summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>
        /// Capacity of the batch channel (default: 1000)
        /// </summary>
        public int BatchChannelCapacity { get; set; } = 1000;

        /// <summary>
        /// Capacity of the publish channel (default: 10000)
        /// </summary>
        public int PublishChannelCapacity { get; set; } = 10000;

        /// <summary>
        /// Drop oldest events when channel is full (default: false)
        /// </summary>
        public bool DropEventsWhenFull { get; set; } = false;

        /// <summary>
        /// Maximum time to wait for batch accumulation (default: 100ms)
        /// </summary>
        public TimeSpan BatchTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
    }

    #endregion

    #region Object Pool Policies

    internal class StateMachineEventPooledObjectPolicy : PooledObjectPolicy<StateMachineEvent>
    {
        public override StateMachineEvent Create()
        {
            return new StateMachineEvent();
        }

        public override bool Return(StateMachineEvent obj)
        {
            // Reset the object for reuse
            obj.EventId = Guid.NewGuid().ToString();
            obj.EventName = string.Empty;
            obj.SourceMachineId = string.Empty;
            obj.TargetMachineId = null;
            obj.Payload = null;
            obj.Headers.Clear();
            obj.Timestamp = DateTime.UtcNow;
            obj.Version = 1;
            obj.CorrelationId = null;
            obj.CausationId = null;

            return true;
        }
    }

    internal class StateChangeEventPooledObjectPolicy : PooledObjectPolicy<StateChangeEvent>
    {
        public override StateChangeEvent Create()
        {
            return new StateChangeEvent();
        }

        public override bool Return(StateChangeEvent obj)
        {
            // Reset the object for reuse
            obj.EventId = Guid.NewGuid().ToString();
            obj.SourceMachineId = string.Empty;
            obj.TargetMachineId = null;
            obj.OldState = null;
            obj.NewState = string.Empty;
            obj.Transition = null;
            obj.Context?.Clear();
            obj.Duration = null;
            obj.Headers.Clear();
            obj.Timestamp = DateTime.UtcNow;

            return true;
        }
    }

    internal class ListPooledObjectPolicy<T> : PooledObjectPolicy<List<T>>
    {
        public override List<T> Create()
        {
            return new List<T>(capacity: 100);
        }

        public override bool Return(List<T> obj)
        {
            obj.Clear();
            return obj.Capacity < 1000; // Don't pool if list grew too large
        }
    }

    #endregion
}