using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Tests;
using Xunit;

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

    [Fact]
    public async Task TestAlwaysTransition_AutomaticFiring()
    {
        // Test that always transitions fire automatically without explicit events
        var stateMachineJson = @"{
            'id': 'autoAlways',
            'initial': 'init',
            'context': { 'ready': false },
            'states': {
                'init': {
                    'always': { 'target': 'ready', 'guard': 'isReady' }
                },
                'ready': {
                    'type': 'final'
                }
            }
        }";

        StateMachine? GetUnderlying() => (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

        bool IsReady(StateMachine sm)
        {
            var readyValue = sm.ContextMap!["ready"];
            return readyValue is bool b && b;
        }

        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["isReady"] = (sm) => IsReady(sm)
        };

        _currentMachine = CreateMachine("autoAlways", stateMachineJson, new Dictionary<string, Action<OrchestratedContext>>(), guards);

        // Set ready = true BEFORE starting
        var underlying = (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;
        underlying!.ContextMap!["ready"] = true;

        await _currentMachine.StartAsync();

        // Wait for automatic transition to ready state
        await WaitForStateAsync(_currentMachine, "ready", timeoutMs: 1000);

        var currentState = _currentMachine.CurrentState;
        Assert.Contains("ready", currentState);
    }

    [Fact]
    public async Task TestAlwaysTransition_EmptyStringEvent()
    {
        // Test that on: { '': ... } syntax works as eventless transition
        var stateMachineJson = @"{
            'id': 'emptyEvent',
            'initial': 'waiting',
            'context': { 'count': 0 },
            'states': {
                'waiting': {
                    'on': {
                        '': { 'target': 'done', 'guard': 'countIsThree' },
                        'INCREMENT': { 'actions': ['increment'] }
                    }
                },
                'done': {
                    'type': 'final'
                }
            }
        }";

        StateMachine? GetUnderlying() => (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

        void Increment(StateMachine sm)
        {
            var countValue = sm.ContextMap!["count"];
            var currentCount = countValue is Newtonsoft.Json.Linq.JValue jv ? jv.ToObject<int>() : (int)(countValue ?? 0);
            sm.ContextMap!["count"] = currentCount + 1;
        }

        bool CountIsThree(StateMachine sm)
        {
            var countValue = sm.ContextMap!["count"];
            var count = countValue is Newtonsoft.Json.Linq.JValue jv ? jv.ToObject<int>() : (int)(countValue ?? 0);
            return count == 3;
        }

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["increment"] = (ctx) => Increment(GetUnderlying()!)
        };

        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["countIsThree"] = (sm) => CountIsThree(sm)
        };

        _currentMachine = CreateMachine("emptyEvent", stateMachineJson, actions, guards);
        await _currentMachine.StartAsync();

        Assert.Contains("waiting", _currentMachine.CurrentState);

        // Increment count to 3
        await SendEventAsync("TEST", _currentMachine, "INCREMENT");
        await SendEventAsync("TEST", _currentMachine, "INCREMENT");
        await SendEventAsync("TEST", _currentMachine, "INCREMENT");

        // Wait for automatic transition (should fire after last INCREMENT)
        await WaitForStateAsync(_currentMachine, "done", timeoutMs: 1000);

        Assert.Contains("done", _currentMachine.CurrentState);
    }

    [Fact]
    public async Task TestAlwaysTransition_MultipleCandidates()
    {
        // Test that first matching always transition executes
        var stateMachineJson = @"{
            'id': 'multiAlways',
            'initial': 'start',
            'context': { 'value': 0 },
            'states': {
                'start': {
                    'always': [
                        { 'target': 'high', 'guard': 'isHigh' },
                        { 'target': 'medium', 'guard': 'isMedium' },
                        { 'target': 'low' }
                    ]
                },
                'high': { 'type': 'final' },
                'medium': { 'type': 'final' },
                'low': { 'type': 'final' }
            }
        }";

        StateMachine? GetUnderlying() => (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

        bool IsHigh(StateMachine sm)
        {
            var value = sm.ContextMap!["value"];
            var v = value is Newtonsoft.Json.Linq.JValue jv ? jv.ToObject<int>() : (int)(value ?? 0);
            return v > 10;
        }

        bool IsMedium(StateMachine sm)
        {
            var value = sm.ContextMap!["value"];
            var v = value is Newtonsoft.Json.Linq.JValue jv ? jv.ToObject<int>() : (int)(value ?? 0);
            return v > 5;
        }

        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["isHigh"] = (sm) => IsHigh(sm),
            ["isMedium"] = (sm) => IsMedium(sm)
        };

        _currentMachine = CreateMachine("multiAlways", stateMachineJson, new Dictionary<string, Action<OrchestratedContext>>(), guards);

        var underlying = GetUnderlying();
        underlying!.ContextMap!["value"] = 15;

        await _currentMachine.StartAsync();

        await WaitForStateAsync(_currentMachine, "high", timeoutMs: 1000);
        Assert.Contains("high", _currentMachine.CurrentState);
    }

    [Fact]
    public async Task TestAlwaysTransition_LoopProtection()
    {
        // Test that infinite loops are prevented
        var stateMachineJson = @"{
            'id': 'loopTest',
            'initial': 'a',
            'context': { 'counter': 0 },
            'states': {
                'a': {
                    'entry': ['incrementCounter'],
                    'always': { 'target': 'b' }
                },
                'b': {
                    'entry': ['incrementCounter'],
                    'always': { 'target': 'a' }
                }
            }
        }";

        StateMachine? GetUnderlying() => (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

        void IncrementCounter(StateMachine sm)
        {
            var counterValue = sm.ContextMap!["counter"];
            var counter = counterValue is Newtonsoft.Json.Linq.JValue jv ? jv.ToObject<int>() : (int)(counterValue ?? 0);
            sm.ContextMap!["counter"] = counter + 1;
        }

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["incrementCounter"] = (ctx) => IncrementCounter(GetUnderlying()!)
        };

        _currentMachine = CreateMachine("loopTest", stateMachineJson, actions, new Dictionary<string, Func<StateMachine, bool>>());
        await _currentMachine.StartAsync();

        // Give it time to loop if it's going to
        await Task.Delay(500);

        var underlying = GetUnderlying();
        var counterValue = underlying!.ContextMap!["counter"];
        var counter = counterValue is Newtonsoft.Json.Linq.JValue jv ? jv.ToObject<int>() : (int)(counterValue ?? 0);

        // Should have stopped after max iterations (10 by default)
        // Counter increments on entry, so we expect around 10-11 increments before loop protection kicks in
        Assert.True(counter <= 15, $"Loop protection failed - counter reached {counter}");
    }

    [Fact]
    public async Task TestAlwaysTransition_NoGuard()
    {
        // Test always transition without guard (should always fire)
        var stateMachineJson = @"{
            'id': 'noGuard',
            'initial': 'start',
            'states': {
                'start': {
                    'always': { 'target': 'end' }
                },
                'end': {
                    'type': 'final'
                }
            }
        }";

        _currentMachine = CreateMachine("noGuard", stateMachineJson, new Dictionary<string, Action<OrchestratedContext>>(), new Dictionary<string, Func<StateMachine, bool>>());
        await _currentMachine.StartAsync();

        await WaitForStateAsync(_currentMachine, "end", timeoutMs: 1000);
        Assert.Contains("end", _currentMachine.CurrentState);
    }

    [Fact]
    public async Task TestAlwaysTransition_WithActions()
    {
        // Test always transition with actions
        var stateMachineJson = @"{
            'id': 'withActions',
            'initial': 'start',
            'context': { 'actionExecuted': false },
            'states': {
                'start': {
                    'always': {
                        'target': 'end',
                        'actions': ['markExecuted']
                    }
                },
                'end': {
                    'type': 'final'
                }
            }
        }";

        StateMachine? GetUnderlying() => (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

        void MarkExecuted(StateMachine sm)
        {
            sm.ContextMap!["actionExecuted"] = true;
        }

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["markExecuted"] = (ctx) => MarkExecuted(GetUnderlying()!)
        };

        _currentMachine = CreateMachine("withActions", stateMachineJson, actions, new Dictionary<string, Func<StateMachine, bool>>());
        await _currentMachine.StartAsync();

        await WaitForStateAsync(_currentMachine, "end", timeoutMs: 1000);

        var underlying = GetUnderlying();
        var actionExecuted = underlying!.ContextMap!["actionExecuted"];
        Assert.True(actionExecuted is bool b && b, "Action should have executed during always transition");
    }
}
