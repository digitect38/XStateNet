using System.Collections.Frozen;

namespace XStateNet2.Core.Engine.ArrayBased;

/// <summary>
/// Bi-directional mapping between string identifiers and integer indices.
/// Uses FrozenDictionary for optimal lookup performance (20-40ns).
/// </summary>
public class StateMap
{
    private readonly FrozenDictionary<string, byte> _stringToIndex;
    private readonly string[] _indexToString;

    public StateMap(Dictionary<string, byte> mapping)
    {
        _stringToIndex = mapping.ToFrozenDictionary();

        // Find max index to size array correctly (indices might not be contiguous)
        byte maxIndex = mapping.Count > 0 ? mapping.Values.Max() : (byte)0;
        _indexToString = new string[maxIndex + 1];

        foreach (var (str, idx) in mapping)
        {
            // Prefer shorter names (display names) over full paths
            // This ensures GetString returns "enteringPin" instead of "authenticating.enteringPin"
            if (_indexToString[idx] == null || str.Length < _indexToString[idx].Length)
            {
                _indexToString[idx] = str;
            }
        }
    }

    public byte GetIndex(string value)
    {
        if (value == null)
            return byte.MaxValue;

        return _stringToIndex.TryGetValue(value, out var index) ? index : byte.MaxValue;
    }

    public string GetString(byte index)
    {
        if (index >= _indexToString.Length)
            return string.Empty;

        return _indexToString[index] ?? string.Empty;
    }

    public bool TryGetIndex(string value, out byte index)
    {
        return _stringToIndex.TryGetValue(value, out index);
    }

    public int Count => _stringToIndex.Count;
}

/// <summary>
/// Complete mapping system for array-based state machine optimization.
/// Provides O(1) array access using integer indices instead of string lookups.
/// </summary>
public class StateMachineMap
{
    public StateMap States { get; }
    public StateMap Events { get; }
    public StateMap Actions { get; }
    public StateMap Guards { get; }

    public StateMachineMap(
        Dictionary<string, byte> states,
        Dictionary<string, byte> events,
        Dictionary<string, byte> actions,
        Dictionary<string, byte> guards)
    {
        States = new StateMap(states);
        Events = new StateMap(events);
        Actions = new StateMap(actions);
        Guards = new StateMap(guards);
    }
}
