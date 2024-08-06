using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpState;

using ActionMap = ConcurrentDictionary<string, List<NamedAction>>;
using GuardMap = ConcurrentDictionary<string, NamedGuard>;
internal static class Parser_Action
{
    public static List<NamedAction> ParseActions(State state, string key, ActionMap actionMap, JToken token)
    {
        List<NamedAction> actions = null;

        if (token[key] == null)
        {
            return actions;
        }

        var jobj = token[key].ToObject<List<string>>();

        if (jobj == null)
        {
            return actions;
        }

        actions = new List<NamedAction>();

        foreach (var actionName in jobj)
        {
            if (actionMap.ContainsKey(actionName))
            {
                actions.AddRange(actionMap[actionName]);
            }
        }

        return actions;
    }
}

