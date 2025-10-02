using System;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.Helpers;
using Xunit;

// Suppress obsolete warning - standalone reset event test with no inter-machine communication
#pragma warning disable CS0618

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
    private readonly ConcurrentBag<string> _actionLog;
    private readonly ConcurrentDictionary<string, int> _counters;
    private ActionMap _actions;
    private GuardMap _guards;
    private ServiceMap _services;

    // ���¸ӽſ� DI�� ����
    private readonly ServiceSignals _signals = new();

    // �׼� �� ����
    void OnServiceStarted() => _signals.Started.TrySetResult(true);
    void OnServiceCompleted() => _signals.Completed.TrySetResult(true);
    void OnServiceCancelled() => _signals.Cancelled.TrySetResult(true);


    public UnitTest_ResetEvent()
    {
        _actionLog = new ConcurrentBag<string>();
        _counters = new ConcurrentDictionary<string, int>();

        _actions = new ActionMap
        {
            ["logEntry"] = new List<NamedAction> { new NamedAction(async (sm) => {
                _actionLog.Add($"entry:{sm.GetActiveStateNames()}");
                _counters["entries"] = _counters.GetValueOrDefault("entries", 0) + 1;
            }, "logEntry") },
            ["logExit"] = new List<NamedAction> { new NamedAction(async (sm) => {
                _actionLog.Add($"exit:{sm.GetActiveStateNames()}");
                _counters["exits"] = _counters.GetValueOrDefault("exits", 0) + 1;
            }, "logExit") },
            ["incrementCounter"] = new List<NamedAction> { new NamedAction(async (sm) => {
                var counterValue = sm.ContextMap?["counter"];
                var counter = 0;
                if(counterValue is Newtonsoft.Json.Linq.JValue jv) {
                    counter = Convert.ToInt32(jv.Value);
                } else if (counterValue != null) {
                    counter = Convert.ToInt32(counterValue);
                }
                sm.ContextMap!["counter"] = counter + 1;
                _actionLog.Add($"counter:{counter + 1}");
            }, "incrementCounter") },
            ["logAction"] = new List<NamedAction> { new NamedAction(async (sm) => {
                _actionLog.Add($"action:executed");
            }, "logAction") },
            ["initializeContext"] = new List<NamedAction> { new NamedAction(async (sm) => {
                sm.ContextMap!["initialized"] = true;
                _actionLog.Add("context:initialized");
            }, "initializeContext") },
            ["modifyContext"] = new List<NamedAction> { new NamedAction(async (sm) => {
                sm.ContextMap!["modified"] = true;
                sm.ContextMap!["counter"] = 99;
                _actionLog.Add("context:modified");
            }, "modifyContext") }
        };

        _guards = new GuardMap
        {
            ["isReady"] = new NamedGuard((sm) => {
                return sm.ContextMap?["ready"] as bool? ?? false;
            }, "isReady")
        };

        _services = new ServiceMap
        {
            ["longRunningService"] = new NamedService(async (sm, ct) => {
                _actionLog.Add("service:started");
                await Task.Delay(1000, ct);
                _actionLog.Add("service:completed");
                return "service result";
            }, "longRunningService")
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

        //_stateMachine = StateMachines.CreateFromScript(script, _actions, _guards);
        _stateMachine.Start();

        // Navigate to non-initial state
        await _stateMachine.SendAsync("START");
        await _stateMachine.SendAsync("PAUSE");

        Assert.Contains($"{_stateMachine.machineId}.paused", _stateMachine.GetActiveStateNames());

        var counterValue = _stateMachine.ContextMap?["counter"];
        Assert.Equal(1, counterValue is Newtonsoft.Json.Linq.JValue jv ? jv.ToObject<int>() : Convert.ToInt32(counterValue));

        _actionLog.Clear();
        _counters.Clear();

        // Act - Send RESET event
        await _stateMachine.SendAsync("RESET");

        // Assert - Should be back at initial state
        Assert.Contains($"{_stateMachine.machineId}.idle", _stateMachine.GetActiveStateNames());
        var resetCounterValue = _stateMachine.ContextMap?["counter"];
        Assert.Equal(0, resetCounterValue is Newtonsoft.Json.Linq.JValue jv2 ? jv2.ToObject<int>() : Convert.ToInt32(resetCounterValue)); // Context reset
        Assert.Contains($"entry:{_stateMachine.machineId}.idle", _actionLog); // Initial entry re-executed
    }

    [Fact]
    public async Task Reset_ClearsModifiedContext()
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
        await _stateMachine.SendAsync("NEXT");

        Assert.True(_stateMachine.ContextMap?["modified"] as bool?);
        var counterBeforeReset = _stateMachine.ContextMap?["counter"];
        Assert.Equal(99, counterBeforeReset is Newtonsoft.Json.Linq.JValue jv1 ? jv1.ToObject<int>() : Convert.ToInt32(counterBeforeReset));

        // Act - Reset
        await _stateMachine.SendAsync("RESET");

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
    public async Task Reset_InNestedState_ReturnsToTopLevelInitial()
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
        await _stateMachine.SendAsync("DEEP");
        await _stateMachine.SendAsync("NEXT");

        Assert.Contains($"{_stateMachine.machineId}.parent2.child3", _stateMachine.GetActiveStateNames());

        _actionLog.Clear();

        // Act - Reset
        await _stateMachine.SendAsync("RESET");

        // Assert - Back to initial parent and child
        Assert.Contains($"{_stateMachine.machineId}.parent1.child1", _stateMachine.GetActiveStateNames());
        // When parent1's entry action runs, the active state is already parent1.child1
        Assert.Contains($"entry:{_stateMachine.machineId}.parent1.child1", _actionLog);
        // There should be at least one entry log for the reset
        Assert.NotEmpty(_actionLog.Where(log => log.StartsWith("entry:")));
    }

    [Fact]
    public async Task Reset_InParallelState_ResetsAllRegions()
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
        await _stateMachine.SendAsync("ADVANCE1");
        await _stateMachine.SendAsync("ADVANCE2");
        
        await _stateMachine.WaitForStateAsync("region1.r1s2", 2000);
        await _stateMachine.WaitForStateAsync("region2.r2s2", 2000);

        var activeStates = _stateMachine.GetActiveStateNames();
        Assert.Contains($"{_stateMachine.machineId}.parallel.region1.r1s2", activeStates);
        Assert.Contains($"{_stateMachine.machineId}.parallel.region2.r2s2", activeStates);

        _actionLog.Clear();

        // Act - Reset
        await _stateMachine.SendAsync("RESET");

        // Wait deterministically for both regions to reach their initial states
        await _stateMachine.WaitForStateAsync("region1.r1s1", 500);
        await _stateMachine.WaitForStateAsync("region2.r2s1", 500);

        // Assert - Both regions back to initial
        activeStates = _stateMachine.GetActiveStateNames();
        Assert.Contains($"{_stateMachine.machineId}.parallel.region1.r1s1", activeStates);
        Assert.Contains($"{_stateMachine.machineId}.parallel.region2.r2s1", activeStates);
    }

    [Fact]
    public async Task Reset_ClearsHistoryStates()
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
        await _stateMachine.SendAsync("ENTER_COMPOUND");
        await _stateMachine.SendAsync("NEXT"); // Go to second
        await _stateMachine.SendAsync("NEXT"); // Go to third
        await _stateMachine.SendAsync("EXIT"); // Exit compound

        // Re-enter compound - should go to third due to history
        var state = await _stateMachine.SendAsync("ENTER_COMPOUND");
        Assert.Contains("compound.third", state);
        state = await _stateMachine.SendAsync("RESET");

        // Enter compound again - should go to initial, not history
        state = await _stateMachine.SendAsync("ENTER_COMPOUND");
        Assert.Contains("compound.first", state);
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

