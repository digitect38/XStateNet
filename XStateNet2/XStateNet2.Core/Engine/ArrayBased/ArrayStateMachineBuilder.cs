using XStateNet2.Core.Runtime;
using System.Text.Json;

namespace XStateNet2.Core.Engine.ArrayBased;

/// <summary>
/// Builder for creating ArrayStateMachine from XState JSON definitions.
/// Automatically converts Dictionary-based XStateMachineScript to optimized byte-indexed arrays.
/// </summary>
public class ArrayStateMachineBuilder
{
    private XStateMachineScript? _script;
    private readonly Dictionary<string, byte> _stateMapping = new();
    private readonly Dictionary<string, byte> _eventMapping = new();
    private readonly Dictionary<string, byte> _actionMapping = new();
    private readonly Dictionary<string, byte> _guardMapping = new();

    /// <summary>
    /// Parse XState JSON and prepare for array compilation
    /// </summary>
    public static ArrayStateMachineBuilder FromJson(string json)
    {
        var builder = new ArrayStateMachineBuilder();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        builder._script = JsonSerializer.Deserialize<XStateMachineScript>(json, options);

        if (builder._script == null)
            throw new InvalidOperationException("Failed to parse JSON");

        return builder;
    }

    /// <summary>
    /// Build the optimized ArrayStateMachine
    /// </summary>
    public ArrayStateMachine Build()
    {
        if (_script == null)
            throw new InvalidOperationException("No script loaded. Call FromJson first.");

        // Phase 1: Collect all unique identifiers and assign byte indices
        CollectIdentifiers(_script);

        // Phase 2: Build StateMachineMap
        var map = new StateMachineMap(
            _stateMapping,
            _eventMapping,
            _actionMapping,
            _guardMapping
        );

        // Phase 3: Convert states to ArrayStateNode[]
        var states = BuildStateArray(_script, map);

        // Phase 4: Create ArrayStateMachine
        var initialStateId = map.States.GetIndex(_script.Initial ?? "");

        // Initialize context with values from the script's context (if any)
        var context = new InterpreterContext(_script.Context);

        return new ArrayStateMachine
        {
            Id = _script.Id ?? "machine",
            InitialStateId = initialStateId,
            States = states,
            Map = map,
            Context = context
        };
    }

    #region Phase 1: Collect Identifiers

    private void CollectIdentifiers(XStateMachineScript script)
    {
        byte stateIndex = 0;
        byte eventIndex = 0;
        byte actionIndex = 0;
        byte guardIndex = 0;

        // Collect states (including nested)
        CollectStates(script.States, ref stateIndex);

        // Collect events from root and all states
        CollectEvents(script.On, ref eventIndex);
        if (script.States != null)
        {
            foreach (var state in script.States.Values)
            {
                CollectEventsFromState(state, ref eventIndex);
            }
        }

        // Collect actions and guards
        if (script.States != null)
        {
            foreach (var state in script.States.Values)
            {
                CollectActionsAndGuards(state, ref actionIndex, ref guardIndex);
            }
        }
    }

    private void CollectStates(IReadOnlyDictionary<string, XStateNode>? states, ref byte index)
    {
        if (states == null) return;

        foreach (var (stateName, stateNode) in states)
        {
            if (!_stateMapping.ContainsKey(stateName))
            {
                _stateMapping[stateName] = index++;
            }

            // Recursively collect nested states
            CollectStates(stateNode.States, ref index);
        }
    }

    private void CollectEvents(IReadOnlyDictionary<string, List<XStateTransition>>? transitions, ref byte index)
    {
        if (transitions == null) return;

        foreach (var eventName in transitions.Keys)
        {
            if (!_eventMapping.ContainsKey(eventName))
            {
                _eventMapping[eventName] = index++;
            }
        }
    }

    private void CollectEventsFromState(XStateNode state, ref byte index)
    {
        CollectEvents(state.On, ref index);

        // Recursively process nested states
        if (state.States != null)
        {
            foreach (var childState in state.States.Values)
            {
                CollectEventsFromState(childState, ref index);
            }
        }
    }

