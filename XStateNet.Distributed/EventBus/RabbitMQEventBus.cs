using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace XStateNet.Distributed.EventBus
{
    /// <summary>
    /// RabbitMQ implementation of the state machine event bus
    /// </summary>
    public class RabbitMQEventBus : IStateMachineEventBus, IDisposable
    {
        private readonly string _connectionString;
        private readonly ILogger<RabbitMQEventBus>? _logger;
        private IConnection? _connection;
        private IChannel? _channel;
        private readonly object _connectionLock = new();
        private readonly ConcurrentDictionary<string, List<IEventBusSubscription>> _subscriptions = new();
        private readonly ConcurrentDictionary<string, Func<object, Task<object>>> _requestHandlers = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<object>> _pendingRequests = new();
        
        // Exchange and queue names
        private const string StateChangeExchange = "xstatenet.state.changes";
        private const string EventExchange = "xstatenet.events";
        private const string BroadcastExchange = "xstatenet.broadcast";
        private const string GroupExchange = "xstatenet.groups";
        private const string RequestExchange = "xstatenet.requests";
        private const string ResponseExchange = "xstatenet.responses";
        
        public bool IsConnected => _connection?.IsOpen == true && _channel?.IsOpen == true;
        
        public event EventHandler<EventBusConnectedEventArgs>? Connected;
        public event EventHandler<EventBusDisconnectedEventArgs>? Disconnected;
        public event EventHandler<EventBusErrorEventArgs>? ErrorOccurred;
        
        public RabbitMQEventBus(string connectionString, ILogger<RabbitMQEventBus>? logger = null)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger;
        }
        
        public async Task ConnectAsync()
        {
            try
            {
                lock (_connectionLock)
                {
                    if (IsConnected)
                        return;
                    
                    var factory = new ConnectionFactory
                    {
                        Uri = new Uri(_connectionString),
                        AutomaticRecoveryEnabled = true,
                        NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                        RequestedHeartbeat = TimeSpan.FromSeconds(60),
                        DispatchConsumersAsync = true
                    };
                    
                    _connection = factory.CreateConnection("XStateNet.EventBus");
                    _channel = _connection.CreateChannel();
                    
                    // Declare exchanges
                    _channel.ExchangeDeclare(StateChangeExchange, ExchangeType.Topic, durable: true);
                    _channel.ExchangeDeclare(EventExchange, ExchangeType.Direct, durable: true);
                    _channel.ExchangeDeclare(BroadcastExchange, ExchangeType.Fanout, durable: true);
                    _channel.ExchangeDeclare(GroupExchange, ExchangeType.Topic, durable: true);
                    _channel.ExchangeDeclare(RequestExchange, ExchangeType.Direct, durable: true);
                    _channel.ExchangeDeclare(ResponseExchange, ExchangeType.Direct, durable: true);
                    
                    // Set up connection event handlers
                    _connection.ConnectionShutdown += OnConnectionShutdown;
                    _connection.ConnectionBlocked += OnConnectionBlocked;
                    _connection.ConnectionUnblocked += OnConnectionUnblocked;
                    
                    _logger?.LogInformation("Connected to RabbitMQ at {ConnectionString}", _connectionString);
                    
                    Connected?.Invoke(this, new EventBusConnectedEventArgs 
                    { 
                        Endpoint = _connectionString,
                        ConnectedAt = DateTime.UtcNow
                    });
                }
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to connect to RabbitMQ");
                ErrorOccurred?.Invoke(this, new EventBusErrorEventArgs
                {
                    Exception = ex,
                    Context = "Connection",
                    IsFatal = true
                });
                throw;
            }
        }
        
        public async Task DisconnectAsync()
        {
            try
            {
                lock (_connectionLock)
                {
                    _channel?.Close();
                    _connection?.Close();
                    
                    _channel?.Dispose();
                    _connection?.Dispose();
                    
                    _channel = null;
                    _connection = null;
                }
                
                Disconnected?.Invoke(this, new EventBusDisconnectedEventArgs
                {
                    Reason = "Manual disconnect",
                    WillReconnect = false
                });
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during disconnect");
                throw;
            }
        }
        
        public async Task PublishStateChangeAsync(string machineId, StateChangeEvent evt)
        {
            try
            {
                EnsureConnected();
                
                evt.SourceMachineId = machineId;
                var json = JsonSerializer.Serialize(evt);
                var body = Encoding.UTF8.GetBytes(json);
                
                var properties = new BasicProperties
                {
                    Persistent = true,
                    ContentType = "application/json",
                    MessageId = evt.EventId,
                    Timestamp = new DateTimeOffset(evt.Timestamp).ToUnixTimeSeconds()
                };
                
                if (!string.IsNullOrEmpty(evt.CorrelationId))
                    properties.CorrelationId = evt.CorrelationId;
                
                var routingKey = $"machine.{machineId}.state.{evt.NewState}";
                
                lock (_connectionLock)
                {
                    _channel!.BasicPublish(
                        exchange: StateChangeExchange,
                        routingKey: routingKey,
                        mandatory: false,
                        basicProperties: properties,
                        body: body
                    );
                }
                
                _logger?.LogDebug("Published state change for {MachineId}: {OldState} -> {NewState}", 
                    machineId, evt.OldState, evt.NewState);
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to publish state change for {MachineId}", machineId);
                throw;
            }
        }
        
        public async Task PublishEventAsync(string targetMachineId, string eventName, object? payload = null)
        {
            try
            {
                EnsureConnected();
                
                var evt = new StateMachineEvent
                {
                    EventName = eventName,
                    TargetMachineId = targetMachineId,
                    Payload = payload
                };
                
                var json = JsonSerializer.Serialize(evt);
                var body = Encoding.UTF8.GetBytes(json);
                
                var properties = new BasicProperties
                {
                    Persistent = true,
                    ContentType = "application/json",
                    MessageId = evt.EventId,
                    Timestamp = new DateTimeOffset(evt.Timestamp).ToUnixTimeSeconds()
                };
                
                lock (_connectionLock)
                {
                    _channel!.BasicPublish(
                        exchange: EventExchange,
                        routingKey: targetMachineId,
                        mandatory: false,
                        basicProperties: properties,
                        body: body
                    );
                }
                
                _logger?.LogDebug("Published event {EventName} to {TargetMachineId}", eventName, targetMachineId);
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to publish event {EventName} to {TargetMachineId}", 
                    eventName, targetMachineId);
                throw;
            }
        }
        
        public async Task BroadcastAsync(string eventName, object? payload = null, string? filter = null)
        {
            try
            {
                EnsureConnected();
                
                var evt = new StateMachineEvent
                {
                    EventName = eventName,
                    Payload = payload
                };
                
                if (!string.IsNullOrEmpty(filter))
                    evt.Headers["Filter"] = filter;
                
                var json = JsonSerializer.Serialize(evt);
                var body = Encoding.UTF8.GetBytes(json);
                
                var properties = new BasicProperties
                {
                    Persistent = false,
                    ContentType = "application/json",
                    MessageId = evt.EventId
                };
                
                lock (_connectionLock)
                {
                    _channel!.BasicPublish(
                        exchange: BroadcastExchange,
                        routingKey: "",
                        mandatory: false,
                        basicProperties: properties,
                        body: body
                    );
                }
                
                _logger?.LogDebug("Broadcast event {EventName}", eventName);
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to broadcast event {EventName}", eventName);
                throw;
            }
        }
        
        public async Task PublishToGroupAsync(string groupName, string eventName, object? payload = null)
        {
            try
            {
                EnsureConnected();
                
                var evt = new StateMachineEvent
                {
                    EventName = eventName,
                    Payload = payload
                };
                
                evt.Headers["GroupName"] = groupName;
                
                var json = JsonSerializer.Serialize(evt);
                var body = Encoding.UTF8.GetBytes(json);
                
                var properties = new BasicProperties
                {
                    Persistent = true,
                    ContentType = "application/json",
                    MessageId = evt.EventId
                };
                
                var routingKey = $"group.{groupName}";
                
                lock (_connectionLock)
                {
                    _channel!.BasicPublish(
                        exchange: GroupExchange,
                        routingKey: routingKey,
                        mandatory: false,
                        basicProperties: properties,
                        body: body
                    );
                }
                
                _logger?.LogDebug("Published event {EventName} to group {GroupName}", eventName, groupName);
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to publish event {EventName} to group {GroupName}", 
                    eventName, groupName);
                throw;
            }
        }
        
        public async Task<IDisposable> SubscribeToMachineAsync(string machineId, Action<StateMachineEvent> handler)
        {
            EnsureConnected();
            
            var queueName = $"machine.{machineId}.events";
            
            lock (_connectionLock)
            {
                _channel!.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false);
                _channel!.QueueBind(queueName, EventExchange, machineId);
            }
            
            var subscription = new RabbitMQSubscription(this, queueName, handler, _logger);
            await subscription.StartAsync(_channel!);
            
            _subscriptions.AddOrUpdate(machineId, 
                new List<IEventBusSubscription> { subscription },
                (key, list) => { list.Add(subscription); return list; });
            
            return subscription;
        }
        
        public async Task<IDisposable> SubscribeToStateChangesAsync(string machineId, Action<StateChangeEvent> handler)
        {
            EnsureConnected();
            
            var queueName = $"machine.{machineId}.state.changes";
            var routingKey = $"machine.{machineId}.state.*";
            
            lock (_connectionLock)
            {
                _channel!.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false);
                _channel!.QueueBind(queueName, StateChangeExchange, routingKey);
            }
            
            Action<StateMachineEvent> wrapper = evt =>
            {
                if (evt is StateChangeEvent stateChange)
                    handler(stateChange);
            };
            
            var subscription = new RabbitMQSubscription(this, queueName, wrapper, _logger);
            await subscription.StartAsync(_channel!);
            
            _subscriptions.AddOrUpdate($"state.{machineId}", 
                new List<IEventBusSubscription> { subscription },
                (key, list) => { list.Add(subscription); return list; });
            
            return subscription;
        }
        
        public async Task<IDisposable> SubscribeToPatternAsync(string pattern, Action<StateMachineEvent> handler)
        {
            EnsureConnected();
            
            var queueName = $"pattern.{Guid.NewGuid():N}";
            
            lock (_connectionLock)
            {
                _channel!.QueueDeclare(queueName, durable: false, exclusive: true, autoDelete: true);
                _channel!.QueueBind(queueName, StateChangeExchange, pattern);
            }
            
            var subscription = new RabbitMQSubscription(this, queueName, handler, _logger);
            await subscription.StartAsync(_channel!);
            
            _subscriptions.AddOrUpdate(pattern, 
                new List<IEventBusSubscription> { subscription },
                (key, list) => { list.Add(subscription); return list; });
            
            return subscription;
        }
        
        public async Task<IDisposable> SubscribeToAllAsync(Action<StateMachineEvent> handler)
        {
            EnsureConnected();
            
            var queueName = $"all.{Guid.NewGuid():N}";
            
            lock (_connectionLock)
            {
                _channel!.QueueDeclare(queueName, durable: false, exclusive: true, autoDelete: true);
                _channel!.QueueBind(queueName, BroadcastExchange, "");
            }
            
            var subscription = new RabbitMQSubscription(this, queueName, handler, _logger);
            await subscription.StartAsync(_channel!);
            
            _subscriptions.AddOrUpdate("all", 
                new List<IEventBusSubscription> { subscription },
                (key, list) => { list.Add(subscription); return list; });
            
            return subscription;
        }
        
        public async Task<IDisposable> SubscribeToGroupAsync(string groupName, Action<StateMachineEvent> handler)
        {
            EnsureConnected();
            
            var queueName = $"group.{groupName}.{Guid.NewGuid():N}";
            var routingKey = $"group.{groupName}";
            
            lock (_connectionLock)
            {
                _channel!.QueueDeclare(queueName, durable: false, exclusive: true, autoDelete: true);
                _channel!.QueueBind(queueName, GroupExchange, routingKey);
            }
            
            var subscription = new RabbitMQSubscription(this, queueName, handler, _logger);
            await subscription.StartAsync(_channel!);
            
            _subscriptions.AddOrUpdate($"group.{groupName}", 
                new List<IEventBusSubscription> { subscription },
                (key, list) => { list.Add(subscription); return list; });
            
            return subscription;
        }
        
        public async Task<TResponse?> RequestAsync<TResponse>(string targetMachineId, string requestType, 
            object? payload = null, TimeSpan? timeout = null)
        {
            EnsureConnected();
            
            var correlationId = Guid.NewGuid().ToString();
            var replyQueue = $"reply.{correlationId}";
            var actualTimeout = timeout ?? TimeSpan.FromSeconds(30);
            
            // Create reply queue
            lock (_connectionLock)
            {
                _channel!.QueueDeclare(replyQueue, durable: false, exclusive: true, autoDelete: true);
                _channel!.QueueBind(replyQueue, ResponseExchange, correlationId);
            }
            
            var tcs = new TaskCompletionSource<object>();
            _pendingRequests[correlationId] = tcs;
            
            // Set up consumer for response
            var consumer = new AsyncEventingBasicConsumer(_channel!);
            consumer.Received += async (sender, args) =>
            {
                try
                {
                    var json = Encoding.UTF8.GetString(args.Body.ToArray());
                    var response = JsonSerializer.Deserialize<StateMachineEvent>(json);
                    
                    if (response?.CorrelationId == correlationId && _pendingRequests.TryRemove(correlationId, out var pending))
                    {
                        pending.SetResult(response.Payload!);
                    }
                    
                    _channel!.BasicAck(args.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error processing response");
                    _channel!.BasicNack(args.DeliveryTag, false, false);
                }
                
                await Task.CompletedTask;
            };
            
            lock (_connectionLock)
            {
                _channel!.BasicConsume(replyQueue, false, consumer);
            }
            
            // Send request
            var request = new StateMachineEvent
            {
                EventName = requestType,
                TargetMachineId = targetMachineId,
                Payload = payload,
                CorrelationId = correlationId
            };
            
            request.Headers["ReplyTo"] = correlationId;
            
            var requestJson = JsonSerializer.Serialize(request);
            var requestBody = Encoding.UTF8.GetBytes(requestJson);
            
            var properties = new BasicProperties
            {
                CorrelationId = correlationId,
                ReplyTo = correlationId,
                Expiration = actualTimeout.TotalMilliseconds.ToString()
            };
            
            lock (_connectionLock)
            {
                _channel!.BasicPublish(
                    exchange: RequestExchange,
                    routingKey: targetMachineId,
                    mandatory: false,
                    basicProperties: properties,
                    body: requestBody
                );
            }
            
            // Wait for response with timeout
            using var cts = new CancellationTokenSource(actualTimeout);
            try
            {
                var response = await tcs.Task.WaitAsync(cts.Token);
                
                if (response is TResponse typedResponse)
                    return typedResponse;
                
                // Try to deserialize if needed
                if (response is JsonElement jsonElement)
                {
                    var json = jsonElement.GetRawText();
                    return JsonSerializer.Deserialize<TResponse>(json);
                }
                
                return default(TResponse);
            }
            catch (OperationCanceledException)
            {
                _pendingRequests.TryRemove(correlationId, out _);
                _logger?.LogWarning("Request to {TargetMachineId} timed out after {Timeout}", 
                    targetMachineId, actualTimeout);
                return default(TResponse);
            }
            finally
            {
                // Clean up reply queue
                try
                {
                    lock (_connectionLock)
                    {
                        _channel!.QueueDelete(replyQueue);
                    }
                }
                catch { }
            }
        }
        
        public async Task RegisterRequestHandlerAsync<TRequest, TResponse>(string requestType, 
            Func<TRequest, Task<TResponse>> handler)
        {
            EnsureConnected();
            
            Func<object, Task<object>> wrapper = async (obj) =>
            {
                var request = obj is TRequest typed ? typed : JsonSerializer.Deserialize<TRequest>(obj.ToString()!);
                var response = await handler(request!);
                return response!;
            };
            
            _requestHandlers[requestType] = wrapper;
            
            await Task.CompletedTask;
        }
        
        private void EnsureConnected()
        {
            if (!IsConnected)
                throw new InvalidOperationException("Event bus is not connected");
        }
        
        private void OnConnectionShutdown(object? sender, ShutdownEventArgs e)
        {
            _logger?.LogWarning("RabbitMQ connection shutdown: {Reason}", e.ReplyText);
            
            Disconnected?.Invoke(this, new EventBusDisconnectedEventArgs
            {
                Reason = e.ReplyText,
                WillReconnect = true,
                ReconnectDelay = TimeSpan.FromSeconds(10)
            });
        }
        
        private void OnConnectionBlocked(object? sender, ConnectionBlockedEventArgs e)
        {
            _logger?.LogWarning("RabbitMQ connection blocked: {Reason}", e.Reason);
        }
        
        private void OnConnectionUnblocked(object? sender, EventArgs e)
        {
            _logger?.LogInformation("RabbitMQ connection unblocked");
        }
        
        public void Dispose()
        {
            foreach (var subscriptionList in _subscriptions.Values)
            {
                foreach (var subscription in subscriptionList)
                {
                    subscription.Dispose();
                }
            }
            
            _subscriptions.Clear();
            _pendingRequests.Clear();
            _requestHandlers.Clear();
            
            _channel?.Dispose();
            _connection?.Dispose();
        }
        
        private class RabbitMQSubscription : IEventBusSubscription
        {
            private readonly RabbitMQEventBus _bus;
            private readonly string _queueName;
            private readonly Action<StateMachineEvent> _handler;
            private readonly ILogger? _logger;
            private string? _consumerTag;
            private IChannel? _channel;
            
            public string SubscriptionId { get; } = Guid.NewGuid().ToString();
            public bool IsActive { get; private set; }
            
            public RabbitMQSubscription(RabbitMQEventBus bus, string queueName, 
                Action<StateMachineEvent> handler, ILogger? logger)
            {
                _bus = bus;
                _queueName = queueName;
                _handler = handler;
                _logger = logger;
            }
            
            public async Task StartAsync(IChannel channel)
            {
                _channel = channel;
                
                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.Received += async (sender, args) =>
                {
                    try
                    {
                        var json = Encoding.UTF8.GetString(args.Body.ToArray());
                        var evt = JsonSerializer.Deserialize<StateMachineEvent>(json);
                        
                        if (evt != null && IsActive)
                        {
                            _handler(evt);
                        }
                        
                        _channel.BasicAck(args.DeliveryTag, false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error processing message in subscription {SubscriptionId}", SubscriptionId);
                        _channel.BasicNack(args.DeliveryTag, false, true);
                    }
                    
                    await Task.CompletedTask;
                };
                
                _consumerTag = _channel.BasicConsume(_queueName, false, consumer);
                IsActive = true;
                
                await Task.CompletedTask;
            }
            
            public async Task PauseAsync()
            {
                IsActive = false;
                await Task.CompletedTask;
            }
            
            public async Task ResumeAsync()
            {
                IsActive = true;
                await Task.CompletedTask;
            }
            
            public void Dispose()
            {
                IsActive = false;
                
                if (!string.IsNullOrEmpty(_consumerTag) && _channel?.IsOpen == true)
                {
                    try
                    {
                        _channel.BasicCancel(_consumerTag);
                    }
                    catch { }
                }
            }
        }
    }
}