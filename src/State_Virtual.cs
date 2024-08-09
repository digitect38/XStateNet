namespace XStateNet;

public abstract class VirtualState : StateBase
{
    public VirtualState(string name, string? parentName, string stateMachineId)
        : base(name, parentName, stateMachineId)
    {
    }    
}

