using XStateNet2.Core.Engine;

namespace SemiFlow.Converter.Helpers;

/// <summary>
/// Builder class to construct XStateNode with mutable collections
/// that can be assigned to readonly properties
/// </summary>
public class XStateNodeBuilder
{
    private Dictionary<string, List<XStateTransition>>? _on;
    private List<object>? _entry;
    private List<object>? _exit;
    private Dictionary<string, XStateNode>? _states;
    private string? _initial;
    private string? _type;
    private XStateInvoke? _invoke;
    private Dictionary<int, List<XStateTransition>>? _after;
    private List<XStateTransition>? _always;
    private XStateTransition? _onDone;
    private string? _description;
    private List<string>? _tags;

    public XStateNodeBuilder WithOn(string eventName, List<XStateTransition> transitions)
    {
        _on ??= new Dictionary<string, List<XStateTransition>>();
        _on[eventName] = transitions;
        return this;
    }

    public XStateNodeBuilder WithEntry(params string[] actions)
    {
        _entry ??= new List<object>();
        _entry.AddRange(actions.Cast<object>());
        return this;
    }

    public XStateNodeBuilder WithExit(params string[] actions)
    {
        _exit ??= new List<object>();
        _exit.AddRange(actions.Cast<object>());
        return this;
    }

    public XStateNodeBuilder WithState(string name, XStateNode state)
    {
        _states ??= new Dictionary<string, XStateNode>();
        _states[name] = state;
        return this;
    }

    public XStateNodeBuilder WithInitial(string initial)
    {
        _initial = initial;
        return this;
    }

    public XStateNodeBuilder WithType(string type)
    {
        _type = type;
        return this;
    }

    public XStateNodeBuilder WithInvoke(XStateInvoke invoke)
    {
        _invoke = invoke;
        return this;
    }

    public XStateNodeBuilder WithAfter(int delay, List<XStateTransition> transitions)
    {
        _after ??= new Dictionary<int, List<XStateTransition>>();
        _after[delay] = transitions;
        return this;
    }

    public XStateNodeBuilder WithAlways(params XStateTransition[] transitions)
    {
        _always ??= new List<XStateTransition>();
        _always.AddRange(transitions);
        return this;
    }

    public XStateNodeBuilder WithOnDone(XStateTransition transition)
    {
        _onDone = transition;
        return this;
    }

    public XStateNodeBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public XStateNodeBuilder WithTags(params string[] tags)
    {
        _tags ??= new List<string>();
        _tags.AddRange(tags);
        return this;
    }

    public XStateNode Build()
    {
        return new XStateNode
        {
            On = _on,
            Entry = _entry,
            Exit = _exit,
            States = _states,
            Initial = _initial,
            Type = _type,
            Invoke = _invoke,
            After = _after,
            Always = _always,
            OnDone = _onDone,
            Description = _description,
            Tags = _tags
        };
    }

    public static XStateNode CreateState()
    {
        return new XStateNodeBuilder().Build();
    }

    public static XStateNode CreateStateWithStates(string initial)
    {
        return new XStateNodeBuilder()
            .WithStates()
            .WithInitial(initial)
            .Build();
    }

    public XStateNodeBuilder WithStates()
    {
        _states = new Dictionary<string, XStateNode>();
        return this;
    }
}

/// <summary>
/// Extension methods to make building easier
/// </summary>
public static class XStateNodeExtensions
{
    public static void AddState(this Dictionary<string, XStateNode> states, string name, XStateNode state)
    {
        states[name] = state;
    }

    public static void AddTransition(this Dictionary<string, List<XStateTransition>> transitions, string eventName, XStateTransition transition)
    {
        if (!transitions.ContainsKey(eventName))
        {
            transitions[eventName] = new List<XStateTransition>();
        }
        transitions[eventName].Add(transition);
    }
}
