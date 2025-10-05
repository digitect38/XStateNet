using Xunit;

// Suppress obsolete warning - SendAsync completion tests with no inter-machine communication
#pragma warning disable CS0618

namespace XStateNet.Tests
{
    public class SendAsyncCompletionTests
    {
        [Fact]
        public async Task SendAsync_WaitsForStateChangeCompletion_SimpleTransition()
        {
            // Arrange
            const string json = @"{
                'id': 'simpleTest',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'START': 'running'
                        }
                    },
                    'running': {
                        'entry': ['logRunningEntry'],
                        'on': {
                            'STOP': 'idle'
                        }
                    }
                }
            }";

            var entryExecuted = false;
            var actions = new ActionMap();
            actions["logRunningEntry"] = new List<NamedAction>
            {
                new NamedAction("logRunningEntry", sm => { Thread.Sleep(50); entryExecuted = true; })
            };

            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, json, false, false, actions, null, null, null, null);
            machine.Start();

            // Act
            var resultState = await machine.SendAsync("START");

            // Assert
            Assert.True(entryExecuted, "Entry action should have completed before SendAsync returns");
            Assert.Contains("running", resultState);
            Assert.Equal(machine.GetActiveStateNames(), resultState);
        }

        [Fact]
        public async Task SendAsync_WaitsForParallelStateCompletion()
        {
            // Arrange
            const string json = @"{
                'id': 'parallelTest',
                'initial': 'parallel',
                'states': {
                    'parallel': {
                        'type': 'parallel',
                        'states': {
                            'region1': {
                                'initial': 'r1_idle',
                                'states': {
                                    'r1_idle': {
                                        'on': {
                                            'GO': 'r1_active'
                                        },
                                        'exit': ['exitR1Idle']
                                    },
                                    'r1_active': {
                                        'entry': ['enterR1Active']
                                    }
                                }
                            },
                            'region2': {
                                'initial': 'r2_idle',
                                'states': {
                                    'r2_idle': {
                                        'on': {
                                            'GO': 'r2_active'
                                        },
                                        'exit': ['exitR2Idle']
                                    },
                                    'r2_active': {
                                        'entry': ['enterR2Active']
                                    }
                                }
                            }
                        }
                    }
                }
            }";

            var actionsExecuted = new List<string>();
            var actions = new ActionMap();
            actions["exitR1Idle"] = new List<NamedAction> { new NamedAction("exitR1Idle", sm => { Thread.Sleep(20); actionsExecuted.Add("exitR1Idle"); }) };
            actions["enterR1Active"] = new List<NamedAction> { new NamedAction("enterR1Active", sm => { Thread.Sleep(20); actionsExecuted.Add("enterR1Active"); }) };
            actions["exitR2Idle"] = new List<NamedAction> { new NamedAction("exitR2Idle", sm => { Thread.Sleep(20); actionsExecuted.Add("exitR2Idle"); }) };
            actions["enterR2Active"] = new List<NamedAction> { new NamedAction("enterR2Active", sm => { Thread.Sleep(20); actionsExecuted.Add("enterR2Active"); }) };

            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, json, false, false, actions, null, null, null, null);
            machine.Start();

            // Act
            var resultState = await machine.SendAsync("GO");

            // Assert
            Assert.Equal(4, actionsExecuted.Count);
            Assert.Contains("exitR1Idle", actionsExecuted);
            Assert.Contains("enterR1Active", actionsExecuted);
            Assert.Contains("exitR2Idle", actionsExecuted);
            Assert.Contains("enterR2Active", actionsExecuted);

            Assert.Contains("r1_active", resultState);
            Assert.Contains("r2_active", resultState);
            Assert.DoesNotContain("r1_idle", resultState);
            Assert.DoesNotContain("r2_idle", resultState);
        }

        [Fact]
        public async Task SendAsync_ReturnsCorrectStateAfterParallelTransitions()
        {
            // Arrange
            const string json = @"{
                'id': 'complexParallel',
                'type': 'parallel',
                'states': {
                    'lights': {
                        'initial': 'red',
                        'states': {
                            'red': {
                                'on': { 'CHANGE': 'green' }
                            },
                            'green': {
                                'on': { 'CHANGE': 'yellow' }
                            },
                            'yellow': {
                                'on': { 'CHANGE': 'red' }
                            }
                        }
                    },
                    'pedestrian': {
                        'initial': 'walk',
                        'states': {
                            'walk': {
                                'on': { 'CHANGE': 'wait' }
                            },
                            'wait': {
                                'on': { 'CHANGE': 'stop' }
                            },
                            'stop': {
                                'on': { 'CHANGE': 'walk' }
                            }
                        }
                    },
                    'counter': {
                        'initial': 'zero',
                        'states': {
                            'zero': {
                                'on': { 'CHANGE': 'one' }
                            },
                            'one': {
                                'on': { 'CHANGE': 'two' }
                            },
                            'two': {
                                'on': { 'CHANGE': 'zero' }
                            }
                        }
                    }
                }
            }";

            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, json, false, false);
            machine.Start();

            // Verify initial state
            var initialState = machine.GetActiveStateNames();
            Assert.Contains("red", initialState);
            Assert.Contains("walk", initialState);
            Assert.Contains("zero", initialState);

            // Act - First transition
            var firstState = await machine.SendAsync("CHANGE");

            // Assert - All regions should have transitioned
            Assert.Contains("green", firstState);
            Assert.Contains("wait", firstState);
            Assert.Contains("one", firstState);
            Assert.DoesNotContain("red", firstState);
            Assert.DoesNotContain("walk", firstState);
            Assert.DoesNotContain("zero", firstState);

            // Act - Second transition
            var secondState = await machine.SendAsync("CHANGE");

            // Assert - All regions should have transitioned again
            Assert.Contains("yellow", secondState);
            Assert.Contains("stop", secondState);
            Assert.Contains("two", secondState);
            Assert.DoesNotContain("green", secondState);
            Assert.DoesNotContain("wait", secondState);
            Assert.DoesNotContain("one", secondState);
        }

        [Fact]
        public async Task SendAsync_ThreadSafe_WaitsForCompletion()
        {
            // Arrange - Use thread-safe state machine
            const string json = @"{
                'id': 'threadSafeTest',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'WORK': 'working'
                        }
                    },
                    'working': {
                        'entry': ['doWork'],
                        'on': {
                            'COMPLETE': 'done'
                        }
                    },
                    'done': {}
                }
            }";

            var workCompleted = false;
            var actions = new ActionMap();
            actions["doWork"] = new List<NamedAction>
            {
                new NamedAction("doWork", sm =>
                {
                    Thread.Sleep(100); // Simulate work
                    workCompleted = true;
                })
            };

            // Create thread-safe state machine
            var machine = StateMachineFactory.CreateFromScript(json, threadSafe: false, false, actions, null, null, null, null);
            machine.Start();

            // Act
            var resultState = await machine.SendAsync("WORK");

            // Assert
            Assert.True(workCompleted, "Work should be completed before SendAsync returns");
            Assert.Contains("working", resultState);
        }

        [Fact]
        public async Task SendAsync_ComparedToSend_BothReturnSameState()
        {
            // Arrange
            const string json = @"{
                'id': 'compareTest',
                'type': 'parallel',
                'states': {
                    'region1': {
                        'initial': 'a',
                        'states': {
                            'a': {
                                'on': { 'NEXT': 'b' }
                            },
                            'b': {}
                        }
                    },
                    'region2': {
                        'initial': 'x',
                        'states': {
                            'x': {
                                'on': { 'NEXT': 'y' }
                            },
                            'y': {}
                        }
                    }
                }
            }";

            // Test with Send
            var machine1 = new StateMachine();
            StateMachineFactory.CreateFromScript(machine1, json);
            machine1.Start();
            var sendResult = machine1.Send("NEXT");

            // Test with SendAsync
            var machine2 = new StateMachine();
            StateMachineFactory.CreateFromScript(machine2, json);
            machine2.Start();
            var sendAsyncResult = await machine2.SendAsync("NEXT");

            // Assert - Both should return the same state
            Assert.Equal(sendResult, sendAsyncResult);
            Assert.Contains("b", sendAsyncResult);
            Assert.Contains("y", sendAsyncResult);
        }

        [Fact]
        public async Task SendAsync_MultipleParallelRegions_AllTransitionsComplete()
        {
            // Arrange - Complex parallel state machine with multiple levels
            const string json = @"{
                'id': 'multiLevel',
                'initial': 'main',
                'states': {
                    'main': {
                        'type': 'parallel',
                        'states': {
                            'system1': {
                                'initial': 'offline',
                                'states': {
                                    'offline': {
                                        'on': { 'ACTIVATE': 'online' },
                                        'exit': ['exitOffline1']
                                    },
                                    'online': {
                                        'entry': ['enterOnline1'],
                                        'type': 'parallel',
                                        'states': {
                                            'monitor': {
                                                'initial': 'watching',
                                                'states': {
                                                    'watching': {},
                                                    'alerting': {}
                                                }
                                            },
                                            'logger': {
                                                'initial': 'logging',
                                                'states': {
                                                    'logging': {},
                                                    'archiving': {}
                                                }
                                            }
                                        }
                                    }
                                }
                            },
                            'system2': {
                                'initial': 'standby',
                                'states': {
                                    'standby': {
                                        'on': { 'ACTIVATE': 'active' },
                                        'exit': ['exitStandby2']
                                    },
                                    'active': {
                                        'entry': ['enterActive2']
                                    }
                                }
                            }
                        }
                    }
                }
            }";

            var actionsExecuted = new List<string>();
            var completionOrder = new List<string>();
            var actions = new ActionMap();
            actions["exitOffline1"] = new List<NamedAction> {
                new NamedAction("exitOffline1", sm => {
                    Thread.Sleep(30);
                    lock(actionsExecuted) actionsExecuted.Add("exitOffline1");
                    lock(completionOrder) completionOrder.Add("exitOffline1");
                })
            };
            actions["enterOnline1"] = new List<NamedAction> {
                new NamedAction("enterOnline1", sm => {
                    Thread.Sleep(20);
                    lock(actionsExecuted) actionsExecuted.Add("enterOnline1");
                    lock(completionOrder) completionOrder.Add("enterOnline1");
                })
            };
            actions["exitStandby2"] = new List<NamedAction> {
                new NamedAction("exitStandby2", sm => {
                    Thread.Sleep(25);
                    lock(actionsExecuted) actionsExecuted.Add("exitStandby2");
                    lock(completionOrder) completionOrder.Add("exitStandby2");
                })
            };
            actions["enterActive2"] = new List<NamedAction> {
                new NamedAction("enterActive2", sm => {
                    Thread.Sleep(15);
                    lock(actionsExecuted) actionsExecuted.Add("enterActive2");
                    lock(completionOrder) completionOrder.Add("enterActive2");
                })
            };

            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, json, false, false, actions, null, null, null, null);
            machine.Start();

            // Act
            var resultState = await machine.SendAsync("ACTIVATE");

            // Assert - All actions should have executed
            Assert.Equal(4, actionsExecuted.Count);
            Assert.Contains("exitOffline1", actionsExecuted);
            Assert.Contains("enterOnline1", actionsExecuted);
            Assert.Contains("exitStandby2", actionsExecuted);
            Assert.Contains("enterActive2", actionsExecuted);

            // Verify final state contains all expected active states
            Assert.Contains("online", resultState);
            Assert.Contains("active", resultState);
            Assert.Contains("watching", resultState);
            Assert.Contains("logging", resultState);
            Assert.DoesNotContain("offline", resultState);
            Assert.DoesNotContain("standby", resultState);
        }

        [Fact]
        public async Task SendAsync_WithGuards_WaitsForCompletionEvenWhenGuardFails()
        {
            // Arrange
            const string json = @"{
                'id': 'guardTest',
                'type': 'parallel',
                'context': { 'canTransition': false },
                'states': {
                    'region1': {
                        'initial': 'state1',
                        'states': {
                            'state1': {
                                'on': {
                                    'TRY': [
                                        {
                                            'target': 'state2',
                                            'guard': 'canTransition'
                                        }
                                    ]
                                }
                            },
                            'state2': {}
                        }
                    },
                    'region2': {
                        'initial': 'stateA',
                        'states': {
                            'stateA': {
                                'on': { 'TRY': 'stateB' }
                            },
                            'stateB': {
                                'entry': ['enteredB']
                            }
                        }
                    }
                }
            }";

            var enteredB = false;
            var actions = new ActionMap();
            actions["enteredB"] = new List<NamedAction>
            {
                new NamedAction("enteredB", sm => { Thread.Sleep(50); enteredB = true; })
            };

            var guards = new GuardMap();
            guards["canTransition"] = new NamedGuard("canTransition",
                sm => sm.ContextMap?["canTransition"]?.ToString() == "true"
            );

            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, json, false, false, actions, guards, null, null, null);
            machine.Start();

            // Act
            var resultState = await machine.SendAsync("TRY");

            // Assert
            // Region1 should not transition (guard fails), Region2 should transition
            Assert.True(enteredB, "Region2 should have completed transition");
            Assert.Contains("state1", resultState); // Region1 stays in state1
            Assert.Contains("stateB", resultState); // Region2 moves to stateB
            Assert.DoesNotContain("state2", resultState);
            Assert.DoesNotContain("stateA", resultState);
        }
    }
}