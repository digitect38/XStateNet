using Xunit;
using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Tests;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DelaysTest
{
    public class SimpleStateMachineTests : OrchestratorTestBase
    {
        private IPureStateMachine? _currentMachine;
        private List<string> _transitionLog = new();

        [Fact]
        public async Task StateMachine_Transitions_From_A_To_B_After_Timeout()
        {
            var uniqueId = $"delayMachine_{Guid.NewGuid():N}";
            string script = @"
            {
                'id' : '" + uniqueId + @"',
                'initial': 'a',
                'states': {
                    'a': {
                        'after': {
                            'delay1234' : {
                                'target': 'b',
                                'actions' : 'transAction'
                            }
                        },
                    },
                    'b':{}
                }
            }";

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["transAction"] = (ctx) => _transitionLog.Add("transitionAction executed")
            };

            var delays = new Dictionary<string, Func<StateMachine, int>>
            {
                ["delay1234"] = (sm) => 1234
            };

            _currentMachine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: uniqueId,
                json: script,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: null,
                services: null,
                delays: delays,
                activities: null
            );

            await _currentMachine.StartAsync();

            // Initially, the state machine should be in state 'a'
            var activeState = _currentMachine.CurrentState;
            Assert.Contains("a", activeState);

            // Wait for the timeout to trigger the transition
            await Task.Delay(1500);

            // The state machine should now be in state 'b'
            activeState = _currentMachine.CurrentState;
            Assert.Contains("b", activeState);
            Assert.Contains("transitionAction executed", _transitionLog);
        }
    }
}

