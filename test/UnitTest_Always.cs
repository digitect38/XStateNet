using NUnit.Framework;
using XStateNet;
using XStateNet.UnitTest;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace AdvancedFeatures;

[TestFixture]
public class StateMachine_AlwaysTests
{
    private StateMachine _stateMachine;
       
    [Test]
    public void TestAlwaysTransition()
    {
        var stateMachineJson = @"{
            'id': 'counter',
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

        var actions = new ConcurrentDictionary<string, List<NamedAction>>
        {
            ["incrementCount"] = [new("incrementCount", (sm) => Increment(sm))],
            ["decrementCount"] = [new("decrementCount", (sm) => Decrement(sm))],
            ["resetCount"] = [new("resetCount", (sm) => ResetCount(sm))],
            ["checkCount"] = [new("checkCount", (sm) => { })],
        };

        void ResetCount(StateMachine sm)
        {
            sm.ContextMap["count"] = 0;
        }



        bool IsSmallNumber(StateMachine sm)
        {
            StateMachine.Log("in IsSmallNumber()...");
            var count = (int)sm.ContextMap["count"];
            StateMachine.Log(">>>>> count = " + count);
            return (int)sm.ContextMap["count"] <= 3;
        }

        bool IsBigNumber(StateMachine sm)
        {
            StateMachine.Log("in IsBigNumber()...");
            var count = (int)sm.ContextMap["count"];
            StateMachine.Log(">>>>> count = " + count);
            return (int)sm.ContextMap["count"] > 3;
        }

        void Increment(StateMachine sm)
        {
            sm.ContextMap["count"] = (int)sm.ContextMap["count"] + 1;

            StateMachine.Log("in Increment()... after increment,");
            var count = (int)sm.ContextMap["count"];
            StateMachine.Log(">>>>> count = " + count);

        };

        void Decrement(StateMachine sm)
        {
            sm.ContextMap["count"] = (int)sm.ContextMap["count"] - 1;
        }
        var guards = new ConcurrentDictionary<string, NamedGuard>
        {
            ["isBigNumber"] = new("isBigNumber", (sm) => IsBigNumber(sm)),
            ["isSmallNumber"] = new("isSmallNumber", (sm) => IsSmallNumber(sm))
        };

        _stateMachine = StateMachine.CreateFromScript(stateMachineJson, actions, guards).Start();

        _stateMachine.RootState.PrintActiveStateTree(0);

        _stateMachine.ContextMap["count"] = 0;

        var currentState = _stateMachine.GetActiveStateString();
        Assert.AreEqual("#counter.smallNumber", currentState);

        // Test incrementing to trigger always transition
        _stateMachine.Send("INCREMENT");
        _stateMachine.Send("INCREMENT");
        _stateMachine.Send("INCREMENT");
        _stateMachine.Send("INCREMENT");
        //_stateMachine.Send("INCREMENT");

        currentState = _stateMachine.GetActiveStateString();

        StateMachine.Log(">>>>> _stateMachine.ContextMap[\"count\"] = " + _stateMachine.ContextMap["count"]);
        Assert.AreEqual("#counter.bigNumber", currentState);

        _stateMachine.Send("DECREMENT");
        _stateMachine.Send("DECREMENT");
        _stateMachine.Send("DECREMENT");
        _stateMachine.Send("DECREMENT");

        currentState = _stateMachine.GetActiveStateString();
        currentState.AssertEquivalence("#counter.smallNumber");
    }
}
