// Note: XStateNet.Distributed namespace is in a separate assembly
// This file uses reflection/dynamic loading to avoid hard dependency

namespace XStateNet.Orchestration
{
    /// <summary>
    /// Unified factory for creating state machines with location transparency
    /// Automatically selects appropriate transport based on configuration
    /// </summary>
    public class UnifiedStateMachineFactory
    {
        private readonly IMessageBus _messageBus;
        private readonly TransportType _transportType;

        public UnifiedStateMachineFactory(TransportType transportType = TransportType.InProcess)
        {
            _transportType = transportType;
            _messageBus = CreateMessageBus(transportType);
        }

        public UnifiedStateMachineFactory(IMessageBus messageBus, TransportType transportType)
        {
            _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
            _transportType = transportType;
        }

        /// <summary>
        /// Create a state machine with location transparency
        /// </summary>
        public async Task<UnifiedStateMachine> CreateAsync(
            string machineId,
            string jsonScript,
            Dictionary<string, Action<OrchestratedContext>>? actions = null,
            Dictionary<string, Func<OrchestratedContext, bool>>? guards = null)
        {
            await _messageBus.ConnectAsync();

            IPureStateMachine machine;

            if (_transportType == TransportType.InProcess)
            {
                // Use orchestrator pattern for in-process
                var orchestrator = (_messageBus as InProcessMessageBus)?.GetOrchestrator()
                    ?? throw new InvalidOperationException("InProcessMessageBus required for InProcess transport");

                machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                    id: machineId,
                    json: jsonScript,
                    orchestrator: orchestrator,
                    orchestratedActions: actions
                // Note: guards parameter removed - if needed, use full signature
                );
            }
            else
            {
                // Distributed transport requires using CreateDistributedAsync() method
                // or using the separate XStateNet.Distributed.UnifiedStateMachineFactory
                throw new NotSupportedException(
                    "For distributed transports (InterProcess/InterNode), please use:\n" +
                    "1. XStateNet.Distributed.DistributedPureStateMachineFactory.CreateFromScriptAsync() directly, OR\n" +
                    "2. Reference XStateNet.Distributed assembly and use the distributed factory extension.");
            }

            return new UnifiedStateMachine(machine, _messageBus, machineId);
        }

        /// <summary>
        /// Create message bus based on transport type
        /// </summary>
        private static IMessageBus CreateMessageBus(TransportType transportType)
        {
            return transportType switch
            {
                TransportType.InProcess => new InProcessMessageBus(),
                TransportType.InterProcess => throw new NotSupportedException(
                    "InterProcess transport requires XStateNet.Distributed assembly. " +
                    "Create DistributedMessageBus manually and pass to constructor."),
                TransportType.InterNode => throw new NotSupportedException(
                    "InterNode transport requires XStateNet.Distributed assembly. " +
                    "Create DistributedMessageBus manually and pass to constructor."),
                _ => throw new ArgumentException($"Unknown transport type: {transportType}")
            };
        }
    }

    /// <summary>
    /// Unified state machine wrapper providing consistent API across all transports
    /// </summary>
    public class UnifiedStateMachine : IDisposable
    {
        private readonly IPureStateMachine _machine;
        private readonly IMessageBus _messageBus;
        private readonly string _machineId;

        public UnifiedStateMachine(IPureStateMachine machine, IMessageBus messageBus, string machineId)
        {
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
            _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
            _machineId = machineId;
        }

        public string MachineId => _machineId;

        /// <summary>
        /// Send event to another machine (location transparent)
        /// </summary>
        public Task SendToAsync(string targetMachineId, string eventName, object? payload = null)
        {
            return _messageBus.SendEventAsync(_machineId, targetMachineId, eventName, payload);
        }

        /// <summary>
        /// Subscribe to events for this machine
        /// </summary>
        public Task<IDisposable> SubscribeAsync(Func<MachineEvent, Task> handler)
        {
            return _messageBus.SubscribeAsync(_machineId, handler);
        }

        /// <summary>
        /// Get current state (wrapped)
        /// </summary>
        public string GetCurrentState()
        {
            return _machine.CurrentState;
        }

        /// <summary>
        /// Wait for state with timeout (polling implementation)
        /// </summary>
        public async Task<string> WaitForStateAsync(string stateName, int timeoutMs = 5000)
        {
            var cts = new System.Threading.CancellationTokenSource(timeoutMs);
            while (!cts.Token.IsCancellationRequested)
            {
                var currentState = _machine.CurrentState;
                if (currentState.Contains(stateName))
                    return currentState;

                try
                {
                    await Task.Delay(50, cts.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
            throw new TimeoutException($"Timeout waiting for state '{stateName}'");
        }

        public void Dispose()
        {
            // Note: We don't dispose the message bus here as it may be shared
            // The factory or application code should dispose the bus
        }
    }

    /// <summary>
    /// Extension method for InProcessMessageBus to expose orchestrator
    /// </summary>
    public static class InProcessMessageBusExtensions
    {
        private static readonly System.Reflection.FieldInfo OrchestratorField =
            typeof(InProcessMessageBus).GetField("_orchestrator",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        public static EventBusOrchestrator? GetOrchestrator(this InProcessMessageBus bus)
        {
            return OrchestratorField?.GetValue(bus) as EventBusOrchestrator;
        }
    }
}
