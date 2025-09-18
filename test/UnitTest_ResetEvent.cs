using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XStateNet;
using Xunit;

namespace XStateV5_Test.CoreFeatures;

/// <summary>
/// Unit tests for RESET event feature in XStateNet
/// RESET event should:
/// - Stop the current state machine
/// - Clear all state information
/// - Reset context to initial values
/// - Restart from initial state with entry actions
/// - Work with nested and parallel states
/// - Clear history states
/// </summary>
public class UnitTest_ResetEvent : IDisposable
{
    private StateMachine? _stateMachine;
    private readonly List<string> _actionLog;
    private readonly Dictionary<string, int> _counters;
    private ActionMap _actions;
    private GuardMap _guards;
    private ServiceMap _services;

    public UnitTest_ResetEvent()
    {
        _actionLog = new List<string>();
        _counters = new Dictionary<string, int>();

        _actions = new ActionMap
        {
            ["logEntry"] = new List<NamedAction> { new NamedAction("logEntry", (sm) => {
                _actionLog.Add($"entry:{sm.GetActiveStateString()}");
                _counters["entries"] = _counters.GetValueOrDefault("entries", 0) + 1;
            }) },
            ["logExit"] = new List<NamedAction> { new NamedAction("logExit", (sm) => {
                _actionLog.Add($"exit:{sm.GetActiveStateString()}");
                _counters["exits"] = _counters.GetValueOrDefault("exits", 0) + 1;
            }) },
            ["incrementCounter"] = new List<NamedAction> { new NamedAction("incrementCounter", (sm) => {
                var counterValue = sm.ContextMap?["counter"];
                var counter = 0;
                if(counterValue is Newtonsoft.Json.Linq.JValue jv) {
                    counter = Convert.ToInt32(jv.Value);
                } else if (counterValue != null) {
                    counter = Convert.ToInt32(counterValue);
                }
                sm.ContextMap!["counter"] = counter + 1;
                _actionLog.Add($"counter:{counter + 1}");
            }) },
            ["logAction"] = new List<NamedAction> { new NamedAction("logAction", (sm) => {
                _actionLog.Add($"action:executed");
            }) },
            ["initializeContext"] = new List<NamedAction> { new NamedAction("initializeContext", (sm) => {
                sm.ContextMap!["initialized"] = true;
                _actionLog.Add("context:initialized");
            }) },
            ["modifyContext"] = new List<NamedAction> { new NamedAction("modifyContext", (sm) => {
                sm.ContextMap!["modified"] = true;
                sm.ContextMap!["counter"] = 99;
                _actionLog.Add("context:modified");
            }) }
        };

        _guards = new GuardMap
        {
            ["isReady"] = new NamedGuard("isReady", (sm) => {
                return sm.ContextMap?["ready"] as bool? ?? false;
            })
        };

        _services = new ServiceMap
        {
            ["longRunningService"] = new NamedService("longRunningService", async (sm, ct) => {
                _actionLog.Add("service:started");
                await Task.Delay(1000, ct);
                _actionLog.Add("service:completed");
                return "service result";
            })
        };
    }

    [Fact]
    public async Task Reset_ReturnsToInitialState()
    {
        // Arrange
        var script = @"
        {
            'id': 'resetTest',
            'initial': 'idle',
            'context': {
                'counter': 0
            },
            'states': {
                'idle': {
                    'entry': 'logEntry',
                    'on': {
                        'START': 'running'
                    }
                },
                'running': {
                    'entry': 'incrementCounter',
                    'on': {
                        'PAUSE': 'paused'
                    }
                },
                'paused': {
                    'entry': 'logEntry'
                }
            }
        }";

        _stateMachine = new StateMachineBuilder()
            .WithJsonScript(script)
            .WithActionMap(_actions)
            .WithGuardMap(_guards)
            .WithIsolation(StateMachineBuilder.IsolationMode.Test)
            .WithAutoStart(false)
            .Build("resetTest");

        //_stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        _stateMachine.Start();

        // Navigate to non-initial state
        _stateMachine.Send("START");
        _stateMachine.Send("PAUSE");
        await Task.Delay(100);

        Assert.Contains("paused", _stateMachine.GetActiveStateString());

        var counterValue = _stateMachine.ContextMap?["counter"];
        Assert.Equal(1, counterValue is Newtonsoft.Json.Linq.JValue jv ? jv.ToObject<int>() : Convert.ToInt32(counterValue));

        _actionLog.Clear();
        _counters.Clear();

        // Act - Send RESET event
        _stateMachine.Send("RESET");
        await Task.Delay(100);

        // Assert - Should be back at initial state
        Assert.Contains("idle", _stateMachine.GetActiveStateString());
        var resetCounterValue = _stateMachine.ContextMap?["counter"];
        Assert.Equal(0, resetCounterValue is Newtonsoft.Json.Linq.JValue jv2 ? jv2.ToObject<int>() : Convert.ToInt32(resetCounterValue)); // Context reset
        Assert.Contains("entry:#resetTest.idle", _actionLog); // Initial entry re-executed
    }

