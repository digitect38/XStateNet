using Newtonsoft.Json.Linq;
namespace XStateNet;

public abstract class StateBase : StateObject
{
    public string? Name { get; set; }
    public string? ParentName { get; set; }
    public TransitionExecutor? transitionExecutor => StateMachine?.transitionExecutor;

    public RealState? Parent => string.IsNullOrEmpty(ParentName) ? null : StateMachine?.GetState(ParentName) as RealState;

    public StateBase(string? stateName, string? parentName, string? stateMachineId) : base(stateMachineId)
    {
        Name = stateName;
        ParentName = parentName;
    }    
}

public abstract class Parser_StateBase : StateObject
{
    public Parser_StateBase(string? machineId) : base(machineId)
    {
    }
    public abstract StateBase Parse(string stateName, string? parentName, JToken stateToken);
}
