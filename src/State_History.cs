using Newtonsoft.Json.Linq;
namespace XStateNet;

public class HistoryState : VirtualState
{
    RealState? lastActivState;
    public HistoryType HistoryType { set; get; }
    public HistoryState(string name, string? parentName, string stateMachineId, HistoryType historyType)
        : base(name, parentName, stateMachineId)
    {
        HistoryType = historyType;
    }

    void RememberLastActiveState(RealState state)
    {
        lastActivState = state;
    }
    public void EntryState(HistoryType historyType = HistoryType.None)
    {
        if (historyType == HistoryType.Deep)
        {
            lastActivState?.EntryState(historyType);
        }
        else
        {
            lastActivState?.EntryState();
        }
    }
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