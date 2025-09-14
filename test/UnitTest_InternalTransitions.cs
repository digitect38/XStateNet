using Xunit;

using System;
using System.Collections.Generic;
using XStateNet;
namespace ActorModelTests;

public class UnitTest_InternalTransitions : IDisposable
{
    private StateMachine? _stateMachine;
    private ActionMap _actions;
    private GuardMap _guards;
    private int _entryCount;
    private int _exitCount;
    private int _actionCount;
    private List<string> _actionLog;
    private Dictionary<string, int> _contextValues;
    
    public UnitTest_InternalTransitions()
    {
        _entryCount = 0;
        _exitCount = 0;
        _actionCount = 0;
        _actionLog = new List<string>();
        _contextValues = new Dictionary<string, int> { ["counter"] = 0 };
        
        _actions = new ActionMap
        {
            ["entryAction"] = new List<NamedAction> { new NamedAction("entryAction", (sm) => {
                _entryCount++;
                _actionLog.Add("entry");
            }) },
            ["exitAction"] = new List<NamedAction> { new NamedAction("exitAction", (sm) => {
                _exitCount++;
                _actionLog.Add("exit");
            }) },
            ["incrementCounter"] = new List<NamedAction> { new NamedAction("incrementCounter", (sm) => {
                _actionCount++;
                var currentValue = sm.ContextMap!["counter"];
                int counter = currentValue is Newtonsoft.Json.Linq.JValue jval 
                    ? jval.ToObject<int>() 
                    : Convert.ToInt32(currentValue ?? 0);
                sm.ContextMap["counter"] = counter + 1;
                _contextValues["counter"] = counter + 1;
                _actionLog.Add($"increment:{sm.ContextMap["counter"]}");
            }) },
            ["updateValue"] = new List<NamedAction> { new NamedAction("updateValue", (sm) => {
                sm.ContextMap!["value"] = "updated";
                _actionLog.Add("update");
            }) },
            ["log"] = new List<NamedAction> { new NamedAction("log", (sm) => {
                _actionLog.Add($"log:{sm.GetActiveStateString()}");
            }) },
            ["incrementInternal"] = new List<NamedAction> { new NamedAction("incrementInternal", (sm) => {
                var currentValue = sm.ContextMap!["internalCount"];
                int internalCount = currentValue is Newtonsoft.Json.Linq.JValue jval 
                    ? jval.ToObject<int>() 
                    : Convert.ToInt32(currentValue ?? 0);
                sm.ContextMap["internalCount"] = internalCount + 1;
            }) },
            ["incrementExternal"] = new List<NamedAction> { new NamedAction("incrementExternal", (sm) => {
                var currentValue = sm.ContextMap!["externalCount"];
                int externalCount = currentValue is Newtonsoft.Json.Linq.JValue jval 
                    ? jval.ToObject<int>() 
                    : Convert.ToInt32(currentValue ?? 0);
                sm.ContextMap["externalCount"] = externalCount + 1;
            }) }
        };
        
        _guards = new GuardMap
        {
            ["lessThanFive"] = new NamedGuard("lessThanFive", (sm) => {
                var currentValue = sm.ContextMap?["counter"];
                int counter = currentValue is Newtonsoft.Json.Linq.JValue jval 
                    ? jval.ToObject<int>() 
                    : Convert.ToInt32(currentValue ?? 0);
                return counter < 5;
            })
        };
    }
    
    [Fact]
    public void TestBasicInternalTransition()
    {
        const string script = @"
        {
            'id': 'internalTest',
            'initial': 'active',
            'context': {
                'counter': 0
            },
            'states': {
                'active': {
                    'entry': 'entryAction',
                    'exit': 'exitAction',
                    'on': {
                        'INCREMENT': {
                            'target': '.',
                            'actions': 'incrementCounter'
                        },
                        'EXTERNAL': 'done'
                    }
                },
                'done': {
                    'type': 'final'
                }
            }
        }";
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        _stateMachine!.Start();
        
        Assert.Contains("active", _stateMachine.GetActiveStateString());
        Assert.Equal(1, _entryCount); // Entry called once on start
        
        // Send internal transition event multiple times
        _stateMachine!.Send("INCREMENT");
        _stateMachine!.Send("INCREMENT");
        _stateMachine!.Send("INCREMENT");
        
        // State should not change, entry/exit should not be called again
        Assert.Contains("active", _stateMachine.GetActiveStateString());
        Assert.Equal(1, _entryCount); // Still only once
        Assert.Equal(0, _exitCount); // Never called for internal
        Assert.Equal(3, _actionCount); // Action called 3 times
        Assert.Equal(3, _contextValues["counter"]);
        
        // Now do external transition
        _stateMachine!.Send("EXTERNAL");
        Assert.Contains("done", _stateMachine.GetActiveStateString());
        Assert.Equal(1, _exitCount); // Exit called on external transition
    }
    
