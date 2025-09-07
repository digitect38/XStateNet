using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetMQ;
using NetMQ.Sockets;
using XStateNet.Distributed.Core;

namespace XStateNet.Distributed.Transports
{
    /// <summary>
    /// ZeroMQ transport for brokerless inter-process and inter-computer communication
    /// </summary>
    public class ZeroMQTransport : IStateMachineTransport, IDisposable
    {
        private RouterSocket? _routerSocket;
        private DealerSocket? _dealerSocket;
        private NetMQPoller? _poller;
        private NetMQBeacon? _beacon;
        
        private readonly ConcurrentDictionary<string, string> _knownEndpoints = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<StateMachineMessage>> _pendingRequests = new();
        private readonly ConcurrentBag<StateMachineMessage> _receivedMessages = new();
        
        private string _address = string.Empty;
        private bool _isConnected;
        private CancellationTokenSource? _cancellationTokenSource;

        public string TransportId { get; } = Guid.NewGuid().ToString();
        public TransportType Type => TransportType.ZeroMQ;
        public bool IsConnected => _isConnected;

        public event EventHandler<ConnectionStatusChangedEventArgs>? ConnectionStatusChanged;
        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

        public Task ConnectAsync(string address, CancellationToken cancellationToken = default)
        {
            if (_isConnected)
                throw new InvalidOperationException("Transport is already connected");

            try
            {
                _address = address;
                _cancellationTokenSource = new CancellationTokenSource();
                _poller = new NetMQPoller();

                // Determine socket type based on address
                if (address.Contains("bind"))
                {
                    // Server mode - use Router socket
                    var bindAddress = address.Replace("bind:", "");
                    _routerSocket = new RouterSocket();
                    _routerSocket.Bind(bindAddress);
                    _routerSocket.ReceiveReady += OnRouterReceiveReady;
                    _poller.Add(_routerSocket);
                }
                else
                {
                    // Client mode - use Dealer socket
                    _dealerSocket = new DealerSocket();
                    _dealerSocket.Options.Identity = Encoding.UTF8.GetBytes(TransportId);
                    _dealerSocket.Connect(address);
                    _dealerSocket.ReceiveReady += OnDealerReceiveReady;
                    _poller.Add(_dealerSocket);
                }

                // Start beacon for discovery
                StartBeacon();

                // Start poller in background
                _poller.RunAsync();
                
                _isConnected = true;
                ConnectionStatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs { IsConnected = true });

                return Task.CompletedTask;
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

            _isConnected = false;
            _cancellationTokenSource?.Cancel();

            StopBeacon();
            
            _poller?.Stop();
            _poller?.Dispose();
            
            _routerSocket?.Close();
            _routerSocket?.Dispose();
            
            _dealerSocket?.Close();
            _dealerSocket?.Dispose();

            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs { IsConnected = false });

            return Task.CompletedTask;
        }