    [Fact]
    public void Reset_ClearsModifiedContext()
    {
        // create uniqueId for isolation


        // Arrange
        var script = @"
        {
            id: 'contextReset',
            initial: 'state1',
            context: {
                counter: 0,
                flag: false,
                data: null
            },
            states: {
                state1: {
                    entry: 'initializeContext',
                    on: {
                        NEXT: 'state2'
                    }
                },
                state2: {
                    entry: 'modifyContext'
                }
            }
        }";
        
        _stateMachine = new StateMachineBuilder()
            .WithJsonScript(script)
            .WithActionMap(_actions)
            .WithGuardMap(_guards)
            //.WithBaseId("contextReset")
            .WithIsolation(StateMachineBuilder.IsolationMode.Test)
            .WithAutoStart(true)
            .Build("contextReset");
        

        // Modify context
        _stateMachine.Send("NEXT");
        Thread.Sleep(50);

        Assert.True(_stateMachine.ContextMap?["modified"] as bool?);
        var counterBeforeReset = _stateMachine.ContextMap?["counter"];
        Assert.Equal(99, counterBeforeReset is Newtonsoft.Json.Linq.JValue jv1 ? jv1.ToObject<int>() : Convert.ToInt32(counterBeforeReset));

        // Act - Reset
        _stateMachine.Send("RESET");
        Thread.Sleep(50);

        // Assert - Context should be reset to initial values
        var counterAfterReset = _stateMachine.ContextMap?["counter"];
        Assert.Equal(0, counterAfterReset is Newtonsoft.Json.Linq.JValue jv2 ? jv2.ToObject<int>() : Convert.ToInt32(counterAfterReset));

        var flagValue = _stateMachine.ContextMap?["flag"];
        Assert.False(flagValue is Newtonsoft.Json.Linq.JValue jvFlag ? jvFlag.ToObject<bool?>() : flagValue as bool?);

        Assert.Null(_stateMachine.ContextMap?["data"]);

        // Modified property should be removed after reset
        Assert.False(_stateMachine.ContextMap?.ContainsKey("modified"));

        // Initialized should be set by the entry action after reset
        var initializedValue = _stateMachine.ContextMap?.ContainsKey("initialized") == true ? _stateMachine.ContextMap["initialized"] : null;
        Assert.True(initializedValue is Newtonsoft.Json.Linq.JValue jvInit ? jvInit.ToObject<bool?>() : initializedValue as bool?);
    }

    [Fact]
    public void Reset_InNestedState_ReturnsToTopLevelInitial()
    {

        // Arrange
        var script = @"
        {
            id: 'nestedReset',
            initial: 'parent1',
            states: {
                parent1: {
                    initial: 'child1',
                    entry: 'logEntry',
                    states: {
                        child1: {
                            entry: 'logEntry',
                            on: {
                                DEEP: 'child2'
                            }
                        },
                        child2: {
                            entry: 'logEntry'
                        }
                    },
                    on: {
                        NEXT: 'parent2'
                    }
                },
                parent2: {
                    initial: 'child3',
                    states: {
                        child3: {
                            entry: 'logEntry'
                        }
                    }
                }
            }
        }";

        _stateMachine = new StateMachineBuilder()
            .WithJsonScript(script)
            .WithActionMap(_actions)
            .WithGuardMap(_guards)
            .WithIsolation(StateMachineBuilder.IsolationMode.Test)
            .WithAutoStart(true)
            .Build("nestedReset");

        // Navigate deep into hierarchy
        _stateMachine.Send("DEEP");
        _stateMachine.Send("NEXT");
        Thread.Sleep(50);

        Assert.Contains("parent2.child3", _stateMachine.GetActiveStateString());

        _actionLog.Clear();

        // Act - Reset
        _stateMachine.Send("RESET");
        Thread.Sleep(50);

        // Assert - Back to initial parent and child
        Assert.Contains("parent1.child1", _stateMachine.GetActiveStateString());
        // When parent1's entry action runs, the active state is already parent1.child1
        Assert.Contains("entry:#nestedReset.parent1.child1", _actionLog);
        // There should be at least one entry log for the reset
        Assert.NotEmpty(_actionLog.Where(log => log.StartsWith("entry:")));
    }

