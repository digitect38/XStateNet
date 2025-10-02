using Xunit;
using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Tests;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdvancedFeatures
{
    public class ComplexStateTests : OrchestratorTestBase
    {
        private IPureStateMachine? _currentMachine;

        StateMachine? GetUnderlying() => (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

        private async Task<IPureStateMachine> CreateStateMachine(string uniqueId)
        {
            var script = GetScript(uniqueId);
            var actions = new Dictionary<string, Action<OrchestratedContext>>();
            var guards = new Dictionary<string, Func<StateMachine, bool>>();

            _currentMachine = CreateMachine(uniqueId, script, actions, guards);
            await _currentMachine.StartAsync();
            return _currentMachine;
        }

        [Fact]
        public async Task GetCurrentSubStatesTest1()
        {
            var uniqueId = $"GetCurrentSubStatesTest1_{Guid.NewGuid():N}";
            var stateMachine = await CreateStateMachine(uniqueId);

            var underlying = GetUnderlying();
            if (underlying != null)
            {
                var currentState = underlying.GetSourceSubStateCollection(null).ToCsvString(underlying, true);
                Assert.Contains("A1a", currentState);
                Assert.Contains("A2", currentState);
            }
        }

        [Fact]
        public async Task GetCurrentSubStatesTest2()
        {
            var uniqueId = $"GetCurrentSubStatesTest2_{Guid.NewGuid():N}";
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", uniqueId, "TO_A1b");
            await Task.Delay(100);

            var underlying = GetUnderlying();
            if (underlying != null)
            {
                var currentState = underlying.GetSourceSubStateCollection(null).ToCsvString(underlying);
                Assert.Contains("A1b", currentState);
                Assert.Contains("A2", currentState);
            }
        }

        [Fact]
        public async Task GetCurrentSubStatesTest3()
        {
            var uniqueId = $"GetCurrentSubStatesTest3_{Guid.NewGuid():N}";
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", uniqueId, "TO_B");
            await Task.Delay(100);

            var underlying = GetUnderlying();
            if (underlying != null)
            {
                var currentState = underlying.GetSourceSubStateCollection(null).ToCsvString(underlying);
                Assert.Contains("B1", currentState);
            }
        }

        private string GetScript(string uniqueId) => @"
            {
                'id': '" + uniqueId + @"',
                'initial': 'A',
                'states': {
                    'A': {
                        'type': 'parallel',
                        'on': {
                            'TO_B': 'B'
                        },
                        'states': {
                            'A1': {
                                'initial': 'A1a',
                                'on': {
                                    'TO_B1': '#{uniqueId}.B.B1'
                                },
                                'states': {
                                    'A1a': {
                                        'on': { 'TO_A1b': 'A1b' }
                                    },
                                    'A1b': {
                                        'on': { 'TO_A1a': 'A1a' }
                                    }
                                }
                            },
                            'A2': {}
                        }
                    },
                    'B': {
                        'on': { 'TO_A': 'A' },
                        'initial': 'B1',
                        'states': {
                            'B1': {
                                'on': { 'TO_B2': 'B2' }
                            },
                            'B2': {
                                'on': { 'TO_B1': 'B1' }
                            }
                        }
                    }
                }
            }";
    }
}
