using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using XStateNet;

namespace XStateV5_Test.AdvancedFeatures
{
    public class UnitTest_InvokeDebug : IDisposable
    {
        private StateMachine? _stateMachine;
        private readonly List<string> _eventLog = new();
        private readonly ActionMap _actions;
        private readonly ServiceMap _services;

        public UnitTest_InvokeDebug()
        {
            _actions = new ActionMap
            {
                ["incrementCounter"] = new List<NamedAction> { new NamedAction("incrementCounter", (sm) => {
                    var counterObj = sm.ContextMap?["counter"];
                    int counter = 0;
                    if (counterObj != null)
                    {
                        // Handle different types that might come from JSON parsing
                        counter = Convert.ToInt32(counterObj);
                    }
                    sm.ContextMap!["counter"] = counter + 1;
                    _eventLog.Add($"counter:incremented:{counter + 1}");
                }) }
            };

            _services = new ServiceMap
            {
                ["quickService"] = new NamedService("quickService", async (sm, ct) => {
                    _eventLog.Add("service:started");
                    await Task.Delay(50, ct);
                    _eventLog.Add("service:completed");
                    return "done";
                })
            };
        }

        public void Dispose()
        {
            _stateMachine?.Stop();
            _stateMachine?.Dispose();
        }

        [Fact]
        public async Task SimpleInvokeTransition_Works()
        {
            // Arrange - Simple state machine with invoke
            var script = @"
            {
                id: 'simpleInvoke',
                initial: 'idle',
                context: {
                    counter: 0
                },
                states: {
                    idle: {
                        on: {
                            START: 'running'
                        }
                    },
                    running: {
                        entry: 'incrementCounter',
                        invoke: {
                            src: 'quickService',
                            onDone: 'done'
                        }
                    },
                    done: {
                        type: 'final'
                    }
                }
            }";

            _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe:false, true,_actions, null, _services);

            // Add debug logging
            _stateMachine.OnTransition += (from, to, evt) => {
                _eventLog.Add($"transition:{from?.Name ?? "null"}->{to?.Name ?? "null"}:{evt}");
            };

            // Debug: Check if states have entry actions after starting
            _eventLog.Add("state-machine-created");

            _stateMachine.Start();
            _eventLog.Add($"initial-state:{_stateMachine.GetActiveStateNames()}");

            // Act
            _stateMachine.Send("START");
            _eventLog.Add($"after-start-event:{_stateMachine.GetActiveStateNames()}");
            await Task.Delay(200);
            _eventLog.Add($"final-state:{_stateMachine.GetActiveStateNames()}");

            // Debug output
            Console.WriteLine("Event log:");
            foreach (var evt in _eventLog)
            {
                Console.WriteLine($"  {evt}");
            }

            // Assert
            Assert.Contains("counter:incremented:1", _eventLog);
            Assert.Contains("service:started", _eventLog);
            Assert.Contains("service:completed", _eventLog);
            Assert.Contains($"{_stateMachine.machineId}.done", _stateMachine.GetActiveStateNames());
        }

        [Fact]
        public async Task InvokeReEntry_SingleIteration()
        {
            // Arrange - State machine that re-enters once
            var script = @"
            {
                id: 'reEntry',
                initial: 'idle',
                context: {
                    counter: 0
                },
                states: {
                    idle: {
                        on: {
                            START: 'running'
                        }
                    },
                    running: {
                        entry: 'incrementCounter',
                        invoke: {
                            src: 'quickService',
                            onDone: 'done'
                        }
                    },
                    done: {
                        type: 'final'
                    }
                }
            }";

            _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe:false, true,_actions, null, _services);
            _stateMachine.Start();

            // Act
            _stateMachine.Send("START");
            await Task.Delay(200);

            // Assert - Should complete one cycle
            var counter = Convert.ToInt32(_stateMachine.ContextMap?["counter"] ?? 0);
            Assert.Equal(1, counter);
            Assert.Equal(1, _eventLog.Count(e => e == "service:started"));
            Assert.Equal(1, _eventLog.Count(e => e == "service:completed"));
        }
    }
}