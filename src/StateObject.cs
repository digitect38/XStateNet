using System;
using System.Collections.Generic;
using System.Linq;
namespace XStateNet;
public abstract class StateObject
{
    public string? machineId;

    public StateMachine? StateMachine {
        get {
            if(machineId == null) throw new Exception("StateMachineId is null");
            return StateMachine.GetInstance(machineId);
        }
    }

    public StateObject(string? machineId)  {
        this.machineId = machineId;
    }
}
