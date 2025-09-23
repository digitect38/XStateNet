using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.Distributed.Core;
using XStateNet.Distributed.Transports;
using Microsoft.Extensions.Logging;

namespace XStateNet.Distributed
{
    /// <summary>
    /// Distributed state machine with location transparency (uses composition)
    /// </summary>
    public class DistributedStateMachine : IDisposable
    {
        private readonly IStateMachine _stateMachine;
        private IStateMachineTransport? _transport;
        private readonly ConcurrentDictionary<string, IStateMachineTransport> _transportCache = new();
        private readonly string _machineId;
        private readonly string _address;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _messageProcessingTask;
        private readonly ILogger<DistributedStateMachine>? _logger;

        /// <summary>
        /// Access to the underlying state machine
        /// </summary>
        public IStateMachine StateMachine => _stateMachine;

        /// <summary>
        /// Create a distributed state machine
        /// </summary>
        /// <param name="machineId">Unique identifier for this machine</param>
        /// <param name="address">Address for communication (e.g., "local://machine1", "tcp://localhost:5555", "amqp://localhost")</param>
        /// <param name="logger">Optional logger</param>
        public DistributedStateMachine(IStateMachine stateMachine, string machineId, string? address = null, ILogger<DistributedStateMachine>? logger = null)
        {
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _machineId = machineId;
            _address = address ?? $"local://{machineId}";
            _logger = logger;
            
            InitializeTransport();
        }

        /// <summary>
        /// Initialize transport based on address
        /// </summary>
        private void InitializeTransport()
        {
            // Auto-select transport based on address
            if (_address.StartsWith("local://"))
            {
                _transport = new InMemoryTransport();
            }
            else if (_address.StartsWith("tcp://") || _address.StartsWith("ipc://") || _address.Contains("bind:"))
            {
                _transport = new ZeroMQTransport();
            }
            else if (_address.StartsWith("amqp://") || _address.StartsWith("rabbitmq://"))
            {
                _transport = new RabbitMQTransport();
            }
            else
            {
                // Default to in-memory
                _transport = new InMemoryTransport();
            }

            _logger?.LogInformation("Initialized {TransportType} transport for machine {MachineId} at {Address}", 
                _transport.Type, _machineId, _address);
        }

