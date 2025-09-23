using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using XStateNet.Distributed.Core;

namespace XStateNet.Distributed.Transports
{
    /// <summary>
    /// In-memory transport for inter-thread communication within the same process
    /// </summary>
    public class InMemoryTransport : IStateMachineTransport
    {
        private static readonly ConcurrentDictionary<string, Channel<StateMachineMessage>> _channels = new();
        private static readonly ConcurrentDictionary<string, StateMachineEndpoint> _registry = new();
        
        private Channel<StateMachineMessage>? _receiveChannel;
        private readonly ConcurrentDictionary<string, List<string>> _subscriptions = new();
        private bool _isConnected;
        private string _address = string.Empty;

        public string TransportId { get; } = Guid.NewGuid().ToString();
        public TransportType Type => TransportType.InMemory;
        public bool IsConnected => _isConnected;

        public event EventHandler<ConnectionStatusChangedEventArgs>? ConnectionStatusChanged;
        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
        
        /// <summary>
        /// Clear all registered endpoints - for testing purposes
        /// </summary>
        public static void ClearRegistry()
        {
            _registry.Clear();
            _channels.Clear();
        }

        public Task ConnectAsync(string address, CancellationToken cancellationToken = default)
        {
            if (_isConnected)
                throw new InvalidOperationException("Transport is already connected");

            _address = address;
            _receiveChannel = Channel.CreateUnbounded<StateMachineMessage>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false
            });

            _channels[address] = _receiveChannel;
            _isConnected = true;

            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs { IsConnected = true });

            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
                return Task.CompletedTask;

            _isConnected = false;
            _channels.TryRemove(_address, out _);
            _registry.TryRemove(_address, out _);
            _receiveChannel?.Writer.TryComplete();

            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs { IsConnected = false });

            return Task.CompletedTask;
        }

        public Task<bool> SendAsync(StateMachineMessage message, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
                return Task.FromResult(false);

            // Direct send to target
            if (_channels.TryGetValue(message.To, out var targetChannel))
            {
                return targetChannel.Writer.TryWrite(message) ? Task.FromResult(true) : Task.FromResult(false);
            }

            // Check pattern subscriptions
            foreach (var (pattern, subscribers) in _subscriptions)
            {
                if (MatchesPattern(message.To, pattern))
                {
                    foreach (var subscriber in subscribers)
                    {
                        if (_channels.TryGetValue(subscriber, out var subChannel))
                        {
                            subChannel.Writer.TryWrite(message);
                        }
                    }
                    return Task.FromResult(true);
                }
            }

            return Task.FromResult(false);
        }

        public async Task<StateMachineMessage?> ReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (!_isConnected || _receiveChannel == null)
                return null;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                var message = await _receiveChannel.Reader.ReadAsync(cts.Token);
                MessageReceived?.Invoke(this, new MessageReceivedEventArgs { Message = message });
                return message;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public async IAsyncEnumerable<StateMachineMessage> SubscribeAsync(
            string pattern,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!_isConnected || _receiveChannel == null)
                yield break;

            // Add subscription
            if (!_subscriptions.ContainsKey(pattern))
                _subscriptions[pattern] = new List<string>();
            
            if (!_subscriptions[pattern].Contains(_address))
                _subscriptions[pattern].Add(_address);

            await foreach (var message in _receiveChannel.Reader.ReadAllAsync(cancellationToken))
            {
                if (MatchesPattern(message.EventName, pattern) || MatchesPattern(message.From, pattern))
                {
                    MessageReceived?.Invoke(this, new MessageReceivedEventArgs { Message = message });
                    yield return message;
                }
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
            var replyTo = $"{_address}.reply.{correlationId}";

            // Create temporary reply channel
            var replyChannel = Channel.CreateUnbounded<StateMachineMessage>();
            _channels[replyTo] = replyChannel;

            try
            {
                // Send request
                var message = new StateMachineMessage
                {
                    From = _address,
                    To = target,
                    EventName = typeof(TRequest).Name,
                    Payload = MessageSerializer.Serialize(request),
                    PayloadType = MessageSerializer.GetTypeName(typeof(TRequest)),
                    CorrelationId = correlationId,
                    ReplyTo = replyTo
                };

                await SendAsync(message, cancellationToken);

                // Wait for response
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);

                var reply = await replyChannel.Reader.ReadAsync(cts.Token);
                
                if (reply.Payload != null)
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
                _channels.TryRemove(replyTo, out _);
            }
        }

        public Task<IEnumerable<StateMachineEndpoint>> DiscoverAsync(
            string query = "*",
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default)
        {
            var endpoints = _registry.Values
                .Where(e => MatchesPattern(e.Id, query))
                .ToList();

            return Task.FromResult<IEnumerable<StateMachineEndpoint>>(endpoints);
        }

        public Task RegisterAsync(StateMachineEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            endpoint.Location = MachineLocation.SameProcess;
            _registry[endpoint.Id] = endpoint;
            return Task.CompletedTask;
        }

        public Task<TransportHealth> GetHealthAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TransportHealth
            {
                IsHealthy = _isConnected,
                Latency = TimeSpan.Zero,
                Diagnostics = new ConcurrentDictionary<string, object>
                {
                    ["ActiveChannels"] = _channels.Count,
                    ["RegisteredEndpoints"] = _registry.Count,
                    ["Subscriptions"] = _subscriptions.Count
                }
            });
        }

        public void Dispose()
        {
            DisconnectAsync().Wait(TimeSpan.FromSeconds(5));
            _receiveChannel = null;
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
    }
}