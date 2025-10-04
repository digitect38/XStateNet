using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Tests;

namespace Test
{
    public class TestAsyncErrorDebugging : OrchestratorTestBase
    {
        private readonly ITestOutputHelper _output;
        private IPureStateMachine? _currentMachine;

        public TestAsyncErrorDebugging(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Debug_AsyncErrorInEntryAction()
        {
            var errorCaught = false;
            Exception? lastError = null;

            StateMachine? GetUnderlying() => (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["throwError"] = (ctx) =>
                {
                    _output.WriteLine("About to throw error from action");
                    throw new InvalidOperationException("Test error from action");
                },
                ["catchError"] = (ctx) =>
                {
                    _output.WriteLine("Error handler called!");
                    errorCaught = true;
                    var underlying = GetUnderlying();
                    lastError = underlying?.ContextMap?["_error"] as Exception;
                }
            };

            string script = @"{
                'id': 'machineId',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'GO': 'processing'
                        }
                    },
                    'processing': {
                        'entry': 'throwError',
                        'onError': {
                            'target': 'errorState',
                            'actions': 'catchError'
                        }
                    },
                    'errorState': {
                        'type': 'final'
                    }
                }
            }";

            _currentMachine = CreateMachine("machineId", script, actions);

            // Subscribe to state changes for debugging
            var underlying = GetUnderlying();
            if (underlying != null)
            {
                underlying.StateChanged += (newStateString) =>
                {
                    _output.WriteLine($"State changed to: {newStateString}");
                };
            }

            await _currentMachine.StartAsync();
            _output.WriteLine($"Initial state: {_currentMachine.CurrentState}");

            // Send GO event through orchestrator
            _output.WriteLine("Sending GO event...");

            var result = await SendEventAsync("TEST", _currentMachine.Id, "GO");
            _output.WriteLine($"Result after GO: Success={result.Success}, Message={result.ErrorMessage ?? "none"}");

            // Wait deterministically for error state
            await WaitForStateAsync(_currentMachine, "errorState");

            var finalState = _currentMachine.CurrentState;
            _output.WriteLine($"Final state: {finalState}");
            _output.WriteLine($"Error caught: {errorCaught}");
            _output.WriteLine($"Last error: {lastError?.Message ?? "null"}");

            // Check context for error info
            if (underlying?.ContextMap != null)
            {
                _output.WriteLine($"Context _error: {underlying.ContextMap.ContainsKey("_error")}");
                _output.WriteLine($"Context _errorMessage: {underlying.ContextMap.GetValueOrDefault("_errorMessage")}");
            }

            Assert.True(errorCaught, "Error handler should have been called");
            Assert.Contains("errorState", finalState);
        }
    }
}
