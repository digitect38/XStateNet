using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;


// StateBase <- RealState <- NormalState
// StateBase <- RealState <- ParallelState
// StateBase <- VirtualState <- HistoryState

namespace XStateNet;

using ActionMap = ConcurrentDictionary<string, List<NamedAction>>;
using ServiceMap = ConcurrentDictionary<string, NamedService>;

/// <summary>
/// 
/// </summary>
public partial class StateMachine
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="key"></param>
    /// <param name="serviceMap"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static NamedService? ParseService(string key, ServiceMap? serviceMap, JToken? token)
    {
        if (token == null)
        {
            throw new Exception("Token is null");
        }

        var srcToken = token[key];

        if (srcToken == null)
        {
            return null;
        }

        var serviceName = srcToken["src"]?.ToString();

        if (serviceName == null)
        {
            return null;
        }

        if (serviceMap == null)
        {
            return null;
        }

        if (serviceMap.TryGetValue(serviceName, out var service))
        {
            return service;
        }
        else
        {
            throw new Exception($"Service {serviceName} not found in the service map.");
        }
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="key"></param>
    /// <param name="actionMap"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static List<NamedAction>? ParseActions(string key, ActionMap? actionMap, JToken token) 
    {
        List<NamedAction>? actions = null;

        if (token[key] == null)
        {
            return null;
        }

        var actionNames = token[key]?.ToObject<List<string>>();

        if (actionNames == null)
        {
            return actions;
        }

        if (actionMap == null)
        {
            return null;
        }

        actions = new List<NamedAction>();

        foreach (var actionName in actionNames)
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

