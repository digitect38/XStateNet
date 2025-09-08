using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly Dictionary<string, Func<Dictionary<string, object>, object, bool>> _conditions;
        private readonly string _jsonConfig;
        private Dictionary<string, object> _context;
        private string? _currentState;
        
        public StateMachineAdapter(string jsonConfig)
        {
            _jsonConfig = jsonConfig;
            _actionMap = new ActionMap();
            _guardMap = new GuardMap();
            _conditions = new Dictionary<string, Func<Dictionary<string, object>, object, bool>>();
            _context = new Dictionary<string, object>();
        }
        
        public void RegisterAction(string name, Action<Dictionary<string, object>, dynamic> action)
        {
            var namedAction = new NamedAction(name, (stateMachine) =>
            {
                // Get context from state machine - ContextMap is a ConcurrentDictionary
                var contextMap = stateMachine.ContextMap;
                if (contextMap != null)
                {
                    var context = new Dictionary<string, object>();
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
        
        public void RegisterCondition(string name, Func<Dictionary<string, object>, dynamic, bool> condition)
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
                    var context = new Dictionary<string, object>();
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
        
        public void RegisterActivity(string name, Action<Dictionary<string, object>, dynamic> activity)
        {
            // Activities are treated as special actions in XStateNet
            RegisterAction(name, activity);
        }
        
        public void Start()
        {
            if (_stateMachine == null)
            {
                // Convert single quotes to double quotes for JSON parsing
                var jsonConfig = _jsonConfig.Replace("'", "\"");
                _stateMachine = XStateNet.StateMachine.CreateFromScript(
                    jsonConfig, 
                    _actionMap,
                    _guardMap,
                    new ServiceMap(),
                    new DelayMap()
                );
                _stateMachine.Start();
                UpdateCurrentState();
            }
        }
        
        public void Send(string eventName)
        {
            _stateMachine?.Send(eventName);
            UpdateCurrentState();
        }
        
        public void Send(StateMachineEvent machineEvent)
        {
            if (machineEvent.Data != null)
            {
                // Store event data in context for actions to access
                _context["_eventData"] = machineEvent.Data;
            }
            _stateMachine?.Send(machineEvent.Name);
            UpdateCurrentState();
        }
        
        private void UpdateCurrentState()
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
                    // Fallback - use context to track state
                    _currentState = _context.ContainsKey("_currentState") ? 
                        _context["_currentState"]?.ToString() ?? "Unknown" : "Unknown";
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