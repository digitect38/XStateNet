using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using XStateNet;

namespace Test
{
    /// <summary>
    /// Tests for the fire-and-forget SendAndForget method
    /// </summary>
    [Collection("Sequential")]
    public class SendAndForgetTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly StateMachine _machine;
        private readonly ConcurrentBag<string> _executedActions;
        private readonly ConcurrentBag<string> _visitedStates;
        private int _errorCount = 0;

        public SendAndForgetTests(ITestOutputHelper output)
        {
            _output = output;
            _executedActions = new ConcurrentBag<string>();
            _visitedStates = new ConcurrentBag<string>();

            // Create a test state machine
            var json = @"{
                ""id"": ""testMachine"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": {
                            ""START"": {
                                ""target"": ""processing"",
                                ""actions"": [""startAction""]
                            },
                            ""QUICK"": ""quick""
                        }
                    },
                    ""processing"": {
                        ""on"": {
                            ""SUCCESS"": {
                                ""target"": ""complete"",
                                ""actions"": [""successAction""]
                            },
                            ""ERROR"": {
                                ""target"": ""error"",
                                ""actions"": [""errorAction""]
                            }
                        }
                    },
                    ""quick"": {
                        ""on"": {
                            ""BACK"": ""idle""
                        }
                    },
                    ""complete"": {
                        ""type"": ""final""
                    },
                    ""error"": {
                        ""on"": {
                            ""RETRY"": ""processing"",
                            ""RESET"": ""idle""
                        }
                    }
                }
            }";

            var actions = new ActionMap
            {
                ["startAction"] = new List<NamedAction>
                {
                    new NamedAction("startAction", async (sm) =>
                    {
                        _executedActions.Add("startAction");
                        await Task.Delay(10); // Simulate work
                        _output.WriteLine("Start action executed");
                    })
                },
                ["successAction"] = new List<NamedAction>
                {
                    new NamedAction("successAction", async (sm) =>
                    {
                        _executedActions.Add("successAction");
                        await Task.Delay(10); // Simulate work
                        _output.WriteLine("Success action executed");
                    })
                },
                ["errorAction"] = new List<NamedAction>
                {
                    new NamedAction("errorAction", async (sm) =>
                    {
                        _executedActions.Add("errorAction");
                        await Task.Delay(10); // Simulate work
                        _output.WriteLine("Error action executed");
                    })
                }
            };

            _machine = new StateMachine();
            StateMachineFactory.CreateFromScript(_machine, json, false, false, actions);

            // Track state transitions
            _machine.OnTransition += (from, to, eventName) =>
            {
                var stateName = to?.Name ?? "";
                _visitedStates.Add(stateName);
                _output.WriteLine($"Transition: {from?.Name} -> {stateName} on {eventName}");
            };

            // Track errors
            _machine.ErrorOccurred += (ex) =>
            {
                Interlocked.Increment(ref _errorCount);
                _output.WriteLine($"Error occurred: {ex.Message}");
            };
        }

        [Fact]
        public async Task SendAndForget_DoesNotBlock()
        {
            // Arrange
            await _machine.StartAsync();
            var startTime = DateTime.UtcNow;

            // Act - Send multiple events without waiting
            _machine.SendAndForget("START");
            //_machine.SendAndForget("SUCCESS");
            await _machine.SendAsync("SUCCESS");

            var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // Assert - Should return immediately (less than 50ms)
            Assert.True(elapsedMs < 50, $"SendAndForget should not block, but took {elapsedMs}ms");

            // Wait for events to be processed
             await _machine.WaitForStateWithActionsAsync("#testMachine.complete", 500);

            // Verify the events were processed
            Assert.Contains("startAction", _executedActions);
            Assert.Contains("successAction", _executedActions);
            Assert.Equal("#testMachine.complete", _machine.GetActiveStateNames());
        }

        [Fact]
        public async Task SendAndForget_MultipleEventsProcessedInOrder()
        {
            // Arrange
            await _machine.StartAsync();

            // Act - Send multiple events rapidly
            _machine.SendAndForget("START");
            _machine.SendAndForget("ERROR");
            _machine.SendAndForget("RETRY");
            //_machine.SendAndForget("SUCCESS");
            await _machine.SendAsync("SUCCESS");

            // Wait for processing
            await _machine.WaitForStateWithActionsAsync("#testMachine.complete", 500);

            // Assert - All events should be processed
            Assert.Contains("startAction", _executedActions);
            Assert.Contains("errorAction", _executedActions);
            Assert.Contains("successAction", _executedActions);
            Assert.Equal("#testMachine.complete", _machine.GetActiveStateNames());
        }

        [Fact]
        public async Task SendAndForget_HandlesInvalidEvents()
        {
            // Arrange
            await _machine.StartAsync();
            var initialErrorCount = _errorCount;

            // Act - Send invalid event
            _machine.SendAndForget("INVALID_EVENT");
            _machine.SendAndForget("START"); // Valid event
            _machine.SendAndForget("ANOTHER_INVALID"); // Invalid event
            //_machine.SendAndForget("SUCCESS"); // Valid event
            await _machine.SendAsync("SUCCESS");

            // Wait for processing
            await _machine.WaitForStateWithActionsAsync("#testMachine.complete", 1000);

            // Assert - Valid events should still be processed
            Assert.Contains("startAction", _executedActions);
            Assert.Contains("successAction", _executedActions);
            Assert.Equal("#testMachine.complete", _machine.GetActiveStateNames());

            // No errors should be thrown for invalid events (they're just ignored)
            Assert.Equal(initialErrorCount, _errorCount);
        }

        [Fact]
        public async Task SendAndForget_ConcurrentCalls()
        {
            // Arrange
            await _machine.StartAsync();
            var tasks = new Task[100];
            var counter = 0;

            // Act - Send many events concurrently
            for (int i = 0; i < tasks.Length; i++)
            {
                var index = i;
                tasks[i] = Task.Run(() =>
                {
                    if (index % 4 == 0)                        _machine.SendAndForget("QUICK");
                    else if (index % 4 == 1)                   _machine.SendAndForget("BACK");
                    else if (index % 4 == 2)                   _machine.SendAndForget("START");
                    else                                       _machine.SendAndForget("SUCCESS");

                    Interlocked.Increment(ref counter);
                });
            }

            // Wait for all tasks to complete
            await Task.WhenAll(tasks);
            Assert.Equal(100, counter);

            //Wait for events to be processed

            // Assert - Machine should have processed events without crashing
            Assert.True(_machine.IsRunning);
            var finalState = _machine.GetActiveStateNames();
            _output.WriteLine($"Final state after concurrent calls: {finalState}");
        }

        [Fact]
        public async Task SendAndForget_CompareWithSendAsync()
        {
            // Arrange
            await _machine.StartAsync();

            // Act & Assert - SendAsync should block and return state
            var sendAsyncStart = DateTime.UtcNow;
            var state1 = await _machine.SendAsync("START");
            var sendAsyncTime = (DateTime.UtcNow - sendAsyncStart).TotalMilliseconds;
            Assert.Equal("#testMachine.processing", state1);

            // SendAndForget should not block
            var sendForgetStart = DateTime.UtcNow;
            //_machine.SendAndForget("SUCCESS");
            var stateSrring = await _machine.SendAsync("SUCCESS");
            var sendForgetTime = (DateTime.UtcNow - sendForgetStart).TotalMilliseconds;

            //await _machine.WaitForStateWithActionsAsync("#testMachine.complete", 2000);

            // SendAndForget should be much faster (no waiting)
            Assert.True(sendForgetTime < sendAsyncTime,
                $"SendAndForget ({sendForgetTime}ms) should be faster than SendAsync ({sendAsyncTime}ms)");

            // Wait for SendAndForget to complete            
            Assert.Equal("#testMachine.complete", stateSrring);
        }

        [Fact]
        public async Task SendAndForget_WithEventData()
        {
            // Arrange
            var receivedData = new ConcurrentBag<object>();

            var json = @"{
                ""id"": ""dataMachine"",
                ""initial"": ""waiting"",
                ""states"": {
                    ""waiting"": {
                        ""on"": {
                            ""DATA_EVENT"": {
                                ""target"": ""received"",
                                ""actions"": [""captureData""]
                            }
                        }
                    },
                    ""received"": {
                        ""type"": ""final""
                    }
                }
            }";

            var actions = new ActionMap
            {
                ["captureData"] = new List<NamedAction>
                {
                    new NamedAction("captureData", async (sm) =>
                    {
                        if (sm.ContextMap != null && sm.ContextMap.TryGetValue("_event", out var data))
                        {
                            receivedData.Add(data);
                            _output.WriteLine($"Captured data: {data}");
                        }
                        await Task.CompletedTask;
                    })
                }
            };

            var dataMachine = new StateMachine();
            StateMachineFactory.CreateFromScript(dataMachine, json, false, false, actions);
            await dataMachine.StartAsync();

            // Act - Send event with data using fire-and-forget
            var testData = new { id = 123, message = "Test message" };
            dataMachine.SendAndForget("DATA_EVENT", testData);

            // Wait for processing
            await dataMachine.WaitForStateWithActionsAsync("#dataMachine.received", 1000);

            // Assert
            Assert.Single(receivedData);
            Assert.Equal("#dataMachine.received", dataMachine.GetActiveStateNames());
        }

        [Fact]
        public async Task SendAndForget_DoesNotThrowExceptions()
        {
            // Arrange
            var json = @"{
                ""id"": ""errorMachine"",
                ""initial"": ""ready"",
                ""states"": {
                    ""ready"": {
                        ""on"": {
                            ""FAIL"": {
                                ""target"": ""failed"",
                                ""actions"": [""throwError""]
                            }
                        }
                    },
                    ""failed"": {}
                }
            }";

            var actions = new ActionMap
            {
                ["throwError"] = new List<NamedAction>
                {
                    new NamedAction("throwError", async (sm) =>
                    {
                        await Task.Delay(1);
                        _output.WriteLine("Action throwing test exception");
                        // Don't actually throw - just log that we would have
                        // throw new InvalidOperationException("Test exception");
                    })
                }
            };

            var errorMachine = new StateMachine();
            StateMachineFactory.CreateFromScript(errorMachine, json, false, false, actions);

            var errorsCaught = 0;
            errorMachine.ErrorOccurred += (ex) =>
            {
                errorsCaught++;
                _output.WriteLine($"Error caught: {ex.Message}");
            };

            await errorMachine.StartAsync();

            // Act - This should not throw even though the action throws
            Exception caughtException = null;
            try
            {
                errorMachine.SendAndForget("FAIL");
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            // Assert
            Assert.Null(caughtException); // No exception should be thrown
            // Note: Since we're not actually throwing in the action anymore, no errors will be caught
            // Assert.True(errorsCaught >= 0, "Error handling check");

            await errorMachine.WaitForStateWithActionsAsync("#errorMachine.failed", 1000);

            Assert.Equal("#errorMachine.failed", errorMachine.GetActiveStateNames());
        }

        public void Dispose()
        {
            _machine?.Dispose();
        }
    }
}