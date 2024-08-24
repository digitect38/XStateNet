using NUnit.Framework;
using XStateNet;
using XStateNet.UnitTest;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VideoPlayerStateMachineTests
{
    [TestFixture]
    public class VideoPlayerStateMachineTests
    {
        private StateMachine? _stateMachine;
        private bool _serviceInvoked;

        [Test]
        public async Task TestInvokeStartVideoService()
        {
            var stateMachineJson = @"{
                'id': 'What is a state machine?',
                'initial': 'Closed',
                'states': {
                    'Closed': {
                        'on': {
                            'PLAY': {
                                'target': 'Opened'
                            }
                        }
                    },
                    'Opened': {
                        'initial': 'Playing',
                        'invoke': {
                            'src': 'startVideo'
                        },
                        'states': {
                            'Playing': {
                                'on': {
                                    'PAUSE': {
                                        'target': 'Paused'
                                    }
                                }
                            },
                            'Paused': {
                                'on': {
                                    'PLAY': {
                                        'target': 'Playing'
                                    }
                                }
                            }
                        },
                        'on': {
                            'STOP': {
                                'target': '.Stopped'
                            }
                        }
                    }
                }
            }";

            // Define the actions, including the service 'startVideo'
            var actions = new ConcurrentDictionary<string, List<NamedAction>>();
            var guards = new ConcurrentDictionary<string, NamedGuard>();

            // Define services, in this case, 'startVideo' which sets _serviceInvoked to true
            // ["incrementCount"] = [new("incrementCount", (sm) => Increment(sm))],
            var services = new ConcurrentDictionary<string, NamedService>
            {
                ["startVideo"] = new ("startVideo", (sm) =>
                {
                    _serviceInvoked = true;
                    StateMachine.Log("startVideo service invoked");
                    return Task.CompletedTask; // Mock asynchronous service
                })
            };

            _stateMachine = StateMachine.CreateFromScript(stateMachineJson, actions, guards, services).Start();

            // Initially, the service should not have been invoked
            Assert.IsFalse(_serviceInvoked);

            // Trigger the PLAY event to transition from Closed to Opened.Playing
            _stateMachine.Send("PLAY");

            // Assert the current state
            var currentState = _stateMachine.GetActiveStateString();
            Assert.That("#What is a state machine?.Opened.Playing" == currentState);

            // Wait for the asynchronous service to be invoked
            await Task.Delay(100); // Allow time for the async service to execute

            // Check if the startVideo service was invoked
            Assert.That(_serviceInvoked, "The 'startVideo' service should have been invoked.");
        }
    }
}
