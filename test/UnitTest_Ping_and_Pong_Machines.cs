using NUnit.Framework;
using XStateNet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdvancedFeatures
{
    [TestFixture]
    public class InterMachinePingPongStateMachinesTests
    {
        private StateMachine _pingStateMachine;
        private StateMachine _pongStateMachine;

        private ConcurrentDictionary<string, List<NamedAction>> _pingActions;
        private ConcurrentDictionary<string, NamedGuard> _pingGuards;

        private ConcurrentDictionary<string, List<NamedAction>> _pongActions;
        private ConcurrentDictionary<string, NamedGuard> _pongGuards;

        private List<string> _transitionLog;

        void Send(StateMachine sm)
        {
            _pingStateMachine.Send("to_a");
        }

        [SetUp]
        public void Setup()
        {
            _transitionLog = new();


            // Load state machines from JSON
            var pingJson = PingPongStateMachine.PingStateMachineScript;
            var pongJson = PingPongStateMachine.PongStateMachineScript;

            _pingStateMachine = new StateMachine();
            _pongStateMachine = new StateMachine();

            // Ping actions
            _pingActions = new ConcurrentDictionary<string, List<NamedAction>>
            {
                ["sendToPongToB"] = new List<NamedAction> { new NamedAction("sendToPongToB", (sm) => _pongStateMachine.Send("to_b")) }
            };
            _pingGuards = new();

            // Pong actions
            _pongActions = new ConcurrentDictionary<string, List<NamedAction>>
            {
                ["sendToPingToA"] = new List<NamedAction> { new NamedAction("sendToPingToA", (sm) => _pingStateMachine.Send("to_a")) }
            };
            _pongGuards = new ConcurrentDictionary<string, NamedGuard>();

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

        [TearDown]
        public void TearDown()
        {
            _pingStateMachine.OnTransition -= LogTransition;
            _pongStateMachine.OnTransition -= LogTransition;
        }

        private void LogTransition(State fromState, AbstractState toState, string eventName)
        {
            _transitionLog.Add($"Transitioned from {fromState.Name} to {toState.Name} on event {eventName}");
        }

        [Test]
        public async Task TestPingPongStateMachines()
        {
            // Initially, both state machines should be in state 'a'
            Assert.AreEqual(_pingStateMachine.GetCurrentState(), "#ping.a");
            Assert.AreEqual(_pongStateMachine.GetCurrentState(), "#pong.a");

            // Wait for the ping to send the 'to_b' event to pong
            await Task.Delay(1500);
            Assert.AreEqual(_pingStateMachine.GetCurrentState(), "#ping.b");
            Assert.AreEqual(_pongStateMachine.GetCurrentState(), "#pong.b");

            // Wait for the pong to send the 'to_a' event to ping
            await Task.Delay(1500);
            Assert.AreEqual(_pingStateMachine.GetCurrentState(), "#ping.a");
            Assert.AreEqual(_pongStateMachine.GetCurrentState(), "#pong.a");

            // Check transition log
            foreach (var log in _transitionLog)
            {
                Console.WriteLine(log);
            }

            Assert.AreEqual(4, _transitionLog.Count);
            Assert.AreEqual("Transitioned from #ping.a to #ping.b on event after:1000", _transitionLog[0]);
            Assert.AreEqual("Transitioned from #pong.a to #pong.b on event to_b", _transitionLog[1]);
            Assert.AreEqual("Transitioned from #pong.b to #pong.a on event after:1000", _transitionLog[2]);
            Assert.AreEqual("Transitioned from #ping.b to #ping.a on event to_a", _transitionLog[3]);
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
