using XStateNet.Orchestration;

namespace XStateNet.Distributed
{
    /// <summary>
    /// Factory for creating distributed pure state machines with orchestrated pattern
    /// Follows same pattern as ExtendedPureStateMachineFactory but for distributed scenarios
    /// </summary>
    public static class DistributedPureStateMachineFactory
    {
        /// <summary>
        /// Create a distributed pure state machine with orchestrated actions and guards
        /// Uses IMessageBus for location-transparent communication
        /// </summary>
        public static async Task<IPureStateMachine> CreateFromScriptAsync(
            string machineId,
            string json,
            IMessageBus messageBus,
            Dictionary<string, Action<DistributedOrchestratedContext>>? orchestratedActions = null,
            Dictionary<string, Func<DistributedOrchestratedContext, bool>>? orchestratedGuards = null,
            Dictionary<string, Func<DistributedOrchestratedContext, Task>>? orchestratedServices = null)
        {
            if (string.IsNullOrEmpty(machineId))
                throw new ArgumentException("Machine ID is required", nameof(machineId));
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("JSON script is required", nameof(json));
            if (messageBus == null)
                throw new ArgumentNullException(nameof(messageBus));

            // Connect to message bus
            await messageBus.ConnectAsync();

            // Create the pure state machine wrapper
            var machine = new DistributedPureStateMachine(
                machineId,
                json,
                messageBus,
                orchestratedActions,
                orchestratedGuards,
                orchestratedServices
            );

            // Subscribe to incoming events
            await machine.InitializeAsync();

            return machine;
        }

        /// <summary>
        /// Create multiple distributed machines that can communicate with each other
        /// </summary>
        public static async Task<Dictionary<string, IPureStateMachine>> CreateMachineGroupAsync(
            IMessageBus messageBus,
            params (string machineId, string json, Dictionary<string, Action<DistributedOrchestratedContext>>? actions)[] machines)
        {
            var result = new Dictionary<string, IPureStateMachine>();

            foreach (var (machineId, json, actions) in machines)
            {
                var machine = await CreateFromScriptAsync(machineId, json, messageBus, actions);
                result[machineId] = machine;
            }

            return result;
        }
    }

    /// <summary>
    /// Distributed pure state machine implementation
    /// </summary>
    internal class DistributedPureStateMachine : IPureStateMachine
    {
        private readonly string _machineId;
        private readonly string _json;
        private readonly IMessageBus _messageBus;
        private readonly Dictionary<string, Action<DistributedOrchestratedContext>>? _orchestratedActions;
        private readonly Dictionary<string, Func<DistributedOrchestratedContext, bool>>? _orchestratedGuards;
        private readonly Dictionary<string, Func<DistributedOrchestratedContext, Task>>? _orchestratedServices;

        // Internal state machine (using old API internally, but wrapped)
        private StateMachine? _internalMachine;
        private IDisposable? _subscription;
        private bool _disposed;

        public DistributedPureStateMachine(
            string machineId,
            string json,
            IMessageBus messageBus,
            Dictionary<string, Action<DistributedOrchestratedContext>>? orchestratedActions,
            Dictionary<string, Func<DistributedOrchestratedContext, bool>>? orchestratedGuards,
            Dictionary<string, Func<DistributedOrchestratedContext, Task>>? orchestratedServices)
        {
            _machineId = machineId;
            _json = json;
            _messageBus = messageBus;
            _orchestratedActions = orchestratedActions;
            _orchestratedGuards = orchestratedGuards;
            _orchestratedServices = orchestratedServices;
        }

        public async Task InitializeAsync()
        {
            // Convert orchestrated actions to ActionMap
            var actionMap = new ActionMap();
            if (_orchestratedActions != null)
            {
                foreach (var kvp in _orchestratedActions)
                {
                    var actionName = kvp.Key;
                    var orchestratedAction = kvp.Value;

                    var namedAction = new NamedAction(actionName, (sm) =>
                    {
                        var ctx = new DistributedOrchestratedContext(_machineId, _messageBus, this);
                        orchestratedAction(ctx);
                    });

                    if (!actionMap.ContainsKey(actionName))
                        actionMap[actionName] = new List<NamedAction>();
                    actionMap[actionName].Add(namedAction);
                }
            }

            // Convert orchestrated guards to GuardMap
            var guardMap = new GuardMap();
            if (_orchestratedGuards != null)
            {
                foreach (var kvp in _orchestratedGuards)
                {
                    var guardName = kvp.Key;
                    var orchestratedGuard = kvp.Value;

                    var namedGuard = new NamedGuard(guardName, (sm) =>
                    {
                        var ctx = new DistributedOrchestratedContext(_machineId, _messageBus, this);
                        return orchestratedGuard(ctx);
                    });

                    guardMap[guardName] = namedGuard;
                }
            }

            // Create internal state machine
            // Suppress obsolete warning - this is internal implementation
#pragma warning disable CS0618
            _internalMachine = StateMachineFactory.CreateFromScript(
                _json,
                threadSafe: false,
                guidIsolate: false,
                actionMap,
                guardMap
            );
#pragma warning restore CS0618

            await _internalMachine.StartAsync();

            // Subscribe to incoming events from message bus
            _subscription = await _messageBus.SubscribeAsync(_machineId, async (evt) =>
            {
                if (_internalMachine != null)
                {
                    await _internalMachine.SendAsync(evt.EventName, evt.Payload);
                }
            });
        }

        // IPureStateMachine interface implementation
        public string Id => _machineId;

        public string CurrentState => _internalMachine?.GetActiveStateNames() ?? "unknown";

        public string CurrentStateName => CurrentState;

        public string MachineId => _machineId;

        public async Task<string> StartAsync()
        {
            if (_internalMachine == null)
                throw new InvalidOperationException("Machine not initialized. Call InitializeAsync first.");

            return CurrentState;
        }

        public void Stop()
        {
            _internalMachine?.Stop();
        }

        public async Task<string> WaitForStateWithActionsAsync(string stateName, int timeoutMs = 5000, System.Threading.CancellationToken cancellationToken = default)
        {
            if (_internalMachine == null)
                throw new InvalidOperationException("Machine not initialized");

            return await _internalMachine.WaitForStateWithActionsAsync(stateName, timeoutMs, cancellationToken);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _subscription?.Dispose();
            _internalMachine?.Stop();
            _disposed = true;
        }
    }
}
