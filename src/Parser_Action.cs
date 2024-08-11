using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;


// StateBase <- RealState <- NormalState
// StateBase <- RealState <- ParallelState
// StateBase <- VirtualState <- HistoryState

namespace XStateNet;

using ActionMap = ConcurrentDictionary<string, List<NamedAction>>;

internal static class Parser_Action
{
    public static List<NamedAction>? ParseActions(RealState state, string key, ActionMap? actionMap, JToken token)
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

        if (actionMap == null)
        {
            return null;
        }

        actions = new List<NamedAction>();

        foreach (var actionName in jobj)
        {
            if (actionMap.TryGetValue(actionName, out var actionList))
            {
                actions.AddRange(actionList);
            }
            else
            {
                throw new Exception($"Action {actionName} not found in the action map.");
            }
        }

        return actions;
    }
}

