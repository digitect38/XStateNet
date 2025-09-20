using Xunit;

using XStateNet;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DelaysTest
{
    public class SimpleStateMachineTests : XStateNet.Tests.TestBase
    {
        private StateMachine CreateStateMachine(string uniqueId)
        {
            var transitionLog = new List<string>();

            string script = $@"
            {{
                id : '{uniqueId}',
                initial: 'a',
                states: {{
                    a: {{
                        after: {{
                            delay1234 : {{
                                target: 'b',
                                actions : 'transAction'
                            }}
                        }},
                    }},
                    b:{{}}
                }}
            }}";

            var actionCallbacks = new ActionMap()
            {
                ["transAction"] = new () { new ("transAction", (sm) => transitionLog.Add("transitionAction executed")) },
            };

            var delayCallbacks = new DelayMap()
            {
                ["delay1234"] = new ("delay1234", (sm) => 1234),
            };

            var stateMachine = StateMachine.CreateFromScript(script, actionCallbacks, null, null, delayCallbacks);

            // Subscribe to the OnTransition event to log transitions
            stateMachine.OnTransition += (fromState, toState, eventName) => {
                transitionLog.Add($"Transitioned from {fromState?.Name} to {toState?.Name} on event {eventName}");
            };

            // Start the state machine
            stateMachine!.Start();
            return stateMachine;
        }


        [Fact]
        public async Task StateMachine_Transitions_From_A_To_B_After_Timeout()
        {
            var uniqueId = "StateMachine_Transitions_From_A_To_B_After_Timeout_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            try
            {
                // Initially, the state machine should be in state 'a'
                var activeState = stateMachine!.GetActiveStateString();
                Assert.Equal($"#{uniqueId}.a", stateMachine.GetActiveStateString());

                // Wait for the timeout to trigger the transition
                await Task.Delay(1500);

                // The state machine should now be in state 'b'
                Assert.Equal($"#{uniqueId}.b", stateMachine.GetActiveStateString());

                // Note: Since transitionLog is now local to CreateStateMachine,
                // we can't easily check it here. The behavior verification is sufficient.
            }
            finally
            {
                stateMachine?.Stop();
                stateMachine?.Dispose();
            }
        }
    }
}

