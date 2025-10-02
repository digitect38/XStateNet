using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using XStateNet;

// Suppress obsolete warning - standalone async test with no inter-machine communication
#pragma warning disable CS0618

namespace Test
{
    public class SimpleAsyncTest
    {
        private readonly ITestOutputHelper _output;

        public SimpleAsyncTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task SimpleAsync_Works()
        {
            var actionExecuted = false;
            var actions = new ActionMap
            {
                ["testAction"] = new System.Collections.Generic.List<NamedAction>
                {
                    new NamedAction("testAction", async (sm) =>
                    {
                        await Task.Delay(1);
                        actionExecuted = true;
                    })
                }
            };

            string script = @"{
                'id': 'simpleTest',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'GO': {
                                'target': 'done',
                                'actions': 'testAction'
                            }
                        }
                    },
                    'done': {
                        'type': 'final'
                    }
                }
            }";

            var machine = StateMachineFactory.CreateFromScript(script, false, false, actions);
            await machine.StartAsync();

            var finalState = await machine.SendAsync("GO");

            Assert.Contains("simpleTest.done", finalState);
            Assert.True(actionExecuted, "Action should have been executed");
        }

        [Fact]
        public async Task Test_SendAsync_Deadlock_Direct()
        {
            _output.WriteLine("=== Testing Direct SendAsync from Action (Expected to Deadlock) ===");

            var completed = new TaskCompletionSource<bool>();
            var actionStarted = false;

            var machineJson = @"{
                'id': 'deadlockTest',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'START': 'working'
                        }
                    },
                    'working': {
                        'entry': ['doWork'],
                        'on': {
                            'DONE': 'complete'
                        }
                    },
                    'complete': {
                        'type': 'final'
                    }
                }
            }";

            var actions = new ActionMap
            {
                ["doWork"] = new List<NamedAction>
                {
                    new NamedAction("doWork", async (sm) =>
                    {
                        _output.WriteLine("  Action started");
                        actionStarted = true;

                        // THIS WILL DEADLOCK!
                        _output.WriteLine("  Attempting direct SendAsync (will deadlock)");
                        try
                        {
                            await sm.SendAsync("DONE");
                            _output.WriteLine("  SendAsync completed (unexpected!)");
                        }
                        catch (Exception ex)
                        {
                            _output.WriteLine($"  Exception: {ex.Message}");
                        }

                        completed.TrySetResult(true);
                    })
                }
            };

            var machine = StateMachineFactory.CreateFromScript(machineJson, false, false, actions);
            await machine.StartAsync();

            _output.WriteLine("Sending START event");
            var sendTask = machine.SendAsync("START");

            // Wait with timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await Task.WhenAny(sendTask, Task.Delay(-1, cts.Token));
                if (!sendTask.IsCompleted)
                {
                    _output.WriteLine("CONFIRMED: Deadlock detected! SendAsync from action causes deadlock.");
                }
            }
            catch (OperationCanceledException)
            {
                _output.WriteLine("Test timed out");
            }

            Assert.True(actionStarted, "Action should have started");
        }

        [Fact]
        public async Task Test_SendAsync_With_Delay_Fix()
        {
            _output.WriteLine("=== Testing SendAsync with Delayed Task.Run ===");

            var completed = new TaskCompletionSource<bool>();
            var transitionCompleted = false;

            var machineJson = @"{
                'id': 'delayTest',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'START': 'working'
                        }
                    },
                    'working': {
                        'entry': ['doWork'],
                        'on': {
                            'DONE': 'complete'
                        }
                    },
                    'complete': {
                        'entry': ['onComplete'],
                        'type': 'final'
                    }
                }
            }";

            var actions = new ActionMap
            {
                ["doWork"] = new List<NamedAction>
                {
                    new NamedAction("doWork", async (sm) =>
                    {
                        _output.WriteLine("  Action: doWork started");

                        // Schedule DONE event after action completes
                        _ = Task.Run(async () =>
                        {
                            _output.WriteLine("    Task.Run: Waiting for action to complete");
                            await Task.Delay(50); // Wait for action to complete
                            _output.WriteLine("    Task.Run: Sending DONE event");
                            await sm.SendAsync("DONE");
                            _output.WriteLine("    Task.Run: DONE event sent");
                        });

                        _output.WriteLine("  Action: doWork completing");
                    })
                },
                ["onComplete"] = new List<NamedAction>
                {
                    new NamedAction("onComplete", (sm) =>
                    {
                        _output.WriteLine("  Action: onComplete executed");
                        transitionCompleted = true;
                        completed.TrySetResult(true);
                        return Task.CompletedTask;
                    })
                }
            };

            var machine = StateMachineFactory.CreateFromScript(machineJson, false, false, actions);
            await machine.StartAsync();

            _output.WriteLine("Sending START event");
            await machine.SendAsync("START");
            _output.WriteLine("START event processed");

            // Wait for completion
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await completed.Task.WaitAsync(cts.Token);
                _output.WriteLine("Test completed successfully");
            }
            catch (OperationCanceledException)
            {
                _output.WriteLine("TEST FAILED: Timeout!");
            }

            var state = machine.GetActiveStateNames();
            _output.WriteLine($"Final state: {state}");

            Assert.True(transitionCompleted, "Should have transitioned to complete state");
            Assert.Contains("complete", state);
        }
    }
}