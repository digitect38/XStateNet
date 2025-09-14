using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// High-performance lock-free in-memory event bus
    /// Optimized for minimal allocations and maximum throughput
    /// </summary>
    public sealed class OptimizedInMemoryEventBus : IStateMachineEventBus, IDisposable
    {
        private readonly ILogger<OptimizedInMemoryEventBus>? _logger;

        // Lock-free data structures
        private readonly ConcurrentDictionary<string, SubscriptionSet> _topicSubscriptions = new();
        private readonly ConcurrentDictionary<string, PatternSubscriptionSet> _patternSubscriptions = new();
        private readonly ConcurrentDictionary<string, RequestHandler> _requestHandlers = new();
        private readonly ConcurrentDictionary<string, PendingRequest> _pendingRequests = new();

        // Object pools
        private readonly Microsoft.Extensions.ObjectPool.ObjectPool<StateMachineEvent> _eventPool;
        private readonly Microsoft.Extensions.ObjectPool.ObjectPool<List<ISubscription>> _listPool;
        private readonly ArrayPool<byte> _byteArrayPool = ArrayPool<byte>.Shared;

        // Channels for async processing
        private readonly Channel<PublishWorkItem> _publishChannel;
        private readonly Channel<BroadcastWorkItem> _broadcastChannel;

        // Performance metrics
        private long _totalEventsPublished;
        private long _totalEventsDelivered;
        private long _totalSubscriptions;

        // Background processing
        private readonly CancellationTokenSource _cts = new();
        private readonly Task[] _processingTasks;

        private volatile int _isConnected;
        private volatile int _disposed;

        public bool IsConnected => _isConnected == 1;

        public event EventHandler<EventBusConnectedEventArgs>? Connected;
        public event EventHandler<EventBusDisconnectedEventArgs>? Disconnected;
        public event EventHandler<EventBusErrorEventArgs>? ErrorOccurred;

        public OptimizedInMemoryEventBus(
            ILogger<OptimizedInMemoryEventBus>? logger = null,
            int workerCount = 4)
        {
            _logger = logger;

            // Initialize object pools
            var poolProvider = new DefaultObjectPoolProvider();
            _eventPool = poolProvider.Create(new EventPoolPolicy());
            _listPool = poolProvider.Create(new ListPoolPolicy());

            // Create unbounded channels for maximum performance
            _publishChannel = Channel.CreateUnbounded<PublishWorkItem>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false
            });

            _broadcastChannel = Channel.CreateUnbounded<BroadcastWorkItem>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false
            });

            // Start worker tasks
            _processingTasks = new Task[workerCount * 2];
            for (int i = 0; i < workerCount; i++)
            {
                _processingTasks[i * 2] = Task.Run(() => ProcessPublishQueueAsync(_cts.Token));
                _processingTasks[i * 2 + 1] = Task.Run(() => ProcessBroadcastQueueAsync(_cts.Token));
            }
        }

        #region Connection Management

        public Task ConnectAsync()
        {
            if (Interlocked.CompareExchange(ref _isConnected, 1, 0) == 0)
            {
                Connected?.Invoke(this, new EventBusConnectedEventArgs
                {
                    Endpoint = "memory://optimized",
                    ConnectedAt = DateTime.UtcNow
                });

                _logger?.LogInformation("OptimizedInMemoryEventBus connected");
            }

            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            if (Interlocked.CompareExchange(ref _isConnected, 0, 1) == 1)
            {
                // Signal channels to complete
                _publishChannel.Writer.TryComplete();
                _broadcastChannel.Writer.TryComplete();

                // Clear all subscriptions
                _topicSubscriptions.Clear();
                _patternSubscriptions.Clear();

                // Cancel pending requests
                foreach (var request in _pendingRequests.Values)
                {
                    request.Completion.TrySetCanceled();
                }
                _pendingRequests.Clear();

                Disconnected?.Invoke(this, new EventBusDisconnectedEventArgs
                {
                    Reason = "Manual disconnect",
                    WillReconnect = false
                });

                _logger?.LogInformation("OptimizedInMemoryEventBus disconnected. Events published: {Published}, delivered: {Delivered}",
                    _totalEventsPublished, _totalEventsDelivered);
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Publishing - Optimized

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task PublishStateChangeAsync(string machineId, StateChangeEvent evt)
        {
            if (_isConnected == 0) return Task.CompletedTask;

            evt.SourceMachineId = machineId;
            return PublishInternalAsync($"state.{machineId}", evt, PublishMode.Topic);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task PublishEventAsync(string targetMachineId, string eventName, object? payload = null)
        {
            if (_isConnected == 0) return Task.CompletedTask;

            var evt = _eventPool.Get();
            evt.EventName = eventName;
            evt.TargetMachineId = targetMachineId;
            evt.Payload = payload;
            evt.Timestamp = DateTime.UtcNow;

            return PublishInternalAsync($"machine.{targetMachineId}", evt, PublishMode.Topic);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task BroadcastAsync(string eventName, object? payload = null, string? filter = null)
        {
            if (_isConnected == 0) return Task.CompletedTask;

            var workItem = new BroadcastWorkItem
            {
                EventName = eventName,
                Payload = payload,
                Filter = filter,
                Timestamp = DateTime.UtcNow
            };

            if (!_broadcastChannel.Writer.TryWrite(workItem))
            {
                return _broadcastChannel.Writer.WriteAsync(workItem).AsTask();
            }

            return Task.CompletedTask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task PublishToGroupAsync(string groupName, string eventName, object? payload = null)
        {
            if (_isConnected == 0) return Task.CompletedTask;

            var evt = _eventPool.Get();
            evt.EventName = eventName;
            evt.Payload = payload;
            evt.Timestamp = DateTime.UtcNow;

            return PublishInternalAsync($"group.{groupName}", evt, PublishMode.Group);
        }

        #endregion

        #region Subscribing - Optimized

        public Task<IDisposable> SubscribeToMachineAsync(string machineId, Action<StateMachineEvent> handler)
        {
            return SubscribeInternalAsync($"machine.{machineId}", handler, false);
        }

        public Task<IDisposable> SubscribeToStateChangesAsync(string machineId, Action<StateChangeEvent> handler)
        {
            return SubscribeInternalAsync($"state.{machineId}", evt =>
            {
                if (evt is StateChangeEvent stateChange)
                    handler(stateChange);
            }, false);
        }

        public Task<IDisposable> SubscribeToPatternAsync(string pattern, Action<StateMachineEvent> handler)
        {
            return SubscribeInternalAsync(pattern, handler, true);
        }

        public Task<IDisposable> SubscribeToAllAsync(Action<StateMachineEvent> handler)
        {
            return SubscribeInternalAsync("*", handler, true);
        }

        public Task<IDisposable> SubscribeToGroupAsync(string groupName, Action<StateMachineEvent> handler)
        {
            return SubscribeInternalAsync($"group.{groupName}", handler, false);
        }

        #endregion

        #region Request/Response - Optimized

        public async Task<TResponse?> RequestAsync<TResponse>(
            string targetMachineId,
            string requestType,
            object? payload = null,
            TimeSpan? timeout = null)
        {
            if (_isConnected == 0) return default;

            var correlationId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            var pendingRequest = new PendingRequest
            {
                CorrelationId = correlationId,
                Completion = tcs,
                Timeout = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(30))
            };

            if (!_pendingRequests.TryAdd(correlationId, pendingRequest))
            {
                return default;
            }

            try
            {
                var request = _eventPool.Get();
                request.EventName = requestType;
                request.TargetMachineId = targetMachineId;
                request.Payload = payload;
                request.CorrelationId = correlationId;
                request.Timestamp = DateTime.UtcNow;

                await PublishInternalAsync($"request.{targetMachineId}.{requestType}", request, PublishMode.Request);

                using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
                using (cts.Token.Register(() => tcs.TrySetCanceled()))
                {
                    var result = await tcs.Task;
                    return result is TResponse response ? response : default;
                }
            }
            catch (TaskCanceledException)
            {
                _logger?.LogDebug("Request timeout for {RequestType} to {TargetMachine}", requestType, targetMachineId);
                return default;
            }
            finally
            {
                _pendingRequests.TryRemove(correlationId, out _);
            }
        }

        public Task RegisterRequestHandlerAsync<TRequest, TResponse>(
            string requestType,
            Func<TRequest, Task<TResponse>> handler)
        {
            var requestHandler = new RequestHandler
            {
                RequestType = requestType,
                HandleAsync = async (obj) =>
                {
                    if (obj is TRequest request)
                    {
                        var response = await handler(request);
                        return response!;
                    }
                    throw new InvalidCastException($"Cannot cast to {typeof(TRequest)}");
                }
            };

            _requestHandlers[requestType] = requestHandler;

            // Subscribe to requests
            _ = SubscribeToPatternAsync($"request.*.{requestType}", async evt =>
            {
                if (evt.CorrelationId != null && _pendingRequests.TryGetValue(evt.CorrelationId, out var pending))
                {
                    try
                    {
                        var response = await requestHandler.HandleAsync(evt.Payload!);
                        pending.Completion.TrySetResult(response);
                    }
                    catch (Exception ex)
                    {
                        pending.Completion.TrySetException(ex);
                    }
                }
            });

            return Task.CompletedTask;
        }

        #endregion

        #region Background Processing

        private async Task ProcessPublishQueueAsync(CancellationToken cancellationToken)
        {
            var buffer = new List<PublishWorkItem>(100);

            try
            {
                await foreach (var item in _publishChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    buffer.Add(item);

                    // Batch processing for efficiency
                    if (buffer.Count >= 100 || !_publishChannel.Reader.TryPeek(out _))
                    {
                        ProcessPublishBatch(buffer);
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
                    ProcessPublishBatch(buffer);
                }
            }
        }

        private async Task ProcessBroadcastQueueAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var item in _broadcastChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    ProcessBroadcast(item);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void ProcessPublishBatch(List<PublishWorkItem> items)
        {
            var subscriptionsToNotify = _listPool.Get();

            try
            {
                foreach (var item in items)
                {
                    subscriptionsToNotify.Clear();

                    // Direct topic match
                    if (_topicSubscriptions.TryGetValue(item.Topic, out var topicSubs))
                    {
                        topicSubs.CopyTo(subscriptionsToNotify);
                    }

                    // Pattern matches
                    foreach (var kvp in _patternSubscriptions)
                    {
                        if (kvp.Value.Matches(item.Topic))
                        {
                            kvp.Value.CopyTo(subscriptionsToNotify);
                        }
                    }

                    // Wildcard
                    if (_topicSubscriptions.TryGetValue("*", out var wildcardSubs))
                    {
                        wildcardSubs.CopyTo(subscriptionsToNotify);
                    }

                    // Notify all subscriptions
                    NotifySubscriptions(subscriptionsToNotify, item.Event);

                    // Return event to pool if it came from pool
                    if (item.ReturnToPool)
                    {
                        _eventPool.Return(item.Event);
                    }

                    Interlocked.Increment(ref _totalEventsPublished);
                }
            }
            finally
            {
                _listPool.Return(subscriptionsToNotify);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessBroadcast(BroadcastWorkItem item)
        {
            var evt = _eventPool.Get();
            evt.EventName = item.EventName;
            evt.Payload = item.Payload;
            evt.Timestamp = item.Timestamp;

            if (!string.IsNullOrEmpty(item.Filter))
            {
                evt.Headers["Filter"] = item.Filter;
            }

            // Notify all topic subscriptions
            foreach (var kvp in _topicSubscriptions)
            {
                kvp.Value.Notify(evt);
            }

            // Notify pattern subscriptions (including "*" for SubscribeToAllAsync)
            foreach (var kvp in _patternSubscriptions)
            {
                if (kvp.Key == "*" || kvp.Value.Matches(item.EventName))
                {
                    kvp.Value.Notify(evt);
                }
            }

            _eventPool.Return(evt);
            Interlocked.Increment(ref _totalEventsPublished);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void NotifySubscriptions(List<ISubscription> subscriptions, StateMachineEvent evt)
        {
            foreach (var sub in subscriptions)
            {
                try
                {
                    sub.Notify(evt);
                    Interlocked.Increment(ref _totalEventsDelivered);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error notifying subscription");
                }
            }
        }

        #endregion

        #region Internal Methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task PublishInternalAsync(string topic, StateMachineEvent evt, PublishMode mode)
        {
            var workItem = new PublishWorkItem
            {
                Topic = topic,
                Event = evt,
                Mode = mode,
                ReturnToPool = mode != PublishMode.External
            };

            if (!_publishChannel.Writer.TryWrite(workItem))
            {
                await _publishChannel.Writer.WriteAsync(workItem);
            }
        }

        private Task<IDisposable> SubscribeInternalAsync(string topic, Action<StateMachineEvent> handler, bool isPattern)
        {
            var subscription = new FastSubscription(topic, handler);
            Interlocked.Increment(ref _totalSubscriptions);

            if (isPattern)
            {
                var set = _patternSubscriptions.GetOrAdd(topic, _ => new PatternSubscriptionSet(topic));
                set.Add(subscription);
            }
            else
            {
                var set = _topicSubscriptions.GetOrAdd(topic, _ => new SubscriptionSet());
                set.Add(subscription);
            }

            return Task.FromResult<IDisposable>(new SubscriptionDisposable(() =>
            {
                if (isPattern)
                {
                    if (_patternSubscriptions.TryGetValue(topic, out var set))
                    {
                        set.Remove(subscription);
                        if (set.IsEmpty)
                        {
                            _patternSubscriptions.TryRemove(topic, out _);
                        }
                    }
                }
                else
                {
                    if (_topicSubscriptions.TryGetValue(topic, out var set))
                    {
                        set.Remove(subscription);
                        if (set.IsEmpty)
                        {
                            _topicSubscriptions.TryRemove(topic, out _);
                        }
                    }
                }

                Interlocked.Decrement(ref _totalSubscriptions);
            }));
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
                Task.WaitAll(_processingTasks, TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Timeout or cancellation during shutdown
            }

            _cts.Dispose();

            _logger?.LogInformation("OptimizedInMemoryEventBus disposed. Total events: {Published}, Delivered: {Delivered}, Subscriptions: {Subscriptions}",
                _totalEventsPublished, _totalEventsDelivered, _totalSubscriptions);
        }

        #endregion

        #region Internal Types

        private enum PublishMode
        {
            Topic,
            Group,
            Request,
            External
        }

        private struct PublishWorkItem
        {
            public string Topic;
            public StateMachineEvent Event;
            public PublishMode Mode;
            public bool ReturnToPool;
        }

        private struct BroadcastWorkItem
        {
            public string EventName;
            public object? Payload;
            public string? Filter;
            public DateTime Timestamp;
        }

        private interface ISubscription
        {
            void Notify(StateMachineEvent evt);
        }

        private sealed class FastSubscription : ISubscription
        {
            private readonly Action<StateMachineEvent> _handler;

            public FastSubscription(string topic, Action<StateMachineEvent> handler)
            {
                _handler = handler;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Notify(StateMachineEvent evt)
            {
                _handler(evt);
            }
        }

        private class SubscriptionSet
        {
            private readonly HashSet<ISubscription> _subscriptions = new();
            private readonly ReaderWriterLockSlim _lock = new();

            public bool IsEmpty
            {
                get
                {
                    _lock.EnterReadLock();
                    try
                    {
                        return _subscriptions.Count == 0;
                    }
                    finally
                    {
                        _lock.ExitReadLock();
                    }
                }
            }

            public void Add(ISubscription subscription)
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

            public void Remove(ISubscription subscription)
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

            public void CopyTo(List<ISubscription> list)
            {
                _lock.EnterReadLock();
                try
                {
                    list.AddRange(_subscriptions);
                }
                finally
                {
                    _lock.ExitReadLock();
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

        private sealed class PatternSubscriptionSet : SubscriptionSet
        {
            private readonly string _pattern;
            private readonly Func<string, bool> _matcher;

            public PatternSubscriptionSet(string pattern)
            {
                _pattern = pattern;
                _matcher = CompilePattern(pattern);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Matches(string topic)
            {
                return _matcher(topic);
            }

            private static Func<string, bool> CompilePattern(string pattern)
            {
                // Simple pattern matching for performance
                if (pattern == "*")
                    return _ => true;

                if (pattern.Contains("*"))
                {
                    var parts = pattern.Split('*');
                    return topic =>
                    {
                        if (parts.Length == 2)
                        {
                            return topic.StartsWith(parts[0]) && topic.EndsWith(parts[1]);
                        }
                        return false;
                    };
                }

                return topic => topic == pattern;
            }
        }

        private class PendingRequest
        {
            public string CorrelationId { get; set; } = string.Empty;
            public TaskCompletionSource<object> Completion { get; set; } = null!;
            public DateTime Timeout { get; set; }
        }

        private class RequestHandler
        {
            public string RequestType { get; set; } = string.Empty;
            public Func<object, Task<object>> HandleAsync { get; set; } = null!;
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

        #region Object Pool Policies

        private class EventPoolPolicy : PooledObjectPolicy<StateMachineEvent>
        {
            public override StateMachineEvent Create() => new StateMachineEvent();

            public override bool Return(StateMachineEvent obj)
            {
                obj.EventId = Guid.NewGuid().ToString();
                obj.EventName = string.Empty;
                obj.SourceMachineId = string.Empty;
                obj.TargetMachineId = null;
                obj.Payload = null;
                obj.Headers.Clear();
                obj.Timestamp = default;
                obj.CorrelationId = null;
                obj.CausationId = null;
                return true;
            }
        }

        private class ListPoolPolicy : PooledObjectPolicy<List<ISubscription>>
        {
            public override List<ISubscription> Create() => new List<ISubscription>(32);

            public override bool Return(List<ISubscription> obj)
            {
                obj.Clear();
                return obj.Capacity < 256;
            }
        }

        #endregion
    }
}