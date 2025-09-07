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

namespace XStateNet.Distributed.EventBus
{
    /// <summary>
    /// RabbitMQ implementation of the state machine event bus
    /// Note: This is a simplified implementation for RabbitMQ.Client v7
    /// </summary>
    public class RabbitMQEventBus : IStateMachineEventBus, IDisposable
    {
        private readonly string _connectionString;
        private readonly ILogger<RabbitMQEventBus>? _logger;
        private IConnection? _connection;
        private IChannel? _channel;
        private readonly object _connectionLock = new();
        private readonly ConcurrentDictionary<string, List<IEventBusSubscription>> _subscriptions = new();
        
        // Exchange and queue names
        private const string StateChangeExchange = "xstatenet.state.changes";
        private const string EventExchange = "xstatenet.events";
        private const string BroadcastExchange = "xstatenet.broadcast";
        private const string GroupExchange = "xstatenet.groups";
        
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
                        ConsumerDispatchConcurrency = 1
                    };
                    
                    _connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
                    _channel = _connection.CreateChannelAsync().GetAwaiter().GetResult();
                    
                    // Declare exchanges
                    _channel.ExchangeDeclareAsync(StateChangeExchange, ExchangeType.Topic, durable: true).GetAwaiter().GetResult();
                    _channel.ExchangeDeclareAsync(EventExchange, ExchangeType.Direct, durable: true).GetAwaiter().GetResult();
                    _channel.ExchangeDeclareAsync(BroadcastExchange, ExchangeType.Fanout, durable: true).GetAwaiter().GetResult();
                    _channel.ExchangeDeclareAsync(GroupExchange, ExchangeType.Topic, durable: true).GetAwaiter().GetResult();
                    
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
                    _channel?.CloseAsync().GetAwaiter().GetResult();
                    _connection?.CloseAsync().GetAwaiter().GetResult();
                    
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
                    Timestamp = new AmqpTimestamp(new DateTimeOffset(evt.Timestamp).ToUnixTimeSeconds())
                };
                
                if (!string.IsNullOrEmpty(evt.CorrelationId))
                    properties.CorrelationId = evt.CorrelationId;
                
                var routingKey = $"machine.{machineId}.state.{evt.NewState}";
                
                await _channel!.BasicPublishAsync(
                    exchange: StateChangeExchange,
                    routingKey: routingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: body
                );
                
                _logger?.LogDebug("Published state change for {MachineId}: {OldState} -> {NewState}", 
                    machineId, evt.OldState, evt.NewState);
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
                    Timestamp = new AmqpTimestamp(new DateTimeOffset(evt.Timestamp).ToUnixTimeSeconds())
                };
                
                await _channel!.BasicPublishAsync(
                    exchange: EventExchange,
                    routingKey: targetMachineId,
                    mandatory: false,
                    basicProperties: properties,
                    body: body
                );
                
                _logger?.LogDebug("Published event {EventName} to {TargetMachineId}", eventName, targetMachineId);
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
                
                await _channel!.BasicPublishAsync(
                    exchange: BroadcastExchange,
                    routingKey: "",
                    mandatory: false,
                    basicProperties: properties,
                    body: body
                );
                
                _logger?.LogDebug("Broadcast event {EventName}", eventName);
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
                
                await _channel!.BasicPublishAsync(
                    exchange: GroupExchange,
                    routingKey: routingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: body
                );
                
                _logger?.LogDebug("Published event {EventName} to group {GroupName}", eventName, groupName);
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
            
            await _channel!.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false);
            await _channel!.QueueBindAsync(queueName, EventExchange, machineId);
            
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
            
            await _channel!.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false);
            await _channel!.QueueBindAsync(queueName, StateChangeExchange, routingKey);
            
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
            
            await _channel!.QueueDeclareAsync(queueName, durable: false, exclusive: true, autoDelete: true);
            await _channel!.QueueBindAsync(queueName, StateChangeExchange, pattern);
            
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
            
            await _channel!.QueueDeclareAsync(queueName, durable: false, exclusive: true, autoDelete: true);
            await _channel!.QueueBindAsync(queueName, BroadcastExchange, "");
            
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
            
            await _channel!.QueueDeclareAsync(queueName, durable: false, exclusive: true, autoDelete: true);
            await _channel!.QueueBindAsync(queueName, GroupExchange, routingKey);
            
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
            // Simplified implementation - full request/response pattern would require more work
            _logger?.LogWarning("Request/Response pattern not fully implemented in this simplified version");
            return await Task.FromResult(default(TResponse));
        }
        
        public async Task RegisterRequestHandlerAsync<TRequest, TResponse>(string requestType, 
            Func<TRequest, Task<TResponse>> handler)
        {
            // Simplified implementation
            _logger?.LogWarning("Request handler registration not fully implemented in this simplified version");
            await Task.CompletedTask;
        }
        
        private void EnsureConnected()
        {
            if (!IsConnected)
                throw new InvalidOperationException("Event bus is not connected");
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
                consumer.ReceivedAsync += async (sender, args) =>
                {
                    try
                    {
                        var json = Encoding.UTF8.GetString(args.Body.ToArray());
                        var evt = JsonSerializer.Deserialize<StateMachineEvent>(json);
                        
                        if (evt != null && IsActive)
                        {
                            _handler(evt);
                        }
                        
                        await _channel.BasicAckAsync(args.DeliveryTag, false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error processing message in subscription {SubscriptionId}", SubscriptionId);
                        await _channel.BasicNackAsync(args.DeliveryTag, false, true);
                    }
                };
                
                _consumerTag = await _channel.BasicConsumeAsync(_queueName, false, consumer);
                IsActive = true;
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
                        _channel.BasicCancelAsync(_consumerTag).GetAwaiter().GetResult();
                    }
                    catch { }
                }
            }
        }
    }
}