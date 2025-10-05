using System.Collections.Concurrent;

namespace XStateNet;

/// <summary>
/// Named activity for the activity map
/// Activities are long-running tasks that start when entering a state and stop when exiting
/// Simplified: Function-first constructor, name optional
/// </summary>
public class NamedActivity
{
    public string? Name { get; }
    public Func<StateMachine, CancellationToken, Action> Activity { get; }

    public NamedActivity(Func<StateMachine, CancellationToken, Action> activity, string? name = null)
    {
        Activity = activity;
        Name = name;
    }

    // Backward compatibility: name-first constructor
    public NamedActivity(string name, Func<StateMachine, CancellationToken, Action> activity)
    {
        Activity = activity;
        Name = name;
    }
}

/// <summary>
/// Activity map for storing named activities
/// </summary>
public class ActivityMap : ConcurrentDictionary<string, NamedActivity>
{
    public ActivityMap() : base() { }

    /// <summary>
    /// Add an activity to the map
    /// </summary>
    public void Add(string name, Func<StateMachine, CancellationToken, Action> activity)
    {
        this[name] = new NamedActivity(name, activity);
    }

    /// <summary>
    /// Add multiple activities at once
    /// </summary>
    public void AddRange(ConcurrentDictionary<string, Func<StateMachine, CancellationToken, Action>> activities)
    {
        foreach (var kvp in activities)
        {
            Add(kvp.Key, kvp.Value);
        }
    }
}