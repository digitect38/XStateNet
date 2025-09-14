using Xunit;

using XStateNet;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DelaysTest
{
    public class SimpleStateMachineTests : XStateNet.Tests.TestBase
    {
        private StateMachine? _stateMachine;
        private List<string> _transitionLog;

        public SimpleStateMachineTests()
        {
            _transitionLog = new List<string>();

            // The script provided in the original question
            var machineId = UniqueMachineId("machine1");
            string script = $@"
            {{
                'id' : '{machineId}',
                'initial': 'a',
                'states': {{
                    'a': {{
                        'after': {{
                            'delay1234' : {{
                                'target': 'b',
                                'actions' : 'transAction'
                            }}
                        }},
                    }},
                    'b':{{}}  
                }}
            }}";

            // Parse the script and create the state machine


            var actionCallbacks = new ActionMap()
            {
                ["transAction"] = new () { new ("transAction", (sm) => _transitionLog.Add("transitionAction executed")) },
            };

            var delayCallbacks = new DelayMap()
            {
                ["delay1234"] = new ("delay1234", (sm) => 1234),
            };


            _stateMachine = StateMachine.CreateFromScript(script, actionCallbacks, null, null, delayCallbacks);

            // Subscribe to the OnTransition event to log transitions
            _stateMachine.OnTransition += LogTransition;

            // Start the state machine
            _stateMachine!.Start();
        }

        
        // Override Dispose to clean up test-specific resources
        public new void Dispose()
        {
            _stateMachine?.Stop();
            _stateMachine?.Dispose();
            _stateMachine = null;
            base.Dispose();
        }

        private void LogTransition(StateNode? fromState, StateNode? toState, string eventName)
        {
            _transitionLog.Add($"Transitioned from {fromState?.Name} to {toState?.Name} on event {eventName}");
        }

        [Fact]
        public async Task StateMachine_Transitions_From_A_To_B_After_Timeout()
        {
            try
            {
                var machineId = UniqueMachineId("machine1");
                
                // Initially, the state machine should be in state 'a'
                var activeState = _stateMachine!.GetActiveStateString();
                Assert.Equal($"#{machineId}.a", _stateMachine.GetActiveStateString());
            
                // Wait for the timeout to trigger the transition
                await Task.Delay(1500);

                // The state machine should now be in state 'b'
                Assert.Equal($"#{machineId}.b", _stateMachine.GetActiveStateString());

                // Check that the transition action was executed
                Assert.Contains("transitionAction executed", _transitionLog);

                // Check the transition log
                Assert.Contains($"Transitioned from #{machineId}.a to #{machineId}.b on event after: 1234", _transitionLog);
            }
            finally
            {
                _stateMachine?.Stop();
                _stateMachine?.Dispose();
            }
        }
    }
}

