namespace XStateNet2.Core.Engine.ArrayBased;

/// <summary>
/// Array-optimized state node using byte indices for O(1) access.
/// Transitions are stored in a 2D array [stateId][eventId] for direct indexing.
/// </summary>
public class ArrayStateNode
{
    /// <summary>
    /// Transitions indexed by [eventId]. Returns list of transitions for that event.
    /// Null if no transitions exist for the event.
    /// </summary>
    public ArrayTransition[]?[]? Transitions { get; set; }

    /// <summary>
    /// Entry actions (list of action indices)
    /// </summary>
    public byte[]? EntryActions { get; set; }

    /// <summary>
    /// Exit actions (list of action indices)
    /// </summary>
    public byte[]? ExitActions { get; set; }

    /// <summary>
    /// Child states indexed by state ID. Null if this is a leaf state.
    /// </summary>
    public ArrayStateNode?[]? ChildStates { get; set; }

    /// <summary>
    /// Initial child state ID (for compound states)
    /// </summary>
    public byte InitialStateId { get; set; } = byte.MaxValue;

    /// <summary>
    /// State type (0=normal, 1=final, 2=parallel)
    /// </summary>
    public byte StateType { get; set; }

    /// <summary>
    /// Always transitions (evaluated on entry)
    /// </summary>
    public ArrayTransition[]? AlwaysTransitions { get; set; }

    /// <summary>
    /// Whether this is a compound state (has children)
    /// </summary>
    public bool IsCompound => ChildStates != null && ChildStates.Length > 0;

    /// <summary>
    /// Whether this is a leaf state (no children)
    /// </summary>
    public bool IsLeaf => !IsCompound;
}

/// <summary>
/// Array-optimized transition using byte indices.
/// </summary>
public class ArrayTransition
{
    /// <summary>
    /// Target state IDs (can be multiple for parallel state transitions)
    /// </summary>
    public byte[]? TargetStateIds { get; set; }

    /// <summary>
    /// Guard condition ID (byte.MaxValue = no guard)
    /// </summary>
    public byte GuardId { get; set; } = byte.MaxValue;

    /// <summary>
    /// Action IDs to execute on this transition
    /// </summary>
    public byte[]? ActionIds { get; set; }

    /// <summary>
    /// Whether this is an internal transition (no state change)
    /// </summary>
    public bool IsInternal { get; set; }

    /// <summary>
    /// Whether this transition has a guard
    /// </summary>
    public bool HasGuard => GuardId != byte.MaxValue;

    /// <summary>
    /// Whether this transition has actions
    /// </summary>
    public bool HasActions => ActionIds != null && ActionIds.Length > 0;
}
