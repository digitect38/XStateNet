using Xunit;
using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Tests;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdvancedFeatures;

public class StateMachine_AlwaysTests : OrchestratorTestBase
{
    private IPureStateMachine? _currentMachine;
       
    [Fact]
    public async Task TestAlwaysTransition()
    {
        var stateMachineJson = @"{
            'id': 'uniqueId',
            'initial': 'smallNumber',
            'context': { 'count': 0 },
            'states': {
                'smallNumber': {
                    'always': { 'target': 'bigNumber', 'guard': 'isBigNumber' }
                },
                'bigNumber': {
                    'always': { 'target': 'smallNumber', 'guard': 'isSmallNumber' }
                }
            },
            'on': {
                'INCREMENT': { 'actions': ['incrementCount', 'checkCount'] },
                'DECREMENT': { 'actions': ['decrementCount', 'checkCount'] },
                'RESET': { 'actions': ['resetCount', 'checkCount'] }
            }
        }";

        StateMachine? GetUnderlying() => (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

        void ResetCount(StateMachine sm)
        {
            sm.ContextMap!["count"] = 0;
        }

        bool IsSmallNumber(StateMachine sm)
        {
            var countValue = sm.ContextMap!["count"];
            var count = countValue is Newtonsoft.Json.Linq.JValue jv ? jv.ToObject<int>() : (int)(countValue ?? 0);
            return count <= 3;
        }

        bool IsBigNumber(StateMachine sm)
        {
            var countValue = sm.ContextMap!["count"];
            var count = countValue is Newtonsoft.Json.Linq.JValue jv ? jv.ToObject<int>() : (int)(countValue ?? 0);
            return count > 3;
        }

        void Increment(StateMachine sm)
        {
            var countValue = sm.ContextMap!["count"];
            var currentCount = countValue is Newtonsoft.Json.Linq.JValue jv ? jv.ToObject<int>() : (int)(countValue ?? 0);
            sm.ContextMap!["count"] = currentCount + 1;
        }

        void Decrement(StateMachine sm)
        {
            var countValue = sm.ContextMap!["count"];
            var currentCount = countValue is Newtonsoft.Json.Linq.JValue jv ? jv.ToObject<int>() : (int)(countValue ?? 0);
            sm.ContextMap!["count"] = currentCount - 1;
        }

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["incrementCount"] = (ctx) => Increment(GetUnderlying()!),
            ["decrementCount"] = (ctx) => Decrement(GetUnderlying()!),
            ["resetCount"] = (ctx) => ResetCount(GetUnderlying()!),
            ["checkCount"] = (ctx) => { }
        };

        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["isBigNumber"] = (sm) => IsBigNumber(sm),
            ["isSmallNumber"] = (sm) => IsSmallNumber(sm)
        };

        _currentMachine = CreateMachine("uniqueId", stateMachineJson, actions, guards);
        await _currentMachine.StartAsync();

        var underlying = GetUnderlying();
        underlying!.ContextMap!["count"] = 0;

        var currentState = _currentMachine.CurrentState;
        Assert.Contains("smallNumber", currentState);

        // Test incrementing to trigger always transition
        await SendEventAsync("TEST", _currentMachine, "INCREMENT");
        await SendEventAsync("TEST", _currentMachine, "INCREMENT");
        await SendEventAsync("TEST", _currentMachine, "INCREMENT");
        await SendEventAsync("TEST", _currentMachine, "INCREMENT");

        // Wait deterministically for state transition
        await WaitForStateAsync(_currentMachine, "bigNumber");

        currentState = _currentMachine.CurrentState;
        Assert.Contains("bigNumber", currentState);

        await SendEventAsync("TEST", _currentMachine, "DECREMENT");
        await SendEventAsync("TEST", _currentMachine, "DECREMENT");
        await SendEventAsync("TEST", _currentMachine, "DECREMENT");
        await SendEventAsync("TEST", _currentMachine, "DECREMENT");

        // Wait deterministically for state transition
        await WaitForStateAsync(_currentMachine, "smallNumber");

        currentState = _currentMachine.CurrentState;
        Assert.Contains("smallNumber", currentState);
    }
}
