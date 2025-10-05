using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XStateNet.GPU.Core;

// Suppress obsolete warning - GPU bridge infrastructure interfaces with legacy StateMachine
// GPU acceleration requires StateMachineFactory.CreateFromScript for parallel instance management
#pragma warning disable CS0618

namespace XStateNet.GPU.Integration
{
    /// <summary>
    /// Bridge between XStateNet state machines and GPU acceleration
    /// Allows running multiple XStateNet instances in parallel on GPU
    /// </summary>
    public class XStateNetGPUBridge : IDisposable
    {
        private readonly GPUStateMachinePool _gpuPool;
        private readonly ConcurrentDictionary<int, StateMachine> _cpuMachines;
        private readonly string _machineDefinitionJson;
        private GPUStateMachineDefinition _gpuDefinition;
        private ConcurrentDictionary<string, int> _stateNameToId;
        private ConcurrentDictionary<string, int> _eventNameToId;
        private bool _disposed;

        public int InstanceCount => _gpuPool?.InstanceCount ?? 0;

        public XStateNetGPUBridge(string machineDefinitionJson)
        {
            _machineDefinitionJson = machineDefinitionJson;
            _gpuPool = new GPUStateMachinePool();
            _cpuMachines = new ConcurrentDictionary<int, StateMachine>();
            _stateNameToId = new ConcurrentDictionary<string, int>();
            _eventNameToId = new ConcurrentDictionary<string, int>();
        }

        /// <summary>
        /// Initialize GPU pool with multiple XStateNet instances
        /// </summary>
        public async Task InitializeAsync(int instanceCount, ActionMap actions = null)
        {
            // Parse XStateNet definition to extract states and events
            _gpuDefinition = ConvertXStateNetToGPUDefinition(_machineDefinitionJson);

            // Initialize GPU pool
            await _gpuPool.InitializeAsync(instanceCount, _gpuDefinition);

            // Create CPU-side XStateNet instances for validation/comparison
            // Use guidIsolate to ensure unique machine IDs for parallel test execution
            for (int i = 0; i < Math.Min(instanceCount, 10); i++) // Keep first 10 for validation
            {
                var machine = StateMachineFactory.CreateFromScript(_machineDefinitionJson, threadSafe: false, true, actions);
                await machine.StartAsync(); // Start the machine so it can process events
                _cpuMachines[i] = machine;
            }
        }

        /// <summary>
        /// Convert XStateNet JSON definition to GPU-compatible format
        /// </summary>
        private GPUStateMachineDefinition ConvertXStateNetToGPUDefinition(string jsonDefinition)
        {
            dynamic config = JsonConvert.DeserializeObject(jsonDefinition);

            // Extract states
            var states = new List<string>();
            var events = new HashSet<string>();
            var transitions = new List<TransitionEntry>();

            ExtractStatesAndEvents(config, states, events);

            // Create GPU definition
            var definition = new GPUStateMachineDefinition(
                config.id?.ToString() ?? "XStateNetMachine",
                states.Count,
                events.Count
            );

            // Map state names
            for (int i = 0; i < states.Count; i++)
            {
                definition.StateNames[i] = states[i];
                _stateNameToId[states[i]] = i;
            }

            // Map event names
            var eventList = events.ToList();
            for (int i = 0; i < eventList.Count; i++)
            {
                definition.EventNames[i] = eventList[i];
                _eventNameToId[eventList[i]] = i;
            }

            // Build transition table
            BuildTransitionTable(config, definition, transitions);
            definition.TransitionTable = transitions.ToArray();

            return definition;
        }

        private void ExtractStatesAndEvents(dynamic config, List<string> states, HashSet<string> events)
        {
            // Add initial state first if specified
            if (config.initial != null)
            {
                states.Add(config.initial.ToString());
            }

            if (config.states != null)
            {
                foreach (var state in config.states)
                {
                    string stateName = state.Name;

                    // Only add if not already present (initial state might already be added)
                    if (!states.Contains(stateName))
                    {
                        states.Add(stateName);
                    }

                    // Extract events from transitions
                    if (state.Value.on != null)
                    {
                        foreach (var transition in state.Value.on)
                        {
                            events.Add(transition.Name);
                        }
                    }
                }
            }
        }

