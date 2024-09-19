using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Linq;
using System.Diagnostics;

namespace XStateNet;

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
        var jsonWithQuotedKeys = ConvertToQuotedKeys(jsonScript);
        //Debug.WriteLine(jsonWithQuotedKeys);
        var rootToken = JObject.Parse(jsonWithQuotedKeys);

        if (rootToken == null)
        {
            throw new Exception("Invalid JSON script!");
        }

        if(stateMachine == null)
        {
            throw new Exception("StateMachine is null!");
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
        var regex = new Regex(@"(?<!\\)(\b[a-zA-Z_][a-zA-Z0-9_]*\b)\s*:");
        if (json == null) throw new Exception("Invalid JSON script!");
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
                    throw new Exception("HistoryState is null!");
                }

                RegisterState(stateBase);
                return; // no more settings needed!

            case StateType.Parallel:

                stateBase = new Parser_ParallelState(machineId).Parse(stateName, parentName, stateToken) as ParallelState;

                if (stateBase == null)
                {
                    throw new Exception("ParallelState is null!");
                }

                RegisterState(stateBase);
                break;

            case StateType.Final:

                stateBase = new Parser_FinalState(machineId).Parse(stateName, parentName, stateToken) as FinalState;

                if (stateBase == null)
                {
                    throw new Exception("FinalState is null!");
                }

                RegisterState(stateBase);
                break;

            default:
                stateBase = new Parser_NormalState(machineId).Parse(stateName, parentName, stateToken) as NormalState;

                if (stateBase == null)
                {
                    throw new Exception("NormalState is null!");
                }

                RegisterState(stateBase);
                break;
        }

        CompoundState state = (CompoundState)stateBase;

        if (state == null)
        {
            throw new Exception("RealState is null!");
        }

        if (parentName == null)
        {
            RootState = state;
        }


        if (!string.IsNullOrEmpty(parentName))
        {
            if(StateMap == null)
            {
                throw new Exception("StateMap is null!");
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

        var result = new List<NamedAction>();

        foreach (var actionName in actionNames)
        {
            if (ActionMap.ContainsKey(actionName))
            {
                result.AddRange(ActionMap[actionName]);
            }
        }
        return result;
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
        List<Transition> transitions = new List<Transition>();

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
        transition.Actions = GetActionCallbacks(actionNames);
        transition.Guard = GetGuardCallback(guard);
        transition.InCondition = !string.IsNullOrEmpty(inCondition) ? GetInConditionCallback(inCondition) : null;

        switch (type)
        {
            case TransitionType.On:
                {
                    if (!source.OnTransitionMap.ContainsKey(@event))
                    {
                        source.OnTransitionMap[@event] = new List<Transition>();
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
    public List<NamedAction> ParseActions(string key, JToken? token)
    {
        List<NamedAction>? actions = null;

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
            if (ActionMap.ContainsKey(actionName))
            {
                actions.AddRange(ActionMap[actionName]);
            }
        }

        return actions;
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


