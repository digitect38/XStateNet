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
        DelayMap? delayCallbacks
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

            stateMachine.machineId = $"#{rootToken["id"]}";
            stateMachine.ActionMap = actionCallbacks;
            stateMachine.GuardMap = guardCallbacks;
            stateMachine.ServiceMap = serviceCallbacks;
            stateMachine.DelayMap = delayCallbacks;

            _instanceMap[stateMachine.machineId] = stateMachine;

            if (rootToken.ContainsKey("context") && rootToken["context"] != null)
            {
                var tokenList = rootToken["context"];

                if (tokenList == null)
                {
                    throw new Exception("Invalid context object!");
                }

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
        DelayMap? delayCallbacks
    )
    {
        var stateMachine = new StateMachine() { };
        return ParseStateMachine(stateMachine, jsonScript, actionCallbacks, guardCallbacks, serviceCallbacks, delayCallbacks);
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
            foreach (var token in onTransitionTokens)
            {
                var eventName = token.Path.Split('.').Last();
                if (token.First != null)
                {
                    ParseTransitions(state, TransitionType.On, eventName, token.First);
                }
            }
        }

        var afterTokens = stateToken["after"];

        if (afterTokens != null)
        {
            foreach (var token in afterTokens)
            {
                var delay = token.Path.Split('.').Last();
                if (token.First != null)
                {
                    ParseTransitions(state, TransitionType.After, delay, token.First);
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

        var states = stateToken["states"];

        if (states != null)
        {
            ParseStates(states, stateName);
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
                result.AddRange(ActionMap[actionName]);
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
                targetName = token["target"]?.ToString();

                if (token["actions"] != null)
                {
                    if (token["actions"] is JArray)
                    {
                        actionNames = token["actions"]?.ToObject<List<string>>();
                    }
                    else
                    {
                        throw new Exception("Actions that non-array type is not yet supported!");
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

        if (string.IsNullOrEmpty(targetName))
        {
            targetName = null; //transitionToken.First.Type == JTokenType.String  ? transitionToken.First.ToString() : null;
        }
        else
        {
            targetName = ResolveAbsolutePath(source.Name, targetName);
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
        else
        {
            throw new Exception("Invalid transition type!");
        }

        transition.SourceName = source.Name;
        transition.TargetName = targetName;
        if(actionNames != null)   transition.Actions = GetActionCallbacks(actionNames);
        if(guard != null)  transition.Guard = GetGuardCallback(guard);
        transition.InCondition = !string.IsNullOrEmpty(inCondition) ? GetInConditionCallback(inCondition) : null;

        switch (type)
        {
            case TransitionType.On:
                {
                    if (!source.OnTransitionMap.ContainsKey(@event))
                    {
                        source.OnTransitionMap[@event] = new List<Transition>(2); // Pre-allocate with typical size
                    }
                    source.OnTransitionMap[@event].Add(transition);
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
                        actions.AddRange(ActionMap[actionName]);
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


