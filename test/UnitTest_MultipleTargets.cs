using Xunit;

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
            var uniqueId = "machine_" + Guid.NewGuid().ToString("N");
            string script = @"
            {
                id: '" + uniqueId + @"',
                type: 'parallel',
                context: {
                    'actionExecuted': '',
                    'resetExecuted': false
                },
                states: {
                    'region1': {
                        initial: 'state1',
                        states: {
                            'state1': {
                                on: {
                                    'EVENT': {
                                        target: 'state2',
                                        actions: 'actionA'
                                    }
                                }
                            },
                            'state2': {}
                        }
                    },
                    'region2': {
                        initial: 'stateA',
                        states: {
                            'stateA': {},
                            'stateB': {}
                        }
                    }
                }
            }";

            _stateMachine = (StateMachine)StateMachineFactory.CreateFromScript(script, threadSafe:false, true,_actions, _guards).Start();

            var initialState = _stateMachine!.GetActiveStateNames();
            Assert.Contains("region1.state1", initialState);
            Assert.Contains("region2.stateA", initialState);

            _stateMachine!.Send("EVENT");

            var finalState = _stateMachine!.GetActiveStateNames();
            Assert.Contains("region1.state2", finalState);
            Assert.Contains("region2.stateA", finalState); // Unchanged
            Assert.Equal("A", _stateMachine.ContextMap!["actionExecuted"]);
        }

        [Fact]
        public void TestMultipleTargetsTransition()
        {
            var uniqueId = "machine_" + Guid.NewGuid().ToString("N");
            string script = @"
            {
                id: '" + uniqueId + @"',
                type: 'parallel',
                context: {
                    'actionExecuted': '',
                    'resetExecuted': false
                },
                on: {
                    'RESET_ALL': {
                        target: [
                            '.region1.state1',
                            '.region2.stateA',
                            '.region3.initial'
                        ],
                        actions: 'resetAction'
                    }
                },
                states: {
                    'region1': {
                        initial: 'state1',
                        states: {
                            'state1': {
                                on: {
                                    'NEXT': 'state2'
                                }
                            },
                            'state2': {}
                        }
                    },
                    'region2': {
                        initial: 'stateA',
                        states: {
                            'stateA': {
                                on: {
                                    'NEXT': 'stateB'
                                }
                            },
                            'stateB': {}
                        }
                    },
                    'region3': {
                        initial: 'initial',
                        states: {
                            initial: {
                                on: {
                                    'NEXT': 'final'
                                }
                            },
                            'final': {}
                        }
                    }
                }
            }";

            _stateMachine = (StateMachine)StateMachineFactory.CreateFromScript(script, threadSafe:false, true,_actions, _guards).Start();

            // Move all regions to their second states
            _stateMachine!.Send("NEXT");

            var movedState = _stateMachine!.GetActiveStateNames();
            Assert.Contains("region1.state2", movedState);
            Assert.Contains("region2.stateB", movedState);
            Assert.Contains("region3.final", movedState);

            // Now reset all regions to their specific states using multiple targets
            _stateMachine!.Send("RESET_ALL");

            var resetState = _stateMachine!.GetActiveStateNames();
            Assert.Contains("region1.state1", resetState);
            Assert.Contains("region2.stateA", resetState);
            Assert.Contains("region3.initial", resetState);
            Assert.True((bool)(_stateMachine.ContextMap!["resetExecuted"] ?? false));
        }

        [Fact]
        public void TestMultipleTargetsInNestedParallel()
        {
            var uniqueId = "machine";// + Guid.NewGuid().ToString("..8");
            string script = @"
            {
                id: '" + uniqueId + @"',
                initial: 'active',
                states: {
                    'active': {
                        type: 'parallel',
                        on: {
                            'EMERGENCY': {
                                target: [
                                    '.left.error',
                                    '.right.stopped'
                                ]
                            }
                        },
                        states: {
                            'left': {
                                initial: 'idle',
                                states: {
                                    'idle': {
                                        on: {
                                            'START': 'running'
                                        }
                                    },
                                    'running': {},
                                    'error': {}
                                }
                            },
                            'right': {
                                initial: 'waiting',
                                states: {
                                    'waiting': {
                                        on: {
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

            _stateMachine = (StateMachine)StateMachineFactory.CreateFromScript(script, threadSafe:false, true,_actions, _guards).Start();

            // Start both regions
            _stateMachine!.Send("START");

            var runningState = _stateMachine!.GetActiveStateNames();
            Assert.Contains("left.running", runningState);
            Assert.Contains("right.processing", runningState);

            // Trigger emergency stop with multiple targets
            _stateMachine!.Send("EMERGENCY");

            var emergencyState = _stateMachine!.GetActiveStateNames();
            Assert.Contains("left.error", emergencyState);
            Assert.Contains("right.stopped", emergencyState);
        }

        [Fact]
        public void TestMixedSingleAndMultipleTargets()
        {
            var uniqueId = "machine" + Guid.NewGuid().ToString("N");
            string script = @"
            {
                id: '" + uniqueId + @"',
                type: 'parallel',
                states: {
                    region1: {
                        initial: 'a',
                        states: {
                            a: {
                                on: {
                                    'EVENT1': 'b',
                                    'EVENT2': {
                                        target: 'c'
                                    }
                                }
                            },
                            b: {},
                            c: {}
                        }
                    },
                    region2: {
                        initial: 'x',
                        on: {
                            'EVENT2': {
                                target: [
                                    '.x',
                                    '#" + uniqueId + @".region1.c'
                                ]
                            }
                        },
                        states: {
                            x: {
                                on: {
                                    'EVENT1': 'y'
                                }
                            },
                            y: {},
                            z: {}
                        }
                    }
                }
            }";

            _stateMachine = (StateMachine)StateMachineFactory.CreateFromScript(script, threadSafe: false, false, _actions, _guards).Start();

            // Test single target transition
            _stateMachine!.Send("EVENT1");

            var state1 = _stateMachine!.GetActiveStateNames();
            Assert.Contains("region1.b", state1);
            Assert.Contains("region2.y", state1);

            // Reset to test multiple targets
            _stateMachine = (StateMachine)StateMachineFactory.CreateFromScript(script, threadSafe: false, false, _actions, _guards).Start();

            // Move region2 to y first
            _stateMachine!.Send("EVENT1");
            var state1_ = _stateMachine!.GetActiveStateNames();
            // Test multiple target transition from region2
            _stateMachine!.Send("EVENT2");

            var state2 = _stateMachine!.GetActiveStateNames();
            Assert.Contains("region1.c", state2);
            Assert.Contains("region2.x", state2); // Reset to x
        }


        public void Dispose()
        {
            // Cleanup if needed
        }
    }
}



