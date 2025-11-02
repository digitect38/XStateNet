using XStateNet2.Core.Runtime;

namespace XStateNet2.Core.Engine.ArrayBased;

/// <summary>
/// Builds array-optimized state machine from standard XState machine.
/// Performs "compilation" step: analyzes JSON, creates integer mappings, converts to arrays.
/// This preprocessing enables O(1) array access at runtime instead of Dictionary lookups.
/// </summary>
public class StateMapBuilder
{
    private readonly Dictionary<string, byte> _stateMap = new();
    private readonly Dictionary<string, byte> _eventMap = new();
    private readonly Dictionary<string, byte> _actionMap = new();
    private readonly Dictionary<string, byte> _guardMap = new();

    private byte _nextStateId = 0;
    private byte _nextEventId = 0;
    private byte _nextActionId = 0;
    private byte _nextGuardId = 0;

    /// <summary>
    /// Build array-optimized state machine from standard script
    /// </summary>
    public ArrayStateMachine Build(XStateMachineScript script, InterpreterContext context)
    {
        // Phase 1: Analyze and build state/event mappings
        AnalyzeStates(script.States);
        AnalyzeActions(context);
        AnalyzeGuards(context);

        // Phase 2: Create temporary map for conversion (actions/guards will be populated during conversion)
        var tempMap = new StateMachineMap(_stateMap, _eventMap, _actionMap, _guardMap);

        // Phase 3: Convert to array representation (this populates _actionMap and _guardMap)
        var states = new ArrayStateNode[_stateMap.Count];
        foreach (var (stateName, stateId) in _stateMap)
        {
            if (script.States.TryGetValue(stateName, out var stateNode))
            {
                states[stateId] = ConvertStateNode(stateNode, tempMap);
            }
        }

        // Phase 4: Create final map with all discovered actions and guards
        var map = new StateMachineMap(_stateMap, _eventMap, _actionMap, _guardMap);

        // Phase 5: Build final machine
        var initialStateId = map.States.GetIndex(script.Initial);

        return new ArrayStateMachine
        {
            Id = script.Id,
            InitialStateId = initialStateId,
            States = states,
            Map = map,
            Context = context
        };
    }

    private void AnalyzeStates(IReadOnlyDictionary<string, XStateNode> states, string prefix = "")
    {
        foreach (var (stateName, stateNode) in states)
        {
            var fullName = string.IsNullOrEmpty(prefix) ? stateName : $"{prefix}.{stateName}";

            // Register state
            if (!_stateMap.ContainsKey(fullName))
            {
                _stateMap[fullName] = _nextStateId++;
            }

            // Analyze events in transitions
            if (stateNode.On != null)
            {
                foreach (var eventName in stateNode.On.Keys)
                {
                    if (!_eventMap.ContainsKey(eventName))
                    {
                        _eventMap[eventName] = _nextEventId++;
                    }
                }
            }

            // Recursively analyze child states
            if (stateNode.States != null && stateNode.States.Count > 0)
            {
                AnalyzeStates(stateNode.States, fullName);
            }
        }
    }

    private void AnalyzeActions(InterpreterContext context)
    {
        // Actions are registered in context, we just need to track them
        // For now, we'll assign IDs dynamically as we encounter action names
        _nextActionId = 0;
    }

    private void AnalyzeGuards(InterpreterContext context)
    {
        // Guards are registered in context, we just need to track them
        // For now, we'll assign IDs dynamically as we encounter guard names
        _nextGuardId = 0;
    }

    private byte GetOrCreateActionId(string actionName)
    {
        if (!_actionMap.TryGetValue(actionName, out var actionId))
        {
            actionId = _nextActionId++;
            _actionMap[actionName] = actionId;
        }
        return actionId;
    }

    private byte GetOrCreateGuardId(string guardName)
    {
        if (!_guardMap.TryGetValue(guardName, out var guardId))
        {
            guardId = _nextGuardId++;
            _guardMap[guardName] = guardId;
        }
        return guardId;
    }

    private ArrayStateNode ConvertStateNode(XStateNode node, StateMachineMap map)
    {
        var arrayNode = new ArrayStateNode
        {
            StateType = node.Type switch
            {
                "final" => 1,
                "parallel" => 2,
                _ => 0
            }
        };

        // Convert entry actions
        if (node.Entry != null && node.Entry.Count > 0)
        {
            var actionIds = new List<byte>();
            foreach (var action in node.Entry)
            {
                if (action is string actionName)
                {
                    actionIds.Add(GetOrCreateActionId(actionName));
                }
            }
            arrayNode.EntryActions = actionIds.ToArray();
        }

        // Convert exit actions
        if (node.Exit != null && node.Exit.Count > 0)
        {
            var actionIds = new List<byte>();
            foreach (var action in node.Exit)
            {
                if (action is string actionName)
                {
                    actionIds.Add(GetOrCreateActionId(actionName));
                }
            }
            arrayNode.ExitActions = actionIds.ToArray();
        }

        // Convert transitions
        if (node.On != null && node.On.Count > 0)
        {
            // Find max event ID to size the array
            byte maxEventId = 0;
            foreach (var eventName in node.On.Keys)
            {
                var eventId = map.Events.GetIndex(eventName);
                if (eventId > maxEventId) maxEventId = eventId;
            }

            arrayNode.Transitions = new ArrayTransition[maxEventId + 1][];

            foreach (var (eventName, transitions) in node.On)
            {
                var eventId = map.Events.GetIndex(eventName);
                var arrayTransitions = new List<ArrayTransition>();

                foreach (var transition in transitions)
                {
                    arrayTransitions.Add(ConvertTransition(transition, map));
                }

                arrayNode.Transitions[eventId] = arrayTransitions.ToArray();
            }
        }

        // Convert always transitions
        if (node.Always != null && node.Always.Count > 0)
        {
            var alwaysTransitions = new List<ArrayTransition>();
            foreach (var transition in node.Always)
            {
                alwaysTransitions.Add(ConvertTransition(transition, map));
            }
            arrayNode.AlwaysTransitions = alwaysTransitions.ToArray();
        }

        // Set initial state for compound states
        if (!string.IsNullOrEmpty(node.Initial))
        {
            arrayNode.InitialStateId = map.States.GetIndex(node.Initial);
        }

        return arrayNode;
    }

    private ArrayTransition ConvertTransition(XStateTransition transition, StateMachineMap map)
    {
        var arrayTransition = new ArrayTransition
        {
            IsInternal = transition.Internal
        };

        // Convert target states
        if (transition.Targets != null && transition.Targets.Count > 0)
        {
            var targetIds = new List<byte>();
            foreach (var target in transition.Targets)
            {
                if (map.States.TryGetIndex(target, out var targetId))
                {
                    targetIds.Add(targetId);
                }
            }
            arrayTransition.TargetStateIds = targetIds.ToArray();
        }

        // Convert guard
        if (!string.IsNullOrEmpty(transition.Cond))
        {
            arrayTransition.GuardId = GetOrCreateGuardId(transition.Cond);
        }

        // Convert actions
        if (transition.Actions != null && transition.Actions.Count > 0)
        {
            var actionIds = new List<byte>();
            foreach (var action in transition.Actions)
            {
                if (action is string actionName)
                {
                    actionIds.Add(GetOrCreateActionId(actionName));
                }
            }
            arrayTransition.ActionIds = actionIds.ToArray();
        }

        return arrayTransition;
    }
}
