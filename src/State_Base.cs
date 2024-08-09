using Newtonsoft.Json.Linq;
namespace XStateNet;

public abstract class StateBase
{
    public string Name { get; set; }
    public string? ParentName { get; set; }
    public StateMachine StateMachine => StateMachine.GetInstance(stateMachineId);
    
    public RealState? Parent => string.IsNullOrEmpty(ParentName) ? null : StateMachine.GetState(ParentName) as RealState;

    public string stateMachineId;

    public StateBase(string name, string? parentName, string stateMachineId)
    {
        Name = name;
        ParentName = parentName;
        this.stateMachineId = stateMachineId;
    }
  
}


public abstract class Parser_StateBase
{
    public Parser_StateBase() { }

    public abstract StateBase Parse(string stateName, string? parentName, string machineId, JToken stateToken);
}
