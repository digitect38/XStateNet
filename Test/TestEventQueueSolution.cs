using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using XStateNet;

// Suppress obsolete warning - event queue solution test with no inter-machine communication
#pragma warning disable CS0618

namespace Test
{
    public class TestEventQueueSolution
    {
        private readonly ITestOutputHelper _output;

        public TestEventQueueSolution(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Test_ThreadSafe_EventQueue_Deadlock()
        {
            _output.WriteLine("=== Testing Thread-Safe EventQueue for Deadlock ==");

            var completed = new TaskCompletionSource<bool>();
            var actionExecuted = false;

            var machineJson = @"{
                'id': 'queueTest',
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
                        _output.WriteLine("  Action started");
                        actionExecuted = true;

                        // Try sending event to self with EventQueue enabled
                        _output.WriteLine("  Attempting SendAsync with EventQueue");
                        try
                        {
                            // Try direct send - should still deadlock even with EventQueue
                            await sm.SendAsync("DONE");
                            _output.WriteLine("  SendAsync completed successfully!");
                        }
                        catch (Exception ex)
                        {
                            _output.WriteLine($"  Exception: {ex.Message}");
                        }
                    })
                },
                ["onComplete"] = new List<NamedAction>
                {
                    new NamedAction("onComplete", (sm) =>
                    {
                        _output.WriteLine("  Complete action executed");
                        completed.TrySetResult(true);
                        return Task.CompletedTask;
                    })
                }
            };

            // Create thread-safe machine with EventQueue enabled
            var machine = StateMachineFactory.CreateFromScript(
                machineJson,
                threadSafe: true,  // This enables EventQueue
                guidIsolate: false,
                actionCallbacks: actions);

            await machine.StartAsync();

            _output.WriteLine("Sending START event to thread-safe machine");
            var sendTask = machine.SendAsync("START");

            // Wait with timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await Task.WhenAny(sendTask, Task.Delay(-1, cts.Token));
                if (!sendTask.IsCompleted)
                {
                    _output.WriteLine("CONFIRMED: Still deadlocks even with EventQueue!");
                    Assert.True(false, "EventQueue doesn't prevent deadlock");
                }
                else
                {
                    _output.WriteLine("SUCCESS: EventQueue prevented deadlock!");
                }
            }
            catch (OperationCanceledException)
            {
                _output.WriteLine("Test timed out - deadlock confirmed");
            }

            Assert.True(actionExecuted, "Action should have started");
        }
    }
}