    [Fact]
    public void TestInternalTransitionWithNullTarget()
    {
        const string script = @"
        {
            'id': 'nullTargetTest',
            'initial': 'counting',
            'states': {
                'counting': {
                    'entry': 'entryAction',
                    'on': {
                        'UPDATE': {
                            'actions': 'incrementCounter'
                        },
                        'FINISH': 'complete'
                    }
                },
                'complete': {
                    'type': 'final'
                }
            }
        }";
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        _stateMachine.ContextMap!["counter"] = 0;
        _stateMachine!.Start();
        
        Assert.Equal(1, _entryCount);
        
        // Null target with actions should be internal
        _stateMachine!.Send("UPDATE");
        _stateMachine!.Send("UPDATE");
        
        Assert.Contains("counting", _stateMachine.GetActiveStateString());
        Assert.Equal(1, _entryCount); // No re-entry
        Assert.Equal(2, _actionCount);
        var counterVal = _stateMachine!.ContextMap!["counter"];
        Assert.Equal(2, counterVal is Newtonsoft.Json.Linq.JValue jv ? jv.ToObject<int>() : (int)(counterVal ?? 0));
    }
    
    [Fact]
    public void TestInternalTransitionInNestedStates()
    {
        const string script = @"
        {
            'id': 'nestedInternal',
            'initial': 'parent',
            'states': {
                'parent': {
                    'initial': 'child',
                    'entry': 'entryAction',
                    'exit': 'exitAction',
                    'states': {
                        'child': {
                            'entry': 'entryAction',
                            'exit': 'exitAction',
                            'on': {
                                'INTERNAL_UPDATE': {
                                    'target': '.',
                                    'actions': 'updateValue'
                                },
                                'EXTERNAL_UPDATE': 'sibling'
                            }
                        },
                        'sibling': {
                            'type': 'final'
                        }
                    }
                }
            }
        }";
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        _stateMachine!.Start();
        
        Assert.Equal(2, _entryCount); // Parent and child entry
        
        _stateMachine!.Send("INTERNAL_UPDATE");
        
        // Should update context without re-entering states
        Assert.Equal(2, _entryCount); // No additional entries
        Assert.Equal("updated", _stateMachine.ContextMap!["value"]);
        Assert.Contains("child", _stateMachine.GetActiveStateString());
        
        _stateMachine!.Send("EXTERNAL_UPDATE");
        
        // External transition should trigger exit/entry
        Assert.Contains("sibling", _stateMachine.GetActiveStateString());
        Assert.Equal(1, _exitCount); // Child exit
    }
    
    [Fact]
    public void TestInternalTransitionWithGuards()
    {
        const string script = @"
        {
            'id': 'guardedInternal',
            'initial': 'counting',
            'context': {
                'counter': 0
            },
            'states': {
                'counting': {
                    'on': {
                        'INCREMENT': [
                            {
                                'target': '.',
                                'cond': 'lessThanFive',
                                'actions': 'incrementCounter'
                            },
                            {
                                'target': 'maxReached'
                            }
                        ]
                    }
                },
                'maxReached': {
                    'entry': 'log',
                    'type': 'final'
                }
            }
        }";
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        _stateMachine!.Start();
        
        // Increment while counter < 5 (internal transitions)
        for (int i = 0; i < 4; i++)
        {
            _stateMachine!.Send("INCREMENT");
            Assert.Contains("counting", _stateMachine.GetActiveStateString());
        }
        
        Assert.Equal(4, _actionCount);
        var counter1 = _stateMachine.ContextMap!["counter"];
        Assert.Equal(4, counter1 is Newtonsoft.Json.Linq.JValue jv1 ? jv1.ToObject<int>() : (int)(counter1 ?? 0));
        
        // One more increment should reach 5 and transition externally
        _stateMachine!.Send("INCREMENT");
        Assert.Equal(5, _actionCount);
        var counter2 = _stateMachine.ContextMap!["counter"];
        Assert.Equal(5, counter2 is Newtonsoft.Json.Linq.JValue jv2 ? jv2.ToObject<int>() : (int)(counter2 ?? 0));
        
        // Next increment should trigger external transition
        _stateMachine!.Send("INCREMENT");
        Assert.Contains("maxReached", _stateMachine.GetActiveStateString());
        Assert.Contains("log:#guardedInternal.maxReached", _actionLog);
    }
    
