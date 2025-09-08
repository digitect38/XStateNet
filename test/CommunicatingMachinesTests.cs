using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Xunit;
using FluentAssertions;
using XStateNet;

namespace InterMachineTests;

public class CommunicatingMachinesTests : XStateNet.Tests.TestBase
{
    /// <summary>
    /// Parent state machine that coordinates child machines
    /// </summary>
    public class ParentMachine : IDisposable
    {
        private StateMachine _stateMachine = null!;
        private readonly List<ChildMachine> _children = new();
        public List<string> EventLog { get; } = new();
        public string Id { get; }
        
        public ParentMachine(string id)
        {
            Id = id;
            InitializeStateMachine();
        }
        
        private void InitializeStateMachine()
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
            
            _stateMachine = StateMachine.CreateFromScript(jsonScript, actionMap);
            _stateMachine.Start();
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
            
            _stateMachine = StateMachine.CreateFromScript(jsonScript, actionMap);
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
        private readonly Dictionary<string, StateMachine> _actors = new();
        private readonly Dictionary<string, List<string>> _subscriptions = new();
        public List<string> MessageLog { get; } = new();
        
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
            await Task.Delay(1000); // Allow state transitions to complete
            
            // Assert
            parent.GetCurrentState().Should().Contain("complete");
            parent.EventLog.Should().Contain("Parent: Starting all children");
            parent.EventLog.Should().Contain(e => e.Contains("changed to ready"));
            parent.EventLog.Should().Contain(e => e.Contains("changed to complete"));
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
            await Task.Delay(500);
            
            // Assert
            parent.GetCurrentState().Should().Contain("error");
            parent.EventLog.Should().Contain("Parent: Stopping all children");
            parent.EventLog.Should().Contain(e => e.Contains("changed to error"));
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
            await Task.Delay(600); // Wait longer for child transitions (100ms init + 200ms ready + processing)
            
            var errorState = parent.GetCurrentState();
            
            parent.Reset();
            await Task.Delay(100);
            
            var resetState = parent.GetCurrentState();
            
            // Assert
            errorState.Should().Contain("error");
            resetState.Should().Contain("idle");
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
            await Task.Delay(100);
            
            // Assert
            actorSystem.MessageLog.Should().Contain("Producer -> Consumer: TEST_MESSAGE");
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
            await Task.Delay(100);
            
            // Assert
            actorSystem.GetSubscriberCount("Producer").Should().Be(2);
            actorSystem.MessageLog.Should().Contain("Producer -> Consumer1: BROADCAST_MESSAGE");
            actorSystem.MessageLog.Should().Contain("Producer -> Consumer2: BROADCAST_MESSAGE");
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
            actorSystem.MessageLog.Should().BeEmpty();
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
            await Task.Delay(2000); // Allow time for all children to complete (100ms init + 200ms ready delay per child)
            
            // Assert - debug output if fails
            if (!children.All(c => c.IsComplete))
            {
                for (int i = 0; i < children.Count; i++)
                {
                    var child = children[i];
                    Console.WriteLine($"Child{i+1}: State={child.GetCurrentState()}, IsComplete={child.IsComplete}");
                }
            }
            
            children.All(c => c.IsComplete).Should().BeTrue("all children should complete");
            parent.GetCurrentState().Should().Contain("complete");
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
        
        var machine = StateMachine.CreateFromScript(json);
        machine.Start();
        return machine;
    }
}
