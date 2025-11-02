using XStateNet2.Core.Runtime;

namespace XStateNet2.Core.Engine.ArrayBased;

/// <summary>
/// Array-optimized state machine for maximum performance.
/// Uses byte indices and array access (5-10ns) instead of Dictionary lookups (20-100ns).
/// Expected: 2-10x faster than FrozenDictionary version, approaching pure Actor performance.
/// </summary>
public class ArrayStateMachine
{
    /// <summary>
    /// Machine ID (for logging)
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Initial state ID
    /// </summary>
    public byte InitialStateId { get; set; }

    /// <summary>
    /// All states indexed by state ID. Direct array access O(1).
    /// </summary>
    public ArrayStateNode[] States { get; set; } = Array.Empty<ArrayStateNode>();

    /// <summary>
    /// Mapping between strings and integer indices
    /// </summary>
    public StateMachineMap Map { get; set; } = null!;

    /// <summary>
    /// Interpreter context with registered actions/guards
    /// </summary>
    public InterpreterContext Context { get; set; } = null!;

    /// <summary>
    /// Total number of states
    /// </summary>
    public int StateCount => States.Length;

    /// <summary>
    /// Get state node by ID (O(1) array access)
    /// </summary>
    public ArrayStateNode? GetState(byte stateId)
    {
        return stateId < States.Length ? States[stateId] : null;
    }

    /// <summary>
    /// Get state node by name (requires string→byte lookup, then array access)
    /// </summary>
    public ArrayStateNode? GetState(string stateName)
    {
        var stateId = Map.States.GetIndex(stateName);
        return GetState(stateId);
    }

    /// <summary>
    /// Get transitions for a state and event (O(1) array access)
    /// </summary>
    public ArrayTransition[]? GetTransitions(byte stateId, byte eventId)
    {
        var state = GetState(stateId);
        if (state?.Transitions == null || eventId >= state.Transitions.Length)
            return null;

        return state.Transitions[eventId];
    }

    /// <summary>
    /// Get state name from ID (O(1) array access)
    /// </summary>
    public string GetStateName(byte stateId)
    {
        return Map.States.GetString(stateId);
    }

    /// <summary>
    /// Get event name from ID (O(1) array access)
    /// </summary>
    public string GetEventName(byte eventId)
    {
        return Map.Events.GetString(eventId);
    }
}
