
namespace XStateNet;

public class HistoryState : AbstractState
{
    State? lastActivState;
    public HistoryType HistoryType { set; get; }
    public HistoryState(string name, string? parentName, string stateMachineId, HistoryType historyType)
        : base(name, parentName, stateMachineId)
    {
        HistoryType = historyType;
    }

    void RememberLastActiveState(State state)
    {
        lastActivState = state;
    }
    public override void EntryState(HistoryType historyType = HistoryType.None)
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

    public override void ExitState()
    {
    }
}