#if false

 
        // Act 1: ����
        var stateString = await _stateMachine.SendAsync("START");

        // Assert 1: running ���� �� ���� ��ȣ ���
        Assert.Contains($"{_stateMachine.machineId}.running", stateString);

        var startedTask = _signals.Started.Task;
        var startedWon = await Task.WhenAny(startedTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(startedTask, startedWon); // ���� ��ȣ�� �;� ��

        var sss = stateString;
        // Act 2: �Ϸ� ���� Reset
        stateString = await _stateMachine.SendAsync("RESET");

        // Assert 2: idle ���� �� ��� ��ȣ, 'completed'�� ���� �� ��
        Assert.Contains($"{_stateMachine.machineId}.idle", stateString);

        var completedTask = _signals.Completed.Task;
        var cancelledTask = _signals.Cancelled.Task;

        // '��Ұ� ����'�� �����ϰ�, �Ϸᰡ �ڴʰ� ������ ������ Ȯ��
        var first = await Task.WhenAny(Task.WhenAny(cancelledTask, completedTask), Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.NotEqual(first, completedTask); // completed�� ���� ���� ����
        Assert.Same(cancelledTask, await Task.WhenAny(cancelledTask, Task.Delay(TimeSpan.FromSeconds(2))));

        // ���Ŀ��� completed�� ���� �ʾƾ� ��(�帮��Ʈ ����)
        var notCompleted = await Task.WhenAny(completedTask, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.NotSame(notCompleted, completedTask);
#else
        //_stateMachine.Start();

        // Start service
        var stateString = await _stateMachine.SendAsync("START");

        // Wait for service to start using Stopwatch
        var sw = Stopwatch.StartNew();
        while (!_actionLog.Contains("service:started") && sw.ElapsedMilliseconds < 100)
        {
            await Task.Yield();
        }

        Assert.Contains("service:started", _actionLog);
        Assert.Contains($"{_stateMachine.machineId}.running", stateString);

        // Act - Reset before service completes
        stateString = await _stateMachine.SendAsync("RESET");

        // Assert - Service should be cancelled, not completed
        Assert.DoesNotContain("service:completed", _actionLog);
        Assert.Contains($"{_stateMachine.machineId}.idle", stateString);

        // Verify service doesn't complete after reset using Stopwatch
        sw.Restart();
        while (sw.ElapsedMilliseconds < 1100)
        {
            if (_actionLog.Contains("service:completed"))
            {
                break;
            }
            await Task.Yield();
        }

        Assert.DoesNotContain("service:completed", _actionLog);
#endif
    }

    [Fact]
    public async Task Reset_ClearsEventQueue()
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
        await _stateMachine.SendAsync("EVENT1");
        await _stateMachine.SendAsync("EVENT2");

        // Send RESET before EVENT2 can process
        await _stateMachine.SendAsync("RESET");

        // Assert - Should be at initial, not state3
        Assert.Contains($"{_stateMachine.machineId}.state1", _stateMachine.GetActiveStateNames());
        Assert.DoesNotContain($"{_stateMachine.machineId}.state3", _stateMachine.GetActiveStateNames());
    }

    [Fact]
    public async Task Reset_CanBeTriggeredMultipleTimes()
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
        await _stateMachine.SendAsync("NEXT");
        await _stateMachine.SendAsync("RESET");
        Assert.Contains($"{_stateMachine.machineId}.initial", _stateMachine.GetActiveStateNames());

        // Second reset
        await _stateMachine.SendAsync("NEXT");
        await _stateMachine.SendAsync("RESET");
        Assert.Contains($"{_stateMachine.machineId}.initial", _stateMachine.GetActiveStateNames());

        // Third reset
        await _stateMachine.SendAsync("RESET");
        Assert.Contains($"{_stateMachine.machineId}.initial", _stateMachine.GetActiveStateNames());

        // Each reset should re-execute initial entry
        Assert.True(_counters["entries"] >= 3);
    }

    [Fact]
    public async Task Reset_FromFinalState_Works()
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
        await _stateMachine.SendAsync("FINISH");
        Assert.Contains($"{_stateMachine.machineId}.done", _stateMachine.GetActiveStateNames());

        // Act - Reset from final state
        await _stateMachine.SendAsync("RESET");

        // Assert - Should restart
        Assert.Contains($"{_stateMachine.machineId}.start", _stateMachine.GetActiveStateNames());
        Assert.DoesNotContain($"{_stateMachine.machineId}.done", _stateMachine.GetActiveStateNames());
    }

    public void Dispose()
    {
        _stateMachine?.Stop();
        _stateMachine = null;
    }
}

// ���δ���/�׽�Ʈ ��: ���� ����������Ŭ ��ȣ
public sealed class ServiceSignals
{
    public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

