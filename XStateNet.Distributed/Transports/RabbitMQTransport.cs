using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using XStateNet.Distributed.Core;

namespace XStateNet.Distributed.Transports
{
    /// <summary>
    /// RabbitMQ transport for broker-based reliable inter-computer communication
    /// </summary>
    public class RabbitMQTransport : IStateMachineTransport
    {
        private IConnection? _connection;
        private IModel? _channel;
        private string _queueName = string.Empty;
        private string _exchangeName = "xstatenet.exchange";
        private string _address = string.Empty;
        private bool _isConnected;
        
        private readonly ConcurrentDictionary<string, TaskCompletionSource<StateMachineMessage>> _pendingRequests = new();
        private readonly ConcurrentBag<StateMachineMessage> _receivedMessages = new();
        private EventingBasicConsumer? _consumer;

        public string TransportId { get; } = Guid.NewGuid().ToString();
        public TransportType Type => TransportType.RabbitMQ;
        public bool IsConnected => _isConnected && _connection?.IsOpen == true;

        public event EventHandler<ConnectionStatusChangedEventArgs>? ConnectionStatusChanged;
        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

        public async Task ConnectAsync(string address, CancellationToken cancellationToken = default)
        {
            if (_isConnected)
                throw new InvalidOperationException("Transport is already connected");

            try
            {
                _address = address;
                
                // Parse connection string
                var factory = new ConnectionFactory();
                if (address.StartsWith("amqp://"))
                {
                    factory.Uri = new Uri(address);
                }
                else
                {
                    factory.HostName = address;
                }
                
                factory.AutomaticRecoveryEnabled = true;
                factory.NetworkRecoveryInterval = TimeSpan.FromSeconds(10);

                // Create connection and channel
                _connection = factory.CreateConnection($"XStateNet-{TransportId}");
                _channel = _connection.CreateModel();

                // Declare exchange
                _channel.ExchangeDeclare(
                    exchange: _exchangeName,
                    type: ExchangeType.Topic,
                    durable: true,
                    autoDelete: false);

                // Create unique queue for this transport
                _queueName = $"xstatenet.{Environment.MachineName}.{TransportId}";
                _channel.QueueDeclare(
                    queue: _queueName,
                    durable: false,
                    exclusive: true,
                    autoDelete: true,
                    arguments: null);

                // Setup consumer
                _consumer = new EventingBasicConsumer(_channel);
                _consumer.Received += OnMessageReceived;
                
                _channel.BasicConsume(
                    queue: _queueName,
                    autoAck: false,
                    consumer: _consumer);

                _isConnected = true;
                ConnectionStatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs { IsConnected = true });

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                ConnectionStatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs 
                { 
                    IsConnected = false, 
                    Exception = ex 
                });
                throw;
            }
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
                return Task.CompletedTask;

