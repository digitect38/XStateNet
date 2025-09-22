using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.GPU.Core;
using Newtonsoft.Json.Linq;
using Serilog;

namespace XStateNet.GPU
{
    /// <summary>
    /// GPU-accelerated state machine that extends XStateNet's StateMachine
    /// Automatically offloads parallel instances to GPU when beneficial
    /// </summary>
    public class GPUAcceleratedStateMachine : IDisposable
    {
        private static readonly ILogger _logger = Log.ForContext<GPUAcceleratedStateMachine>();
        private readonly string _definitionJson;
        private readonly StateMachine _masterMachine; // Reference implementation
        private GPUStateMachinePool _gpuPool;
        private GPUStateMachineDefinition _gpuDefinition;
        private readonly Dictionary<string, int> _stateMap;
        private readonly Dictionary<string, int> _eventMap;
        private readonly List<StateMachine> _cpuInstances;
        private readonly int _gpuThreshold;
        private bool _useGPU;
        private bool _forceGPU;
        private bool _forceCPU;
        private ActionExecutor _actionExecutor;
        private StateMachineComplexityAnalyzer.ComplexityReport _complexityReport;

        /// <summary>
        /// Creates a GPU-accelerated state machine
        /// </summary>
        /// <param name="definitionJson">XStateNet JSON definition</param>
        /// <param name="gpuThreshold">Number of instances before switching to GPU (default 100)</param>
        /// <param name="forceGPU">Force GPU usage even for complex machines (default false)</param>
        /// <param name="forceCPU">Force CPU usage even for simple machines (default false)</param>
        public GPUAcceleratedStateMachine(string definitionJson, int gpuThreshold = 100,
            bool forceGPU = false, bool forceCPU = false)
        {
            _definitionJson = definitionJson;
            _gpuThreshold = gpuThreshold;
            _forceGPU = forceGPU;
            _forceCPU = forceCPU;

            // Analyze complexity first
            _complexityReport = StateMachineComplexityAnalyzer.Analyze(definitionJson);

            // Use guidIsolate for master machine to avoid conflicts
            _masterMachine = StateMachine.CreateFromScript(definitionJson, true);
            _stateMap = new Dictionary<string, int>();
            _eventMap = new Dictionary<string, int>();
            _cpuInstances = new List<StateMachine>();
            _useGPU = false;
            _actionExecutor = new ActionExecutor();
        }

        /// <summary>
        /// Create multiple instances of the state machine
        /// Automatically uses GPU if count exceeds threshold and complexity allows
        /// </summary>
        public async Task<StateMachinePool> CreatePoolAsync(int instanceCount)
        {
            bool shouldUseGPU = DetermineExecutionMode(instanceCount);

            if (shouldUseGPU)
            {
                try
                {
                    // Try to use GPU for large instance counts
                    await InitializeGPUAsync(instanceCount);
                    return new StateMachinePool(this, instanceCount, true);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to initialize GPU, falling back to CPU");
                    _useGPU = false;
                    // Fall through to CPU initialization
                }
            }

            // Use CPU for small instance counts or complex machines
            _logger.Information("Using CPU execution for {InstanceCount} instances", instanceCount);
            for (int i = 0; i < instanceCount; i++)
            {
                var instance = StateMachine.CreateFromScript(_definitionJson, true);
                instance.Start(); // Start instances so they can process events
                _cpuInstances.Add(instance);
            }
            return new StateMachinePool(this, instanceCount, false);
        }

        private bool DetermineExecutionMode(int instanceCount)
        {
            // Check forced modes
            if (_forceCPU)
            {
                _logger.Information("CPU execution forced by configuration");
                return false;
            }

            if (_forceGPU)
            {
                _logger.Warning("GPU execution forced despite complexity: {Level}",
                    _complexityReport.Level);
                return true;
            }

            // Check complexity
            if (!_complexityReport.GpuSuitable)
            {
                _logger.Information(
                    "State machine complexity ({Level}) requires CPU execution. " +
                    "Use forceGPU=true to override.",
                    _complexityReport.Level);
                return false;
            }

            // Check instance count threshold
            if (instanceCount < _gpuThreshold)
            {
                _logger.Information(
                    "Instance count ({Count}) below GPU threshold ({Threshold}), using CPU",
                    instanceCount, _gpuThreshold);
                return false;
            }

            _logger.Information(
                "Using GPU execution: {Count} instances, complexity={Level}",
                instanceCount, _complexityReport.Level);
            return true;
        }

        private async Task InitializeGPUAsync(int instanceCount)
        {
            _useGPU = true;
            _gpuPool = new GPUStateMachinePool();

            // Convert XStateNet definition to GPU format
            _gpuDefinition = ConvertToGPUDefinition();

            // Initialize GPU pool
            await _gpuPool.InitializeAsync(instanceCount, _gpuDefinition);

            Console.WriteLine($"GPU Acceleration Enabled: {instanceCount} instances on {_gpuPool.AcceleratorName}");
        }

