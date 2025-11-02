using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace XStateNet2.Core.Engine;

/// <summary>
/// Optimized state lookup using array indexing and FrozenDictionary.
/// Provides O(1) array access for state nodes and their transitions.
/// FrozenDictionary provides 10-15% faster lookups than Dictionary for read-heavy scenarios.
/// </summary>
public class StateIndex
{
    private readonly XStateNode[] _statesByIndex;
    private readonly FrozenDictionary<string, int> _stateNameToIndex;
    private readonly IReadOnlyDictionary<string, List<XStateTransition>>?[] _transitionsByStateIndex;

    public StateIndex(IReadOnlyDictionary<string, XStateNode> states)
    {
        if (states == null || states.Count == 0)
        {
            _statesByIndex = Array.Empty<XStateNode>();
            _stateNameToIndex = FrozenDictionary<string, int>.Empty;
            _transitionsByStateIndex = Array.Empty<IReadOnlyDictionary<string, List<XStateTransition>>?>();
            return;
        }

        var stateCount = states.Count;
        _statesByIndex = new XStateNode[stateCount];
        var tempNameToIndex = new Dictionary<string, int>(stateCount, StringComparer.Ordinal);
        _transitionsByStateIndex = new IReadOnlyDictionary<string, List<XStateTransition>>?[stateCount];

        int index = 0;
        foreach (var (stateName, stateNode) in states)
        {
            tempNameToIndex[stateName] = index;
            _statesByIndex[index] = stateNode;

            // Cache transitions for this state (null if no transitions)
            _transitionsByStateIndex[index] = stateNode.On;

            index++;
        }

        // Freeze the name-to-index dictionary for optimal read performance
        _stateNameToIndex = tempNameToIndex.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// Get state index by name. Returns -1 if not found.
    /// </summary>
    public int GetStateIndex(string stateName)
    {
        return _stateNameToIndex.TryGetValue(stateName, out var index) ? index : -1;
    }

    /// <summary>
    /// Get state node by index. Returns null if index is invalid.
    /// Fast O(1) array access.
    /// </summary>
    public XStateNode? GetStateByIndex(int index)
    {
        return index >= 0 && index < _statesByIndex.Length ? _statesByIndex[index] : null;
    }

    /// <summary>
    /// Get state node by name. Returns null if not found.
    /// Requires dictionary lookup + array access.
    /// </summary>
    public XStateNode? GetStateByName(string stateName)
    {
        var index = GetStateIndex(stateName);
        return GetStateByIndex(index);
    }

    /// <summary>
    /// Try to get transitions for a specific state and event type.
    /// Fast O(1) array access + dictionary lookup for event type.
    /// </summary>
    public bool TryGetTransitions(int stateIndex, string eventType, out List<XStateTransition>? transitions)
    {
        transitions = null;

        if (stateIndex < 0 || stateIndex >= _transitionsByStateIndex.Length)
            return false;

        var eventDict = _transitionsByStateIndex[stateIndex];
        if (eventDict == null)
            return false;

        return eventDict.TryGetValue(eventType, out transitions);
    }

    /// <summary>
    /// Get the transitions dictionary for a state. Returns null if no transitions.
    /// Fast O(1) array access.
    /// </summary>
    public IReadOnlyDictionary<string, List<XStateTransition>>? GetTransitionsForState(int stateIndex)
    {
        if (stateIndex < 0 || stateIndex >= _transitionsByStateIndex.Length)
            return null;

        return _transitionsByStateIndex[stateIndex];
    }

    /// <summary>
    /// Total number of states in the index.
    /// </summary>
    public int StateCount => _statesByIndex.Length;
}
