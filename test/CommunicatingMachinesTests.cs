using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Xunit;

using XStateNet;
using XStateNet.Helpers;

namespace InterMachineTests;

public class CommunicatingMachinesTests : XStateNet.Tests.TestBase
{
    /// <summary>
    /// Helper class for deterministic waiting in tests
    /// </summary>
    public static class TestHelpers
    {   
        public static async Task WaitForConditionAsync(
            Func<bool> condition,
            int timeoutMs = 5000,
            int pollIntervalMs = 10)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition() && stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                await Task.Delay(pollIntervalMs);
            }

            if (!condition())
            {
                throw new TimeoutException($"Condition not met within {timeoutMs}ms timeout");
            }
        }

        public static async Task WaitForStateAsync(
            ParentMachine parent,
            string expectedState,
            int timeoutMs = 5000)
        {
            await WaitForConditionAsync(
                () => parent.GetCurrentState().Contains(expectedState),
                timeoutMs);
        }
        
        public static async Task WaitForEventLogAsync(
            ParentMachine parent,
            string expectedLog,
            int timeoutMs = 5000)
        {
            await WaitForConditionAsync(
                () => parent.EventLog.Any(e => e.Contains(expectedLog)),
                timeoutMs);
        }

        public static async Task WaitForEventLogAsync(
            ParentMachine parent,
            Func<string, bool> predicate,
            int timeoutMs = 5000)
        {
            await WaitForConditionAsync(
                () => parent.EventLog.Any(predicate),
                timeoutMs);
        }

        public static async Task WaitForActorMessageAsync(
            ActorSystem actorSystem,
            string actorName,
            string expectedMessage,
            int timeoutMs = 5000)
        {
            // Wait for the message to appear in the actor system's message log
            await WaitForConditionAsync(
                () => actorSystem.MessageLog.Any(m => m.Contains($"-> {actorName}:") && m.Contains(expectedMessage)),
                timeoutMs);
        }
    }
    /// <summary>
    /// Parent state machine that coordinates child machines
    /// </summary>
    public class ParentMachine : IDisposable
    {
        private StateMachine _stateMachine = null!;
        private readonly List<ChildMachine> _children = new();
        public ConcurrentBag<string> EventLog { get; } = new();
        public string Id { get; }
        
        public ParentMachine(string id)
        {
            Id = id;
            InitializeStateMachine();
        }
        
        private async void InitializeStateMachine()
        {
            var jsonScript = $@"{{
                'id': '{Id}',
                'initial': 'idle',
                'states': {{
                    'idle': {{
                        'on': {{
                            'START': 'coordinating'
                        }}
                    }},
                    'coordinating': {{
                        'entry': 'startChildren',
                        'on': {{
                            'CHILD_READY': 'processing',
                            'CHILD_ERROR': 'error'
                        }}
                    }},
                    'processing': {{
                        'on': {{
                            'ALL_COMPLETE': 'complete',
                            'CHILD_ERROR': 'error'
                        }}
                    }},
                    'error': {{
                        'entry': 'stopChildren',
                        'on': {{
                            'RESET': 'idle'
                        }}
                    }},
                    'complete': {{
                        'type': 'final'
                    }}
                }}
            }}";
            
            var actionMap = new ActionMap();
            actionMap["startChildren"] = new List<NamedAction>
            {
                new NamedAction("startChildren", (sm) => 
                {
                    EventLog.Add("Parent: Starting all children");
                    foreach (var child in _children)
                    {
                        child.Start();
                    }
                })
            };
            
            actionMap["stopChildren"] = new List<NamedAction>
            {
                new NamedAction("stopChildren", (sm) => 
                {
                    EventLog.Add("Parent: Stopping all children");
                    foreach (var child in _children)
                    {
                        child.Stop();
                    }
                })
            };
            
            _stateMachine = StateMachine.CreateFromScript(jsonScript, guidIsolate: true, actionMap);
            await _stateMachine.StartAsync();
        }
        
        public void AddChild(ChildMachine child)
        {
            _children.Add(child);
            
            // Subscribe to child events
            child.OnStateChange += (sender, state) =>
            {
                EventLog.Add($"Parent received: Child {child.Id} changed to {state}");
                
                if (state == "ready")
                {
                    _stateMachine.Send("CHILD_READY");
                }
                else if (state == "error")
                {
                    _stateMachine.Send("CHILD_ERROR");
                }
                else if (state == "complete")
                {
                    CheckAllComplete();
                }
            };
        }
        
        private void CheckAllComplete()
        {
            if (_children.All(c => c.IsComplete))
            {
                _stateMachine.Send("ALL_COMPLETE");
            }
        }
        
        public void Start()
        {
            _stateMachine.Send("START");
        }
        
        public void Reset()
        {
            _stateMachine.Send("RESET");
        }
        
        public string GetCurrentState()
        {
            return _stateMachine.GetSourceSubStateCollection(null).ToCsvString(_stateMachine, true);
        }
        
        public void Dispose()
        {
            _stateMachine?.Stop();
            _stateMachine?.Dispose();
            foreach (var child in _children)
            {
                child?.Dispose();
            }
            _children.Clear();
        }
    }
    
    /// <summary>
    /// Child state machine that communicates with parent
    /// </summary>
    public class ChildMachine : IDisposable
    {
        private StateMachine _stateMachine = null!;
        public string Id { get; }
        public bool IsComplete { get; private set; }
        public bool SimulateError { get; set; }
        public event EventHandler<string>? OnStateChange;
        
        public ChildMachine(string id)
        {
            Id = id;
            InitializeStateMachine();
        }
        
        private void InitializeStateMachine()
        {
            var jsonScript = $@"{{
                'id': '{Id}',
                'initial': 'waiting',
                'states': {{
                    'waiting': {{
                        'on': {{
                            'START': 'initializing'
                        }}
                    }},
                    'initializing': {{
                        'after': {{
                            '100': {{
                                'target': 'ready'
                            }}
                        }}
                    }},
                    'ready': {{
                        'entry': 'notifyReady',
                        'on': {{
                            'PROCESS': 'processing'
                        }},
                        'after': {{
                            '200': {{
                                'target': 'processing'
                            }}
                        }}
                    }},
                    'processing': {{
                        'entry': 'doWork',
                        'on': {{
                            'SUCCESS': 'complete',
                            'FAIL': 'error'
                        }}
                    }},
                    'error': {{
                        'entry': 'notifyError',
                        'on': {{
                            'RETRY': 'processing',
                            'STOP': 'stopped'
                        }}
                    }},
                    'complete': {{
                        'entry': 'notifyComplete',
                        'type': 'final'
                    }},
                    'stopped': {{
                        'type': 'final'
                    }}
                }}
            }}";
            
            var actionMap = new ActionMap();
            
            actionMap["notifyReady"] = new List<NamedAction>
            {
                new NamedAction("notifyReady", (sm) => 
                {
                    OnStateChange?.Invoke(this, "ready");
                })
            };
            
            actionMap["doWork"] = new List<NamedAction>
            {
                new NamedAction("doWork", (sm) => 
                {
                    // For testing, immediately send success/failure
                    if (SimulateError)
                    {
                        sm.Send("FAIL");
                    }
                    else
                    {
                        sm.Send("SUCCESS");
                    }
                })
            };
            
            actionMap["notifyComplete"] = new List<NamedAction>
            {
                new NamedAction("notifyComplete", (sm) => 
                {
                    IsComplete = true;
                    OnStateChange?.Invoke(this, "complete");
                })
            };
            
            actionMap["notifyError"] = new List<NamedAction>
            {
                new NamedAction("notifyError", (sm) => 
                {
                    OnStateChange?.Invoke(this, "error");
                })
            };
            
            _stateMachine = StateMachine.CreateFromScript(jsonScript, guidIsolate: true, actionMap);
        }
        
        public void Start()
        {
            try
            {
                _stateMachine.Start();
            }
            catch
            {
                // Already started, ignore
            }
            _stateMachine.Send("START");
        }
        
        public void Stop()
        {
            _stateMachine.Send("STOP");
        }
        
        public void Process()
        {
            _stateMachine.Send("PROCESS");
        }
        
        public string GetCurrentState()
        {
            return _stateMachine.GetSourceSubStateCollection(null).ToCsvString(_stateMachine, true);
        }
        
        public void Dispose()
        {
            _stateMachine?.Stop();
            _stateMachine?.Dispose();
        }
    }
    
    /// <summary>
    /// Actor system for state machine communication
    /// </summary>
    public class ActorSystem : IDisposable
    {
        private readonly ConcurrentDictionary<string, StateMachine> _actors = new();
        private readonly ConcurrentDictionary<string, List<string>> _subscriptions = new();
        public ConcurrentBag<string> MessageLog { get; } = new();
        
        public void RegisterActor(string actorId, StateMachine stateMachine)
        {
            _actors[actorId] = stateMachine;
            _subscriptions[actorId] = new List<string>();
        }
        
        public void SendMessage(string fromActor, string toActor, string message)
        {
            if (_actors.TryGetValue(toActor, out var targetMachine))
            {
                MessageLog.Add($"{fromActor} -> {toActor}: {message}");
                targetMachine.Send(message);
            }
        }
        
        public void Broadcast(string fromActor, string message)
        {
            if (_subscriptions.TryGetValue(fromActor, out var subscribers))
            {
                foreach (var subscriber in subscribers)
                {
                    SendMessage(fromActor, subscriber, message);
                }
            }
        }
        
        public void Subscribe(string publisher, string subscriber)
        {
            if (!_subscriptions.ContainsKey(publisher))
            {
                _subscriptions[publisher] = new List<string>();
            }
            _subscriptions[publisher].Add(subscriber);
        }
        
        public int GetSubscriberCount(string publisher)
        {
            return _subscriptions.TryGetValue(publisher, out var subs) ? subs.Count : 0;
        }
        
        public void Dispose()
        {
            foreach (var actor in _actors.Values)
            {
                actor?.Stop();
                actor?.Dispose();
            }
            _actors.Clear();
            _subscriptions.Clear();
        }
    }
    
    [Fact]
    public async Task ParentChild_Should_CoordinateSuccessfully()
    {
        // Setup
        ParentMachine parent = null;
        
        try
        {
            // Arrange
            var testId = Guid.NewGuid().ToString("N").Substring(0, 8);
            parent = new ParentMachine($"parent_{testId}");
            
            for (int i = 1; i <= 3; i++)
            {
                var child = new ChildMachine($"child{i}_{testId}");
                parent.AddChild(child);
            }
            
            // Act
            parent.Start();

            // Wait for specific state transitions deterministically
            await TestHelpers.WaitForStateAsync(parent, "complete");

            // Assert
            Assert.Contains("complete", parent.GetCurrentState());
            Assert.Contains("Parent: Starting all children", parent.EventLog);
            Assert.Contains(parent.EventLog, e => e.Contains("changed to ready"));
            Assert.Contains(parent.EventLog, e => e.Contains("changed to complete"));
        }
        finally
        {
            // Cleanup
            parent?.Dispose();
        }
    }
    
    [Fact]
    public async Task ParentChild_Should_HandleChildError()
    {
        // Setup
        ParentMachine parent = null;
        
        try
        {
            // Arrange
            var testId = Guid.NewGuid().ToString("N").Substring(0, 8);
            parent = new ParentMachine($"parent_{testId}");
            var errorChild = new ChildMachine($"errorChild_{testId}") { SimulateError = true };
            var goodChild = new ChildMachine($"goodChild_{testId}");
            
            parent.AddChild(errorChild);
            parent.AddChild(goodChild);
            
            // Act
            parent.Start();

            // Wait for error state and error log entry
            await TestHelpers.WaitForStateAsync(parent, "error");
            await TestHelpers.WaitForEventLogAsync(parent, $"errorChild_{testId} changed to error");

            // Assert
            Assert.Contains("error", parent.GetCurrentState());
            Assert.Contains("Parent: Stopping all children", parent.EventLog);
            Assert.Contains(parent.EventLog, e => e.Contains($"errorChild_{testId} changed to error"));
        }
        finally
        {
            // Cleanup
            parent?.Dispose();
        }
    }
    
    [Fact]
    public async Task ParentChild_Should_ResetAfterError()
    {
        // Setup
        ParentMachine parent = null;
        
        try
        {
            // Arrange
            var testId = Guid.NewGuid().ToString("N").Substring(0, 8);
            parent = new ParentMachine($"parent_{testId}");
            var errorChild = new ChildMachine($"errorChild_{testId}") { SimulateError = true };
            parent.AddChild(errorChild);
            
            // Act
            parent.Start();

            // Wait for error state
            await TestHelpers.WaitForStateAsync(parent, "error");
            var errorState = parent.GetCurrentState();

            parent.Reset();

            // Wait for idle state after reset
            await TestHelpers.WaitForStateAsync(parent, "idle");
            var resetState = parent.GetCurrentState();
            
            // Assert
            Assert.Contains("error", errorState);
            Assert.Contains("idle", resetState);
        }
        finally
        {
            // Cleanup
            parent?.Dispose();
        }
    }
    
    [Fact]
    public async Task ActorSystem_Should_RegisterAndCommunicate()
    {
        // Setup
        ActorSystem actorSystem = null;
        StateMachine producer = null;
        StateMachine consumer = null;
        
        try
        {
            // Arrange
            var testId = Guid.NewGuid().ToString("N").Substring(0, 8);
            actorSystem = new ActorSystem();
            producer = CreateTestActor($"producer_{testId}");
            consumer = CreateTestActor($"consumer_{testId}");
            
            // Act
            actorSystem.RegisterActor("Producer", producer);
            actorSystem.RegisterActor("Consumer", consumer);

            actorSystem.SendMessage("Producer", "Consumer", "TEST_MESSAGE");

            // Wait for message to be received
            await TestHelpers.WaitForActorMessageAsync(actorSystem, "Consumer", "TEST_MESSAGE");

            // Assert
            Assert.Contains("Producer -> Consumer: TEST_MESSAGE", actorSystem.MessageLog);
        }
        finally
        {
            // Cleanup
            actorSystem?.Dispose();
            // Note: Actors are disposed by ActorSystem
        }
    }
    
    [Fact]
    public async Task ActorSystem_Should_BroadcastToSubscribers()
    {
        // Setup
        ActorSystem actorSystem = null;
        
        try
        {
            // Arrange
            var testId = Guid.NewGuid().ToString("N").Substring(0, 8);
            actorSystem = new ActorSystem();
            var producer = CreateTestActor($"producer_{testId}");
            var consumer1 = CreateTestActor($"consumer1_{testId}");
            var consumer2 = CreateTestActor($"consumer2_{testId}");
            
            actorSystem.RegisterActor("Producer", producer);
            actorSystem.RegisterActor("Consumer1", consumer1);
            actorSystem.RegisterActor("Consumer2", consumer2);
            
            actorSystem.Subscribe("Producer", "Consumer1");
            actorSystem.Subscribe("Producer", "Consumer2");
            
            // Act
            actorSystem.Broadcast("Producer", "BROADCAST_MESSAGE");

            // Wait for both consumers to receive the message
            await TestHelpers.WaitForActorMessageAsync(actorSystem, "Consumer1", "BROADCAST_MESSAGE");
            await TestHelpers.WaitForActorMessageAsync(actorSystem, "Consumer2", "BROADCAST_MESSAGE");

            // Assert
            Assert.Equal(2, actorSystem.GetSubscriberCount("Producer"));
            Assert.Contains("Producer -> Consumer1: BROADCAST_MESSAGE", actorSystem.MessageLog);
            Assert.Contains("Producer -> Consumer2: BROADCAST_MESSAGE", actorSystem.MessageLog);
        }
        finally
        {
            // Cleanup
            actorSystem?.Dispose();
        }
    }
    
    [Fact]
    public void ActorSystem_Should_HandleMissingActor()
    {
        // Setup
        ActorSystem actorSystem = null;
        
        try
        {
            // Arrange
            var testId = Guid.NewGuid().ToString("N").Substring(0, 8);
            actorSystem = new ActorSystem();
            var producer = CreateTestActor($"producer_{testId}");
            actorSystem.RegisterActor("Producer", producer);
            
            // Act
            actorSystem.SendMessage("Producer", "NonExistent", "TEST");
            
            // Assert
            Assert.Empty(actorSystem.MessageLog);
        }
        finally
        {
            // Cleanup
            actorSystem?.Dispose();
        }
    }

    //[Fact(Skip = "Complex timing issue - children state machines not completing properly")]
    [Fact]
    public async Task MultipleChildren_Should_CompleteIndependently()
    {
        // Setup
        ParentMachine parent = null;
        
        try
        {
            // Arrange
            var testId = Guid.NewGuid().ToString("N").Substring(0, 8);
            parent = new ParentMachine($"parent_{testId}");
            var children = new List<ChildMachine>();
            
            for (int i = 1; i <= 5; i++)
            {
                var child = new ChildMachine($"child{i}_{testId}");
                children.Add(child);
                parent.AddChild(child);
            }
            
            // Act
            parent.Start();

            // Wait for all children to complete
            await TestHelpers.WaitForStateAsync(parent, "complete");

            // Also wait for all complete messages in log
            for (int i = 1; i <= 5; i++)  // Fixed: Child IDs start from 1, not 0
            {
                var childId = $"child{i}_{testId}";
                await TestHelpers.WaitForEventLogAsync(parent, $"{childId} changed to complete");
            }

            // Assert - debug output if fails
            if (!children.All(c => c.IsComplete))
            {
                for (int i = 0; i < children.Count; i++)
                {
                    var child = children[i];
                    Console.WriteLine($"Child{i+1}: State={child.GetCurrentState()}, IsComplete={child.IsComplete}");
                }
            }
            
            Assert.True(children.All(c => c.IsComplete), "all children should complete");
            Assert.Contains("complete", parent.GetCurrentState());
        }
        finally
        {
            // Cleanup
            parent?.Dispose();
        }
    }
    
    private StateMachine CreateTestActor(string id)
    {
        var json = $@"{{
            'id': '{id}',
            'initial': 'idle',
            'states': {{
                'idle': {{
                    'on': {{
                        'TEST_MESSAGE': 'received',
                        'BROADCAST_MESSAGE': 'received'
                    }}
                }},
                'received': {{
                    'after': {{
                        '100': {{
                            'target': 'idle'
                        }}
                    }}
                }}
            }}
        }}";
        
        var machine = StateMachine.CreateFromScript(json, guidIsolate: true);
        machine.Start();
        return machine;
    }
}