            try
            {
                _isConnected = false;
                
                _channel?.Close();
                _channel?.Dispose();
                
                _connection?.Close();
                _connection?.Dispose();

                ConnectionStatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs { IsConnected = false });
            }
            catch { }

            return Task.CompletedTask;
        }

        public Task<bool> SendAsync(StateMachineMessage message, CancellationToken cancellationToken = default)
        {
            if (!IsConnected || _channel == null)
                return Task.FromResult(false);

            try
            {
                var properties = _channel.CreateBasicProperties();
                properties.Persistent = false;
                properties.ContentType = "application/octet-stream";
                properties.MessageId = message.Id;
                properties.CorrelationId = message.CorrelationId;
                properties.ReplyTo = message.ReplyTo;
                properties.Priority = (byte)Math.Min(message.Priority, 9);
                
                if (message.Expiry.HasValue)
                {
                    properties.Expiration = message.Expiry.Value.TotalMilliseconds.ToString();
                }

                // Add headers
                if (message.Headers.Count > 0)
                {
                    properties.Headers = message.Headers.ToDictionary(k => k.Key, v => (object)v.Value);
                }

                var body = MessageSerializer.SerializeMessage(message);
                var routingKey = message.To;

                _channel.BasicPublish(
                    exchange: _exchangeName,
                    routingKey: routingKey,
                    basicProperties: properties,
                    body: body);

                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        public async Task<StateMachineMessage?> ReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var endTime = DateTime.UtcNow.Add(timeout);
            
            while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
            {
                if (_receivedMessages.TryTake(out var message))
                {
                    return message;
                }
                
                await Task.Delay(10, cancellationToken);
            }

            return null;
        }

        public async IAsyncEnumerable<StateMachineMessage> SubscribeAsync(
            string pattern,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!IsConnected || _channel == null)
                yield break;

            // Bind queue to pattern
            try
            {
                var routingKey = pattern.Replace("*", "#");
                _channel.QueueBind(
                    queue: _queueName,
                    exchange: _exchangeName,
                    routingKey: routingKey);
            }
            catch { }

            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                if (_receivedMessages.TryTake(out var message))
                {
                    if (MatchesPattern(message.EventName, pattern) || 
                        MatchesPattern(message.From, pattern))
                    {
                        yield return message;
                    }
                    else
                    {
                        // Put it back if it doesn't match
                        _receivedMessages.Add(message);
                    }
                }
                
                await Task.Delay(10, cancellationToken);
            }
        }

        public async Task<TResponse?> RequestAsync<TRequest, TResponse>(
            string target,
            TRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
            where TRequest : class
            where TResponse : class
        {
            var correlationId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<StateMachineMessage>();
            _pendingRequests[correlationId] = tcs;

            try
            {
                // Create temporary reply queue
                var replyQueue = $"{_queueName}.reply.{correlationId}";
                if (_channel != null)
                {
                    _channel.QueueDeclare(
                        queue: replyQueue,
                        durable: false,
                        exclusive: true,
                        autoDelete: true);

                    // Bind reply queue
                    _channel.QueueBind(
                        queue: replyQueue,
                        exchange: _exchangeName,
                        routingKey: replyQueue);
                }

                var message = new StateMachineMessage
                {
                    From = TransportId,
                    To = target,
                    EventName = typeof(TRequest).Name,
                    Payload = MessageSerializer.Serialize(request),
                    PayloadType = MessageSerializer.GetTypeName(typeof(TRequest)),
                    CorrelationId = correlationId,
                    ReplyTo = replyQueue
                };

                await SendAsync(message, cancellationToken);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);
                
                var reply = await tcs.Task.WaitAsync(cts.Token);
                
                if (reply?.Payload != null)
                {
                    return MessageSerializer.Deserialize<TResponse>(reply.Payload);
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            finally
            {
                _pendingRequests.TryRemove(correlationId, out _);
            }
        }

        public async Task<IEnumerable<StateMachineEndpoint>> DiscoverAsync(
            string query = "*",
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default)
        {
            var endpoints = new List<StateMachineEndpoint>();
            
            if (!IsConnected || _channel == null)
                return endpoints;

            try
            {
                // Create discovery queue
                var discoveryQueue = _channel.QueueDeclare(
                    queue: "",
                    durable: false,
                    exclusive: true,
                    autoDelete: true);

                // Bind to discovery exchange
                _channel.QueueBind(
                    queue: discoveryQueue.QueueName,
                    exchange: _exchangeName,
                    routingKey: "discovery.announce");

                // Send discovery request
                var discoveryMessage = new StateMachineMessage
                {
                    From = TransportId,
                    To = "discovery.request",
                    EventName = "DISCOVER",
                    Payload = MessageSerializer.Serialize(query)
                };
                
                await SendAsync(discoveryMessage, cancellationToken);

                // Collect responses
                var endTime = DateTime.UtcNow.Add(timeout == default ? TimeSpan.FromSeconds(3) : timeout);
                var consumer = new EventingBasicConsumer(_channel);
                var receivedEndpoints = new ConcurrentBag<StateMachineEndpoint>();
                
                consumer.Received += (sender, args) =>
                {
                    try
                    {
                        var message = MessageSerializer.DeserializeMessage(args.Body.ToArray());
                        if (message?.EventName == "ANNOUNCE" && message.Payload != null)
                        {
                            var endpoint = MessageSerializer.Deserialize<StateMachineEndpoint>(message.Payload);
                            if (endpoint != null)
                            {
                                receivedEndpoints.Add(endpoint);
                            }
                        }
                    }
                    catch { }
                };

                _channel.BasicConsume(
                    queue: discoveryQueue.QueueName,
                    autoAck: true,
                    consumer: consumer);

                while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken);
                }

                endpoints.AddRange(receivedEndpoints);
            }
            catch { }

            return endpoints;
        }

        public async Task RegisterAsync(StateMachineEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                return;

            endpoint.Location = MachineLocation.Remote;
            
            // Announce presence
            var announceMessage = new StateMachineMessage
            {
                From = TransportId,
                To = "discovery.announce",
                EventName = "ANNOUNCE",
                Payload = MessageSerializer.Serialize(endpoint)
            };
            
            await SendAsync(announceMessage, cancellationToken);

            // Setup periodic heartbeat
            _ = Task.Run(async () =>
            {
                while (IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(30000, cancellationToken);
                    endpoint.LastHeartbeat = DateTime.UtcNow;
                    await SendAsync(announceMessage, cancellationToken);
                }
            }, cancellationToken);
        }

        public Task<TransportHealth> GetHealthAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TransportHealth
            {
                IsHealthy = IsConnected,
                Latency = TimeSpan.FromMilliseconds(5), // RabbitMQ typical latency
                Diagnostics = new Dictionary<string, object>
                {
                    ["QueueName"] = _queueName,
                    ["ExchangeName"] = _exchangeName,
                    ["PendingRequests"] = _pendingRequests.Count,
                    ["ReceivedMessages"] = _receivedMessages.Count,
                    ["ConnectionState"] = _connection?.IsOpen ?? false
                }
            });
        }

        private void OnMessageReceived(object? sender, BasicDeliverEventArgs e)
        {
            try
            {
                var message = MessageSerializer.DeserializeMessage(e.Body.ToArray());
                if (message != null)
                {
                    // Check if this is a response to a pending request
                    if (!string.IsNullOrEmpty(e.BasicProperties?.CorrelationId) && 
                        _pendingRequests.TryRemove(e.BasicProperties.CorrelationId, out var tcs))
                    {
                        tcs.SetResult(message);
                    }
                    else
                    {
                        _receivedMessages.Add(message);
                        MessageReceived?.Invoke(this, new MessageReceivedEventArgs { Message = message });
                    }
                    
                    // Acknowledge message
                    _channel?.BasicAck(e.DeliveryTag, false);
                }
            }
            catch
            {
                // Reject message
                _channel?.BasicNack(e.DeliveryTag, false, true);
            }
        }

        private bool MatchesPattern(string value, string pattern)
        {
            if (pattern == "*")
                return true;

            if (pattern.EndsWith("*"))
                return value.StartsWith(pattern[..^1]);

            if (pattern.StartsWith("*"))
                return value.EndsWith(pattern[1..]);

            return value == pattern;
        }

        public void Dispose()
        {
            DisconnectAsync().Wait(TimeSpan.FromSeconds(5));
        }
    }
}