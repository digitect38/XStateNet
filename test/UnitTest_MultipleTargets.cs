using Xunit;
using FluentAssertions;
using XStateNet;
using XStateNet.UnitTest;
using System.Collections.Generic;

namespace MultipleTargetsTests
{
    public class MultipleTargetsTests : IDisposable
    {
        private StateMachine? _stateMachine;
        private ActionMap _actions;
        private GuardMap _guards;

        public MultipleTargetsTests()
        {
            _actions = new ActionMap
            {
                ["actionA"] = [new("actionA", (sm) =>
                {
                    if(sm.ContextMap != null) sm.ContextMap["actionExecuted"] = "A";
                })],
                ["actionB"] = [new("actionB", (sm) =>
                {
                    if(sm.ContextMap != null) sm.ContextMap["actionExecuted"] = "B";
                })],
                ["resetAction"] = [new("resetAction", (sm) =>
                {
                    if(sm.ContextMap != null) sm.ContextMap["resetExecuted"] = true;
                })]
            };

            _guards = new GuardMap();
        }

        [Fact]
        public void TestSingleTargetTransition()
        {
            const string script = @"
            {
                'id': 'machine',
                'type': 'parallel',
                'context': {
                    'actionExecuted': '',
                    'resetExecuted': false
                },
                'states': {
                    'region1': {
                        'initial': 'state1',
                        'states': {
                            'state1': {
                                'on': {
                                    'EVENT': {
                                        'target': 'state2',
                                        'actions': 'actionA'
                                    }
                                }
                            },
                            'state2': {}
                        }
                    },
                    'region2': {
                        'initial': 'stateA',
                        'states': {
                            'stateA': {},
                            'stateB': {}
                        }
                    }
                }
            }";

            _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards).Start();

            var initialState = _stateMachine!.GetActiveStateString();
            initialState.Should().Contain("region1.state1");
            initialState.Should().Contain("region2.stateA");

            _stateMachine!.Send("EVENT");

            var finalState = _stateMachine!.GetActiveStateString();
            finalState.Should().Contain("region1.state2");
            finalState.Should().Contain("region2.stateA"); // Unchanged
            _stateMachine.ContextMap!["actionExecuted"].Should().Be("A");
        }

        [Fact]
        public void TestMultipleTargetsTransition()
        {
            const string script = @"
            {
                'id': 'machine',
                'type': 'parallel',
                'context': {
                    'actionExecuted': '',
                    'resetExecuted': false
                },
                'on': {
                    'RESET_ALL': {
                        'target': [
                            '.region1.state1',
                            '.region2.stateA',
                            '.region3.initial'
                        ],
                        'actions': 'resetAction'
                    }
                },
                'states': {
                    'region1': {
                        'initial': 'state1',
                        'states': {
                            'state1': {
                                'on': {
                                    'NEXT': 'state2'
                                }
                            },
                            'state2': {}
                        }
                    },
                    'region2': {
                        'initial': 'stateA',
                        'states': {
                            'stateA': {
                                'on': {
                                    'NEXT': 'stateB'
                                }
                            },
                            'stateB': {}
                        }
                    },
                    'region3': {
                        'initial': 'initial',
                        'states': {
                            'initial': {
                                'on': {
                                    'NEXT': 'final'
                                }
                            },
                            'final': {}
                        }
                    }
                }
            }";

            _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards).Start();

            // Move all regions to their second states
            _stateMachine!.Send("NEXT");

            var movedState = _stateMachine!.GetActiveStateString();
            movedState.Should().Contain("region1.state2");
            movedState.Should().Contain("region2.stateB");
            movedState.Should().Contain("region3.final");

            // Now reset all regions to their specific states using multiple targets
            _stateMachine!.Send("RESET_ALL");

            var resetState = _stateMachine!.GetActiveStateString();
            resetState.Should().Contain("region1.state1");
            resetState.Should().Contain("region2.stateA");
            resetState.Should().Contain("region3.initial");
            ((bool)(_stateMachine.ContextMap!["resetExecuted"] ?? false)).Should().BeTrue();
        }

        [Fact]
        public void TestMultipleTargetsInNestedParallel()
        {
            const string script = @"
            {
                'id': 'machine',
                'initial': 'active',
                'states': {
                    'active': {
                        'type': 'parallel',
                        'on': {
                            'EMERGENCY': {
                                'target': [
                                    '.left.error',
                                    '.right.stopped'
                                ]
                            }
                        },
                        'states': {
                            'left': {
                                'initial': 'idle',
                                'states': {
                                    'idle': {
                                        'on': {
                                            'START': 'running'
                                        }
                                    },
                                    'running': {},
                                    'error': {}
                                }
                            },
                            'right': {
                                'initial': 'waiting',
                                'states': {
                                    'waiting': {
                                        'on': {
                                            'START': 'processing'
                                        }
                                    },
                                    'processing': {},
                                    'stopped': {}
                                }
                            }
                        }
                    }
                }
            }";

            _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards).Start();

            // Start both regions
            _stateMachine!.Send("START");

            var runningState = _stateMachine!.GetActiveStateString();
            runningState.Should().Contain("left.running");
            runningState.Should().Contain("right.processing");

            // Trigger emergency stop with multiple targets
            _stateMachine!.Send("EMERGENCY");

            var emergencyState = _stateMachine!.GetActiveStateString();
            emergencyState.Should().Contain("left.error");
            emergencyState.Should().Contain("right.stopped");
        }

        [Fact]
        public void TestMixedSingleAndMultipleTargets()
        {
            const string script = @"
            {
                'id': 'machine',
                'type': 'parallel',
                'states': {
                    'region1': {
                        'initial': 'a',
                        'states': {
                            'a': {
                                'on': {
                                    'EVENT1': 'b',
                                    'EVENT2': {
                                        'target': 'c'
                                    }
                                }
                            },
                            'b': {},
                            'c': {}
                        }
                    },
                    'region2': {
                        'initial': 'x',
                        'on': {
                            'EVENT2': {
                                'target': [
                                    '.x',
                                    '#machine.region1.c'
                                ]
                            }
                        },
                        'states': {
                            'x': {
                                'on': {
                                    'EVENT1': 'y'
                                }
                            },
                            'y': {},
                            'z': {}
                        }
                    }
                }
            }";

            _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards).Start();

            // Test single target transition
            _stateMachine!.Send("EVENT1");

            var state1 = _stateMachine!.GetActiveStateString();
            state1.Should().Contain("region1.b");
            state1.Should().Contain("region2.y");

            // Reset to test multiple targets
            _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards).Start();

            // Move region2 to y first
            _stateMachine!.Send("EVENT1");

            // Test multiple target transition from region2
            _stateMachine!.Send("EVENT2");

            var state2 = _stateMachine!.GetActiveStateString();
            state2.Should().Contain("region1.c");
            state2.Should().Contain("region2.x"); // Reset to x
        }


        public void Dispose()
        {
            // Cleanup if needed
        }
    }
}



