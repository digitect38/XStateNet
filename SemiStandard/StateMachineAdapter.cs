using System.Collections.Concurrent;
using XStateNet;

namespace SemiStandard
{
    /// <summary>
    /// Adapter class to provide a simpler API for SEMI standard implementations
    /// Wraps XStateNet's JSON-based StateMachine with a more convenient API
    /// </summary>
    public class StateMachineAdapter
    {
        private XStateNet.StateMachine? _stateMachine;
        private readonly ActionMap _actionMap;
        private readonly GuardMap _guardMap;
        private readonly ConcurrentDictionary<string, Func<ConcurrentDictionary<string, object>, object, bool>> _conditions;
        private readonly string _jsonConfig;
        private ConcurrentDictionary<string, object> _context;
        private string? _currentState;

        public StateMachineAdapter(string jsonConfig)
        {
            _jsonConfig = jsonConfig;
            _actionMap = new ActionMap();
            _guardMap = new GuardMap();
            _conditions = new ConcurrentDictionary<string, Func<ConcurrentDictionary<string, object>, object, bool>>();
            _context = new ConcurrentDictionary<string, object>();
        }

        public void RegisterAction(string name, Action<ConcurrentDictionary<string, object>, dynamic> action)
        {
            var namedAction = new NamedAction(name, (stateMachine) =>
            {
                // Get context from state machine - ContextMap is a ConcurrentDictionary
                var contextMap = stateMachine.ContextMap;
                if (contextMap != null)
                {
                    var context = new ConcurrentDictionary<string, object>();
                    foreach (var kvp in contextMap)
                    {
                        if (kvp.Value != null)
                            context[kvp.Key] = kvp.Value;
                    }
                    var eventData = stateMachine.GetType().GetProperty("LastEvent")?.GetValue(stateMachine);
                    action(context, eventData ?? new { });
                }
            });

            if (!_actionMap.ContainsKey(name))
            {
                _actionMap[name] = new List<NamedAction>();
            }
            _actionMap[name].Add(namedAction);
        }

        public void RegisterCondition(string name, Func<ConcurrentDictionary<string, object>, dynamic, bool> condition)
        {
            _conditions[name] = (context, @event) =>
            {
                _context = context;
                return condition(context, @event);
            };

            // Register as a guard in the GuardMap for XStateNet
            var namedGuard = new NamedGuard(name, (stateMachine) =>
            {
                var contextMap = stateMachine.ContextMap;
                if (contextMap != null)
                {
                    var context = new ConcurrentDictionary<string, object>();
                    foreach (var kvp in contextMap)
                    {
                        if (kvp.Value != null)
                            context[kvp.Key] = kvp.Value;
                    }
                    // Use reflection to get LastEvent property
                    var eventData = stateMachine.GetType().GetProperty("LastEvent")?.GetValue(stateMachine);
                    return _conditions[name](context, eventData ?? new { });
                }
                return false;
            });

            if (!_guardMap.ContainsKey(name))
            {
                _guardMap[name] = namedGuard;
            }
            else
            {
                // GuardMap stores NamedGuard directly, not a list
                // If we need to support multiple guards with same name, we'd need to chain them
                _guardMap[name] = namedGuard;
            }
        }

        public void RegisterActivity(string name, Action<ConcurrentDictionary<string, object>, dynamic> activity)
        {
            // Activities are treated as special actions in XStateNet
            RegisterAction(name, activity);
        }

        public async Task StartAsync()
        {
            if (_stateMachine == null)
            {
                // Convert single quotes to double quotes for JSON parsing
                var jsonConfig = _jsonConfig.Replace("'", "\"");
                // Suppress obsolete warning - this is a legacy adapter class
                // For new code, use ExtendedPureStateMachineFactory with EventBusOrchestrator
#pragma warning disable CS0618
                _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(
                    jsonConfig,
                    threadSafe: false,
                    guidIsolate: false,
                    _actionMap,
                    _guardMap,
                    new ServiceMap(),
                    new DelayMap()
                );
#pragma warning restore CS0618
                await _stateMachine.StartAsync();
                await UpdateCurrentStateAsync();
            }
        }

