using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XStateNet;

namespace XStateNet.Examples;

/// <summary>
/// Example demonstrating communication between multiple XStateNet state machines
/// </summary>
public class CommunicatingMachinesExample
{
    /// <summary>
    /// Parent state machine that coordinates child machines
    /// </summary>
    public class ParentMachine
    {
        private StateMachine _stateMachine;
        private readonly List<ChildMachine> _children = new();
        
        public ParentMachine()
        {
            InitializeStateMachine();
        }
        
        private void InitializeStateMachine()
        {
            var jsonScript = @"{
                id: 'parentMachine',
                initial: 'idle',
                states: {
                    idle: {
                        on: {
                            START: 'coordinating'
                        }
                    },
                    coordinating: {
                        entry: 'startChildren',
                        on: {
                            CHILD_READY: 'processing',
                            CHILD_ERROR: 'error'
                        }
                    },
                    processing: {
                        on: {
                            ALL_COMPLETE: 'complete',
                            CHILD_ERROR: 'error'
                        }
                    },
                    error: {
                        entry: 'stopChildren',
                        on: {
                            RESET: 'idle'
                        }
                    },
                    complete: {
                        type: 'final'
                    }
                }
            }";
            
            var actionMap = new ActionMap();
            actionMap["startChildren"] = new List<NamedAction>
            {
                new NamedAction("startChildren", (sm) => 
                {
                    Console.WriteLine("Parent: Starting all children");
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
                    Console.WriteLine("Parent: Stopping all children");
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
                Console.WriteLine($"Parent received: Child {child.Id} changed to {state}");
                
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
    }
    
    /// <summary>
    /// Child state machine that communicates with parent
    /// </summary>
    public class ChildMachine
    {
        private StateMachine _stateMachine;
        public string Id { get; }
        public bool IsComplete { get; private set; }
        public event EventHandler<string>? OnStateChange;
        
        public ChildMachine(string id)
        {
            Id = id;
            InitializeStateMachine();
        }
        
        private void InitializeStateMachine()
        {
            var jsonScript = @"{
                id: 'childMachine',
                initial: 'waiting',
                states: {
                    waiting: {
                        on: {
                            START: 'initializing'
                        }
                    },
                    initializing: {
                        after: {
                            '1000': {
                                target: 'ready'
                            }
                        }
                    },
                    ready: {
                        entry: 'notifyReady',
                        on: {
                            PROCESS: 'processing'
                        }
                    },
                    processing: {
                        entry: 'doWork',
                        on: {
                            SUCCESS: 'complete',
                            FAIL: 'error'
                        }
                    },
                    error: {
                        entry: 'notifyError',
                        on: {
                            RETRY: 'processing',
                            STOP: 'stopped'
                        }
                    },
                    complete: {
                        entry: 'notifyComplete',
                        type: 'final'
                    },
                    stopped: {
                        type: 'final'
                    }
                }
            }";
            
            var actionMap = new ActionMap();
            
            actionMap["notifyReady"] = new List<NamedAction>
            {
                new NamedAction("notifyReady", (sm) => 
                {
                    Console.WriteLine($"Child {Id}: Ready");
                    OnStateChange?.Invoke(this, "ready");
                })
            };
            
            actionMap["doWork"] = new List<NamedAction>
            {
                new NamedAction("doWork", async (sm) => 
                {
                    Console.WriteLine($"Child {Id}: Processing...");
                    await Task.Delay(Random.Shared.Next(1000, 3000));
                    
                    // Simulate success/failure
                    if (Random.Shared.Next(100) > 20) // 80% success rate
                    {
                        sm.Send("SUCCESS");
                    }
                    else
                    {
                        sm.Send("FAIL");
                    }
                })
            };
            
            actionMap["notifyComplete"] = new List<NamedAction>
            {
                new NamedAction("notifyComplete", (sm) => 
                {
                    Console.WriteLine($"Child {Id}: Complete");
                    IsComplete = true;
                    OnStateChange?.Invoke(this, "complete");
                })
            };
            
            actionMap["notifyError"] = new List<NamedAction>
            {
                new NamedAction("notifyError", (sm) => 
                {
                    Console.WriteLine($"Child {Id}: Error");
                    OnStateChange?.Invoke(this, "error");
                })
            };
            
            _stateMachine = StateMachine.CreateFromScript(jsonScript, actionMap);
            _stateMachine.Start();
        }
        
        public void Start()
        {
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
    }
    
    /// <summary>
    /// Example of Actor pattern - state machines as actors
    /// </summary>
    public class ActorSystem
    {
        private readonly Dictionary<string, StateMachine> _actors = new();
        private readonly Dictionary<string, List<string>> _subscriptions = new();
        
        /// <summary>
        /// Register an actor (state machine) in the system
        /// </summary>
        public void RegisterActor(string actorId, StateMachine stateMachine)
        {
            _actors[actorId] = stateMachine;
            _subscriptions[actorId] = new List<string>();
        }
        
        /// <summary>
        /// Send message from one actor to another
        /// </summary>
        public void SendMessage(string fromActor, string toActor, string message)
        {
            if (_actors.TryGetValue(toActor, out var targetMachine))
            {
                Console.WriteLine($"Message: {fromActor} -> {toActor}: {message}");
                targetMachine.Send(message);
            }
        }
        
        /// <summary>
        /// Broadcast message from one actor to multiple actors
        /// </summary>
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
        
        /// <summary>
        /// Subscribe one actor to another's broadcasts
        /// </summary>
        public void Subscribe(string publisher, string subscriber)
        {
            if (!_subscriptions.ContainsKey(publisher))
            {
                _subscriptions[publisher] = new List<string>();
            }
            _subscriptions[publisher].Add(subscriber);
        }
    }
    
    /// <summary>
    /// Run the example
    /// </summary>
    public static async Task RunExample()
    {
        Console.WriteLine("=== Parent-Child Communication Example ===");
        
        // Create parent machine
        var parent = new ParentMachine();
        
        // Create and register child machines
        for (int i = 1; i <= 3; i++)
        {
            var child = new ChildMachine($"Child{i}");
            parent.AddChild(child);
        }
        
        // Start the parent (which starts children)
        parent.Start();
        
        // Wait for children to be ready
        await Task.Delay(2000);
        
        // Tell children to process
        foreach (var child in new[] { "Child1", "Child2", "Child3" })
        {
            Console.WriteLine($"Starting processing for {child}");
        }
        
        await Task.Delay(5000);
        
        Console.WriteLine("\n=== Actor System Example ===");
        
        // Create actor system
        var actorSystem = new ActorSystem();
        
        // Create state machines as actors
        var producer = CreateProducerActor();
        var consumer1 = CreateConsumerActor("Consumer1");
        var consumer2 = CreateConsumerActor("Consumer2");
        
        // Register actors
        actorSystem.RegisterActor("Producer", producer);
        actorSystem.RegisterActor("Consumer1", consumer1);
        actorSystem.RegisterActor("Consumer2", consumer2);
        
        // Set up subscriptions
        actorSystem.Subscribe("Producer", "Consumer1");
        actorSystem.Subscribe("Producer", "Consumer2");
        
        // Start communication
        producer.Send("START");
        
        // Simulate producer broadcasting
        await Task.Delay(1000);
        actorSystem.Broadcast("Producer", "NEW_DATA");
        
        await Task.Delay(1000);
        actorSystem.SendMessage("Consumer1", "Consumer2", "SYNC");
        
        Console.WriteLine("\nExample complete!");
    }
    
    private static StateMachine CreateProducerActor()
    {
        var json = @"{
            id: 'producer',
            initial: 'idle',
            states: {
                idle: {
                    on: {
                        START: 'producing'
                    }
                },
                producing: {
                    entry: 'produce',
                    on: {
                        NEW_DATA: 'broadcasting'
                    }
                },
                broadcasting: {
                    after: {
                        '500': {
                            target: 'producing'
                        }
                    }
                }
            }
        }";
        
        var actionMap = new ActionMap();
        actionMap["produce"] = new List<NamedAction>
        {
            new NamedAction("produce", (sm) => Console.WriteLine("Producer: Generating data"))
        };
        
        var machine = StateMachine.CreateFromScript(json, actionMap);
        machine.Start();
        return machine;
    }
    
    private static StateMachine CreateConsumerActor(string name)
    {
        var json = @"{
            id: 'consumer',
            initial: 'waiting',
            states: {
                waiting: {
                    on: {
                        NEW_DATA: 'processing',
                        SYNC: 'syncing'
                    }
                },
                processing: {
                    entry: 'process',
                    after: {
                        '1000': {
                            target: 'waiting'
                        }
                    }
                },
                syncing: {
                    entry: 'sync',
                    after: {
                        '500': {
                            target: 'waiting'
                        }
                    }
                }
            }
        }";
        
        var actionMap = new ActionMap();
        actionMap["process"] = new List<NamedAction>
        {
            new NamedAction("process", (sm) => Console.WriteLine($"{name}: Processing data"))
        };
        actionMap["sync"] = new List<NamedAction>
        {
            new NamedAction("sync", (sm) => Console.WriteLine($"{name}: Syncing with other consumer"))
        };
        
        var machine = StateMachine.CreateFromScript(json, actionMap);
        machine.Start();
        return machine;
    }
}