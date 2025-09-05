using NUnit.Framework;
using XStateNet;
using XStateNet.UnitTest;
using System.Collections.Generic;

namespace MultipleTargetsTests
{
    [TestFixture]
    public class MultipleTargetsTests
    {
        private StateMachine? _stateMachine;
        private ActionMap _actions;
        private GuardMap _guards;
        
        [SetUp]
        public void Setup()
        {
            _actions = new ActionMap
            {
                ["actionA"] = [new("actionA", (sm) => sm.ContextMap["actionExecuted"] = "A")],
                ["actionB"] = [new("actionB", (sm) => sm.ContextMap["actionExecuted"] = "B")],
                ["resetAction"] = [new("resetAction", (sm) => sm.ContextMap["resetExecuted"] = true)]
            };
            
            _guards = new GuardMap();
        }
        
        [Test]
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
                                        'actions': ['actionA']
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
            
            var initialState = _stateMachine.GetActiveStateString();
            Assert.IsTrue(initialState.Contains("region1.state1"));
            Assert.IsTrue(initialState.Contains("region2.stateA"));
            
            _stateMachine.Send("EVENT");
            
            var finalState = _stateMachine.GetActiveStateString();
            Assert.IsTrue(finalState.Contains("region1.state2"));
            Assert.IsTrue(finalState.Contains("region2.stateA")); // Unchanged
            Assert.AreEqual("A", _stateMachine.ContextMap["actionExecuted"]);
        }
        
        [Test]
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
                        'actions': ['resetAction']
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
            _stateMachine.Send("NEXT");
            
            var movedState = _stateMachine.GetActiveStateString();
            Assert.IsTrue(movedState.Contains("region1.state2"));
            Assert.IsTrue(movedState.Contains("region2.stateB"));
            Assert.IsTrue(movedState.Contains("region3.final"));
            
            // Now reset all regions to their specific states using multiple targets
            _stateMachine.Send("RESET_ALL");
            
            var resetState = _stateMachine.GetActiveStateString();
            Assert.IsTrue(resetState.Contains("region1.state1"));
            Assert.IsTrue(resetState.Contains("region2.stateA"));
            Assert.IsTrue(resetState.Contains("region3.initial"));
            Assert.IsTrue((bool)_stateMachine.ContextMap["resetExecuted"]);
        }
        
        [Test]
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
            _stateMachine.Send("START");
            
            var runningState = _stateMachine.GetActiveStateString();
            Assert.IsTrue(runningState.Contains("left.running"));
            Assert.IsTrue(runningState.Contains("right.processing"));
            
            // Trigger emergency stop with multiple targets
            _stateMachine.Send("EMERGENCY");
            
            var emergencyState = _stateMachine.GetActiveStateString();
            Assert.IsTrue(emergencyState.Contains("left.error"));
            Assert.IsTrue(emergencyState.Contains("right.stopped"));
        }
        
        [Test]
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
            _stateMachine.Send("EVENT1");
            
            var state1 = _stateMachine.GetActiveStateString();
            Assert.IsTrue(state1.Contains("region1.b"));
            Assert.IsTrue(state1.Contains("region2.y"));
            
            // Reset to test multiple targets
            _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards).Start();
            
            // Move region2 to y first
            _stateMachine.Send("EVENT1");
            
            // Test multiple target transition from region2
            _stateMachine.Send("EVENT2");
            
            var state2 = _stateMachine.GetActiveStateString();
            Assert.IsTrue(state2.Contains("region1.c"));
            Assert.IsTrue(state2.Contains("region2.x")); // Reset to x
        }
    }
}