        /// <summary>
        /// Start the distributed state machine
        /// </summary>
        public void Start()
        {
            _stateMachine.Start();
            
            _cancellationTokenSource = new CancellationTokenSource();
            
            // Connect transport
            Task.Run(async () =>
            {
                try
                {
                    await _transport!.ConnectAsync(_address, _cancellationTokenSource.Token);
                    
                    // Register this machine
                    await _transport.RegisterAsync(new StateMachineEndpoint
                    {
                        Id = _machineId,
                        Address = _address,
                        Location = DetermineLocation(),
                        Metadata = new ConcurrentDictionary<string, string>
                        {
                            ["Version"] = "1.0",
                            ["Type"] = this.GetType().Name
                        }
                    }, _cancellationTokenSource.Token);
                    
                    // Start message processing
                    _messageProcessingTask = ProcessIncomingMessages(_cancellationTokenSource.Token);
                    
                    _logger?.LogInformation("Distributed state machine {MachineId} started", _machineId);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to start distributed state machine {MachineId}", _machineId);
                    throw;
                }
            }).Wait(TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Send event to a remote state machine
        /// </summary>
        public async Task SendToMachineAsync(string targetMachine, string eventName, object? payload = null, CancellationToken cancellationToken = default)
        {
            var message = new StateMachineMessage
            {
                From = _machineId,
                To = targetMachine,
                EventName = eventName,
                Payload = payload != null ? MessageSerializer.Serialize(payload) : null,
                PayloadType = payload?.GetType().FullName
            };

            // Get appropriate transport for target
            var transport = await GetTransportForTarget(targetMachine);
            var success = await transport.SendAsync(message, cancellationToken);
            
            if (success)
            {
                _logger?.LogDebug("Sent event {EventName} from {From} to {To}", eventName, _machineId, targetMachine);
            }
            else
            {
                _logger?.LogWarning("Failed to send event {EventName} from {From} to {To}", eventName, _machineId, targetMachine);
            }
        }

        /// <summary>
        /// Send event locally or remotely based on target
        /// </summary>
        public void Send(string eventName)
        {
            // Check if this is a remote event (format: "machine@event")
            if (eventName.Contains('@'))
            {
                var parts = eventName.Split('@');
                if (parts.Length == 2)
                {
                    var targetMachine = parts[0];
                    var actualEvent = parts[1];
                    Task.Run(async () => await SendToMachineAsync(targetMachine, actualEvent));
                    return;
                }
            }

            // Local event
            _stateMachine.Send(eventName);
        }

        /// <summary>
        /// Send event asynchronously
        /// </summary>
        public async Task SendAsync(string eventName, object? eventData = null)
        {
            // Check if this is a remote event (format: "machine@event")
            if (eventName.Contains('@'))
            {
                var parts = eventName.Split('@');
                if (parts.Length == 2)
                {
                    var targetMachine = parts[0];
                    var actualEvent = parts[1];
                    await SendToMachineAsync(targetMachine, actualEvent, eventData);
                    return;
                }
            }

            // Local event
            await _stateMachine.SendAsync(eventName, eventData);
        }

        /// <summary>
        /// Send event asynchronously and return the new state
        /// </summary>
        public async Task<string> SendAsyncWithState(string eventName, object? eventData = null)
        {
            // Check if this is a remote event (format: "machine@event")
            if (eventName.Contains('@'))
            {
                var parts = eventName.Split('@');
                if (parts.Length == 2)
                {
                    var targetMachine = parts[0];
                    var actualEvent = parts[1];
                    await SendToMachineAsync(targetMachine, actualEvent, eventData);
                    // For remote events, return current state since we can't know remote state
                    return _stateMachine.GetActiveStateString();
                }
            }

            // Local event
            return await _stateMachine.SendAsyncWithState(eventName, eventData);
        }

        /// <summary>
        /// Request-Response pattern
        /// </summary>
        public async Task<TResponse?> RequestAsync<TRequest, TResponse>(
            string targetMachine, 
            TRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
            where TRequest : class
            where TResponse : class
        {
            var transport = await GetTransportForTarget(targetMachine);
            return await transport.RequestAsync<TRequest, TResponse>(targetMachine, request, timeout, cancellationToken);
        }

        /// <summary>
        /// Subscribe to events from other machines
        /// </summary>
        public async IAsyncEnumerable<StateMachineMessage> SubscribeAsync(string pattern, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_transport == null)
                yield break;

            await foreach (var message in _transport.SubscribeAsync(pattern, cancellationToken))
            {
                yield return message;
            }
        }

        /// <summary>
        /// Discover other state machines
        /// </summary>
        public async Task<IEnumerable<StateMachineEndpoint>> DiscoverMachinesAsync(string query = "*", TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            if (_transport == null)
                return Enumerable.Empty<StateMachineEndpoint>();

            return await _transport.DiscoverAsync(query, timeout ?? TimeSpan.FromSeconds(3), cancellationToken);
        }

        /// <summary>
        /// Get health status
        /// </summary>
        public async Task<TransportHealth?> GetHealthAsync(CancellationToken cancellationToken = default)
        {
            return _transport != null ? await _transport.GetHealthAsync(cancellationToken) : null;
        }

        /// <summary>
        /// Process incoming messages
        /// </summary>
        private async Task ProcessIncomingMessages(CancellationToken cancellationToken)
        {
            if (_transport == null)
                return;

            try
            {
                await foreach (var message in _transport.SubscribeAsync(_machineId, cancellationToken))
                {
                    try
                    {
                        _logger?.LogDebug("Received event {EventName} from {From}", message.EventName, message.From);
                        
                        // Process the event in the state machine
                        _stateMachine.Send(message.EventName);
                        
                        // If this is a request, send response
                        if (!string.IsNullOrEmpty(message.ReplyTo) && !string.IsNullOrEmpty(message.CorrelationId))
                        {
                            // TODO: Implement response logic based on state machine result
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error processing message {MessageId}", message.Id);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in message processing loop");
            }
        }

        /// <summary>
        /// Get or create transport for target
        /// </summary>
        private async Task<IStateMachineTransport> GetTransportForTarget(string target)
        {
            // For now, use the same transport
            // In future, this could select different transports based on target location
            if (_transport != null)
                return _transport;

            throw new InvalidOperationException("Transport not initialized");
        }

        /// <summary>
        /// Determine machine location
        /// </summary>
        private MachineLocation DetermineLocation()
        {
            if (_address.StartsWith("local://"))
                return MachineLocation.SameProcess;
            
            if (_address.StartsWith("ipc://"))
                return MachineLocation.SameMachine;
            
            return MachineLocation.Remote;
        }

        /// <summary>
        /// Stop the distributed state machine
        /// </summary>
        public void Stop()
        {
            _stateMachine.Stop();
            
            _cancellationTokenSource?.Cancel();
            
            try
            {
                _messageProcessingTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch { }
            
            _transport?.DisconnectAsync().Wait(TimeSpan.FromSeconds(5));
            _transport?.Dispose();
            
            _logger?.LogInformation("Distributed state machine {MachineId} stopped", _machineId);
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Stop();
                _cancellationTokenSource?.Dispose();
                _transport?.Dispose();
                
                foreach (var transport in _transportCache.Values)
                {
                    transport.Dispose();
                }
                
                _stateMachine?.Dispose();
            }
        }
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Factory for creating distributed state machines
    /// </summary>
    public static class DistributedStateMachineFactory
    {
        /// <summary>
        /// Create a distributed state machine from JSON
        /// </summary>
        public static DistributedStateMachine CreateFromScript(
            string machineId,
            string jsonScript,
            string? address = null,
            ActionMap? actionCallbacks = null,
            GuardMap? guardCallbacks = null,
            ServiceMap? serviceCallbacks = null,
            DelayMap? delayCallbacks = null,
            ILogger<DistributedStateMachine>? logger = null)
        {
            // Create the base state machine from script
            // Note: CreateFromScript would require a factory or builder pattern
            // For now, this is a placeholder
            throw new NotImplementedException("CreateFromScript requires implementation of StateMachine factory");

            // // Wrap it in a distributed state machine
            // var distributedMachine = new DistributedStateMachine(baseMachine, machineId, address, logger);
            //
            // return distributedMachine;
        }
    }
}