using Newtonsoft.Json.Linq;


// StateBase <- RealState <- NormalState
// StateBase <- RealState <- ParallelState
// StateBase <- VirtualState <- HistoryState

namespace XStateNet;

/// <summary>
/// 
/// </summary>
public partial class StateMachine
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="key"></param>
    /// <param name="delayMap"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static NamedDelay? ParseDelay(string key, DelayMap? delayMap, JToken? token)
    {
        if (token == null)
        {
            throw new Exception("Token is null");
        }

        var delayName = token[key]?.ToString();

        if (delayName == null)
        {
            return null;
        }

        if (delayMap == null)
        {
            return null;
        }

        if (delayMap.TryGetValue(delayName, out var delay))
        {
            return delay;
        }
        else
        {
            throw new Exception($"Delay {delayName} not found in the delay map.");
        }
    }

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

        // Handle both string and array formats
        List<string>? actionNames = null;
        var actionToken = token[key];

        if (actionToken?.Type == JTokenType.String)
        {
            // Single action as string
            var singleAction = actionToken.ToObject<string>();
            if (singleAction != null)
            {
                actionNames = new List<string> { singleAction };
            }
        }
        else if (actionToken?.Type == JTokenType.Array)
        {
            // Multiple actions as array
            actionNames = actionToken.ToObject<List<string>>();
        }

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
            if (actionMap.ContainsKey(actionName))
            {
                // Use thread-safe GetActions method
                var actionList = actionMap.GetActions(actionName);
                actions.AddRange(actionList);
            }
            else
            {
                throw new Exception($"Action {actionName} not found in the action map.");
            }
        }

        return actions;
    }

    /// <summary>
    /// Parse activities from JSON token
    /// </summary>
    /// <param name="key"></param>
    /// <param name="activityMap"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public static List<NamedActivity>? ParseActivities(string key, ActivityMap? activityMap, JToken token)
    {
        List<NamedActivity>? activities = null;

        if (token[key] == null)
        {
            return null;
        }

        // Handle both string and array formats
        List<string>? activityNames = null;
        var activityToken = token[key];

        if (activityToken?.Type == JTokenType.String)
        {
            // Single activity as string
            var singleActivity = activityToken.ToObject<string>();
            if (singleActivity != null)
            {
                activityNames = new List<string> { singleActivity };
            }
        }
        else if (activityToken?.Type == JTokenType.Array)
        {
            // Multiple activities as array
            activityNames = activityToken.ToObject<List<string>>();
        }

        if (activityNames == null)
        {
            return activities;
        }

        if (activityMap == null)
        {
            // Log warning that activities are defined but no activity map provided
            Logger.Warning($"Activities {string.Join(", ", activityNames)} defined but no activity map provided");
            return null;
        }

        activities = new List<NamedActivity>();

        foreach (var activityName in activityNames)
        {
            if (activityMap.TryGetValue(activityName, out var namedActivity))
            {
                activities.Add(namedActivity);
            }
            else
            {
                Logger.Warning($"Activity '{activityName}' is not defined in the activity map");
            }
        }

        return activities;
    }
}

