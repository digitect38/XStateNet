using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Tests;
using Xunit;
using Xunit.Abstractions;

namespace ActorModelTests;

public class UnitTest_InternalTransitions : OrchestratorTestBase
{
    private readonly ITestOutputHelper _output;
    private IPureStateMachine? _currentMachine;
    private int _entryCount;
    private int _exitCount;
    private int _actionCount;
    private ConcurrentBag<string> _actionLog;
    private ConcurrentDictionary<string, int> _contextValues;

    public UnitTest_InternalTransitions(ITestOutputHelper output)
    {
        _output = output;
    }

    StateMachine? GetUnderlying() => (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

    private void ResetCounters()
    {
        _entryCount = 0;
        _exitCount = 0;
        _actionCount = 0;
        _actionLog = new ConcurrentBag<string>();
        _contextValues = new ConcurrentDictionary<string, int> { ["counter"] = 0 };
    }

    private Dictionary<string, Action<OrchestratedContext>> CreateActions()
    {
        return new Dictionary<string, Action<OrchestratedContext>>
        {
            ["entryAction"] = (ctx) => {
                _entryCount++;
                _actionLog.Add("entry");
            },
            ["exitAction"] = (ctx) => {
                _exitCount++;
                _actionLog.Add("exit");
            },
            ["incrementCounter"] = (ctx) => {
                _actionCount++;
                var underlying = GetUnderlying();
                if (underlying?.ContextMap != null)
                {
                    var currentValue = underlying.ContextMap["counter"];
                    int counter = currentValue is Newtonsoft.Json.Linq.JValue jval
                        ? jval.ToObject<int>()
                        : Convert.ToInt32(currentValue ?? 0);
                    underlying.ContextMap["counter"] = counter + 1;
                    _contextValues["counter"] = counter + 1;
                    _actionLog.Add($"increment:{underlying.ContextMap["counter"]}");
                }
            },
            ["updateValue"] = (ctx) => {
                var underlying = GetUnderlying();
                if (underlying?.ContextMap != null)
                {
                    underlying.ContextMap["value"] = "updated";
                    _actionLog.Add("update");
                }
            },
            ["log"] = (ctx) => {
                var underlying = GetUnderlying();
                if (underlying != null)
                {
                    _actionLog.Add($"log:{underlying.GetActiveStateNames()}");
                }
            },
            ["incrementInternal"] = (ctx) => {
                var underlying = GetUnderlying();
                if (underlying?.ContextMap != null)
                {
                    var currentValue = underlying.ContextMap["internalCount"];
                    int internalCount = currentValue is Newtonsoft.Json.Linq.JValue jval
                        ? jval.ToObject<int>()
                        : Convert.ToInt32(currentValue ?? 0);
                    underlying.ContextMap["internalCount"] = internalCount + 1;
                }
            },
            ["incrementExternal"] = (ctx) => {
                var underlying = GetUnderlying();
                if (underlying?.ContextMap != null)
                {
                    var currentValue = underlying.ContextMap["externalCount"];
                    int externalCount = currentValue is Newtonsoft.Json.Linq.JValue jval
                        ? jval.ToObject<int>()
                        : Convert.ToInt32(currentValue ?? 0);
                    underlying.ContextMap["externalCount"] = externalCount + 1;
                }
            }
        };
    }

    private Dictionary<string, Func<StateMachine, bool>> CreateGuards()
    {
        return new Dictionary<string, Func<StateMachine, bool>>
        {
            ["lessThanFive"] = (sm) => {
                var currentValue = sm.ContextMap?["counter"];
                int counter = currentValue is Newtonsoft.Json.Linq.JValue jval
                    ? jval.ToObject<int>()
                    : Convert.ToInt32(currentValue ?? 0);
                return counter < 5;
            }
        };
    }

    [Fact]
    public async Task TestBasicInternalTransition()
    {
        ResetCounters();

        string script = @$"
        {{
            id: 'TEST',
            initial: 'active',
            context: {{
                counter: 0
            }},
            states: {{
                active: {{
                    entry: 'entryAction',
                    exit: 'exitAction',
                    on: {{
                        INCREMENT: {{
                            target: '.',
                            actions: 'incrementCounter'
                        }},
                        EXTERNAL: 'done'
                    }}
                }},
                done: {{
                    type: 'final'
                }}
            }}
        }}";

        var actions = CreateActions();
        var guards = CreateGuards();
        _currentMachine = CreateMachine("basicInternalTrans", script, actions, guards);
        await _currentMachine.StartAsync();

        var currentState = _currentMachine.CurrentState;
        Assert.Contains("active", currentState);
        Assert.Equal(1, _entryCount); // Entry called once on start

        var uniqueId = _currentMachine.Id;
        // Send internal transition event multiple times
        _output.WriteLine($"Sending first INCREMENT. ActionCount before: {_actionCount}");
        await SendEventAsync("TEST", uniqueId, "INCREMENT");
        await Task.Delay(100);
        _output.WriteLine($"After first INCREMENT. ActionCount: {_actionCount}, EntryCount: {_entryCount}, ExitCount: {_exitCount}");

        await SendEventAsync("TEST", uniqueId, "INCREMENT");
        await Task.Delay(100);
        _output.WriteLine($"After second INCREMENT. ActionCount: {_actionCount}");

        await SendEventAsync("TEST", uniqueId, "INCREMENT");
        await Task.Delay(100);
        _output.WriteLine($"After third INCREMENT. ActionCount: {_actionCount}");
        _output.WriteLine($"Action log: {string.Join(", ", _actionLog)}");

        // State should not change, entry/exit should not be called again
        currentState = _currentMachine.CurrentState;
        Assert.Contains("active", currentState);
        Assert.Equal(1, _entryCount); // Still only once
        Assert.Equal(0, _exitCount); // Never called for internal
        Assert.Equal(3, _actionCount); // Action called 3 times
        Assert.Equal(3, _contextValues["counter"]);

        // Now do external transition
        await SendEventAsync("TEST", uniqueId, "EXTERNAL");
        await Task.Delay(100);
        currentState = _currentMachine.CurrentState;
        Assert.Contains("done", currentState);
        Assert.Equal(1, _exitCount); // Exit called on external transition
    }

    [Fact]
    public async Task TestInternalTransitionWithNullTarget()
    {
        ResetCounters();

        string script = $$"""
        {
            id: 'machineId',
            initial: 'counting',
            states: {
                counting: {
                    entry: 'entryAction',
                    on: {
                        UPDATE: {
                            actions: 'incrementCounter'
                        },
                        FINISH: 'complete'
                    }
                },
                complete: {
                    type: 'final'
                }
            }
        }
        """;

        var actions = CreateActions();
        var guards = CreateGuards();
        _currentMachine = CreateMachine("machineId", script, actions, guards);

        var underlying = GetUnderlying();
        if (underlying?.ContextMap != null)
        {
            underlying.ContextMap["counter"] = 0;
        }

        await _currentMachine.StartAsync();

        Assert.Equal(1, _entryCount);

        // Null target with actions should be internal
        await SendEventAsync("TEST", _currentMachine.Id, "UPDATE");
        await Task.Delay(100);
        await SendEventAsync("TEST", _currentMachine.Id, "UPDATE");
        await Task.Delay(100);

        var currentState = _currentMachine.CurrentState;
        Assert.Contains("counting", currentState);
        Assert.Equal(1, _entryCount); // No re-entry
        Assert.Equal(2, _actionCount);

        underlying = GetUnderlying();
        if (underlying?.ContextMap != null)
        {
            var counterVal = underlying.ContextMap["counter"];
            Assert.Equal(2, counterVal is Newtonsoft.Json.Linq.JValue jv ? jv.ToObject<int>() : (int)(counterVal ?? 0));
        }
    }

    [Fact]
    public async Task TestInternalTransitionInNestedStates()
    {
        ResetCounters();

        string script = $$"""
        {
            id: 'machineId',
            initial: 'parent',
            states: {
                parent: {
                    initial: 'child',
                    entry: 'entryAction',
                    exit: 'exitAction',
                    states: {
                        child: {
                            entry: 'entryAction',
                            exit: 'exitAction',
                            on: {
                                INTERNAL_UPDATE: {
                                    target: '.',
                                    actions: 'updateValue'
                                },
                                EXTERNAL_UPDATE: 'sibling'
                            }
                        },
                        sibling: {
                            type: 'final'
                        }
                    }
                }
            }
        }
        """;

        var actions = CreateActions();
        var guards = CreateGuards();
        _currentMachine = CreateMachine("machineId", script, actions, guards);
        await _currentMachine.StartAsync();

        Assert.Equal(2, _entryCount); // Parent and child entry

        await SendEventAsync("TEST", _currentMachine.Id, "INTERNAL_UPDATE");
        await Task.Delay(100);

        // Should update context without re-entering states
        Assert.Equal(2, _entryCount); // No additional entries

        var underlying = GetUnderlying();
        if (underlying?.ContextMap != null)
        {
            Assert.Equal("updated", underlying.ContextMap["value"]);
        }

        var currentState = _currentMachine.CurrentState;
        Assert.Contains("child", currentState);

        await SendEventAsync("TEST", _currentMachine.Id, "EXTERNAL_UPDATE");
        await Task.Delay(100);

        // External transition should trigger exit/entry
        currentState = _currentMachine.CurrentState;
        Assert.Contains("sibling", currentState);
        Assert.Equal(1, _exitCount); // Child exit
    }

    [Fact]
    public async Task TestInternalTransitionWithGuards()
    {
        ResetCounters();

        string script = $$"""
        {
            id: 'machineId',
            initial: 'counting',
            context: {
                counter: 0
            },
            states: {
                counting: {
                    on: {
                        INCREMENT: [
                            {
                                target: '.',
                                cond: 'lessThanFive',
                                actions: 'incrementCounter'
                            },
                            {
                                target: 'maxReached'
                            }
                        ]
                    }
                },
                maxReached: {
                    entry: 'log',
                    type: 'final'
                }
            }
        }
        """;

        var actions = CreateActions();
        var guards = CreateGuards();
        _currentMachine = CreateMachine("machineId", script, actions, guards);
        await _currentMachine.StartAsync();

        // Increment while counter < 5 (internal transitions)
        for (int i = 0; i < 4; i++)
        {
            await SendEventAsync("TEST", _currentMachine.Id, "INCREMENT");
            await Task.Delay(100);
            var currentState = _currentMachine.CurrentState;
            Assert.Contains("counting", currentState);
        }

        Assert.Equal(4, _actionCount);

        var underlying = GetUnderlying();
        if (underlying?.ContextMap != null)
        {
            var counter1 = underlying.ContextMap["counter"];
            Assert.Equal(4, counter1 is Newtonsoft.Json.Linq.JValue jv1 ? jv1.ToObject<int>() : (int)(counter1 ?? 0));

            // One more increment should reach 5 and transition externally
            await SendEventAsync("TEST", _currentMachine.Id, "INCREMENT");
            await Task.Delay(100);
            Assert.Equal(5, _actionCount);
            var counter2 = underlying.ContextMap["counter"];
            Assert.Equal(5, counter2 is Newtonsoft.Json.Linq.JValue jv2 ? jv2.ToObject<int>() : (int)(counter2 ?? 0));
        }

        // Next increment should trigger external transition
        await SendEventAsync("TEST", _currentMachine.Id, "INCREMENT");
        await Task.Delay(100);
        var finalState = _currentMachine.CurrentState;
        Assert.Contains("maxReached", finalState);
    }

    [Fact]
    public async Task TestInternalTransitionInParallelStates()
    {
        ResetCounters();

        string script = $$"""
        {
            id: 'machineId',
            type: 'parallel',
            states: {
                regionA: {
                    initial: 'stateA',
                    states: {
                        stateA: {
                            entry: 'entryAction',
                            on: {
                                UPDATE_A: {
                                    target: '.',
                                    actions: 'incrementCounter'
                                }
                            }
                        }
                    }
                },
                regionB: {
                    initial: 'stateB',
                    states: {
                        stateB: {
                            entry: 'entryAction',
                            on: {
                                UPDATE_B: {
                                    target: '.',
                                    actions: 'updateValue'
                                }
                            }
                        }
                    }
                }
            }
        }
        """;

        var actions = CreateActions();
        var guards = CreateGuards();
        _currentMachine = CreateMachine("machineId", script, actions, guards);

        var underlying = GetUnderlying();
        if (underlying?.ContextMap != null)
        {
            underlying.ContextMap["counter"] = 0;
        }

        await _currentMachine.StartAsync();

        var initialEntries = _entryCount;

        // Update region A internally
        await SendEventAsync("TEST", _currentMachine.Id, "UPDATE_A");
        await Task.Delay(100);
        Assert.Equal(initialEntries, _entryCount); // No re-entry

        underlying = GetUnderlying();
        if (underlying?.ContextMap != null)
        {
            var counterValue = underlying.ContextMap["counter"];
            Assert.Equal(1, counterValue is Newtonsoft.Json.Linq.JValue jv3 ? jv3.ToObject<int>() : (int)(counterValue ?? 0));

            // Update region B internally
            await SendEventAsync("TEST", _currentMachine.Id, "UPDATE_B");
            await Task.Delay(100);
            Assert.Equal(initialEntries, _entryCount); // Still no re-entry
            Assert.Equal("updated", underlying.ContextMap["value"]);
        }

        // Both regions should still be in their original states
        var activeStates = _currentMachine.CurrentState;
        Assert.Contains("stateA", activeStates);
        Assert.Contains("stateB", activeStates);
    }

    [Fact]
    public async Task TestMixedInternalAndExternalTransitions()
    {
        ResetCounters();

        string script = $$"""
        {
            id: 'machineId',
            initial: 'idle',
            context: {
                internalCount: 0,
                externalCount: 0
            },
            states: {
                idle: {
                    on: {
                        START: 'active'
                    }
                },
                active: {
                    entry: 'entryAction',
                    exit: 'exitAction',
                    on: {
                        INTERNAL: {
                            target: '.',
                            actions: 'incrementInternal'
                        },
                        EXTERNAL: {
                            target: 'active',
                            actions: 'incrementExternal'
                        },
                        DONE: 'complete'
                    }
                },
                complete: {
                    type: 'final'
                }
            }
        }
        """;

        var actions = CreateActions();
        var guards = CreateGuards();
        _currentMachine = CreateMachine("machineId", script, actions, guards);
        await _currentMachine.StartAsync();

        await SendEventAsync("TEST", _currentMachine.Id, "START");
        await Task.Delay(100);
        var initialEntries = _entryCount;
        var initialExits = _exitCount;

        // Internal transitions
        await SendEventAsync("TEST", _currentMachine.Id, "INTERNAL");
        await Task.Delay(100);
        await SendEventAsync("TEST", _currentMachine.Id, "INTERNAL");
        await Task.Delay(100);

        Assert.Equal(initialEntries, _entryCount);
        Assert.Equal(initialExits, _exitCount);

        var underlying = GetUnderlying();
        if (underlying?.ContextMap != null)
        {
            var internalCount1 = underlying.ContextMap["internalCount"];
            var externalCount1 = underlying.ContextMap["externalCount"];
            Assert.Equal(2, internalCount1 is Newtonsoft.Json.Linq.JValue jvi1 ? jvi1.ToObject<int>() : (int)(internalCount1 ?? 0));
            Assert.Equal(0, externalCount1 is Newtonsoft.Json.Linq.JValue jve1 ? jve1.ToObject<int>() : (int)(externalCount1 ?? 0));

            // External self-transitions
            await SendEventAsync("TEST", _currentMachine.Id, "EXTERNAL");
            await Task.Delay(100);
            await SendEventAsync("TEST", _currentMachine.Id, "EXTERNAL");
            await Task.Delay(100);  

            Assert.Equal(initialEntries + 2, _entryCount); // Re-entered twice
            Assert.Equal(initialExits + 2, _exitCount); // Exited twice
            var internalCount2 = underlying.ContextMap["internalCount"];
            var externalCount2 = underlying.ContextMap["externalCount"];
            Assert.Equal(2, internalCount2 is Newtonsoft.Json.Linq.JValue jvi2 ? jvi2.ToObject<int>() : (int)(internalCount2 ?? 0));
            Assert.Equal(2, externalCount2 is Newtonsoft.Json.Linq.JValue jve2 ? jve2.ToObject<int>() : (int)(externalCount2 ?? 0));
        }

        await SendEventAsync("TEST", _currentMachine.Id, "DONE");
        await Task.Delay(100);
        var currentState = _currentMachine.CurrentState;
        Assert.Contains("complete", currentState);
    }
}