        private void BuildTransitionTable(dynamic config, GPUStateMachineDefinition definition, List<TransitionEntry> transitions)
        {
            if (config.states == null) return;

            foreach (var state in config.states)
            {
                string fromStateName = state.Name;
                if (!_stateNameToId.ContainsKey(fromStateName)) continue;
                int fromStateId = _stateNameToId[fromStateName];

                if (state.Value.on != null)
                {
                    foreach (var transition in state.Value.on)
                    {
                        string eventName = transition.Name;
                        if (!_eventNameToId.ContainsKey(eventName)) continue;
                        int eventId = _eventNameToId[eventName];

                        string targetState;
                        // Handle both string and object transitions
                        if (transition.Value.Type == Newtonsoft.Json.Linq.JTokenType.String)
                        {
                            targetState = transition.Value.ToString();
                        }
                        else if (transition.Value.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                        {
                            targetState = transition.Value.target?.ToString() ?? fromStateName;
                        }
                        else
                        {
                            targetState = fromStateName;
                        }

                        if (!_stateNameToId.ContainsKey(targetState)) continue;
                        int toStateId = _stateNameToId[targetState];

                        transitions.Add(new TransitionEntry
                        {
                            FromState = fromStateId,
                            EventType = eventId,
                            ToState = toStateId,
                            ActionId = 0, // Could map actions if needed
                            GuardId = 0   // Could map guards if needed
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Send event to specific instance
        /// </summary>
        public void Send(int instanceId, string eventName, object eventData = null)
        {
            _gpuPool.SendEvent(instanceId, eventName);

            // Mirror to CPU instance if tracked
            if (_cpuMachines.ContainsKey(instanceId))
            {
                _cpuMachines[instanceId].Send(eventName, eventData);
            }
        }

        /// <summary>
        /// Send event to all instances
        /// </summary>
        public async Task BroadcastAsync(string eventName, object eventData = null)
        {
            var instanceIds = Enumerable.Range(0, _gpuPool.InstanceCount).ToArray();
            var eventNames = Enumerable.Repeat(eventName, _gpuPool.InstanceCount).ToArray();

            await _gpuPool.SendEventBatchAsync(instanceIds, eventNames);

            // Mirror to CPU instances - but only send to instances we're tracking for validation
            foreach (var kvp in _cpuMachines)
            {
                kvp.Value.Send(eventName, eventData);
            }
        }

        /// <summary>
        /// Process all pending events on GPU
        /// </summary>
        public async Task ProcessEventsAsync()
        {
            await _gpuPool.ProcessEventsAsync();
        }

        /// <summary>
        /// Get current state of an instance
        /// </summary>
        public string GetState(int instanceId)
        {
            return _gpuPool.GetState(instanceId);
        }

        /// <summary>
        /// Get state distribution across all instances
        /// </summary>
        public async Task<ConcurrentDictionary<string, int>> GetStateDistributionAsync()
        {
            return await _gpuPool.GetStateDistributionAsync();
        }

        /// <summary>
        /// Validate GPU results against CPU implementation
        /// </summary>
        public async Task<bool> ValidateConsistencyAsync()
        {
            // Give CPU state machines a moment to process transitions
            await Task.Delay(10);

            foreach (var kvp in _cpuMachines)
            {
                int instanceId = kvp.Key;
                var cpuMachine = kvp.Value;

                string gpuState = _gpuPool.GetState(instanceId);
                string cpuStateRaw = cpuMachine.GetActiveStateNames();
                string cpuState = GetSimpleStateName(cpuStateRaw);

                if (gpuState != cpuState)
                {
                    Console.WriteLine($"Instance {instanceId} mismatch:");
                    Console.WriteLine($"  GPU state: '{gpuState}'");
                    Console.WriteLine($"  CPU state raw: '{cpuStateRaw}'");
                    Console.WriteLine($"  CPU state simple: '{cpuState}'");

                    // Debug: Show all GPU states
                    Console.WriteLine($"  All GPU states:");
                    for (int i = 0; i < _gpuDefinition.StateCount; i++)
                    {
                        Console.WriteLine($"    [{i}] = '{_gpuDefinition.StateNames[i]}'");
                    }

                    return false;
                }
            }

            return true;
        }

        private string GetSimpleStateName(string fullStateName)
        {
            if (string.IsNullOrEmpty(fullStateName)) return "";

            // Remove the machine ID prefix (e.g., "#TrafficLight.red" -> "red")
            // First remove the # if present
            if (fullStateName.StartsWith("#"))
            {
                fullStateName = fullStateName.Substring(1);
            }

            // Then extract state name after last dot
            int lastDot = fullStateName.LastIndexOf('.');
            return lastDot >= 0 ? fullStateName.Substring(lastDot + 1) : fullStateName;
        }

        /// <summary>
        /// Create XStateNet machine from GPU instance state
        /// </summary>
        public StateMachine CreateMachineFromInstance(int instanceId)
        {
            // Use guidIsolate to avoid conflicts
            var machine = StateMachineFactory.CreateFromScript(_machineDefinitionJson, false, true);

            // Sync state from GPU
            string currentState = _gpuPool.GetState(instanceId);
            // Would need to implement state restoration in XStateNet

            return machine;
        }

        public void Dispose()
        {
            if (_disposed) return;

            foreach (var machine in _cpuMachines.Values)
            {
                machine?.Dispose();
            }

            _gpuPool?.Dispose();
            _disposed = true;
        }
    }
}