        private GPUStateMachineDefinition ConvertToGPUDefinition()
        {
            // Parse the JSON definition to extract states and events
            var jsonObj = Newtonsoft.Json.Linq.JObject.Parse(_definitionJson);

            var states = new List<string>();
            var events = new HashSet<string>();
            var transitions = new List<TransitionEntry>();
            var actions = new Dictionary<string, int>();

            // Extract initial state
            string? initialState = jsonObj["initial"]?.ToString();
            if (!string.IsNullOrEmpty(initialState))
            {
                states.Add(initialState);
            }

            // Extract all states and their transitions
            var statesObj = jsonObj["states"] as Newtonsoft.Json.Linq.JObject;
            if (statesObj != null)
            {
                foreach (var state in statesObj.Properties())
                {
                    string stateName = state.Name;
                    if (!states.Contains(stateName))
                    {
                        states.Add(stateName);
                    }

                    // Extract events from transitions
                    // state.Value should be an object with "on" property
                    if (state.Value.Type != Newtonsoft.Json.Linq.JTokenType.Object) continue;

                    var stateValue = state.Value as Newtonsoft.Json.Linq.JObject;
                    if (stateValue == null) continue;

                    var onToken = stateValue["on"];
                    if (onToken == null || onToken.Type != Newtonsoft.Json.Linq.JTokenType.Object) continue;

                    var onObj = onToken as Newtonsoft.Json.Linq.JObject;
                    if (onObj != null)
                    {
                        foreach (var transition in onObj.Properties())
                        {
                            string eventName = transition.Name;
                            events.Add(eventName);

                            // Get target state - handle both string and object transitions
                            string? targetState;
                            if (transition.Value.Type == Newtonsoft.Json.Linq.JTokenType.String)
                            {
                                targetState = transition.Value.ToString();
                            }
                            else if (transition.Value.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                            {
                                targetState = transition.Value["target"]?.ToString() ?? stateName;
                            }
                            else
                            {
                                targetState = stateName; // Stay in same state if no target
                            }

                            if (!states.Contains(targetState))
                            {
                                states.Add(targetState);
                            }
                        }
                    }
                }
            }

            // Create GPU definition
            // Get machine ID from JSON or use default
            string machineId = jsonObj["id"]?.ToString() ?? "GPUMachine";

            var definition = new GPUStateMachineDefinition(
                machineId,
                states.Count,
                events.Count
            );

            // Map states to indices
            for (int i = 0; i < states.Count; i++)
            {
                definition.StateNames[i] = states[i];
                _stateMap[states[i]] = i;
            }

            // Map events to indices
            var eventList = events.ToList();
            for (int i = 0; i < eventList.Count; i++)
            {
                definition.EventNames[i] = eventList[i];
                _eventMap[eventList[i]] = i;
            }

            // Build transition table from the JSON
            if (statesObj != null)
            {
                foreach (var state in statesObj.Properties())
                {
                    string fromStateName = state.Name;
                    if (!_stateMap.ContainsKey(fromStateName)) continue;
                    int fromStateId = _stateMap[fromStateName];

                    // state.Value should be an object with "on" property
                    if (state.Value.Type != Newtonsoft.Json.Linq.JTokenType.Object) continue;

                    var stateValue = state.Value as Newtonsoft.Json.Linq.JObject;
                    if (stateValue == null) continue;

                    var onToken = stateValue["on"];
                    if (onToken == null || onToken.Type != Newtonsoft.Json.Linq.JTokenType.Object) continue;

                    var onObj = onToken as Newtonsoft.Json.Linq.JObject;
                    if (onObj != null)
                    {
                        foreach (var transition in onObj.Properties())
                        {
                            string eventName = transition.Name;
                            if (!_eventMap.ContainsKey(eventName)) continue;
                            int eventId = _eventMap[eventName];

                            // Handle both string and object transitions
                            string targetState;
                            if (transition.Value.Type == Newtonsoft.Json.Linq.JTokenType.String)
                            {
                                targetState = transition.Value.ToString();
                            }
                            else if (transition.Value.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                            {
                                targetState = transition.Value["target"]?.ToString() ?? fromStateName;
                            }
                            else
                            {
                                targetState = fromStateName;
                            }

                            if (!_stateMap.ContainsKey(targetState)) continue;
                            int toStateId = _stateMap[targetState];

                            // Extract action if present (only for object transitions)
                            int actionId = 0;
                            if (transition.Value.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                            {
                                var actionToken = transition.Value["action"];
                                if (actionToken != null)
                                {
                                    string actionName = actionToken.ToString();
                                    if (!actions.ContainsKey(actionName))
                                    {
                                        actionId = actions.Count + 1; // 0 means no action
                                        actions[actionName] = actionId;
                                    }
                                    else
                                    {
                                        actionId = actions[actionName];
                                    }
                                }
                            }

                            transitions.Add(new TransitionEntry
                            {
                                FromState = fromStateId,
                                EventType = eventId,
                                ToState = toStateId,
                                ActionId = actionId,
                                GuardId = 0
                            });
                        }
                    }
                }
            }

            definition.TransitionTable = transitions.ToArray();
            return definition;
        }

        /// <summary>
        /// Register an action handler that will be called when transitions occur
        /// </summary>
        public void RegisterAction(string actionName, Func<object, Task> handler)
        {
            _actionExecutor.RegisterAction(actionName, handler);
        }

        /// <summary>
        /// Send event to specific instance
        /// </summary>
        public async Task SendAsync(int instanceId, string eventName, object eventData = null)
        {
            if (_useGPU)
            {
                _gpuPool.SendEvent(instanceId, eventName);
                await _gpuPool.ProcessEventsAsync();

                // Execute any pending actions from GPU transitions
                await _actionExecutor.ExecutePendingActionsAsync();
            }
            else if (instanceId < _cpuInstances.Count)
            {
                _cpuInstances[instanceId].Send(eventName, eventData);
            }
        }

        /// <summary>
        /// Broadcast event to all instances
        /// </summary>
        public async Task BroadcastAsync(string eventName, object eventData = null)
        {
            if (_useGPU)
            {
                int count = _gpuPool.InstanceCount;
                var instanceIds = Enumerable.Range(0, count).ToArray();
                var eventNames = Enumerable.Repeat(eventName, count).ToArray();
                await _gpuPool.SendEventBatchAsync(instanceIds, eventNames);

                // Execute any pending actions from GPU transitions
                await _actionExecutor.ExecutePendingActionsAsync();
            }
            else
            {
                foreach (var instance in _cpuInstances)
                {
                    instance.Send(eventName, eventData);
                }
            }
        }

        /// <summary>
        /// Get current state of an instance
        /// </summary>
        public string GetState(int instanceId)
        {
            if (_useGPU)
            {
                return _gpuPool.GetState(instanceId);
            }
            else if (instanceId < _cpuInstances.Count)
            {
                var stateString = _cpuInstances[instanceId].GetActiveStateString();
                // Extract simple state name
                int lastDot = stateString.LastIndexOf('.');
                return lastDot >= 0 ? stateString.Substring(lastDot + 1) : stateString;
            }
            return null;
        }

        /// <summary>
        /// Get performance metrics
        /// </summary>
        public PerformanceMetrics GetMetrics()
        {
            if (_useGPU)
            {
                var gpuMetrics = _gpuPool.GetMetrics();
                return new PerformanceMetrics
                {
                    InstanceCount = gpuMetrics.InstanceCount,
                    ExecutionMode = "GPU",
                    AcceleratorType = gpuMetrics.AcceleratorType,
                    MemoryUsed = gpuMetrics.MemoryUsed,
                    MaxParallelism = gpuMetrics.MaxParallelism
                };
            }
            else
            {
                return new PerformanceMetrics
                {
                    InstanceCount = _cpuInstances.Count,
                    ExecutionMode = "CPU",
                    AcceleratorType = "CPU",
                    MemoryUsed = GC.GetTotalMemory(false),
                    MaxParallelism = Environment.ProcessorCount
                };
            }
        }

        public void Dispose()
        {
            _masterMachine?.Dispose();
            _gpuPool?.Dispose();
            foreach (var instance in _cpuInstances)
            {
                instance?.Dispose();
            }
        }
    }

    /// <summary>
    /// Pool of state machine instances (CPU or GPU backed)
    /// </summary>
    public class StateMachinePool
    {
        private readonly GPUAcceleratedStateMachine _parent;
        private readonly int _instanceCount;
        private readonly bool _isGPU;

        internal StateMachinePool(GPUAcceleratedStateMachine parent, int instanceCount, bool isGPU)
        {
            _parent = parent;
            _instanceCount = instanceCount;
            _isGPU = isGPU;
        }

        public int Count => _instanceCount;
        public bool IsGPUAccelerated => _isGPU;

        public async Task SendAsync(int instanceId, string eventName, object eventData = null)
        {
            await _parent.SendAsync(instanceId, eventName, eventData);
        }

        public async Task BroadcastAsync(string eventName, object eventData = null)
        {
            await _parent.BroadcastAsync(eventName, eventData);
        }

        public string GetState(int instanceId)
        {
            return _parent.GetState(instanceId);
        }
    }

    public class PerformanceMetrics
    {
        public int InstanceCount { get; set; }
        public string ExecutionMode { get; set; }
        public string AcceleratorType { get; set; }
        public long MemoryUsed { get; set; }
        public int MaxParallelism { get; set; }

        public override string ToString()
        {
            return $"{ExecutionMode} Mode: {InstanceCount} instances, " +
                   $"{MemoryUsed / (1024 * 1024)}MB memory, " +
                   $"{MaxParallelism} max parallelism, " +
                   $"Accelerator: {AcceleratorType}";
        }
    }
}