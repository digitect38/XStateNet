using Xunit;
using FluentAssertions;
using XStateNet;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DelaysTest
{
    public class SimpleStateMachineTests : XStateNet.Tests.TestBase
    {
        private StateMachine _stateMachine;
        private List<string> _transitionLog;

        public SimpleStateMachineTests()
        {
            _transitionLog = new List<string>();

            // The script provided in the original question
            string script = @"
            {
                'id' : 'machine1',
                'initial': 'a',
                'states': {
                    'a': {
                        'after': {
                            'delay1234': {
                                'target': 'b',
                                'actions' : ['transAction']
                            }
                        },
                    },
                    'b':{}  
                }
            }";

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

        
        // Dispose method removed - handled by TestBase

        private void LogTransition(StateNode? fromState, StateNode? toState, string eventName)
        {
            _transitionLog.Add($"Transitioned from {fromState?.Name} to {toState?.Name} on event {eventName}");
        }

        [Fact]
        public async Task StateMachine_Transitions_From_A_To_B_After_Timeout()
        {
            // Initially, the state machine should be in state 'a'
            var activeState = _stateMachine!.GetActiveStateString();
            _stateMachine.GetActiveStateString().Should().Be("#machine1.a");
            
            // Wait for the timeout to trigger the transition
            await Task.Delay(1500);

            // The state machine should now be in state 'b'
            _stateMachine.GetActiveStateString().Should().Be("#machine1.b");

            // Check that the transition action was executed
            _transitionLog.Should().Contain("transitionAction executed");

            // Check the transition log
            _transitionLog.Should().Contain("Transitioned from #machine1.a to #machine1.b on event after: 1234");
        }
    }
}