        public Task<bool> SendAsync(StateMachineMessage message, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
                return Task.FromResult(false);

            try
            {
                var data = MessageSerializer.SerializeMessage(message);
                var frames = new List<byte[]>();

                if (_routerSocket != null)
                {
                    // Router needs identity frame
                    if (_knownEndpoints.TryGetValue(message.To, out var identity))
                    {
                        frames.Add(Encoding.UTF8.GetBytes(identity));
                    }
                    else
                    {
                        // Broadcast to all known endpoints
                        foreach (var id in _knownEndpoints.Values)
                        {
                            _routerSocket.SendMoreFrame(Encoding.UTF8.GetBytes(id));
                            _routerSocket.SendFrame(data);
                        }
                        return Task.FromResult(true);
                    }
                }

                // Add message data
                frames.Add(data);

                // Send frames
                if (_routerSocket != null)
                {
                    for (int i = 0; i < frames.Count - 1; i++)
                    {
                        _routerSocket.SendMoreFrame(frames[i]);
                    }
                    _routerSocket.SendFrame(frames.Last());
                }
                else if (_dealerSocket != null)
                {
                    _dealerSocket.SendFrame(data);
                }

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
            while (!cancellationToken.IsCancellationRequested && _isConnected)
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
                var message = new StateMachineMessage
                {
                    From = TransportId,
                    To = target,
                    EventName = typeof(TRequest).Name,
                    Payload = MessageSerializer.Serialize(request),
                    PayloadType = MessageSerializer.GetTypeName(typeof(TRequest)),
                    CorrelationId = correlationId,
                    ReplyTo = TransportId
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
            
            if (_beacon != null)
            {
                // Send discovery request
                _beacon.Publish($"DISCOVER:{query}");
                
                var endTime = DateTime.UtcNow.Add(timeout == default ? TimeSpan.FromSeconds(3) : timeout);
                
                while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
                {
                    // NetMQBeacon doesn't have ReceiveString method
                    // We'll handle discovery through regular messaging for now
                    // This is a simplified implementation
                    await Task.Delay(100, cancellationToken);
                    
                    await Task.Delay(10, cancellationToken);
                }
            }

            return endpoints;
        }

        public Task RegisterAsync(StateMachineEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            _knownEndpoints[endpoint.Id] = endpoint.Address;
            return Task.CompletedTask;
        }

        public Task<TransportHealth> GetHealthAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TransportHealth
            {
                IsHealthy = _isConnected,
                Latency = TimeSpan.FromMilliseconds(1), // ZeroMQ typical latency
                Diagnostics = new Dictionary<string, object>
                {
                    ["KnownEndpoints"] = _knownEndpoints.Count,
                    ["PendingRequests"] = _pendingRequests.Count,
                    ["ReceivedMessages"] = _receivedMessages.Count
                }
            });
        }

        private void OnRouterReceiveReady(object? sender, NetMQSocketEventArgs e)
        {
            try
            {
                var message = new NetMQMessage();
                e.Socket.TryReceiveMultipartMessage(ref message);
                
                if (message.FrameCount >= 2)
                {
                    var identity = message[0].ConvertToString();
                    var data = message[1].ToByteArray();
                    
                    // Remember this endpoint
                    if (!_knownEndpoints.ContainsKey(identity))
                    {
                        _knownEndpoints[identity] = identity;
                    }
                    
                    ProcessReceivedMessage(data);
                }
            }
            catch { }
        }

        private void OnDealerReceiveReady(object? sender, NetMQSocketEventArgs e)
        {
            try
            {
                var data = e.Socket.ReceiveFrameBytes();
                ProcessReceivedMessage(data);
            }
            catch { }
        }

        private void ProcessReceivedMessage(byte[] data)
        {
            var message = MessageSerializer.DeserializeMessage(data);
            if (message != null)
            {
                // Check if this is a response to a pending request
                if (!string.IsNullOrEmpty(message.CorrelationId) && 
                    _pendingRequests.TryRemove(message.CorrelationId, out var tcs))
                {
                    tcs.SetResult(message);
                }
                else
                {
                    _receivedMessages.Add(message);
                    MessageReceived?.Invoke(this, new MessageReceivedEventArgs { Message = message });
                }
            }
        }

        private void StartBeacon()
        {
            try
            {
                _beacon = new NetMQBeacon();
                _beacon.Configure(9999);
                _beacon.Subscribe("DISCOVER:");
                
                Task.Run(async () =>
                {
                    while (_isConnected && !_cancellationTokenSource!.Token.IsCancellationRequested)
                    {
                        _beacon.Publish($"ANNOUNCE:{TransportId}:{_address}");
                        await Task.Delay(5000, _cancellationTokenSource.Token);
                    }
                });
            }
            catch { }
        }

        private void StopBeacon()
        {
            try
            {
                _beacon?.Unsubscribe();
                _beacon?.Dispose();
                _beacon = null;
            }
            catch { }
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
            _cancellationTokenSource?.Dispose();
        }
    }
}