    [Fact]
    public void Reset_InParallelState_ResetsAllRegions()
    {
        // Arrange
        var script = @"
        {
            id: 'parallelReset',
            initial: 'parallel',
            states: {
                parallel: {
                    type: 'parallel',
                    states: {
                        region1: {
                            initial: 'r1s1',
                            states: {
                                r1s1: {
                                    entry: 'logEntry',
                                    on: {
                                        ADVANCE1: 'r1s2'
                                    }
                                },
                                r1s2: {
                                    entry: 'logEntry'
                                }
                            }
                        },
                        region2: {
                            initial: 'r2s1',
                            states: {
                                r2s1: {
                                    entry: 'logEntry',
                                    on: {
                                        ADVANCE2: 'r2s2'
                                    }
                                },
                                r2s2: {
                                    entry: 'logEntry'
                                }
                            }
                        }
                    }
                }
            }
        }";

        _stateMachine = new StateMachineBuilder()
            .WithJsonScript(script)
            .WithActionMap(_actions)
            .WithGuardMap(_guards)
            .WithIsolation(StateMachineBuilder.IsolationMode.Test)
            .WithAutoStart(true)
            .Build("parallelReset");

        // Advance both regions
        _stateMachine.Send("ADVANCE1");
        _stateMachine.Send("ADVANCE2");
        Thread.Sleep(50);

        var activeStates = _stateMachine.GetActiveStateString();
        Assert.Contains("region1.r1s2", activeStates);
        Assert.Contains("region2.r2s2", activeStates);

        _actionLog.Clear();

        // Act - Reset
        _stateMachine.Send("RESET");
        Thread.Sleep(50);

        // Assert - Both regions back to initial
        activeStates = _stateMachine.GetActiveStateString();
        Assert.Contains("region1.r1s1", activeStates);
        Assert.Contains("region2.r2s1", activeStates);
    }

    [Fact]
    public void Reset_ClearsHistoryStates()
    {
        // Arrange
        var script = @"
        {
            id: 'historyReset',
            initial: 'main',
            states: {
                main: {
                    on: {
                        ENTER_COMPOUND: 'compound'
                    }
                },
                compound: {
                    initial: 'first',
                    history: 'shallow',
                    states: {
                        first: {
                            on: {
                                NEXT: 'second'
                            }
                        },
                        second: {
                            on: {
                                NEXT: 'third'
                            }
                        },
                        third: {}
                    },
                    on: {
                        EXIT: 'main'
                    }
                }
            }
        }";

        _stateMachine = new StateMachineBuilder()
            .WithJsonScript(script)
            .WithActionMap(_actions)
            .WithGuardMap(_guards)
            .WithIsolation(StateMachineBuilder.IsolationMode.Test)
            .WithAutoStart(true)
            .Build("historyReset");

        // Build history
        _stateMachine.Send("ENTER_COMPOUND");
        _stateMachine.Send("NEXT"); // Go to second
        _stateMachine.Send("NEXT"); // Go to third
        _stateMachine.Send("EXIT"); // Exit compound
        Thread.Sleep(50);

        // Re-enter compound - should go to third due to history
        _stateMachine.Send("ENTER_COMPOUND");
        Thread.Sleep(50);
        Assert.Contains("compound.third", _stateMachine.GetActiveStateString());

        // Act - Reset
        _stateMachine.Send("RESET");
        Thread.Sleep(50);

        // Enter compound again - should go to initial, not history
        _stateMachine.Send("ENTER_COMPOUND");
        Thread.Sleep(50);
        Assert.Contains("compound.first", _stateMachine.GetActiveStateString());
    }

    [Fact]
    public async Task Reset_CancelsActiveServices()
    {
        // Arrange
        var script = @"
        {
            id: 'serviceReset',
            initial: 'idle',
            states: {
                idle: {
                    on: {
                        START: 'running'
                    }
                },
                running: {
                    invoke: {
                        src: 'longRunningService',
                        onDone: 'complete',
                        onError: 'error'
                    }
                },
                complete: {},
                error: {}
            }
        }";

        _stateMachine = new StateMachineBuilder()
            .WithJsonScript(script)
            .WithActionMap(_actions)
            .WithGuardMap(_guards)
            .WithServiceMap(_services)
            .WithIsolation(StateMachineBuilder.IsolationMode.Test)
            .WithAutoStart(true)
            .Build("serviceReset");

        //_stateMachine.Start();

        // Start service
        _stateMachine.Send("START");
        await Task.Delay(100); // Let service start

        Assert.Contains("service:started", _actionLog);
        Assert.Contains("running", _stateMachine.GetActiveStateString());

        // Act - Reset before service completes
        _stateMachine.Send("RESET");
        await Task.Delay(200);

        // Assert - Service should be cancelled, not completed
        Assert.DoesNotContain("service:completed", _actionLog);
        Assert.Contains("idle", _stateMachine.GetActiveStateString());

        // Verify service doesn't complete after reset
        await Task.Delay(1000);
        Assert.DoesNotContain("service:completed", _actionLog);
    }