    private void CollectActionsAndGuards(XStateNode state, ref byte actionIndex, ref byte guardIndex)
    {
        // Collect entry/exit actions
        CollectActionList(state.Entry, ref actionIndex);
        CollectActionList(state.Exit, ref actionIndex);

        // Collect actions and guards from transitions
        if (state.On != null)
        {
            foreach (var transitions in state.On.Values)
            {
                foreach (var transition in transitions)
                {
                    CollectActionList(transition.Actions, ref actionIndex);

                    if (!string.IsNullOrEmpty(transition.Cond) && !_guardMapping.ContainsKey(transition.Cond))
                    {
                        _guardMapping[transition.Cond] = guardIndex++;
                    }
                }
            }
        }

        // Collect from always transitions
        if (state.Always != null)
        {
            foreach (var transition in state.Always)
            {
                CollectActionList(transition.Actions, ref actionIndex);

                if (!string.IsNullOrEmpty(transition.Cond) && !_guardMapping.ContainsKey(transition.Cond))
                {
                    _guardMapping[transition.Cond] = guardIndex++;
                }
            }
        }

        // Recursively process nested states
        if (state.States != null)
        {
            foreach (var childState in state.States.Values)
            {
                CollectActionsAndGuards(childState, ref actionIndex, ref guardIndex);
            }
        }
    }

    private void CollectActionList(List<object>? actions, ref byte index)
    {
        if (actions == null) return;

        foreach (var action in actions)
        {
            string? actionName = null;

            if (action is string str)
            {
                actionName = str;
            }
            else if (action is ActionDefinition actionDef && !string.IsNullOrEmpty(actionDef.Type))
            {
                actionName = actionDef.Type;
            }

            if (actionName != null && !_actionMapping.ContainsKey(actionName))
            {
                _actionMapping[actionName] = index++;
            }
        }
    }

    #endregion

    #region Phase 2: Build State Array

    private ArrayStateNode[] BuildStateArray(XStateMachineScript script, StateMachineMap map)
    {
        var stateCount = _stateMapping.Count;
        var states = new ArrayStateNode[stateCount];

        // Initialize all states
        for (int i = 0; i < stateCount; i++)
        {
            states[i] = new ArrayStateNode();
        }

        // Build each state (including nested ones)
        if (script.States != null)
        {
            BuildStatesRecursively(script.States, states, map);
        }

        return states;
    }

    private void BuildStatesRecursively(IReadOnlyDictionary<string, XStateNode> stateNodes, ArrayStateNode[] states, StateMachineMap map)
    {
        foreach (var (stateName, stateNode) in stateNodes)
        {
            var stateId = map.States.GetIndex(stateName);
            states[stateId] = BuildStateNode(stateNode, map);

            // Recursively process nested states
            if (stateNode.States != null && stateNode.States.Count > 0)
            {
                BuildStatesRecursively(stateNode.States, states, map);
            }
        }
    }

    private ArrayStateNode BuildStateNode(XStateNode node, StateMachineMap map)
    {
        var arrayNode = new ArrayStateNode();

        // Set state type
        if (node.Type?.Equals("final", StringComparison.OrdinalIgnoreCase) == true)
        {
            arrayNode.StateType = 1; // Final
        }
        else if (node.Type?.Equals("parallel", StringComparison.OrdinalIgnoreCase) == true)
        {
            arrayNode.StateType = 2; // Parallel
        }

        // Set initial state
        if (!string.IsNullOrEmpty(node.Initial))
        {
            arrayNode.InitialStateId = map.States.GetIndex(node.Initial);
        }

        // Convert entry/exit actions
        arrayNode.EntryActions = ConvertActionList(node.Entry, map);
        arrayNode.ExitActions = ConvertActionList(node.Exit, map);

        // Convert transitions
        arrayNode.Transitions = BuildTransitionsArray(node.On, map)!;

        // Convert always transitions
        arrayNode.AlwaysTransitions = BuildAlwaysTransitions(node.Always, map);

        // Convert child states (for compound/parallel states)
        if (node.States != null && node.States.Count > 0)
        {
            var childArray = new ArrayStateNode?[node.States.Count];
            int index = 0;

            foreach (var (childName, childNode) in node.States)
            {
                childArray[index++] = BuildStateNode(childNode, map);
            }

            arrayNode.ChildStates = childArray;
        }

        return arrayNode;
    }

