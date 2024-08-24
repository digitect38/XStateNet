using Newtonsoft.Json.Linq;
namespace XStateNet;

public abstract class StateNode : StateObject
{
    public string? Name { get; set; }
    public string? ParentName { get; set; }
    public TransitionExecutor? transitionExecutor => StateMachine?.transitionExecutor;

    public CompoundState? Parent => string.IsNullOrEmpty(ParentName) ? null : StateMachine?.GetState(ParentName) as CompoundState;

    public StateNode(string? stateName, string? parentName, string? stateMachineId) : base(stateMachineId)
    {
        Name = stateName;
        ParentName = parentName;
    }    
}

/// <summary>
/// 
/// </summary>
public abstract class Parser_StateBase : StateObject
{
    public Parser_StateBase(string? machineId) : base(machineId)
    {
    }
    public abstract StateNode Parse(string stateName, string? parentName, JToken stateToken);
    
}
