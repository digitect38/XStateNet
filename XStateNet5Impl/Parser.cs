using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Linq;
using System.Diagnostics;
using System.Text.Json;

namespace XStateNet;

/// <summary>
/// 상태 기계를 파싱하고 관리하는 클래스입니다.
/// JSON 스크립트를 파싱하여 상태, 전이, 액션 등을 생성하고 처리합니다.
/// </summary>
public partial class StateMachine
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="stateMachine"></param>
    /// <param name="jsonScript"></param>
    /// <param name="actionCallbacks"></param>
    /// <param name="guardCallbacks"></param>
    /// <param name="serviceCallbacks"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static StateMachine ParseStateMachine(StateMachine stateMachine, string? jsonScript,
        ActionMap? actionCallbacks,
        GuardMap? guardCallbacks,
        ServiceMap? serviceCallbacks,
        DelayMap? delayCallbacks,
        ActivityMap? activityCallbacks = null
    )
    {
        return ParseStateMachine(stateMachine, jsonScript, false, actionCallbacks, guardCallbacks, serviceCallbacks, delayCallbacks, activityCallbacks);
    }

    public static StateMachine ParseStateMachine(StateMachine stateMachine, string? jsonScript,
        bool guidIsolate,
        ActionMap? actionCallbacks,
        GuardMap? guardCallbacks,
        ServiceMap? serviceCallbacks,
        DelayMap? delayCallbacks,
        ActivityMap? activityCallbacks = null
    )
    {
        if (string.IsNullOrWhiteSpace(jsonScript))
        {
            throw new ArgumentException("JSON script cannot be null or empty", nameof(jsonScript));
        }

        if (stateMachine == null)
        {
            throw new ArgumentNullException(nameof(stateMachine), "StateMachine cannot be null");
        }

        // Validate JSON input for security
        Security.ValidateJsonInput(jsonScript);

        try
        {
            var jsonWithQuotedKeys = ConvertToQuotedKeys(jsonScript);
            var rootToken = JObject.Parse(jsonWithQuotedKeys);

            if (rootToken == null)
            {
                throw new JsonException("Invalid JSON script");
            }

            if (!rootToken.ContainsKey("id"))
            {
                throw new JsonException("JSON script must contain an 'id' field");
            }

            // If guidIsolate is true, append a unique GUID to the machine ID
            string machineId = rootToken["id"]?.ToString() ?? "machine";
            if (guidIsolate)
            { 
                machineId = $"{machineId}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            }
            stateMachine.machineId = $"#{machineId}";
            stateMachine.ActionMap = actionCallbacks;
            stateMachine.GuardMap = guardCallbacks;
            stateMachine.ServiceMap = serviceCallbacks;
            stateMachine.DelayMap = delayCallbacks;
            stateMachine.ActivityMap = activityCallbacks;

            // Thread-safe registration with conflict handling
            if (!_instanceMap.TryAdd(stateMachine.machineId, stateMachine))
            {
                // If the ID already exists, try to remove it first (might be from a previous test)
                _instanceMap.TryRemove(stateMachine.machineId, out _);
                _instanceMap.TryAdd(stateMachine.machineId, stateMachine);
            }

            if (rootToken.ContainsKey("context") && rootToken["context"] != null)
            {
                var tokenList = rootToken["context"];

                if (tokenList == null)
                {
                    throw new Exception("Invalid context object!");
                }

                // Store original context JSON for RESET functionality
                stateMachine._originalContextJson = tokenList.ToString();

                foreach (JToken contextItem in tokenList)
                {
                    if (contextItem != null && contextItem.First != null && stateMachine.ContextMap != null)
                    {
                        stateMachine.ContextMap[contextItem.Path.Split('.').Last()] = contextItem.First;
                    }
                }
            }

            stateMachine.ParseState($"{stateMachine.machineId}", rootToken, null);

            return stateMachine;
        }
        catch (JsonException ex)
        {
            throw new JsonException("Error parsing JSON", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Unexpected error while parsing state machine", ex);
        }
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="jsonScript"></param>
    /// <param name="actionCallbacks"></param>
    /// <param name="guardCallbacks"></param>
    /// <param name="serviceCallbacks"></param>
    /// <returns></returns>
    public static StateMachine ParseStateMachine(string? jsonScript,
        ActionMap? actionCallbacks,
        GuardMap? guardCallbacks,
        ServiceMap? serviceCallbacks,
        DelayMap? delayCallbacks,
        ActivityMap? activityCallbacks = null
    )
    {
        var stateMachine = new StateMachine() { };
        return ParseStateMachine(stateMachine, jsonScript, actionCallbacks, guardCallbacks, serviceCallbacks, delayCallbacks, activityCallbacks);
    }

    public static StateMachine ParseStateMachine(string? jsonScript,
        bool guidIsolate,
        ActionMap? actionCallbacks,
        GuardMap? guardCallbacks,
        ServiceMap? serviceCallbacks,
        DelayMap? delayCallbacks,
        ActivityMap? activityCallbacks = null
    )
    {
        var stateMachine = new StateMachine() { };
        return ParseStateMachine(stateMachine, jsonScript, guidIsolate, actionCallbacks, guardCallbacks, serviceCallbacks, delayCallbacks, activityCallbacks);
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="json"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private static string ConvertToQuotedKeys(string? json)
    {
        if (json == null) 
            throw new ArgumentNullException(nameof(json), "JSON script cannot be null");
        
        // Use safe regex with timeout to prevent ReDoS
        var regex = Security.CreateSafeRegex(@"(?<!\\)(\b[a-zA-Z_][a-zA-Z0-9_]*\b)\s*:");
        var result = regex.Replace(json, "\"$1\":");
        return result;
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="stateName"></param>
    /// <param name="stateToken"></param>
    /// <param name="parentName"></param>
    /// <exception cref="Exception"></exception>
    public void ParseState(string stateName, JToken stateToken, string? parentName)
    {
        StateNode? stateBase;

        var stateTypeStr = stateToken["type"]?.ToString();

        StateType stateType = 
            stateTypeStr == "parallel" ? StateType.Parallel : 
            stateTypeStr == "history" ? StateType.History : 
            stateTypeStr == "final" ? StateType.Final : StateType.Normal;

        switch (stateType)
        {
            case StateType.History:
                var historyType = ParseHistoryType(stateToken);

                stateBase = new Parser_HistoryState(machineId, historyType).Parse(stateName, parentName, stateToken) as HistoryState;

                if (stateBase == null)
                {
                    throw new InvalidOperationException($"Failed to create HistoryState for {stateName}");
                }

                RegisterState(stateBase);
                return; // no more settings needed!

            case StateType.Parallel:

                stateBase = new Parser_ParallelState(machineId).Parse(stateName, parentName, stateToken) as ParallelState;

                if (stateBase == null)
                {
                    throw new InvalidOperationException($"Failed to create ParallelState for {stateName}");
                }

                RegisterState(stateBase);
                break;

            case StateType.Final:

                stateBase = new Parser_FinalState(machineId).Parse(stateName, parentName, stateToken) as FinalState;

                if (stateBase == null)
                {
                    throw new InvalidOperationException($"Failed to create FinalState for {stateName}");
                }

                RegisterState(stateBase);
                break;

            default:
                stateBase = new Parser_NormalState(machineId).Parse(stateName, parentName, stateToken) as NormalState;

                if (stateBase == null)
                {
                    throw new InvalidOperationException($"Failed to create NormalState for {stateName}");
                }

                RegisterState(stateBase);
                break;
        }

        CompoundState state = (CompoundState)stateBase;

        if (state == null)
        {
            throw new InvalidOperationException($"Failed to cast state to CompoundState for {stateName}");
        }

        if (parentName == null)
        {
            RootState = state;
        }


        if (!string.IsNullOrEmpty(parentName))
        {
            if(StateMap == null)
            {
                throw new InvalidOperationException("StateMap is not initialized");
            }
            var parent = (CompoundState)StateMap[parentName];
            parent.SubStateNames.Add(stateName);
        }

        var onTransitionTokens = stateToken["on"];

        if (onTransitionTokens != null)
        {
            foreach (JProperty prop in onTransitionTokens)
            {
                var eventName = prop.Name;

                // Debug logging
                Logger.Debug($"Parsing on transition with event name: '{eventName}'");

                if (prop.Value != null)
                {
                    // Handle empty string event as 'always' transition (XState v4 compatibility)
                    // The event name might come through as just empty or as the literal string "''"
                    if (string.IsNullOrEmpty(eventName) || eventName == "''" || eventName == "\"\"")
                    {
                        Logger.Debug($"Converting empty event '{eventName}' to always transition");
                        ParseTransitions(state, TransitionType.Always, "always", prop.Value);
                    }
                    else
                    {
                        ParseTransitions(state, TransitionType.On, eventName, prop.Value);
                    }
                }
            }
        }

        var afterTokens = stateToken["after"];

        if (afterTokens != null)
        {
            foreach (JProperty prop in afterTokens)
            {
                var delay = prop.Name;
                if (prop.Value != null)
                {
                    ParseTransitions(state, TransitionType.After, delay, prop.Value);
                }
            }
        }

        var alwaysToken = stateToken["always"];

        if (alwaysToken != null)
        {            
            if(alwaysToken == null)
            {
                throw new Exception("Always token is null!");
            }

            ParseTransitions(state, TransitionType.Always, "always", alwaysToken);
        }

        var onDoneToken = stateToken["onDone"];

        if (onDoneToken != null)
        {
            if (onDoneToken == null)
            {
                throw new Exception("onDone token is null!");
            }

            ParseTransitions(state, TransitionType.OnDone, "onDone", onDoneToken);
        }
        
        // Parse onError transitions
        var onErrorToken = stateToken["onError"];
        if (onErrorToken != null)
        {
            ParseTransitions(state, TransitionType.OnError, "onError", onErrorToken);
        }

        var states = stateToken["states"];

        if (states != null)
        {
            ParseStates(states, stateName);
        }

        // Check if this compound state has history configuration
        var historyToken = stateToken["history"];
        if (historyToken != null && state is NormalState normalState)
        {
            var historyTypeStr = historyToken.ToString();
            var historyType = historyTypeStr == "deep" ? HistoryType.Deep : HistoryType.Shallow;

            // Create a history substate for this compound state
            var historyStateName = $"{stateName}.#history";
            var historyState = new HistoryState(historyStateName, stateName, machineId, historyType);

            // Register the history state
            if (StateMap != null)
            {
                StateMap[historyStateName] = historyState;
            }

            // Set it as the history substate of this normal state
            normalState.HistorySubState = historyState;

            // Add to parent's substates
            normalState.SubStateNames.Add(historyStateName);
        }

        //Parser_Action.ParseActions("exit", ActionMap, stateToken);
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="statesToken"></param>
    /// <param name="parentName"></param>
    private void ParseStates(JToken statesToken, string parentName)
    {
        foreach (var subToken in statesToken)
        {
            var stateName = parentName != null ? $"{parentName}.{subToken.Path.Split('.').Last()}" : subToken.Path.Split('.').Last();

            if (subToken.First != null)
            {
                ParseState(stateName, subToken.First, parentName);
            }
        }
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="actionNames"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private List<NamedAction>? GetActionCallbacks(List<string> actionNames)
    {
        if (actionNames == null) return null;       

        if(ActionMap == null)
        {
            throw new Exception("ActionMap is null!");
        }

        // Lazy initialization - avoid allocating if empty
        List<NamedAction>? result = null;

        foreach (var actionName in actionNames)
        {
            if (ActionMap.ContainsKey(actionName))
            {
                result ??= new List<NamedAction>();
                // Use thread-safe GetActions method
                var actions = ActionMap.GetActions(actionName);
                result.AddRange(actions);
            }
        }
        return result ?? new List<NamedAction>();
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="guardName"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private NamedGuard? GetGuardCallback(string guardName)
    {
        if (guardName == null) return null;

        if (GuardMap == null)
        {
            throw new Exception("ActionMap is null!");
        }

        if (GuardMap.ContainsKey(guardName))
        {
            return GuardMap[guardName];
        }
        else
        {
            return null;
        }
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="state"></param>
    /// <param name="type"></param>
    /// <param name="event"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private List<Transition> ParseTransitions(CompoundState state, TransitionType type, string @event, JToken token)
    {
        List<Transition> transitions = new List<Transition>(4); // Pre-allocate with typical size

        if (token.Type == JTokenType.Array)
        {
            foreach (var trToken in token)
            {
                transitions.Add(ParseTransition(state, type, @event, trToken));
            }
        }
        else if (token.Type == JTokenType.Object)
        {
            transitions.Add(ParseTransition(state, type, @event, token));
        }
        else if (token.Type == JTokenType.String)
        {
            transitions.Add(ParseTransition(state, type, @event, token));
        }
        else
        {
            throw new Exception($"Unexpected token type: {token.Type}!");
        }

        return transitions;
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="source"></param>
    /// <param name="type"></param>
    /// <param name="event"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private Transition ParseTransition(CompoundState source, TransitionType type, string @event, JToken token)
    {

        List<string>? actionNames = null;
        string? targetName = null;
        List<string>? targetNames = null;
        string? guard = null;
        string? inCondition = null;

        if (token == null) throw new Exception("Token is null!");

        if (token.Type == JTokenType.String)
        {
            // implicit target case..."on": { "TIMER": "yellow"  }
            targetName = token.ToString();
        }
        else if (token.Type == JTokenType.Object)
        {
            try
            {
                var targetToken = token["target"];
                
                // Check if target is an array (multiple targets)
                if (targetToken != null && targetToken.Type == JTokenType.Array)
                {
                    targetNames = targetToken.ToObject<List<string>>();
                }
                else
                {
                    targetName = targetToken?.ToString();
                }

                if (token["actions"] != null)
                {
                    var actionsToken = token["actions"];
                    if (actionsToken is JArray)
                    {
                        actionNames = actionsToken.ToObject<List<string>>();
                    }
                    else if (actionsToken?.Type == JTokenType.String)
                    {
                        // Handle single action as string
                        var singleAction = actionsToken.ToString();
                        if (!string.IsNullOrEmpty(singleAction))
                        {
                            actionNames = new List<string> { singleAction };
                        }
                    }
                    else
                    {
                        throw new Exception("Actions must be either a string or an array of strings!");
                    }
                }

                var guardTokeen = token["guard"];
                var condToken = token["cond"];
                
                if(guardTokeen != null)
                {
                    guard = guardTokeen.ToString();
                }
                else if(condToken != null)
                {
                    guard = condToken.ToString();
                }

                inCondition = token["in"]?.ToString();
            }
            catch (Exception)
            {
                throw new Exception("Invalid transition object!");
            }
        }

        // Handle single target (skip resolution for internal transitions)
        if (!string.IsNullOrEmpty(targetName) && targetName != ".")
        {
            targetName = ResolveAbsolutePath(source.Name, targetName);
        }
        
        // Handle multiple targets
        if (targetNames != null && targetNames.Count > 0)
        {
            for (int i = 0; i < targetNames.Count; i++)
            {
                if (targetNames[i] != ".")
                {
                    targetNames[i] = ResolveAbsolutePath(source.Name, targetNames[i]) ?? targetNames[i];
                }
            }
        }

        Transition? transition = null;

        if (type == TransitionType.On)
        {
            transition = new OnTransition(machineId)
            {
                Event = @event,
            };
        }
        else if (type == TransitionType.After)
        {
            transition = new AfterTransition(machineId)
            {
                Delay = @event
            };
        }
        else if (type == TransitionType.Always)
        {
            transition = new AlwaysTransition(machineId)
            {
            };
        }
        else if (type == TransitionType.OnDone)
        {
            transition = new OnDoneTransition(machineId)
            {
            };
        }
        else if (type == TransitionType.OnError)
        {
            transition = new OnErrorTransition(machineId)
            {
            };
        }
        else
        {
            throw new Exception("Invalid transition type!");
        }

        transition.SourceName = source.Name;
        transition.TargetName = targetName;
        transition.TargetNames = targetNames; // Set multiple targets if present
        if(actionNames != null)   transition.Actions = GetActionCallbacks(actionNames);
        if(guard != null)  transition.Guard = GetGuardCallback(guard);
        transition.InCondition = !string.IsNullOrEmpty(inCondition) ? GetInConditionCallback(inCondition) : () => true;
        
        // Check for internal transition (target is null or ".")
        if (targetName == "." || (targetName == null && targetNames == null && transition.Actions != null))
        {
            transition.IsInternal = true;
            transition.TargetName = null; // Internal transitions don't have targets
        }

        switch (type)
        {
            case TransitionType.On:
                {
                    // Use thread-safe AddTransition method
                    source.OnTransitionMap.AddTransition(@event, transition);
                }
                break;
            case TransitionType.After:
                {
                    source.AfterTransition = transition as AfterTransition;
                }
                break;
            case TransitionType.Always:
                {
                    source.AlwaysTransition = transition as AlwaysTransition;
                }
                break;
            case TransitionType.OnDone:
                {
                    source.OnDoneTransition = transition as OnDoneTransition;
                }
                break;
            case TransitionType.OnError:
                {
                    // OnError transitions are stored in the OnTransitionMap
                    // Use thread-safe AddTransition method
                    source.OnTransitionMap.AddTransition("onError", transition);
                }
                break;

            default:
                throw new Exception("Invalid transition type!");
        }

        return transition;
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="key"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public List<NamedAction>? ParseActions(string? key, JToken? token)
    {
        List<NamedAction>? actions = null;

        if (token != null && key != null)
        {

            var jobj = token[key]?.ToObject<List<string>>();

            if (jobj == null)
            {
                return actions;
            }

            actions = new List<NamedAction>(4); // Pre-allocate with typical size

            if(ActionMap != null)
                foreach (var actionName in jobj)
                {
                    if (ActionMap.ContainsKey(actionName))
                    {
                        // Use thread-safe GetActions method
                        var actionList = ActionMap.GetActions(actionName);
                        actions.AddRange(actionList);
                    }
                }

            return actions;
        }

        return null;
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="currentPath"></param>
    /// <param name="target"></param>
    /// <returns></returns>
    public static string? ResolveAbsolutePath(string? currentPath, string target)
    {
        if (target.StartsWith("#"))
        {
            return target;
        }

        if (target.StartsWith("."))
        {
            return currentPath + target;
        }

        var currentPathArray = currentPath?.Split('.').ToList();
        if(currentPathArray != null)
        {
            var pathArray = new List<string>(currentPathArray);

            pathArray.RemoveAt(pathArray.Count - 1);
            pathArray.Add(target);

            return string.Join(".", pathArray);
        }
        else
        {
            return null;
        }
    }

    HistoryType ParseHistoryType(JToken token) =>  token["history"]?.ToString() == "deep" ? HistoryType.Deep : HistoryType.Shallow;

}


