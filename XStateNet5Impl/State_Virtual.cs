namespace XStateNet;

public abstract class VirtualState : StateNode
{
    public VirtualState(string? stateName, string? parentName, string? stateMachineId)
        : base(stateName, parentName, stateMachineId)
    {
    }    
}