    [Fact]
    public void TestInternalTransitionInParallelStates()
    {
        const string script = @"
        {
            'id': 'parallelInternal',
            'type': 'parallel',
            'states': {
                'regionA': {
                    'initial': 'stateA',
                    'states': {
                        'stateA': {
                            'entry': 'entryAction',
                            'on': {
                                'UPDATE_A': {
                                    'target': '.',
                                    'actions': 'incrementCounter'
                                }
                            }
                        }
                    }
                },
                'regionB': {
                    'initial': 'stateB',
                    'states': {
                        'stateB': {
                            'entry': 'entryAction',
                            'on': {
                                'UPDATE_B': {
                                    'target': '.',
                                    'actions': 'updateValue'
                                }
                            }
                        }
                    }
                }
            }
        }";
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        _stateMachine.ContextMap!["counter"] = 0;
        _stateMachine!.Start();
        
        var initialEntries = _entryCount;
        
        // Update region A internally
        _stateMachine!.Send("UPDATE_A");
        Assert.Equal(initialEntries, _entryCount); // No re-entry
        var counterValue = _stateMachine.ContextMap!["counter"];
        Assert.Equal(1, counterValue is Newtonsoft.Json.Linq.JValue jv3 ? jv3.ToObject<int>() : (int)(counterValue ?? 0));
        
        // Update region B internally
        _stateMachine!.Send("UPDATE_B");
        Assert.Equal(initialEntries, _entryCount); // Still no re-entry
        Assert.Equal("updated", _stateMachine.ContextMap["value"]);
        
        // Both regions should still be in their original states
        var activeStates = _stateMachine!.GetActiveStateString();
        Assert.Contains("regionA.stateA", activeStates);
        Assert.Contains("regionB.stateB", activeStates);
    }
    
    [Fact]
    public void TestMixedInternalAndExternalTransitions()
    {
        const string script = @"
        {
            'id': 'mixedTransitions',
            'initial': 'idle',
            'context': {
                'internalCount': 0,
                'externalCount': 0
            },
            'states': {
                'idle': {
                    'on': {
                        'START': 'active'
                    }
                },
                'active': {
                    'entry': 'entryAction',
                    'exit': 'exitAction',
                    'on': {
                        'INTERNAL': {
                            'target': '.',
                            'actions': 'incrementInternal'
                        },
                        'EXTERNAL': {
                            'target': 'active',
                            'actions': 'incrementExternal'
                        },
                        'DONE': 'complete'
                    }
                },
                'complete': {
                    'type': 'final'
                }
            }
        }";
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        _stateMachine!.Start();
        
        _stateMachine!.Send("START");
        var initialEntries = _entryCount;
        var initialExits = _exitCount;
        
        // Internal transitions
        _stateMachine!.Send("INTERNAL");
        _stateMachine!.Send("INTERNAL");
        
        Assert.Equal(initialEntries, _entryCount);
        Assert.Equal(initialExits, _exitCount);
        var internalCount1 = _stateMachine.ContextMap!["internalCount"];
        var externalCount1 = _stateMachine.ContextMap!["externalCount"];
        Assert.Equal(2, internalCount1 is Newtonsoft.Json.Linq.JValue jvi1 ? jvi1.ToObject<int>() : (int)(internalCount1 ?? 0));
        Assert.Equal(0, externalCount1 is Newtonsoft.Json.Linq.JValue jve1 ? jve1.ToObject<int>() : (int)(externalCount1 ?? 0));
        
        // External self-transitions
        _stateMachine!.Send("EXTERNAL");
        _stateMachine!.Send("EXTERNAL");
        
        Assert.Equal(initialEntries + 2, _entryCount); // Re-entered twice
        Assert.Equal(initialExits + 2, _exitCount); // Exited twice
        var internalCount2 = _stateMachine.ContextMap!["internalCount"];
        var externalCount2 = _stateMachine.ContextMap!["externalCount"];
        Assert.Equal(2, internalCount2 is Newtonsoft.Json.Linq.JValue jvi2 ? jvi2.ToObject<int>() : (int)(internalCount2 ?? 0));
        Assert.Equal(2, externalCount2 is Newtonsoft.Json.Linq.JValue jve2 ? jve2.ToObject<int>() : (int)(externalCount2 ?? 0));
        
        _stateMachine!.Send("DONE");
        Assert.Contains("complete", _stateMachine.GetActiveStateString());
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}