    private byte[]? ConvertActionList(List<object>? actions, StateMachineMap map)
    {
        if (actions == null || actions.Count == 0)
            return null;

        var result = new List<byte>();

        foreach (var action in actions)
        {
            string? actionName = null;

            if (action is string str)
            {
                actionName = str;
            }
            else if (action is ActionDefinition actionDef && !string.IsNullOrEmpty(actionDef.Type))
            {
                actionName = actionDef.Type;
            }

            if (actionName != null)
            {
                result.Add(map.Actions.GetIndex(actionName));
            }
        }

        return result.Count > 0 ? result.ToArray() : null;
    }

    private ArrayTransition?[][]? BuildTransitionsArray(IReadOnlyDictionary<string, List<XStateTransition>>? transitions, StateMachineMap map)
    {
        if (transitions == null || transitions.Count == 0)
            return null;

        var eventCount = _eventMapping.Count;
        var transitionArray = new ArrayTransition?[eventCount][];

        foreach (var (eventName, transitionList) in transitions)
        {
            var eventId = map.Events.GetIndex(eventName);
            var arrayTransitions = new ArrayTransition[transitionList.Count];

            for (int i = 0; i < transitionList.Count; i++)
            {
                arrayTransitions[i] = ConvertTransition(transitionList[i], map);
            }

            transitionArray[eventId] = arrayTransitions;
        }

        return transitionArray;
    }

    private ArrayTransition[]? BuildAlwaysTransitions(List<XStateTransition>? always, StateMachineMap map)
    {
        if (always == null || always.Count == 0)
            return null;

        var result = new ArrayTransition[always.Count];

        for (int i = 0; i < always.Count; i++)
        {
            result[i] = ConvertTransition(always[i], map);
        }

        return result;
    }

    private ArrayTransition ConvertTransition(XStateTransition transition, StateMachineMap map)
    {
        var arrayTransition = new ArrayTransition
        {
            IsInternal = transition.Internal
        };

        // Convert target states
        if (!string.IsNullOrEmpty(transition.Target))
        {
            var targets = transition.Target.Split(',', StringSplitOptions.TrimEntries);
            arrayTransition.TargetStateIds = targets.Select(t => map.States.GetIndex(NormalizeStateName(t))).ToArray();
        }

        // Convert guard
        if (!string.IsNullOrEmpty(transition.Cond))
        {
            arrayTransition.GuardId = map.Guards.GetIndex(transition.Cond);
        }
        else
        {
            arrayTransition.GuardId = byte.MaxValue; // No guard
        }

        // Convert actions
        arrayTransition.ActionIds = ConvertActionList(transition.Actions, map);

        return arrayTransition;
    }

    /// <summary>
    /// Normalize state names by removing absolute reference syntax (#machineId.stateName)
    /// Examples:
    ///   - "#atm.operational" -> "operational"
    ///   - "enteringPin" -> "enteringPin"
    /// </summary>
    private string NormalizeStateName(string stateName)
    {
        if (string.IsNullOrEmpty(stateName))
            return stateName;

        // Handle absolute references: #machineId.stateName
        if (stateName.StartsWith("#"))
        {
            var dotIndex = stateName.IndexOf('.');
            if (dotIndex > 0 && dotIndex < stateName.Length - 1)
            {
                return stateName.Substring(dotIndex + 1);
            }
            // If no dot, return without #
            return stateName.Substring(1);
        }

        return stateName;
    }

    #endregion
}