    [Fact]
    public void Reset_ClearsEventQueue()
    {
        // Arrange
        var script = @"
        {
            id: 'queueReset',
            initial: 'state1',
            states: {
                state1: {
                    on: {
                        EVENT1: 'state2'
                    }
                },
                state2: {
                    entry: 'logEntry',
                    on: {
                        EVENT2: 'state3'
                    }
                },
                state3: {
                    entry: 'logEntry'
                }
            }
        }";

        _stateMachine = new StateMachineBuilder()
            .WithJsonScript(script)
            .WithActionMap(_actions)
            .WithGuardMap(_guards)
            .WithIsolation(StateMachineBuilder.IsolationMode.Test)
            .WithAutoStart(false)
            .Build("queueReset");

        _stateMachine.Start();

        // Queue multiple events before processing
        _stateMachine.Send("EVENT1");
        _stateMachine.Send("EVENT2");

        // Send RESET before EVENT2 can process
        _stateMachine.Send("RESET");
        Thread.Sleep(100);

        // Assert - Should be at initial, not state3
        Assert.Contains("state1", _stateMachine.GetActiveStateString());
        Assert.DoesNotContain("state3", _stateMachine.GetActiveStateString());
    }

    [Fact]
    public void Reset_CanBeTriggeredMultipleTimes()
    {
        // Arrange
        var script = @"
        {
            id: 'multiReset',
            initial: 'initial',
            context: {
                counter: 0
            },
            states: {
                initial: {
                    entry: ['incrementCounter', 'logEntry'],
                    on: {
                        NEXT: 'other'
                    }
                },
                other: {}
            }
        }";

        _stateMachine = new StateMachineBuilder()
            .WithJsonScript(script)
            .WithActionMap(_actions)
            .WithGuardMap(_guards)
            .WithIsolation(StateMachineBuilder.IsolationMode.Test)
            .WithAutoStart(false)
            .Build("multiReset");

        _stateMachine.Start();

        // First reset
        _stateMachine.Send("NEXT");
        _stateMachine.Send("RESET");
        Thread.Sleep(50);
        Assert.Contains("initial", _stateMachine.GetActiveStateString());

        // Second reset
        _stateMachine.Send("NEXT");
        _stateMachine.Send("RESET");
        Thread.Sleep(50);
        Assert.Contains("initial", _stateMachine.GetActiveStateString());

        // Third reset
        _stateMachine.Send("RESET");
        Thread.Sleep(50);
        Assert.Contains("initial", _stateMachine.GetActiveStateString());

        // Each reset should re-execute initial entry
        Assert.True(_counters["entries"] >= 3);
    }

    [Fact]
    public void Reset_FromFinalState_Works()
    {
        // Arrange
        var script = @"
        {
            id: 'finalReset',
            initial: 'start',
            states: {
                start: {
                    on: {
                        FINISH: 'done'
                    }
                },
                done: {
                    type: 'final',
                    entry: 'logEntry'
                }
            }
        }";

        _stateMachine = new StateMachineBuilder()
            .WithJsonScript(script)
            .WithActionMap(_actions)
            .WithGuardMap(_guards)
            .WithIsolation(StateMachineBuilder.IsolationMode.Test)
            .WithAutoStart(false)
            .Build("finalReset");

        _stateMachine.Start();

        // Go to final state
        _stateMachine.Send("FINISH");
        Thread.Sleep(50);
        Assert.Contains("done", _stateMachine.GetActiveStateString());

        // Act - Reset from final state
        _stateMachine.Send("RESET");
        Thread.Sleep(50);

        // Assert - Should restart
        Assert.Contains("start", _stateMachine.GetActiveStateString());
        Assert.DoesNotContain("done", _stateMachine.GetActiveStateString());
    }

    public void Dispose()
    {
        _stateMachine?.Stop();
        _stateMachine = null;
    }
}