        // Keep synchronous Start for backward compatibility
        public void Start()
        {
            StartAsync().GetAwaiter().GetResult();
        }

        // Keep synchronous Send methods for backward compatibility
        public void Send(string eventName)
        {
            SendAsync(eventName).GetAwaiter().GetResult();
        }

        public void Send(StateMachineEvent machineEvent)
        {
            SendAsync(machineEvent).GetAwaiter().GetResult();
        }

        public async Task SendAsync(string eventName)
        {
            if (_stateMachine != null)
            {
                await _stateMachine.SendAsync(eventName);
                await UpdateCurrentStateAsync();
            }
        }

        public async Task SendAsync(StateMachineEvent machineEvent)
        {
            if (_stateMachine != null)
            {
                if (machineEvent.Data != null)
                {
                    // Store event data in context for actions to access
                    _context["_eventData"] = machineEvent.Data;
                }
                await _stateMachine.SendAsync(machineEvent.Name, machineEvent.Data);
                await UpdateCurrentStateAsync();
            }
        }

        private void UpdateCurrentState()
        {
            UpdateCurrentStateAsync().GetAwaiter().GetResult();
        }

        private async Task UpdateCurrentStateAsync()
        {
            if (_stateMachine != null)
            {
                // Try to get current state from the state machine
                // XStateNet stores state differently, we'll use the context or internal state
                var currentStateProperty = _stateMachine.GetType().GetProperty("State");
                if (currentStateProperty != null)
                {
                    var stateValue = currentStateProperty.GetValue(_stateMachine);
                    if (stateValue != null)
                    {
                        _currentState = stateValue.ToString();
                    }
                }
                else
                {
                    // Fallback - get active states from state machine
                    var activeStates = _stateMachine.GetActiveStateNames();
                    _currentState = !string.IsNullOrEmpty(activeStates) ? activeStates : "Unknown";
                }
            }
        }

        public IEnumerable<StateNode> CurrentStates
        {
            get
            {
                if (string.IsNullOrEmpty(_currentState))
                {
                    return new[] { new StateNode { Name = "Unknown" } };
                }
                return new[] { new StateNode { Name = _currentState } };
            }
        }

        /// <summary>
        /// Waits for the state machine to reach a specific state and complete all associated actions
        /// </summary>
        /// <param name="stateName">The name of the state to wait for</param>
        /// <param name="timeoutMs">Timeout in milliseconds (default: 5000)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The state name when reached</returns>
        public async Task<string> WaitForStateWithActionsAsync(string stateName, int timeoutMs = 5000, CancellationToken cancellationToken = default)
        {
            if (_stateMachine == null)
            {
                throw new InvalidOperationException("State machine is not started");
            }

            // Delegate to the underlying state machine's implementation
            return await _stateMachine.WaitForStateWithActionsAsync(stateName, timeoutMs, cancellationToken);
        }

        /// <summary>
        /// Synchronous version of WaitForStateWithActionsAsync for backward compatibility
        /// </summary>
        public string WaitForStateWithActions(string stateName, int timeoutMs = 5000)
        {
            return WaitForStateWithActionsAsync(stateName, timeoutMs).GetAwaiter().GetResult();
        }

        public class StateNode
        {
            public string Name { get; set; } = string.Empty;
        }
    }

    /// <summary>
    /// Event class for state machine events with data
    /// </summary>
    public class StateMachineEvent
    {
        public string Name { get; set; } = string.Empty;
        public object? Data { get; set; }
    }

    /// <summary>
    /// Extension class to provide static factory method
    /// </summary>
    public static class StateMachineFactory
    {
        public static StateMachineAdapter Create(string jsonConfig)
        {
            return new StateMachineAdapter(jsonConfig);
        }
    }
}