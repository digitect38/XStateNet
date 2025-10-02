using Xunit;

using XStateNet;
using XStateNet.UnitTest;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Suppress obsolete warning - standalone video player test with no inter-machine communication
// For tests with inter-machine communication, use OrchestratorTestBase with EventBusOrchestrator
#pragma warning disable CS0618

namespace VideoPlayerStateMachineTests
{
    public class VideoPlayerStateMachineTests : IDisposable
    {
        private StateMachine? _stateMachine;
        private bool _serviceInvoked;

        [Fact]
        public async Task TestInvokeStartVideoService()
        {
            var jsonScript = @"{
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
            var actions = new ActionMap();
            var guards = new GuardMap();

            // Define services, in this case, 'startVideo' which sets _serviceInvoked to true
            // ["incrementCount"] = [new("incrementCount", async (sm) => Increment(sm))],
            var services = new ServiceMap
            {
                ["startVideo"] = new("startVideo", (sm, ct) =>
                {
                    _serviceInvoked = true;
                    StateMachine.Log("startVideo service invoked");
                    return Task.FromResult<object>(null);
                })
            };

            _stateMachine = (StateMachine)StateMachineFactory.CreateFromScript(jsonScript, threadSafe:false, false, actions, guards, services).Start();

            // Initially, the service should not have been invoked
            Assert.False(_serviceInvoked);

            // Trigger the PLAY event to transition from Closed to Opened.Playing
            _stateMachine!.Send("PLAY");

            // Assert the current state
            var currentState = _stateMachine!.GetActiveStateNames();
            Assert.Equal("#What is a state machine?.Opened.Playing", currentState);

            // Wait for the asynchronous service to be invoked
            await Task.Delay(100); // Allow time for the async service to execute

            // Check if the startVideo service was invoked
            Assert.True(_serviceInvoked);
        }


        public void Dispose()
        {
            // Cleanup if needed
        }
    }
}