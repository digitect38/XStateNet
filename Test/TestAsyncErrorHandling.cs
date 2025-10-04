using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Tests;

namespace Test
{
    public class TestAsyncErrorHandling : OrchestratorTestBase
    {
        private readonly ITestOutputHelper _output;
        private IPureStateMachine? _currentMachine;

        public TestAsyncErrorHandling(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task AsyncAction_ThrowsError_TransitionsToErrorState()
        {
            var errorHandled = false;

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["throwError"] = (ctx) =>
                {
                    throw new InvalidOperationException("Test error");
                },
                ["handleError"] = (ctx) =>
                {
                    errorHandled = true;
                }
            };

            string script = @"{
                'id': 'machineId',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'START': 'processing'
                        }
                    },
                    'processing': {
                        'entry': 'throwError',
                        'onError': {
                            'target': 'error',
                            'actions': 'handleError'
                        }
                    },
                    'error': {
                        'type': 'final'
                    }
                }
            }";
                        
            _currentMachine = CreateMachine("machineId", script, actions);
            await _currentMachine.StartAsync();

            var uniqueId = _currentMachine.Id;
            var initialState = _currentMachine.CurrentState;
            Assert.Contains("idle", initialState);

            // Send START event through orchestrator
            await SendEventAsync("TEST", uniqueId, "START");

            // Wait deterministically for error state
            await WaitForStateAsync(_currentMachine, "error");

            // Check if we transitioned to error state
            var finalState = _currentMachine.CurrentState;
            Assert.Contains("error", finalState);
            Assert.True(errorHandled, "Error handler should have been called");
        }

        [Fact]
        public async Task TestAsyncTransitionWithOrchestrator()
        {
            var messageCount = 0;

            var machineJson = @"{
                'id': 'machineId',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'START': 'sending'
                        }
                    },
                    'sending': {
                        'entry': ['sendMessage'],
                        'on': {
                            'SENT': 'idle',
                            'DONE': 'complete'
                        }
                    },
                    'complete': {
                        'type': 'final'
                    }
                }
            }";

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["sendMessage"] = (ctx) =>
                {
                    messageCount++;
                    _output.WriteLine($"Sending message #{messageCount}");

                    if (messageCount < 3)
                    {
                        // Use orchestrator's RequestSelfSend for deadlock-free communication
                        _output.WriteLine($"Requesting SENT event for message #{messageCount}");
                        ctx.RequestSelfSend("SENT");
                        _output.WriteLine($"SENT event queued for message #{messageCount}");
                    }
                    else
                    {
                        // Complete the test
                        _output.WriteLine("Requesting DONE event");
                        ctx.RequestSelfSend("DONE");
                        _output.WriteLine("DONE event queued");
                    }
                }
            };
                        
            _currentMachine = CreateMachine("machineId", machineJson, actions);
            var uniqueId = _currentMachine.Id;
            await _currentMachine.StartAsync();

            // Send START events through orchestrator
            for (int i = 0; i < 3; i++)
            {
                _output.WriteLine($"Sending START event #{i+1}");
                await SendEventAsync("TEST", uniqueId, "START");
            }

            // Wait deterministically for complete state (no timeout needed!)
            await WaitForStateAsync(_currentMachine, "complete", timeoutMs: 5000);

            _output.WriteLine("Test completed successfully");

            // Verify final state
            var finalState = _currentMachine.CurrentState;
            Assert.Contains("complete", finalState);
            Assert.Equal(3, messageCount);
        }
    }
}
