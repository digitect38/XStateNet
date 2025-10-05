using System.Collections.Concurrent;
using XStateNet;
using Xunit;

// Suppress obsolete warning - standalone activities test with no inter-machine communication
// For tests with inter-machine communication, use OrchestratorTestBase with EventBusOrchestrator
#pragma warning disable CS0618

namespace XStateV5_Test.AdvancedFeatures;

/// <summary>
/// Unit tests for Activities feature in XStateNet
/// Activities are long-running tasks that are active while a state is active
/// They should:
/// - Start when entering a state
/// - Continue running while the state is active
/// - Stop when exiting the state
/// - Support cancellation
/// - Support multiple activities per state
/// </summary>
public class UnitTest_Activities : IDisposable
{
    private StateMachine? _stateMachine;
    private readonly ConcurrentBag<string> _activityLog;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activityTokens;
    private readonly ConcurrentDictionary<string, int> _activityCounters;
    private ActionMap _actions;
    private ActivityMap _activities;
    private GuardMap _guards;

    public UnitTest_Activities()
    {
        _activityLog = new ConcurrentBag<string>();
        _activityTokens = new ConcurrentDictionary<string, CancellationTokenSource>();
        _activityCounters = new ConcurrentDictionary<string, int>();

        // Initialize action map
        _actions = new ActionMap();
        _actions.SetActions("logAction", new List<NamedAction> { new NamedAction("logAction", (sm) => {
            _activityLog.Add("action:logAction");
        }) });

        // Initialize activity map - Activities should return a cleanup function
        _activities = new ActivityMap
        {
            ["monitorActivity"] = new NamedActivity("monitorActivity", (sm, token) =>
            {
                _activityLog.Add("start:monitorActivity");
                var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                _activityTokens.TryAdd("monitorActivity", cts);
                _activityCounters.TryAdd("monitorActivity", 0);

                // Simulate continuous monitoring
                Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        var count = _activityCounters.AddOrUpdate("monitorActivity", 1, (k, v) => v + 1);
                        _activityLog.Add($"tick:monitorActivity:{count}");
                        await Task.Delay(100, cts.Token);
                    }
                }, cts.Token);

                // Return cleanup function
                return () =>
                {
                    _activityLog.Add("stop:monitorActivity");
                    cts.Cancel();
                    cts.Dispose();
                    _activityTokens.TryRemove("monitorActivity", out _);
                };
            }),

            ["pollingActivity"] = new NamedActivity("pollingActivity", (sm, token) =>
            {
                _activityLog.Add("start:pollingActivity");
                var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                _activityTokens.TryAdd("pollingActivity", cts);
                _activityCounters.TryAdd("pollingActivity", 0);

                Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        var count = _activityCounters.AddOrUpdate("pollingActivity", 1, (k, v) => v + 1);
                        _activityLog.Add($"poll:pollingActivity:{count}");
                        await Task.Delay(150, cts.Token);
                    }
                }, cts.Token);

                return () =>
                {
                    _activityLog.Add("stop:pollingActivity");
                    cts.Cancel();
                    cts.Dispose();
                    _activityTokens.TryRemove("pollingActivity", out _);
                };
            }),

            ["loggingActivity"] = new NamedActivity("loggingActivity", (sm, token) =>
            {
                _activityLog.Add("start:loggingActivity");
                var cts = CancellationTokenSource.CreateLinkedTokenSource(token);

                Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        _activityLog.Add("log:loggingActivity:active");
                        await Task.Delay(200, cts.Token);
                    }
                }, cts.Token);

                return () =>
                {
                    _activityLog.Add("stop:loggingActivity");
                    cts.Cancel();
                    cts.Dispose();
                };
            })
        };

        _guards = new GuardMap();
    }

    [Fact]
    public async Task Activity_StartsWhenEnteringState()
    {
        // Arrange
        var script = @"
        {
            id: 'activityMachine',
            initial: 'idle',
            states: {
                idle: {
                    on: {
                        START: 'monitoring'
                    }
                },
                monitoring: {
                    activities: ['monitorActivity'],
                    on: {
                        STOP: 'idle'
                    }
                }
            }
        }";

        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe: false, guidIsolate: true, _actions, _guards, null, null, _activities);
        await _stateMachine.StartAsync();

        // Act
        _stateMachine.Send("START");
        Thread.Sleep(50); // Let activity start

        // Assert
        Assert.Contains("start:monitorActivity", _activityLog);
        Assert.True(_activityTokens.ContainsKey("monitorActivity"));
    }

    [Fact]
    public async Task Activity_StopsWhenExitingState()
    {
        // Arrange
        var script = @"
        {
            id: 'activityMachine',
            initial: 'idle',
            states: {
                idle: {
                    on: {
                        START: 'monitoring'
                    }
                },
                monitoring: {
                    activities: ['monitorActivity'],
                    on: {
                        STOP: 'idle'
                    }
                }
            }
        }";

        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe: false, true, _actions, _guards, null, null, _activities);
        await _stateMachine.StartAsync();

        // Act
        _stateMachine.Send("START");
        Thread.Sleep(250); // Let activity run
        _stateMachine.Send("STOP");
        Thread.Sleep(50); // Let activity stop

        // Assert
        Assert.Contains("start:monitorActivity", _activityLog);
        Assert.Contains("stop:monitorActivity", _activityLog);
        Assert.False(_activityTokens.ContainsKey("monitorActivity"));
    }

    [Fact]
    public async Task Activity_ContinuesRunningWhileStateIsActive()
    {
        // Arrange
        var uniqueId = "activityMachine" + Guid.NewGuid().ToString("N");
        var script = @"
        {
            id: '" + uniqueId + @"',
            initial: 'monitoring',
            states: {
                monitoring: {
                    activities: ['monitorActivity'],
                    on: {
                        STOP: 'idle'
                    }
                },
                idle: {}
            }
        }";

        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe: false, true, _actions, _guards, null, null, _activities);

        // Act
        await _stateMachine.StartAsync();
        Thread.Sleep(350); // Let activity run for multiple ticks

        // Assert
        Assert.Contains("start:monitorActivity", _activityLog);
        Assert.True(_activityCounters.TryGetValue("monitorActivity", out var monitorCount) && monitorCount >= 3,
            $"Expected at least 3 ticks, but got {monitorCount}");

        // Verify continuous execution
        _activityCounters.TryGetValue("monitorActivity", out var activityCount);
        for (int i = 1; i <= activityCount; i++)
        {
            Assert.Contains($"tick:monitorActivity:{i}", _activityLog);
        }
    }

    [Fact]
    public async Task MultipleActivities_CanRunSimultaneously()
    {
        var uniqueId = "multiActivityMachine" + Guid.NewGuid().ToString("N");
        // Arrange
        var script = @"
        {
            id: '" + uniqueId + @"',
            initial: 'active',
            states: {
                active: {
                    activities: ['monitorActivity', 'pollingActivity', 'loggingActivity'],
                    on: {
                        STOP: 'idle'
                    }
                },
                idle: {}
            }
        }";

        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe: false, true, _actions, _guards, null, null, _activities);

        // Act
        await _stateMachine.StartAsync();
        Thread.Sleep(250); // Let activities run

        // Assert
        Assert.Contains("start:monitorActivity", _activityLog);
        Assert.Contains("start:pollingActivity", _activityLog);
        Assert.Contains("start:loggingActivity", _activityLog);

        // All activities should be running
        Assert.True(_activityCounters.TryGetValue("monitorActivity", out var monitorCount2) && monitorCount2 >= 2);
        Assert.True(_activityCounters.TryGetValue("pollingActivity", out var pollingCount) && pollingCount >= 1);
        Assert.Contains("log:loggingActivity:active", _activityLog);
    }

    [Fact]
    public async Task Activities_InNestedStates_WorkCorrectly()
    {
        var uniqueId = "nestedActivityMachine" + Guid.NewGuid().ToString("N");
        // Arrange
        var script = @"
        {
            id: '" + uniqueId + @"',
            initial: 'parent',
            states: {
                parent: {
                    activities: ['monitorActivity'],
                    initial: 'child1',
                    states: {
                        child1: {
                            activities: ['pollingActivity'],
                            on: {
                                NEXT: 'child2'
                            }
                        },
                        child2: {
                            activities: ['loggingActivity']
                        }
                    },
                    on: {
                        EXIT: 'outside'
                    }
                },
                outside: {}
            }
        }";

        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe: false, true, _actions, _guards, null, null, _activities);

        // Act
        await _stateMachine.StartAsync();
        Thread.Sleep(150); // Let activities start

        // Assert - Both parent and child activities should start
        Assert.Contains("start:monitorActivity", _activityLog);
        Assert.Contains("start:pollingActivity", _activityLog);

        // Transition to sibling
        _stateMachine.Send("NEXT");
        Thread.Sleep(150);

        // pollingActivity should stop, loggingActivity should start
        // monitorActivity should continue
        Assert.Contains("stop:pollingActivity", _activityLog);
        Assert.Contains("start:loggingActivity", _activityLog);
        Assert.DoesNotContain("stop:monitorActivity", _activityLog);

        // Exit parent state
        _stateMachine.Send("EXIT");
        Thread.Sleep(50);

        // All activities should stop
        Assert.Contains("stop:monitorActivity", _activityLog);
        Assert.Contains("stop:loggingActivity", _activityLog);
    }

    [Fact]
    public async Task Activities_InParallelStates_RunIndependently()
    {
        // Arrange
        var script = @"
        {
            id: 'parallelMachine',
            initial: 'active',
            states: {
                active: {
                    type: 'parallel',
                    states: {
                        region1: {
                            initial: 'monitoring',
                            states: {
                                monitoring: {
                                    activities: ['monitorActivity']
                                }
                            }
                        },
                        region2: {
                            initial: 'polling',
                            states: {
                                polling: {
                                    activities: ['pollingActivity']
                                }
                            }
                        }
                    },
                    on: {
                        STOP: 'idle'
                    }
                },
                idle: {}
            }
        }";

        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe: false, true, _actions, _guards, null, null, _activities);

        // Act
        await _stateMachine.StartAsync();
        Thread.Sleep(200);

        // Assert - Both parallel activities should be running
        Assert.Contains("start:monitorActivity", _activityLog);
        Assert.Contains("start:pollingActivity", _activityLog);
        Assert.True(_activityCounters.TryGetValue("monitorActivity", out var monitorCount3) && monitorCount3 >= 1);
        Assert.True(_activityCounters.TryGetValue("pollingActivity", out var pollingCount2) && pollingCount2 >= 1);

        // Stop all
        _stateMachine.Send("STOP");
        Assert.Contains("stop:monitorActivity", _activityLog);
        Assert.Contains("stop:pollingActivity", _activityLog);
    }

    [Fact]
    public async Task Activity_WithStateContext_AccessesContext()
    {
        // Arrange
        var contextAwareActivity = new NamedActivity("contextActivity", (sm, token) =>
        {
            _activityLog.Add($"start:contextActivity:value={sm.ContextMap?["value"]}");

            var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var currentValue = sm.ContextMap?["value"];
                    _activityLog.Add($"read:contextActivity:value={currentValue}");
                    await Task.Delay(100, cts.Token);
                }
            }, cts.Token);

            return () =>
            {
                _activityLog.Add($"stop:contextActivity:value={sm.ContextMap?["value"]}");
                cts.Cancel();
                cts.Dispose();
            };
        });

        _activities["contextActivity"] = contextAwareActivity;

        var uniqueId = "contextMachine" + Guid.NewGuid().ToString("N");
        var script = @"
        {
            id: 'contextMachine',
            initial: 'active',
            context: {
                value: 42
            },
            states: {
                active: {
                    activities: ['contextActivity'],
                    on: {
                        UPDATE: {
                            actions: ['updateValue']
                        },
                        STOP: 'idle'
                    }
                },
                idle: {}
            }
        }";

        _actions.SetActions("updateValue", new List<NamedAction> {
            new NamedAction("updateValue", (sm) => {
                if (sm.ContextMap != null)
                    sm.ContextMap["value"] = 100;
            })
        });

        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe: false, true, _actions, _guards, null, null, _activities);

        // Act
        await _stateMachine.StartAsync();
        Thread.Sleep(150);
        _stateMachine.Send("UPDATE");
        Thread.Sleep(150);
        _stateMachine.Send("STOP");

        // Assert
        Assert.Contains("start:contextActivity:value=42", _activityLog);
        Assert.Contains("read:contextActivity:value=42", _activityLog);
        Assert.Contains("read:contextActivity:value=100", _activityLog);
        Assert.Contains("stop:contextActivity:value=100", _activityLog);
    }

    [Fact]
    public async Task Activity_ErrorHandling_DoesNotCrashStateMachine()
    {
        // Arrange
        var errorActivity = new NamedActivity("errorActivity", (sm, token) =>
        {
            _activityLog.Add("start:errorActivity");

            var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task.Run(async () =>
            {
                await Task.Delay(50, cts.Token);
                _activityLog.Add("error:errorActivity:throwing");
                throw new InvalidOperationException("Activity error");
            }, cts.Token);

            return () =>
            {
                _activityLog.Add("stop:errorActivity");
                cts.Cancel();
                cts.Dispose();
            };
        });

        _activities["errorActivity"] = errorActivity;

        var script = @"
        {
            id: 'uniqueId',
            initial: 'active',
            states: {
                active: {
                    activities: ['errorActivity'],
                    on: {
                        STOP: 'idle'
                    }
                },
                idle: {}
            }
        }";

        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe: false, true, _actions, _guards, null, null, _activities);

        // Act
        await _stateMachine.StartAsync();
        Thread.Sleep(150); // Let error occur

        // Machine should still be responsive
        _stateMachine.Send("STOP");
        Thread.Sleep(50);

        // Assert
        Assert.Contains("start:errorActivity", _activityLog);
        Assert.Contains("error:errorActivity:throwing", _activityLog);
        Assert.Contains("stop:errorActivity", _activityLog);

        // Machine should transition successfully despite error
        // Note: GetCurrentState is not a method in XStateNet, we need to check state differently
        // Assert.Equal("idle", _stateMachine.GetCurrentState());
    }

    [Fact]
    public async Task Activity_RestartOnReentry_WorksCorrectly()
    {
        // Arrange
        var script = @"
        {
            id: 'reentryMachine',
            initial: 'idle',
            states: {
                idle: {
                    on: {
                        START: 'active'
                    }
                },
                active: {
                    activities: ['monitorActivity'],
                    on: {
                        STOP: 'idle'
                    }
                }
            }
        }";

        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe: false, true, _actions, _guards, null, null, _activities);
        await _stateMachine.StartAsync();

        // Act - First entry
        _stateMachine.Send("START");
        Thread.Sleep(150);
        _activityCounters.TryGetValue("monitorActivity", out var firstCount);

        // Exit
        _stateMachine.Send("STOP");
        Thread.Sleep(50);

        // Clear counter for second entry
        _activityCounters.AddOrUpdate("monitorActivity", 0, (k, v) => 0);

        // Re-enter
        _stateMachine.Send("START");
        Thread.Sleep(150);
        _activityCounters.TryGetValue("monitorActivity", out var secondCount);

        // Assert
        Assert.True(firstCount >= 1, "Activity should run on first entry");
        Assert.True(secondCount >= 1, "Activity should restart on re-entry");

        // Check proper start/stop sequence
        var startCount = _activityLog.Where(x => x == "start:monitorActivity").Count();
        var stopCount = _activityLog.Where(x => x == "stop:monitorActivity").Count();

        Assert.Equal(2, startCount); // Started twice
        Assert.Equal(1, stopCount);  // Stopped once (still running after second start)
    }

    public void Dispose()
    {
        // Clean up any remaining activities
        foreach (var cts in _activityTokens.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _activityTokens.Clear();

        _stateMachine?.Stop();
        _stateMachine = null;
    }
}

// Use the ActivityMap and NamedActivity from XStateNet namespace