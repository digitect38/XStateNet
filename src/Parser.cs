using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace XStateNet;

using ActionMap = ConcurrentDictionary<string, List<NamedAction>>;
using GuardMap = ConcurrentDictionary<string, NamedGuard>;


public partial class StateMachine
{
    public static StateMachine ParseStateMachine(StateMachine stateMachine, string jsonScript, ActionMap? actionCallbacks, GuardMap? guardCallbacks)
    {
        var jsonWithQuotedKeys = ConvertToQuotedKeys(jsonScript);
        var rootToken = JObject.Parse(jsonWithQuotedKeys);

        stateMachine.machineId = $"#{rootToken["id"]}";
        stateMachine.ActionMap = actionCallbacks;
        stateMachine.GuardMap = guardCallbacks;


        _instanceMap[stateMachine.machineId] = stateMachine;

        if (rootToken.ContainsKey("context") && rootToken["context"] != null)
        {
            foreach (JToken? contextItem in rootToken["context"])
            {
                stateMachine.ContextMap[contextItem.Path.Split('.').Last()] = contextItem.First;
            }
        }
        stateMachine.ParseState($"{stateMachine.machineId}", rootToken, null);
        stateMachine.InitializeCurrentStates();
        stateMachine.RootState.PrintCurrentStateTree(0);

        return stateMachine;
    }
    public static StateMachine ParseStateMachine(string jsonScript, ActionMap? actionCallbacks, GuardMap? guardCallbacks)
    {
        var stateMachine = new StateMachine() { };
        return ParseStateMachine(stateMachine, jsonScript, actionCallbacks, guardCallbacks);
    }

    private static string ConvertToQuotedKeys(string json)
    {
        var regex = new Regex(@"(?<!\\)(\b[a-zA-Z_][a-zA-Z0-9_]*\b)\s*:");
        var result = regex.Replace(json, "\"$1\":");
        return result;
    }

    public void ParseState(string stateName, JToken stateToken, string? parentName)
    {
        StateBase stateBase = null;

        var stateTypeStr = stateToken["type"]?.ToString();
        StateType stateType = stateTypeStr == "parallel" ? StateType.Parallel : stateTypeStr == "history" ? StateType.History : StateType.Normal;

        switch (stateType)
        {
            case StateType.History:
                var historyType = ParseHistoryType(stateToken);

                var histState = new Parser_HistoryState(machineId, historyType).Parse(stateName, parentName, stateToken) as HistoryState;
                RegisterState(histState);
                return; // no more settings

            case StateType.Parallel:
                stateBase = new Parser_ParallelState(machineId).Parse(stateName, parentName, stateToken) as ParallelState;
                RegisterState(stateBase);
                break;

            default:
                stateBase = new Parser_NormalState(machineId).Parse(stateName, parentName, stateToken) as NormalState;
                RegisterState(stateBase);
                break;
        }
    
        RealState state = stateBase as RealState;

        if (parentName == null)
        {
            RootState = state;
        }


        if (!string.IsNullOrEmpty(parentName))
        {
            var parent = StateMap[parentName] as RealState;
            parent.SubStateNames.Add(stateName);
        }
        else
        {

        }

        if (stateToken["on"] != null)
        {
            foreach (var transitionToken in stateToken["on"])
            {
                var eventName = transitionToken.Path.Split('.').Last();
                ParseTransitions(state, TransitionType.On, eventName, transitionToken.First);
            }
        }
        else if (stateToken["after"] != null)
        {
            foreach (var afterToken in stateToken["after"])
            {
                var delay = afterToken.Path.Split('.').Last();
                ParseTransitions(state, TransitionType.After, delay, afterToken.First);
            }
        }
        else if (stateToken["always"] != null)
        {
            var alwaysToken = stateToken["always"];
            ParseTransitions(state, TransitionType.Always, null, alwaysToken);
        }

        if (stateToken["states"] != null)
        {
            ParseStates(stateToken["states"], stateName);
        }
        Parser_Action.ParseActions(state, "exit", ActionMap, stateToken);
    }

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

    private List<Transition> ParseTransitions(RealState state, TransitionType type, string @event, JToken token)
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

    private Transition ParseTransition(RealState source, TransitionType type, string @event, JToken token)
    {

        List<string> actionNames = null;
        string targetName = "";
        string guard = null;
        string inCondition = null;

        if (token.Type == JTokenType.String)
        {
            // implicit target case..."on": { "TIMER": "yellow"  }
            targetName = token.ToString();
        }
        else if (token.Type == JTokenType.Object)
        {
            // explicit target or no target case...
            //    1) "on": { "TIMER": { target: "yellow" } }
            //    2) "on": { "TIMER": { actions: [ "incrementCount", "checkCount" ] } }

            try
            {
                targetName = token["target"]?.ToString();
                actionNames = token["actions"]?.ToObject<List<string>>();
                guard = token["guard"] != null ? token["guard"].ToString() : token["cond"] != null ? token["cond"].ToString() : null;
                //guard = token["guard"]?.ToString();
                inCondition = token["in"]?.ToString();
            }
            catch (Exception ex)
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

        Transition transition = null;

        if (type == TransitionType.On)
        {
            transition = new OnTransition()
            {
                Event = @event,
            };
        }
        else if (type == TransitionType.After)
        {
            transition = new AfterTransition()
            {
                Delay = int.Parse(@event),
            };
        }
        else if (type == TransitionType.Always)
        {
            transition = new AlwaysTransition()
            {
            };
        }
        else
        {
            throw new Exception("Invalid transition type!");
        }

        transition.stateMachineId = machineId;
        transition.SourceName = source.Name;
        transition.TargetName = targetName;
        transition.Actions = GetActionCallbacks(actionNames);
        transition.Guard = GetGuardCallback(guard);
        transition.InCondition = !string.IsNullOrEmpty(inCondition) ? GetInConditionCallback(inCondition) : null;

        var key = StateMachine.GenerateKey(source.Name, @event);

        switch (type)
        {
            case TransitionType.On:
                {
                    if (!source.OnTransitionMap.ContainsKey(key))
                    {
                        source.OnTransitionMap[key] = new List<Transition>();
                    }
                    source.OnTransitionMap[key].Add(transition);
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
            default:
                throw new Exception("Invalid transition type!");
        }


        return transition;
    }

    
    public List<NamedAction> ParseActions(string key, JToken token)
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
            if (ActionMap.ContainsKey(actionName))
            {
                actions.AddRange(ActionMap[actionName]);
            }
        }

        return actions;
    }
    
    public static string ResolveAbsolutePath(string currentPath, string target)
    {
        if (target.StartsWith("#"))
        {
            return target;
        }

        if (target.StartsWith("."))
        {
            return currentPath + target;
        }

        var currentPathArray = currentPath.Split('.').ToList();
        var pathArray = new List<string>(currentPathArray);

        pathArray.RemoveAt(pathArray.Count - 1);
        pathArray.Add(target);

        return string.Join(".", pathArray);
    }

    HistoryType ParseHistoryType(JToken token) =>  token["history"]?.ToString() == "deep" ? HistoryType.Deep : HistoryType.Shallow;
   

    /*
    public (List<State> existList, List<State> entryList) GetExitPath(State source, State target)
    {
        Stack<State> exitStack = new Stack<State>();
        StateMachine.
    }
    */

}


