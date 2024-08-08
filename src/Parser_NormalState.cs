using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace XStateNet;


public abstract class Parser_StateBase
{
    public Parser_StateBase() { }

    public abstract AbstractState Parse(string stateName, string? parentName, string machineId, JToken stateToken);
}

public class Parser_NormalState
{
    public Parser_NormalState() { }

    public virtual AbstractState Parse(string stateName, string? parentName, string machineId, JToken stateToken)
    {
        StateMachine stateMachine = StateMachine.GetInstance(machineId);

        var state = new NormalState(stateName, parentName, machineId)
        {
            InitialStateName = (stateToken["initial"] != null) ? stateName + "." + stateToken["initial"].ToString() : null,
        };

        state.InitialStateName = state.InitialStateName != null ? StateMachine.ResolveAbsolutePath(stateName, state.InitialStateName) : null;

        state.EntryActions = Parser_Action.ParseActions(state, "entry", stateMachine.ActionMap, stateToken);
        state.ExitActions = Parser_Action.ParseActions(state, "exit", stateMachine.ActionMap, stateToken);

        return state;
    }
}

public class Parser_ParallelState
{
    public Parser_ParallelState() { }

    public virtual AbstractState Parse(string stateName, string? parentName, string machineId, JToken stateToken)
    {
        StateMachine stateMachine = StateMachine.GetInstance(machineId);

        var state = new ParallelState(stateName, parentName, machineId);

        state.EntryActions = Parser_Action.ParseActions(state, "entry", stateMachine.ActionMap, stateToken);
        state.ExitActions = Parser_Action.ParseActions(state, "exit", stateMachine.ActionMap, stateToken);

        return state;
    }
}

internal class Parser_HistoryState : Parser_NormalState
{
    HistoryType historyType;
    public Parser_HistoryState(HistoryType historyType) 
    {
        this.historyType = historyType;
    }
    public override AbstractState Parse(string stateName, string? parentName, string machineId, JToken stateToken)
    {
        return new HistoryState(stateName, parentName, machineId, historyType);
    }
}
