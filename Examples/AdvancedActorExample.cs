using System;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.Actors;

namespace XStateNet.Examples;

/// <summary>
/// Example demonstrating advanced Actor features with XStateNet
/// </summary>
public class AdvancedActorExample
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== XStateNet Advanced Actor System Example ===\n");

        // Create actor system
        var system = new ActorSystemV2("OrderProcessingSystem");

        try
        {
            // Example 1: Simple Actor Communication
            await DemoSimpleActors(system);

            // Example 2: State Machine Actors
            await DemoStateMachineActors(system);

            // Example 3: Supervisor Pattern
            await DemoSupervisorPattern(system);

            // Example 4: Complex Order Processing System
            await DemoOrderProcessingSystem(system);
        }
        finally
        {
            await system.Shutdown();
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    static async Task DemoSimpleActors(ActorSystemV2 system)
    {
        Console.WriteLine("1. Simple Actor Communication Demo");
        Console.WriteLine("-----------------------------------");

        // Create a greeting actor
        var greeter = system.ActorOf(Props.Create<GreeterActor>(), "greeter");

        // Tell pattern (fire and forget)
        await greeter.TellAsync(new Greet("World"));
        await Task.Delay(100);

        // Ask pattern (request-response)
        var response = await greeter.AskAsync<string>(new GetGreeting("Alice"), TimeSpan.FromSeconds(1));
        Console.WriteLine($"Response: {response}");

        Console.WriteLine();
    }

    static async Task DemoStateMachineActors(ActorSystemV2 system)
    {
        Console.WriteLine("2. State Machine Actor Demo");
        Console.WriteLine("---------------------------");

        // Create traffic light state machine
        var trafficLightJson = @"{
            'id': 'trafficLight',
            'initial': 'red',
            'states': {
                'red': {
                    'entry': 'turnOnRed',
                    'exit': 'turnOffRed',
                    'on': { 'TIMER': 'green' }
                },
                'green': {
                    'entry': 'turnOnGreen',
                    'exit': 'turnOffGreen',
                    'on': { 'TIMER': 'yellow' }
                },
                'yellow': {
                    'entry': 'turnOnYellow',
                    'exit': 'turnOffYellow',
                    'on': { 'TIMER': 'red' }
                }
            }
        }";

        var actions = new ActionMap
        {
            ["turnOnRed"] = new List<NamedAction> { new NamedAction("turnOnRed", _ => Console.WriteLine("🔴 Red light ON")) },
            ["turnOffRed"] = new List<NamedAction> { new NamedAction("turnOffRed", _ => Console.WriteLine("   Red light OFF")) },
            ["turnOnGreen"] = new List<NamedAction> { new NamedAction("turnOnGreen", _ => Console.WriteLine("🟢 Green light ON")) },
            ["turnOffGreen"] = new List<NamedAction> { new NamedAction("turnOffGreen", _ => Console.WriteLine("   Green light OFF")) },
            ["turnOnYellow"] = new List<NamedAction> { new NamedAction("turnOnYellow", _ => Console.WriteLine("🟡 Yellow light ON")) },
            ["turnOffYellow"] = new List<NamedAction> { new NamedAction("turnOffYellow", _ => Console.WriteLine("   Yellow light OFF")) }
        };

        var trafficMachine = StateMachine.CreateFromScript(trafficLightJson, actions);
        var trafficActor = system.ActorOf(Props.CreateStateMachine(trafficMachine), "traffic");

        // Cycle through traffic lights
        for (int i = 0; i < 6; i++)
        {
            await trafficActor.TellAsync(new StateEvent("TIMER"));
            await Task.Delay(500);
        }

        Console.WriteLine();
    }

    static async Task DemoSupervisorPattern(ActorSystemV2 system)
    {
        Console.WriteLine("3. Supervisor Pattern Demo");
        Console.WriteLine("--------------------------");

        // Create a supervisor with workers
        var supervisor = system.ActorOf(Props.Create<WorkerSupervisor>(), "supervisor");

        // Create workers
        await supervisor.TellAsync(new CreateWorkers(3));
        await Task.Delay(100);

        // Distribute work
        for (int i = 1; i <= 10; i++)
        {
            await supervisor.TellAsync(new WorkItem(i, $"Task {i}"));
            await Task.Delay(50);
        }

        await Task.Delay(500);
        Console.WriteLine();
    }

    static async Task DemoOrderProcessingSystem(ActorSystemV2 system)
    {
        Console.WriteLine("4. Complex Order Processing System Demo");
        Console.WriteLine("---------------------------------------");

        // Create order processing state machine
        var orderProcessingJson = @"{
            'id': 'orderProcessor',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'NEW_ORDER': {
                            'target': 'validating',
                            'actions': ['validateOrder']
                        }
                    }
                },
                'validating': {
                    'on': {
                        'VALID': 'processing',
                        'INVALID': 'rejected'
                    }
                },
                'processing': {
                    'type': 'parallel',
                    'states': {
                        'payment': {
                            'initial': 'pending',
                            'states': {
                                'pending': {
                                    'on': { 'PAYMENT_RECEIVED': 'completed' }
                                },
                                'completed': { 'type': 'final' }
                            }
                        },
                        'inventory': {
                            'initial': 'checking',
                            'states': {
                                'checking': {
                                    'on': {
                                        'IN_STOCK': 'reserved',
                                        'OUT_OF_STOCK': 'backordered'
                                    }
                                },
                                'reserved': { 'type': 'final' },
                                'backordered': { 'type': 'final' }
                            }
                        },
                        'shipping': {
                            'initial': 'preparing',
                            'states': {
                                'preparing': {
                                    'on': { 'READY_TO_SHIP': 'shipped' }
                                },
                                'shipped': { 'type': 'final' }
                            }
                        }
                    },
                    'onDone': 'completed'
                },
                'completed': {
                    'type': 'final',
                    'entry': 'notifyCustomer'
                },
                'rejected': {
                    'type': 'final',
                    'entry': 'notifyRejection'
                }
            }
        }";

        var orderActions = new ActionMap
        {
            ["validateOrder"] = new List<NamedAction> {
                new NamedAction("validateOrder", sm => {
                    Console.WriteLine("📋 Validating order...");
                    Task.Delay(300).Wait();
                    sm.Send("VALID");
                })
            },
            ["notifyCustomer"] = new List<NamedAction> {
                new NamedAction("notifyCustomer", _ => Console.WriteLine("✅ Order completed! Customer notified."))
            },
            ["notifyRejection"] = new List<NamedAction> {
                new NamedAction("notifyRejection", _ => Console.WriteLine("❌ Order rejected! Customer notified."))
            }
        };

        var orderMachine = StateMachine.CreateFromScript(orderProcessingJson, orderActions);
        var orderProcessor = system.ActorOf(Props.CreateStateMachine(orderMachine), "orderProcessor");

        // Process an order
        Console.WriteLine("Processing Order #12345:");
        await orderProcessor.TellAsync(new StateEvent("NEW_ORDER"));
        await Task.Delay(500);

        // Simulate parallel processing
        await orderProcessor.TellAsync(new StateEvent("PAYMENT_RECEIVED"));
        Console.WriteLine("💳 Payment received");
        await Task.Delay(200);

        await orderProcessor.TellAsync(new StateEvent("IN_STOCK"));
        Console.WriteLine("📦 Items in stock and reserved");
        await Task.Delay(200);

        await orderProcessor.TellAsync(new StateEvent("READY_TO_SHIP"));
        Console.WriteLine("🚚 Package ready to ship");
        await Task.Delay(500);

        Console.WriteLine();
    }
}

