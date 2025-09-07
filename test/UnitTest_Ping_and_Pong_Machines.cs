using Xunit;
using FluentAssertions;
using XStateNet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdvancedFeatures
{
    public class InterMachinePingPongStateMachinesTests : XStateNet.Tests.TestBase
    {
        private StateMachine _pingStateMachine;
        private StateMachine _pongStateMachine;

        private ActionMap _pingActions;
        private GuardMap _pingGuards;

        private ActionMap _pongActions;
        private GuardMap _pongGuards;

        private List<string> _transitionLog;

        void Send(StateMachine sm)
        {
            _pingStateMachine.Send("to_a");
        }

        
        public InterMachinePingPongStateMachinesTests()
        {
            _transitionLog = new();


            // Load state machines from JSON
            var pingJson = PingPongStateMachine.PingStateMachineScript;
            var pongJson = PingPongStateMachine.PongStateMachineScript;

            _pingStateMachine = new StateMachine();
            _pongStateMachine = new StateMachine();

            // Ping actions
            _pingActions = new ActionMap()//new ConcurrentDictionary<string, List<NamedAction>>
            {
                ["sendToPongToB"] = new List<NamedAction> { new NamedAction("sendToPongToB", (sm) => _pongStateMachine.Send("to_b")) }
            };
            _pingGuards = new();

            // Pong actions
            _pongActions = new ActionMap //ConcurrentDictionary<string, List<NamedAction>>
            {
                ["sendToPingToA"] = new List<NamedAction> { new NamedAction("sendToPingToA", (sm) => _pingStateMachine.Send("to_a")) }
            };
            _pongGuards = new GuardMap(); // ConcurrentDictionary<string, NamedGuard>();

            StateMachine.CreateFromScript(_pingStateMachine, pingJson, _pingActions, _pingGuards);
            StateMachine.CreateFromScript(_pongStateMachine, pongJson, _pongActions, _pongGuards);

            _pingStateMachine.ActionMap = _pingActions;
            _pingStateMachine.GuardMap = _pingGuards;

            _pongStateMachine.ActionMap = _pongActions;
            _pongStateMachine.GuardMap = _pongGuards;

            // Subscribe to OnTransition events
            _pingStateMachine.OnTransition += LogTransition;
            _pongStateMachine.OnTransition += LogTransition;

            _pingStateMachine.Start();
            _pongStateMachine.Start();
        }

        
        // Dispose handled by TestBase - just remove event handlers
        protected new void Dispose()
        {
            _pingStateMachine.OnTransition -= LogTransition;
            _pongStateMachine.OnTransition -= LogTransition;
            base.Dispose();
        }
        private void LogTransition(StateNode? fromState, StateNode? toState, string eventName)
        {
            _transitionLog.Add($"Transitioned from {fromState?.Name} to {toState?.Name} on event {eventName}");
        }

        [Fact]
        public async Task TestPingPongStateMachines()
        {
            // Initially, both state machines should be in state 'a'
            _pingStateMachine.GetActiveStateString().Should().Be("#ping.a");
            _pongStateMachine.GetActiveStateString().Should().Be("#pong.a");

            // Wait for the ping to send the 'to_b' event to pong
            await Task.Delay(1100);
            _pingStateMachine.GetActiveStateString().Should().Be("#ping.b");
            _pongStateMachine.GetActiveStateString().Should().Be("#pong.b");

            // Wait for the pong to send the 'to_a' event to ping
            await Task.Delay(1100);
            _pingStateMachine.GetActiveStateString().Should().Be("#ping.a");
            _pongStateMachine.GetActiveStateString().Should().Be("#pong.a");

            // Wait for the pong to send the 'to_a' event to ping
            await Task.Delay(1100);
            _pingStateMachine.GetActiveStateString().Should().Be("#ping.b");
            _pongStateMachine.GetActiveStateString().Should().Be("#pong.b");

            // Check transition log
            foreach (var log in _transitionLog)
            {
                StateMachine.Log($"log:{log}");
            }

            _transitionLog.Count.Should().Be(6);


            _transitionLog[0].Should().Be("Transitioned from #ping.a to #ping.b on event after: 1000");
            _transitionLog[1].Should().Be("Transitioned from #pong.a to #pong.b on event to_b");

            _transitionLog[2].Should().Be("Transitioned from #pong.b to #pong.a on event after: 1000");
            _transitionLog[3].Should().Be("Transitioned from #ping.b to #ping.a on event to_a");

            _transitionLog[4].Should().Be("Transitioned from #ping.a to #ping.b on event after: 1000");
            _transitionLog[5].Should().Be("Transitioned from #pong.a to #pong.b on event to_b");
        }


        public static class PingPongStateMachine
        {
            public static string PingStateMachineScript => @"
            {
                'id': 'ping',
                'initial': 'a',
                'states': {
                    'a': {
                        'after': {
                            '1000': {
                                'target': 'b',
                                'actions': ['sendToPongToB']
                            }
                        }
                    },
                    'b': {
                        'on': {
                            'to_a': {
                                'target': 'a'
                            }
                        }
                    }
                }
            }";

            public static string PongStateMachineScript => @"
            {
                'id': 'pong',
                'initial': 'a',
                'states': {
                    'a': {
                        'on': {
                            'to_b': {
                                'target': 'b'
                            }
                        }
                    },
                    'b': {
                        'after': {
                            '1000': {
                                'target': 'a',
                                'actions': ['sendToPingToA']
                            }
                        }
                    }
                }
            }";
        }
    }
}



