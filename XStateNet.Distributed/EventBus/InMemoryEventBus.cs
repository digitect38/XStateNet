using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace XStateNet.Distributed.EventBus
{
    /// <summary>
    /// In-memory implementation of the state machine event bus
    /// Perfect for single-process applications and testing
    /// </summary>
    public class InMemoryEventBus : IStateMachineEventBus, IDisposable
    {
        private readonly ILogger<InMemoryEventBus>? _logger;
        private readonly ConcurrentDictionary<string, List<SubscriptionInfo>> _subscriptions = new();
        private readonly ConcurrentDictionary<string, Channel<StateMachineEvent>> _channels = new();
        private readonly ConcurrentDictionary<string, RequestHandlerInfo> _requestHandlers = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<object>> _pendingRequests = new();
        private bool _isConnected;
        private readonly object _lock = new();

        public bool IsConnected => _isConnected;

        public event EventHandler<EventBusConnectedEventArgs>? Connected;
        public event EventHandler<EventBusDisconnectedEventArgs>? Disconnected;
        public event EventHandler<EventBusErrorEventArgs>? ErrorOccurred;

        public InMemoryEventBus(ILogger<InMemoryEventBus>? logger = null)
        {
            _logger = logger;
        }

        #region Connection Management

        public Task ConnectAsync()
        {
            lock (_lock)
            {
                if (_isConnected)
                    return Task.CompletedTask;

                _isConnected = true;
                _logger?.LogInformation("InMemoryEventBus connected");

                Connected?.Invoke(this, new EventBusConnectedEventArgs
                {
                    Endpoint = "memory://localhost",
                    ConnectedAt = DateTime.UtcNow
                });
            }

            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            lock (_lock)
            {
                if (!_isConnected)
                    return Task.CompletedTask;

                _isConnected = false;

                // Close all channels
                foreach (var channel in _channels.Values)
                {
                    channel.Writer.TryComplete();
                }
                _channels.Clear();

                // Clear subscriptions
                _subscriptions.Clear();

                // Cancel pending requests
                foreach (var request in _pendingRequests.Values)
                {
                    request.TrySetCanceled();
                }
                _pendingRequests.Clear();

                _logger?.LogInformation("InMemoryEventBus disconnected");

                Disconnected?.Invoke(this, new EventBusDisconnectedEventArgs
                {
                    Reason = "Manual disconnect",
                    WillReconnect = false
                });
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Publishing

        public Task PublishStateChangeAsync(string machineId, StateChangeEvent evt)
        {
            if (!_isConnected)
                return Task.CompletedTask;

            evt.SourceMachineId = machineId;
            return PublishToSubscribersAsync($"state.{machineId}", evt);
        }

        public Task PublishEventAsync(string targetMachineId, string eventName, object? payload = null)
        {
            if (!_isConnected)
                return Task.CompletedTask;

            var evt = new StateMachineEvent
            {
                EventName = eventName,
                TargetMachineId = targetMachineId,
                Payload = payload,
                Timestamp = DateTime.UtcNow
            };

            return PublishToSubscribersAsync($"machine.{targetMachineId}", evt);
        }

        public Task BroadcastAsync(string eventName, object? payload = null, string? filter = null)
        {
            if (!_isConnected)
                return Task.CompletedTask;

            var evt = new StateMachineEvent
            {
                EventName = eventName,
                Payload = payload,
                Timestamp = DateTime.UtcNow
            };

            if (!string.IsNullOrEmpty(filter))
                evt.Headers["Filter"] = filter;

            return PublishToSubscribersAsync("broadcast", evt);
        }

        public Task PublishToGroupAsync(string groupName, string eventName, object? payload = null)
        {
            if (!_isConnected)
                return Task.CompletedTask;

            var evt = new StateMachineEvent
            {
                EventName = eventName,
                Payload = payload,
                Timestamp = DateTime.UtcNow
            };

            return PublishToSubscribersAsync($"group.{groupName}", evt);
        }

        #endregion

        #region Subscribing

        public Task<IDisposable> SubscribeToMachineAsync(string machineId, Action<StateMachineEvent> handler)
        {
            return SubscribeInternalAsync($"machine.{machineId}", handler);
        }

        public Task<IDisposable> SubscribeToStateChangesAsync(string machineId, Action<StateChangeEvent> handler)
        {
            return SubscribeInternalAsync($"state.{machineId}", evt =>
            {
                if (evt is StateChangeEvent stateChange)
                    handler(stateChange);
            });
        }

        public Task<IDisposable> SubscribeToPatternAsync(string pattern, Action<StateMachineEvent> handler)
        {
            return SubscribeInternalAsync(pattern, handler, isPattern: true);
        }

        public Task<IDisposable> SubscribeToAllAsync(Action<StateMachineEvent> handler)
        {
            return SubscribeInternalAsync("*", handler, isPattern: true);
        }

        public Task<IDisposable> SubscribeToGroupAsync(string groupName, Action<StateMachineEvent> handler)
        {
            return SubscribeInternalAsync($"group.{groupName}", handler);
        }

        #endregion

        #region Request/Response

        public async Task<TResponse?> RequestAsync<TResponse>(
            string targetMachineId,
            string requestType,
            object? payload = null,
            TimeSpan? timeout = null)
        {
            if (!_isConnected)
                return default;

            var correlationId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<object>();
            _pendingRequests[correlationId] = tcs;

            var request = new StateMachineEvent
            {
                EventName = requestType,
                TargetMachineId = targetMachineId,
                Payload = payload,
                CorrelationId = correlationId,
                Timestamp = DateTime.UtcNow
            };

            // Publish request
            await PublishToSubscribersAsync($"request.{targetMachineId}.{requestType}", request);

            // Wait for response
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
            try
            {
                await using (cts.Token.Register(() => tcs.TrySetCanceled()))
                {
                    var result = await tcs.Task;
                    return result is TResponse response ? response : default;
                }
            }
            catch (TaskCanceledException)
            {
                _logger?.LogWarning("Request timeout for {RequestType} to {TargetMachine}", requestType, targetMachineId);
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
            var handlerInfo = new RequestHandlerInfo
            {
                RequestType = requestType,
                Handler = async (obj) =>
                {
                    if (obj is TRequest request)
                    {
                        var response = await handler(request);
                        return response!;
                    }
                    throw new InvalidCastException($"Cannot cast {obj?.GetType()} to {typeof(TRequest)}");
                }
            };

            _requestHandlers[requestType] = handlerInfo;

            // Subscribe to requests of this type
            _ = SubscribeToPatternAsync($"request.*.{requestType}", async evt =>
            {
                if (evt.CorrelationId != null && evt.Payload != null)
                {
                    try
                    {
                        var response = await handlerInfo.Handler(evt.Payload);

                        // Send response
                        if (_pendingRequests.TryGetValue(evt.CorrelationId, out var tcs))
                        {
                            tcs.TrySetResult(response);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error handling request {RequestType}", requestType);
                    }
                }
            });

            return Task.CompletedTask;
        }

        #endregion

        #region Private Methods

        private async Task<IDisposable> SubscribeInternalAsync(
            string topic,
            Action<StateMachineEvent> handler,
            bool isPattern = false)
        {
            if (!_isConnected)
                await ConnectAsync();

            var subscription = new SubscriptionInfo
            {
                Id = Guid.NewGuid().ToString(),
                Topic = topic,
                Handler = handler,
                IsPattern = isPattern,
                Pattern = isPattern ? ConvertToRegex(topic) : null
            };

            _subscriptions.AddOrUpdate(topic,
                new List<SubscriptionInfo> { subscription },
                (_, list) =>
                {
                    list.Add(subscription);
                    return list;
                });

            _logger?.LogDebug("Added subscription {SubscriptionId} to topic {Topic}", subscription.Id, topic);

            return new SubscriptionDisposable(() =>
            {
                if (_subscriptions.TryGetValue(topic, out var list))
                {
                    list.Remove(subscription);
                    if (list.Count == 0)
                    {
                        _subscriptions.TryRemove(topic, out _);
                    }
                }
            });
        }

        private async Task PublishToSubscribersAsync(string topic, StateMachineEvent evt)
        {
            var tasks = new List<Task>();

            // Direct topic subscribers (non-pattern)
            if (_subscriptions.TryGetValue(topic, out var directSubs))
            {
                foreach (var sub in directSubs.Where(s => !s.IsPattern).ToList())
                {
                    tasks.Add(Task.Run(() => InvokeHandler(sub, evt)));
                }
            }

            // Pattern matching subscribers (including wildcard "*")
            foreach (var kvp in _subscriptions.Where(s => s.Value.Any(v => v.IsPattern)))
            {
                foreach (var sub in kvp.Value.Where(s => s.IsPattern).ToList())
                {
                    if (kvp.Key == "*")
                    {
                        // Wildcard pattern matches everything
                        tasks.Add(Task.Run(() => InvokeHandler(sub, evt)));
                    }
                    else if (sub.Pattern != null && sub.Pattern.IsMatch(topic))
                    {
                        tasks.Add(Task.Run(() => InvokeHandler(sub, evt)));
                    }
                }
            }

            await Task.WhenAll(tasks);
        }

        private void InvokeHandler(SubscriptionInfo subscription, StateMachineEvent evt)
        {
            try
            {
                subscription.Handler(evt);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error invoking handler for subscription {SubscriptionId}", subscription.Id);
                ErrorOccurred?.Invoke(this, new EventBusErrorEventArgs
                {
                    Exception = ex,
                    Context = $"Handler invocation for {subscription.Topic}",
                    IsFatal = false
                });
            }
        }

        private Regex ConvertToRegex(string pattern)
        {
            // Convert wildcard pattern to regex
            // * matches any string
            // ? matches any single character
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            return new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        #endregion

        public void Dispose()
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }

        #region Internal Classes

        private class SubscriptionInfo
        {
            public string Id { get; set; } = string.Empty;
            public string Topic { get; set; } = string.Empty;
            public Action<StateMachineEvent> Handler { get; set; } = null!;
            public bool IsPattern { get; set; }
            public Regex? Pattern { get; set; }
        }

        private class RequestHandlerInfo
        {
            public string RequestType { get; set; } = string.Empty;
            public Func<object, Task<object>> Handler { get; set; } = null!;
        }

        private class SubscriptionDisposable : IDisposable
        {
            private readonly Action _disposeAction;
            private bool _disposed;

            public SubscriptionDisposable(Action disposeAction)
            {
                _disposeAction = disposeAction;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposeAction();
                    _disposed = true;
                }
            }
        }

        #endregion
    }
}