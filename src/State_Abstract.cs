using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace XStateNet;

public abstract class AbstractState
{
    public string Name { get; set; }
    public string? ParentName { get; set; }
    public StateMachine StateMachine => StateMachine.GetInstance(stateMachineId);

    public string stateMachineId;

    public AbstractState(string name, string? parentName, string stateMachineId)
    {
        Name = name;
        ParentName = parentName;
        this.stateMachineId = stateMachineId;
    }

    public abstract void EntryState(HistoryType historyType = HistoryType.None);
    public abstract void ExitState();
}