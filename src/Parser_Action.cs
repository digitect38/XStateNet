using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;


// AbstractState <- RealState <- NormalState
// AbstractState <- RealState <- ParallelState
// AbstractState <- HistoryState

namespace XStateNet;

using ActionMap = ConcurrentDictionary<string, List<NamedAction>>;
using GuardMap = ConcurrentDictionary<string, NamedGuard>;
internal static class Parser_Action
{
    public static List<NamedAction>? ParseActions(RealState state, string key, ActionMap actionMap, JToken token)
    {
        List<NamedAction>? actions = null;

        if (token[key] == null)
        {
            return null;
        }

        var jobj = token[key]?.ToObject<List<string>>();

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

