namespace XStateNet;

public abstract class VirtualState : StateBase
{
    public VirtualState(string? stateName, string? parentName, string? stateMachineId)
        : base(stateName, parentName, stateMachineId)
    {
    }    
}

