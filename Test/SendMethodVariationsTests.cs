using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XStateNet;
using Xunit;

namespace XStateNet.Tests
{
    public class SendMethodVariationsTests
    {
        private const string SimpleStateMachineJson = @"{
            'id': 'sendTest',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'START': 'running',
                        'INVALID': 'nonexistent'
                    }
                },
                'running': {
                    'entry': ['logRunning'],
                    'on': {
                        'STOP': 'idle',
                        'PAUSE': 'paused'
                    }
                },
                'paused': {
                    'on': {
                        'RESUME': 'running',
                        'STOP': 'idle'
                    }
                }
            }
        }";

        private const string ParallelStateMachineJson = @"{
            'id': 'parallelTest',
            'type': 'parallel',
            'states': {
                'region1': {
                    'initial': 'a',
                    'states': {
                        'a': {
                            'on': { 'NEXT': 'b' }
                        },
                        'b': {
                            'on': { 'NEXT': 'c' }
                        },
                        'c': {}
                    }
                },
                'region2': {
                    'initial': 'x',
                    'states': {
                        'x': {
                            'on': { 'NEXT': 'y' }
                        },
                        'y': {
                            'on': { 'NEXT': 'z' }
                        },
                        'z': {}
                    }
                }
            }
        }";

        #region Send - Synchronous

        [Fact]
        public void Send_BasicTransition_ReturnsCorrectState()
        {
            // Arrange
            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, SimpleStateMachineJson);
            machine.Start();

            // Act
            var result = machine.Send("START");

            // Assert
            Assert.Contains("running", result);
        }

        [Fact]
        public void Send_InvalidEvent_StaysInCurrentState()
        {
            // Arrange
            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, SimpleStateMachineJson);
            machine.Start();

            // Act
            var result = machine.Send("NONEXISTENT");

            // Assert
            Assert.Contains("idle", result);
        }

        #endregion

        #region SendAsync - Asynchronous with completion

        [Fact]
        public async Task SendAsync_BasicTransition_ReturnsCorrectState()
        {
            // Arrange
            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, SimpleStateMachineJson);
            machine.Start();

            // Act
            var result = await machine.SendAsync("START");

            // Assert
            Assert.Contains("running", result);
        }

        [Fact]
        public async Task SendAsync_ParallelStates_WaitsForAllTransitions()
        {
            // Arrange
            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, ParallelStateMachineJson);
            machine.Start();

            // Act
            var result = await machine.SendAsync("NEXT");

            // Assert
            Assert.Contains("region1.b", result);
            Assert.Contains("region2.y", result);
            Assert.DoesNotContain("region1.a", result);
            Assert.DoesNotContain("region2.x", result);
        }

        [Fact]
        public async Task SendAsync_WithEventData_PassesDataCorrectly()
        {
            // Arrange
            object? capturedData = null;
            var actions = new ActionMap();
            actions["captureData"] = new List<NamedAction>
            {
                new NamedAction("captureData", sm => capturedData = sm.ContextMap["_event"])
            };

            const string json = @"{
                'id': 'dataTest',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'DATA_EVENT': {
                                'target': 'active',
                                'actions': ['captureData']
                            }
                        }
                    },
                    'active': {}
                }
            }";

            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, json, false, false, actions, null, null, null, null);
            machine.Start();

            var testData = new { value = 42, message = "test" };

            // Act
            var result = await machine.SendAsync("DATA_EVENT", testData);

            // Assert
            Assert.Contains("active", result);
            Assert.Equal(testData, capturedData);
        }

        #endregion

        #region SendAndForget - Fire and forget

        [Fact]
        public async Task SendAndForget_DoesNotBlock_ExecutesInBackground()
        {
            // Arrange
            var transitionCompleted = false;
            var actions = new ActionMap();
            actions["markComplete"] = new List<NamedAction>
            {
                new NamedAction("markComplete", sm =>
                {
                    Thread.Sleep(100); // Simulate work
                    transitionCompleted = true;
                })
            };

            const string json = @"{
                'id': 'forgetTest',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'START': {
                                'target': 'running',
                                'actions': ['markComplete']
                            }
                        }
                    },
                    'running': {}
                }
            }";

            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, json, false, false, actions, null, null, null, null);
            machine.Start();

            // Act
            machine.SendAndForget("START");

            // Assert - should return immediately
            Assert.False(transitionCompleted, "Action should not have completed immediately");

            // Wait for background execution
            await Task.Delay(150);
            Assert.True(transitionCompleted, "Action should have completed in background");
            Assert.Contains("running", machine.GetActiveStateNames());
        }

        #endregion

        #region TrySend and TrySendAsync

        [Fact]
        public void TrySend_ValidTransition_ReturnsTrue()
        {
            // Arrange
            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, SimpleStateMachineJson);
            machine.Start();

            // Act
            var success = machine.TrySend("START");

            // Assert
            Assert.True(success);
            Assert.Contains("running", machine.GetActiveStateNames());
        }

        [Fact]
        public void TrySend_MachineNotRunning_ReturnsFalse()
        {
            // Arrange
            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, SimpleStateMachineJson);
            // Don't start the machine

            // Act
            var success = machine.TrySend("START");

            // Assert
            Assert.False(success);
        }

        [Fact]
        public async Task TrySendAsync_ValidTransition_ReturnsTrueAndState()
        {
            // Arrange
            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, SimpleStateMachineJson);
            machine.Start();

            // Act
            var (success, state) = await machine.TrySendAsync("START");

            // Assert
            Assert.True(success);
            Assert.Contains("running", state);
        }

        [Fact]
        public async Task TrySendAsync_MachineNotRunning_ReturnsFalseAndCurrentState()
        {
            // Arrange
            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, SimpleStateMachineJson);
            // Don't start the machine

            // Act
            var (success, state) = await machine.TrySendAsync("START");

            // Assert
            Assert.False(success);
            Assert.NotNull(state);
        }

        #endregion

        #region SendWithTimeout

        [Fact]
        public async Task SendWithTimeoutAsync_CompletesWithinTimeout_ReturnsState()
        {
            // Arrange
            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, SimpleStateMachineJson);
            machine.Start();

            // Act
            var result = await machine.SendWithTimeoutAsync("START", TimeSpan.FromSeconds(1));

            // Assert
            Assert.Contains("running", result);
        }

        [Fact]
        public async Task SendWithTimeoutAsync_ExceedsTimeout_ThrowsOperationCanceledException()
        {
            // Arrange
            var actions = new ActionMap();
            actions["slowAction"] = new List<NamedAction>
            {
                new NamedAction("slowAction", sm => Thread.Sleep(1000)) // Simulate slow action
            };

            const string json = @"{
                'id': 'timeoutTest',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'SLOW': {
                                'target': 'done',
                                'actions': ['slowAction']
                            }
                        }
                    },
                    'done': {}
                }
            }";

            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, json, false, false, actions, null, null, null, null);
            machine.Start();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await machine.SendWithTimeoutAsync("SLOW", TimeSpan.FromMilliseconds(50));
            });
        }

        #endregion

        #region SendWithCancellation

        [Fact]
        public async Task SendWithCancellationAsync_NotCancelled_ReturnsState()
        {
            // Arrange
            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, SimpleStateMachineJson);
            machine.Start();

            using var cts = new CancellationTokenSource();

            // Act
            var result = await machine.SendWithCancellationAsync("START", cts.Token);

            // Assert
            Assert.Contains("running", result);
        }

        [Fact]
        public async Task SendWithCancellationAsync_Cancelled_ThrowsOperationCanceledException()
        {
            // Arrange
            var actions = new ActionMap();
            actions["slowAction"] = new List<NamedAction>
            {
                new NamedAction("slowAction", sm => Thread.Sleep(1000))
            };

            const string json = @"{
                'id': 'cancelTest',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'SLOW': {
                                'target': 'done',
                                'actions': ['slowAction']
                            }
                        }
                    },
                    'done': {}
                }
            }";

            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, json, false, false, actions, null, null, null, null);
            machine.Start();

            using var cts = new CancellationTokenSource();

            // Act
            var sendTask = machine.SendWithCancellationAsync("SLOW", cts.Token);
            await Task.Delay(50);
            cts.Cancel();

            // Assert
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await sendTask);
        }

        #endregion

        #region SendBatch

        [Fact]
        public void SendBatch_MultipleEvents_ProcessesInOrder()
        {
            // Arrange
            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, SimpleStateMachineJson);
            machine.Start();

            // Act
            var results = machine.SendBatch("START", "PAUSE", "RESUME");

            // Assert
            Assert.Equal(3, results.Count);
            Assert.Contains("running", results[0]);
            Assert.Contains("paused", results[1]);
            Assert.Contains("running", results[2]);
        }

        [Fact]
        public async Task SendBatchAsync_MultipleEvents_ProcessesInOrder()
        {
            // Arrange
            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, SimpleStateMachineJson);
            machine.Start();

            // Act
            var results = await machine.SendBatchAsync("START", "PAUSE", "STOP");

            // Assert
            Assert.Equal(3, results.Count);
            Assert.Contains("running", results[0]);
            Assert.Contains("paused", results[1]);
            Assert.Contains("idle", results[2]);
        }

        [Fact]
        public async Task SendBatchAsync_WithEventData_ProcessesCorrectly()
        {
            // Arrange
            var capturedValues = new List<int>();
            var actions = new ActionMap();
            actions["captureValue"] = new List<NamedAction>
            {
                new NamedAction("captureValue", sm =>
                {
                    if (sm.ContextMap["_event"] is int value)
                        capturedValues.Add(value);
                })
            };

            const string json = @"{
                'id': 'batchDataTest',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'ADD': {
                                'actions': ['captureValue']
                            }
                        }
                    }
                }
            }";

            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, json, false, false, actions, null, null, null, null);
            machine.Start();

            // Act
            var results = await machine.SendBatchAsync(
                ("ADD", 1),
                ("ADD", 2),
                ("ADD", 3)
            );

            // Assert
            Assert.Equal(3, results.Count);
            Assert.Equal(new[] { 1, 2, 3 }, capturedValues);
        }

        #endregion

        #region SendWithCallback

        [Fact]
        public async Task SendWithCallback_InvokesCallbackWithResult()
        {
            // Arrange
            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, SimpleStateMachineJson);
            machine.Start();

            string? callbackResult = null;
            var callbackCompleted = new TaskCompletionSource<bool>();

            // Act
            machine.SendWithCallback("START", result =>
            {
                callbackResult = result;
                callbackCompleted.SetResult(true);
            });

            // Assert
            await callbackCompleted.Task;
            Assert.Contains("running", callbackResult);
        }

        [Fact]
        public async Task SendWithCallbackAsync_InvokesAsyncCallback()
        {
            // Arrange
            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, SimpleStateMachineJson);
            machine.Start();

            string? callbackResult = null;

            // Act
            await machine.SendWithCallbackAsync("START", async result =>
            {
                await Task.Delay(10); // Simulate async work
                callbackResult = result;
            });

            // Assert
            Assert.Contains("running", callbackResult);
        }

        #endregion

        #region Thread-Safe State Machine Tests

        [Fact]
        public async Task SendAsync_ThreadSafe_HandlesEventQueueCorrectly()
        {
            // Arrange
            var machine = StateMachineFactory.CreateFromScript(SimpleStateMachineJson, threadSafe: true);
            machine.Start();

            // Act
            var result = await machine.SendAsync("START");

            // Assert
            Assert.Contains("running", result);
        }

        [Fact]
        public async Task SendAndForget_ThreadSafe_WorksWithEventQueue()
        {
            // Arrange
            var transitionCompleted = false;
            var actions = new ActionMap();
            actions["markComplete"] = new List<NamedAction>
            {
                new NamedAction("markComplete", sm => transitionCompleted = true)
            };

            const string json = @"{
                'id': 'threadSafeTest',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'START': {
                                'target': 'running',
                                'actions': ['markComplete']
                            }
                        }
                    },
                    'running': {}
                }
            }";

            var machine = StateMachineFactory.CreateFromScript(json, threadSafe: false, false, actions, null, null, null, null);
            machine.Start();

            // Act
            machine.SendAndForget("START");

            // Assert - wait for event queue to process
            await Task.Delay(100);
            Assert.True(transitionCompleted);
            Assert.Contains("running", machine.GetActiveStateNames());
        }

        #endregion

        #region Parallel Execution Tests

        [Fact]
        public async Task SendAsync_ConcurrentCalls_HandledSafely()
        {
            // Arrange
            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, ParallelStateMachineJson);
            machine.Start();

            // Act - Send multiple events concurrently
            var tasks = new[]
            {
                Task.Run(() => machine.SendAsync("NEXT")),
                Task.Run(() => machine.SendAsync("NEXT")),
                Task.Run(() => machine.SendAsync("NEXT"))
            };

            var results = await Task.WhenAll(tasks);

            // Assert - Machine should have processed events sequentially
            var finalState = machine.GetActiveStateNames();
            Assert.Contains("c", finalState);
            Assert.Contains("z", finalState);
        }

        #endregion
    }
}