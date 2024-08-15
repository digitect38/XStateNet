using System;
using System.Collections.Generic;
using System.Linq;
namespace XStateNet;

public static class Helper
{
    public static void WriteLine(int indent, string msg)
    {
        for (int i = 0; i < indent; i++)
        {
            Console.Write("  ");
        }
        StateMachine.Log(msg);
    }

    public static string ToCsvString(this IEnumerable<StateBase> collection, string separator = ";")
    {
        return string.Join(separator, collection.Select(state => state.Name));
    }
    
    public static string ToCsvString(this IEnumerable<string> collection, StateMachine stateMachine, bool leafOnly = true, string separator = ";")
    {
        if (leafOnly)
            return string.Join(separator, collection.Where(state => 
                ((RealState)(stateMachine.GetState(state))).SubStateNames.Count == 0).Select(state => state));
        else
            return string.Join(separator, collection.Select(state => state));
    }
    
    public static string ToCsvString(this ICollection<string> collection, StateMachine stateMachine, bool leafOnly = true, string separator = ";")
    {
        if (leafOnly)
            return string.Join(separator, collection.Where(state =>
                ((RealState)(stateMachine.GetState(state))).SubStateNames.Count == 0).Select(state => state));
        else
            return string.Join(separator, collection.Select(state => state));
    }

    public static StateBase ToState(this string stateName, StateMachine stateMachine)
    {
        return stateMachine.GetState(stateName);
    }
}