#region Actor Implementations

public class GreeterActor : ActorBase
{
    protected override Task Receive(object message)
    {
        switch (message)
        {
            case Greet greet:
                Console.WriteLine($"Hello, {greet.Name}!");
                break;

            case GetGreeting getGreeting:
                var greeting = $"Hello, {getGreeting.Name}! How are you?";
                Context.Sender?.TellAsync(greeting);
                break;
        }
        return Task.CompletedTask;
    }
}

public class WorkerSupervisor : ActorBase
{
    private readonly List<ActorRef> _workers = new();
    private int _nextWorkerIndex = 0;

    protected override async Task Receive(object message)
    {
        switch (message)
        {
            case CreateWorkers create:
                for (int i = 0; i < create.Count; i++)
                {
                    var worker = Context.SpawnChild($"worker-{i}", Props.Create<WorkerActor>());
                    _workers.Add(worker);
                    Context.Watch(worker); // Monitor worker lifecycle
                }
                Console.WriteLine($"Created {create.Count} workers");
                break;

            case WorkItem work:
                if (_workers.Count > 0)
                {
                    // Round-robin work distribution
                    var worker = _workers[_nextWorkerIndex % _workers.Count];
                    await worker.TellAsync(work);
                    _nextWorkerIndex++;
                }
                break;

            case Terminated terminated:
                Console.WriteLine($"Worker {terminated.Actor.Path} terminated");
                _workers.RemoveAll(w => w.Path == terminated.Actor.Path);

                // Restart worker if needed
                var workerName = terminated.Actor.Path.Split('/').Last();
                var newWorker = Context.SpawnChild(workerName, Props.Create<WorkerActor>());
                _workers.Add(newWorker);
                Context.Watch(newWorker);
                Console.WriteLine($"Restarted worker: {workerName}");
                break;

            case ActorFailure failure:
                Console.WriteLine($"Worker failure: {failure.Exception.Message}");
                // Supervisor decides what to do based on supervision strategy
                break;
        }
    }
}

public class WorkerActor : ActorBase
{
    private static readonly Random _random = new();

    protected override async Task Receive(object message)
    {
        switch (message)
        {
            case WorkItem work:
                Console.WriteLine($"  Worker {Context.Self.Path} processing: {work.Description}");

                // Simulate work
                await Task.Delay(_random.Next(100, 300));

                // Randomly fail sometimes (for demo)
                if (_random.Next(10) == 0)
                {
                    throw new Exception("Random worker failure!");
                }

                Console.WriteLine($"  Worker {Context.Self.Path} completed: {work.Description}");
                break;
        }
    }
}

#endregion

#region Message Types

public record Greet(string Name);
public record GetGreeting(string Name);
public record CreateWorkers(int Count);
public record WorkItem(int Id, string Description);

#endregion