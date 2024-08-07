using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace SharpState;


public abstract class Parser_StateBase
{
    public Parser_StateBase() { }

    public abstract StateBase Parse(string stateName, string? parentName, string machineId, JToken stateToken);
}

public class Parser_NormalState
{
    public Parser_NormalState() { }
        
    public virtual StateBase Parse(string stateName, string? parentName, string machineId, JToken stateToken)
    {
        StateMachine stateMachine = StateMachine.GetInstance(machineId);

        var state = new State(stateName, parentName, machineId)
        {

            // Note :
            // History state is a special case of a state that can be the target of a transition.
            // History state is kind of a pseudo state that is not a real state but a PLACEHOLER for the last active state.
            //

            IsParallel = stateToken["type"]?.ToString() == "parallel",
            InitialStateName = (stateToken["initial"] != null) ? stateName + "." + stateToken["initial"].ToString() : null,

        };

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
    public override StateBase Parse(string stateName, string parentName, string machineId, JToken stateToken)
    {
        var state = new HistoryState(stateName, parentName, machineId, historyType);
        return state;
    }
}
