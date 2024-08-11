using Newtonsoft.Json.Linq;
namespace XStateNet;

public class HistoryState : VirtualState
{
    public HistoryType HistoryType { set; get; }
    public HistoryState(string name, string? parentName, string stateMachineId, HistoryType historyType)
        : base(name, parentName, stateMachineId)
    {
        HistoryType = historyType;

        if (Parent == null)
        {
            throw new Exception("History state should have parent");
        }

        if (Parent is NormalState)
        {
            ((NormalState)Parent).HistorySubState = this;
        }
        else
        {
            throw new Exception("History state should be child of Normal state");
        }
    }

    // Note:  history stateonly used to buildtransition list. So not used in transition.
}


public class Parser_HistoryState : Parser_StateBase
{
    HistoryType historyType;

    public Parser_HistoryState(string machineId, HistoryType historyType) : base(machineId)
    {
        this.historyType = historyType;
    }

    public override StateBase Parse(string stateName, string? parentName, JToken stateToken)
    {
        return new HistoryState(stateName, parentName, machineId, historyType);
    }    
}