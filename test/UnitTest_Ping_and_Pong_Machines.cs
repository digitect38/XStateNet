using Xunit;

using XStateNet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdvancedFeatures
{
    public class InterMachinePingPongStateMachinesTests : XStateNet.Tests.TestBase, IDisposable
    {
        private StateMachine _pingStateMachine;
        private StateMachine _pongStateMachine;

        private ActionMap _pingActions;
        private GuardMap _pingGuards;

        private ActionMap _pongActions;
        private GuardMap _pongGuards;

        private List<string> _transitionLog;
        
        // Store unique IDs for this test instance
        private string _pingId;
        private string _pongId;

        void Send(StateMachine sm)
        {
            _pingStateMachine.Send("to_a");
        }

        
        public InterMachinePingPongStateMachinesTests()
        {
            // Constructor should be minimal - actual setup happens in SetupTest()
        }

        
        /// <summary>
        /// Setup method to be called at the beginning of each test
        /// </summary>
        private void SetupTest()
        {
            // Initialize fresh state for this test
            _transitionLog = new List<string>();

            // Create unique IDs for this test instance
            _pingId = UniqueMachineId("ping");
            _pongId = UniqueMachineId("pong");

            // Load state machines from JSON with unique IDs
            var pingJson = PingPongStateMachine.GetPingStateMachineScript(_pingId);
            var pongJson = PingPongStateMachine.GetPongStateMachineScript(_pongId);

            _pingStateMachine = new StateMachine();
            _pongStateMachine = new StateMachine();

            // Ping actions
            _pingActions = new ActionMap()
            {
                ["sendToPongToB"] = new List<NamedAction> { new NamedAction("sendToPongToB", (sm) => _pongStateMachine.Send("to_b")) }
            };
            _pingGuards = new GuardMap();

            // Pong actions
            _pongActions = new ActionMap()
            {
                ["sendToPingToA"] = new List<NamedAction> { new NamedAction("sendToPingToA", (sm) => _pingStateMachine.Send("to_a")) }
            };
            _pongGuards = new GuardMap();

            StateMachine.CreateFromScript(_pingStateMachine, pingJson, _pingActions, _pingGuards);
            StateMachine.CreateFromScript(_pongStateMachine, pongJson, _pongActions, _pongGuards);

            _pingStateMachine.ActionMap = _pingActions;
            _pingStateMachine.GuardMap = _pingGuards;

            _pongStateMachine.ActionMap = _pongActions;
            _pongStateMachine.GuardMap = _pongGuards;

            // Subscribe to OnTransition events
            _pingStateMachine.OnTransition += LogTransition;
            _pongStateMachine.OnTransition += LogTransition;

            // Start the state machines
            _pingStateMachine.Start();
            _pongStateMachine.Start();
        }
        
        /// <summary>
        /// Cleanup method to be called at the end of each test
        /// </summary>
        private void CleanupTest()
        {
            // Unsubscribe from events
            if (_pingStateMachine != null)
            {
                _pingStateMachine.OnTransition -= LogTransition;
                _pingStateMachine.Stop();
                _pingStateMachine.Dispose();
                _pingStateMachine = null;
            }
            
            if (_pongStateMachine != null)
            {
                _pongStateMachine.OnTransition -= LogTransition;
                _pongStateMachine.Stop();
                _pongStateMachine.Dispose();
                _pongStateMachine = null;
            }
            
            // Clear any remaining references
            _transitionLog?.Clear();
            _transitionLog = null;
            _pingActions = null;
            _pongActions = null;
            _pingGuards = null;
            _pongGuards = null;
            _pingId = null;
            _pongId = null;
        }
        
        // IDisposable implementation
        public new void Dispose()
        {
            CleanupTest();
            base.Dispose();
        }
        private void LogTransition(StateNode? fromState, StateNode? toState, string eventName)
        {
            _transitionLog.Add($"Transitioned from {fromState?.Name} to {toState?.Name} on event {eventName}");
        }

        [Fact]
        public async Task TestPingPongStateMachines()
        {
            // Setup test environment
            SetupTest();
            
            try
            {
                // Initially, both state machines should be in state 'a'
            Assert.Equal($"#{_pingId}.a", _pingStateMachine.GetActiveStateString());
            Assert.Equal($"#{_pongId}.a", _pongStateMachine.GetActiveStateString());

            // Wait for the ping to send the 'to_b' event to pong
            await Task.Delay(1100);
            Assert.Equal($"#{_pingId}.b", _pingStateMachine.GetActiveStateString());
            Assert.Equal($"#{_pongId}.b", _pongStateMachine.GetActiveStateString());

            // Wait for the pong to send the 'to_a' event to ping
            await Task.Delay(1100);
            Assert.Equal($"#{_pingId}.a", _pingStateMachine.GetActiveStateString());
            Assert.Equal($"#{_pongId}.a", _pongStateMachine.GetActiveStateString());

            // Wait for the pong to send the 'to_a' event to ping
            await Task.Delay(1100);
            Assert.Equal($"#{_pingId}.b", _pingStateMachine.GetActiveStateString());
            Assert.Equal($"#{_pongId}.b", _pongStateMachine.GetActiveStateString());

            // Check transition log
            foreach (var log in _transitionLog)
            {
                StateMachine.Log($"log:{log}");
            }

            Assert.Equal(6, _transitionLog.Count);


            Assert.Equal($"Transitioned from #{_pingId}.a to #{_pingId}.b on event after: 1000", _transitionLog[0]);
            Assert.Equal($"Transitioned from #{_pongId}.a to #{_pongId}.b on event to_b", _transitionLog[1]);

            Assert.Equal($"Transitioned from #{_pongId}.b to #{_pongId}.a on event after: 1000", _transitionLog[2]);
            Assert.Equal($"Transitioned from #{_pingId}.b to #{_pingId}.a on event to_a", _transitionLog[3]);

                Assert.Equal($"Transitioned from #{_pingId}.a to #{_pingId}.b on event after: 1000", _transitionLog[4]);
                Assert.Equal($"Transitioned from #{_pongId}.a to #{_pongId}.b on event to_b", _transitionLog[5]);
            }
            finally
            {
                // Always cleanup, even if test fails
                CleanupTest();
            }
        }


        public static class PingPongStateMachine
        {
            public static string GetPingStateMachineScript(string id) => $@"
            {{
                'id': '{id}',
                'initial': 'a',
                'states': {{
                    'a': {{
                        'after': {{
                            '1000': {{
                                'target': 'b',
                                'actions': 'sendToPongToB'
                            }}
                        }}
                    }},
                    'b': {{
                        'on': {{
                            'to_a': {{
                                'target': 'a'
                            }}
                        }}
                    }}
                }}
            }}";

            public static string GetPongStateMachineScript(string id) => $@"
            {{
                'id': '{id}',
                'initial': 'a',
                'states': {{
                    'a': {{
                        'on': {{
                            'to_b': {{
                                'target': 'b'
                            }}
                        }}
                    }},
                    'b': {{
                        'after': {{
                            '1000': {{
                                'target': 'a',
                                'actions': 'sendToPingToA'
                            }}
                        }}
                    }}
                }}
            }}";
        }
    }
}



