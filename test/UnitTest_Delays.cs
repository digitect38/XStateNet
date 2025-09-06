using NUnit.Framework;
using XStateNet;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DelaysTest
{
    [TestFixture]
    public class SimpleStateMachineTests
    {
        private StateMachine _stateMachine;
        private List<string> _transitionLog;

        [SetUp]
        public void Setup()
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

        [TearDown]
        public void TearDown()
        {
            //_stateMachine.OnTransition -= LogTransition;
        }

        private void LogTransition(StateNode? fromState, StateNode? toState, string eventName)
        {
            _transitionLog.Add($"Transitioned from {fromState?.Name} to {toState?.Name} on event {eventName}");
        }

        [Test]
        public async Task StateMachine_Transitions_From_A_To_B_After_Timeout()
        {
            // Initially, the state machine should be in state 'a'
            var activeState = _stateMachine!.GetActiveStateString();
            Assert.That(_stateMachine.GetActiveStateString() == "#machine1.a");
            
            // Wait for the timeout to trigger the transition
            await Task.Delay(1500);

            // The state machine should now be in state 'b'
            Assert.That(_stateMachine.GetActiveStateString() == "#machine1.b");

            // Check that the transition action was executed
            Assert.That(_transitionLog.Contains("transitionAction executed"));

            // Check the transition log
            Assert.That(_transitionLog.Contains("Transitioned from #machine1.a to #machine1.b on event after: 1234"));
        }
    }
}
