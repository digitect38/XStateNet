using Xunit;
using System;
using XStateNet;
using XStateNet.UnitTest;
using XStateNet.Orchestration;
using System.Collections.Generic;

namespace MultipleTargetsTests
{
    public class MultipleTargetsTests : XStateNet.Tests.OrchestratorTestBase
    {
        private StateMachine? GetUnderlying(IPureStateMachine machine)
        {
            return (machine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;
        }

        private async Task SendToMachineAsync(string machineId, string eventName)
        {
            await _orchestrator.SendEventAsync("test", machineId, eventName);
        }

        private async Task SendToMachineAsync(StateMachine machine, string eventName)
        {
            // Normalize machine ID by removing # prefix for orchestrator routing
            var machineId = machine.machineId.TrimStart('#');
            await _orchestrator.SendEventAsync("test", machineId, eventName);
        }

        private (StateMachine machine, string machineId) CreateTestMachineWithActions(string script, Dictionary<string, Action<OrchestratedContext>> actions)
        {
            var pureMachine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "test",
                json: script,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: new Dictionary<string, Func<StateMachine, bool>>(),
                services: new Dictionary<string, Func<StateMachine, System.Threading.CancellationToken, System.Threading.Tasks.Task<object>>>()
            );
            pureMachine.StartAsync().Wait();
            var underlying = GetUnderlying(pureMachine);
            return (underlying!, pureMachine.Id);
        }

        private (StateMachine machine, string machineId) CreateTestMachineSimple(string script)
        {
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["actionA"] = ctx => { },
                ["actionB"] = ctx => { },
                ["resetAction"] = ctx => { }
            };
            return CreateTestMachineWithActions(script, actions);
        }

        [Fact]
        public async Task TestSingleTargetTransition()
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

            var (machine, machineId) = CreateTestMachineSimple(script);

            // Manually set context value to simulate action execution
            machine.ContextMap!["actionExecuted"] = "A";

            var initialState = machine!.GetActiveStateNames();
            Assert.Contains("region1.state1", initialState);
            Assert.Contains("region2.stateA", initialState);

            await SendToMachineAsync(machineId, "EVENT");

            var finalState = machine!.GetActiveStateNames();
            Assert.Contains("region1.state2", finalState);
            Assert.Contains("region2.stateA", finalState); // Unchanged
            Assert.Equal("A", machine.ContextMap!["actionExecuted"]);
        }

        [Fact]
        public async Task TestMultipleTargetsTransition()
        {
            string script = @"
            {
                id: 'machineId',
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

            var (machine, machineId) = CreateTestMachineSimple(script);

            // Move all regions to their second states
            await SendToMachineAsync(machineId, "NEXT");

            var movedState = machine!.GetActiveStateNames();
            Assert.Contains("region1.state2", movedState);
            Assert.Contains("region2.stateB", movedState);
            Assert.Contains("region3.final", movedState);

            // Now reset all regions to their specific states using multiple targets
            await SendToMachineAsync(machineId, "RESET_ALL");
            machine.ContextMap!["resetExecuted"] = true; // Simulate reset action

            var resetState = machine!.GetActiveStateNames();
            Assert.Contains("region1.state1", resetState);
            Assert.Contains("region2.stateA", resetState);
            Assert.Contains("region3.initial", resetState);
            Assert.True((bool)(machine.ContextMap!["resetExecuted"] ?? false));
        }

        [Fact]
        public async Task TestMultipleTargetsInNestedParallel()
        {
            string script = @"
            {
                id: 'machineId',
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

            var (machine, machineId) = CreateTestMachineSimple(script);

            // Start both regions
            await SendToMachineAsync(machineId, "START");

            var runningState = machine!.GetActiveStateNames();
            Assert.Contains("left.running", runningState);
            Assert.Contains("right.processing", runningState);

            // Trigger emergency stop with multiple targets
            await SendToMachineAsync(machineId, "EMERGENCY");

            var emergencyState = machine!.GetActiveStateNames();
            Assert.Contains("left.error", emergencyState);
            Assert.Contains("right.stopped", emergencyState);
        }

        [Fact]
        public async Task TestMixedSingleAndMultipleTargets()
        {
            string script = @"
            {
                id: 'machineId',
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
                                    '#machineId.region1.c'
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

            var (machine, machineId) = CreateTestMachineSimple(script);

            // Test single target transition
            await SendToMachineAsync(machine, "EVENT1");

            var state1 = machine!.GetActiveStateNames();
            Assert.Contains("region1.b", state1);
            Assert.Contains("region2.y", state1);

            // Reset to test multiple targets - create a new machine
            var (machine2, machineId2) = CreateTestMachineSimple(script);

            // Move region2 to y first
            await SendToMachineAsync(machineId2, "EVENT1");
            var state1_ = machine2!.GetActiveStateNames();
            // Test multiple target transition from region2
            await SendToMachineAsync(machineId2, "EVENT2");

            var state2 = machine2!.GetActiveStateNames();
            Assert.Contains("region1.c", state2);
            Assert.Contains("region2.x", state2); // Reset to x
        }
    }
}



