using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.Distributed.EventBus;
using XStateNet.Distributed.PubSub;
using XStateNet.Distributed.PubSub.Optimized;

// Suppress obsolete warning - Example/demo code demonstrating distributed pub/sub patterns
// Examples show StateMachineFactory.CreateFromScript usage for educational purposes
#pragma warning disable CS0618

namespace XStateNet.Distributed.Examples
{
    /// <summary>
    /// Example demonstrating pub/sub architecture with XStateNet
    /// </summary>
    public class PubSubExample
    {
        public static async Task RunExample()
        {
            Console.WriteLine("=== XStateNet Pub/Sub Example ===\n");

            // Create logger factory
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole().SetMinimumLevel(LogLevel.Information);
            });

            // Create event bus (in-memory for this example)

            var eventBus = new InMemoryEventBus(loggerFactory.CreateLogger<InMemoryEventBus>());
            

            // Example 1: Traffic Light System with Event Notifications
            await RunTrafficLightExample(eventBus, loggerFactory);
            
            // Example 2: Order Processing System with Multiple Machines
            await RunOrderProcessingExample(eventBus, loggerFactory);

            // Example 3: Event Aggregation Example
            await RunEventAggregationExample(eventBus, loggerFactory);
            
            Console.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Traffic light example with state change notifications
        /// </summary>
        private static async Task RunTrafficLightExample(IStateMachineEventBus eventBus, ILoggerFactory loggerFactory)
        {
            Console.WriteLine("\n--- Traffic Light Example ---");

            // Create traffic light state machine
            var trafficLightJson = @"{
                'id': 'traffic-light',
                'initial': 'red',
                'states': {
                    'red': {
                        'on': {
                            'TIMER': 'green'
                        },
                        'entry': ['turnOnRed', 'notifyRedLight']
                    },
                    'green': {
                        'on': {
                            'TIMER': 'yellow'
                        },
                        'entry': ['turnOnGreen', 'notifyGreenLight']
                    },
                    'yellow': {
                        'on': {
                            'TIMER': 'red'
                        },
                        'entry': ['turnOnYellow', 'notifyYellowLight']
                    }
                }
            }";

            var actionMap = new ActionMap
            {
                ["turnOnRed"] = new List<NamedAction> { new NamedAction("turnOnRed", sm => Console.WriteLine("🔴 Red light ON")) },
                ["turnOnGreen"] = new List<NamedAction> { new NamedAction("turnOnGreen", sm => Console.WriteLine("🟢 Green light ON")) },
                ["turnOnYellow"] = new List<NamedAction> { new NamedAction("turnOnYellow", sm => Console.WriteLine("🟡 Yellow light ON")) },
                ["notifyRedLight"] = new List<NamedAction> { new NamedAction("notifyRedLight", sm => { }) },
                ["notifyGreenLight"] = new List<NamedAction> { new NamedAction("notifyGreenLight", sm => { }) },
                ["notifyYellowLight"] = new List<NamedAction> { new NamedAction("notifyYellowLight", sm => { }) }
            };

            var trafficLight = StateMachineFactory.CreateFromScript(trafficLightJson, false, true, actionMap);

            // Create event notification service
#if false
            var notificationService = new EventNotificationService(
                trafficLight,
                eventBus,
                "traffic-light-1",
                loggerFactory.CreateLogger<EventNotificationService>());
#else
            var notificationService = new OptimizedEventNotificationService(
                trafficLight,
                eventBus,
                "traffic-light-1",
                null, // EventServiceOptions (optional)
                loggerFactory.CreateLogger<OptimizedEventNotificationService>());
#endif
            // Subscribe to state changes
            var stateChangeSub = await eventBus.SubscribeToStateChangesAsync("traffic-light-1", stateChange =>
            {
                Console.WriteLine($"📡 State Changed: {stateChange.OldState} → {stateChange.NewState}");
            });

            // Subscribe to all events
            var allEventsSub = await eventBus.SubscribeToAllAsync(evt =>
            {
                Console.WriteLine($"📨 Event: {evt.EventName} from {(evt.SourceMachineId == "" ? "<None>" : evt.SourceMachineId)}");
            });

            // Start services
            await notificationService.StartAsync();
            trafficLight.Start();

            // Simulate traffic light cycles
            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(1000);
                trafficLight.Send("TIMER");
            }

            // Clean up
            stateChangeSub.Dispose();
            allEventsSub.Dispose();
            await notificationService.StopAsync();
            trafficLight.Stop();
        }

        /// <summary>
        /// Order processing example with multiple communicating machines
        /// </summary>
        private static async Task RunOrderProcessingExample(IStateMachineEventBus eventBus, ILoggerFactory loggerFactory)
        {
            Console.WriteLine("\n--- Order Processing Example ---");

            // Create Order Machine
            var orderMachineJson = @"{
                'id': 'order-machine',
                'initial': 'pending',
                'states': {
                    'pending': {
                        'on': {
                            'APPROVE': 'processing',
                            'REJECT': 'cancelled'
                        }
                    },
                    'processing': {
                        'on': {
                            'SHIP': 'shipped',
                            'CANCEL': 'cancelled'
                        },
                        'entry': ['notifyWarehouse']
                    },
                    'shipped': {
                        'on': {
                            'DELIVER': 'delivered'
                        },
                        'entry': ['notifyCustomer']
                    },
                    'delivered': {
                        'type': 'final'
                    },
                    'cancelled': {
                        'type': 'final',
                        'entry': ['notifyCustomer']
                    }
                }
            }";

            // Create Inventory Machine
            var inventoryMachineJson = @"{
                'id': 'inventory-machine',
                'initial': 'available',
                'states': {
                    'available': {
                        'on': {
                            'RESERVE': 'reserved',
                            'CHECK': [
                                {
                                    'target': 'available',
                                    'cond': 'hasStock',
                                    'actions': ['confirmStock']
                                },
                                {
                                    'target': 'outOfStock'
                                }
                            ]
                        }
                    },
                    'reserved': {
                        'on': {
                            'RELEASE': 'available',
                            'CONSUME': 'consumed'
                        }
                    },
                    'consumed': {
                        'type': 'final'
                    },
                    'outOfStock': {
                        'on': {
                            'RESTOCK': 'available'
                        }
                    }
                }
            }";

            var orderActions = new ActionMap
            {
                ["notifyWarehouse"] = new List<NamedAction> {
                    new NamedAction("notifyWarehouse", async sm => {
                        Console.WriteLine("📦 Notifying warehouse to prepare order");
                        // Send event to inventory machine
                        await eventBus.PublishEventAsync("inventory-1", "RESERVE");
                    })
                },
                ["notifyCustomer"] = new List<NamedAction> {
                    new NamedAction("notifyCustomer", sm => Console.WriteLine("📧 Customer notified"))
                }
            };

            var inventoryActions = new ActionMap
            {
                ["confirmStock"] = new List<NamedAction> {
                    new NamedAction("confirmStock", async sm => {
                        Console.WriteLine("✅ Stock confirmed");
                        // Send confirmation back to order machine
                        await eventBus.PublishEventAsync("order-1", "STOCK_CONFIRMED");
                    })
                }
            };

            var inventoryGuards = new GuardMap
            {
                ["hasStock"] = new NamedGuard("hasStock", sm => true) // Simulate stock available
            };

            var orderMachine = StateMachineFactory.CreateFromScript(orderMachineJson, false, true, orderActions);
            var inventoryMachine = StateMachineFactory.CreateFromScript(inventoryMachineJson, false, true, inventoryActions, inventoryGuards);

            // Create notification services
            var orderNotifications = new EventNotificationService(
                orderMachine, eventBus, "order-1",
                loggerFactory.CreateLogger<EventNotificationService>());

            var inventoryNotifications = new EventNotificationService(
                inventoryMachine, eventBus, "inventory-1",
                loggerFactory.CreateLogger<EventNotificationService>());

            // Subscribe to cross-machine events
            await eventBus.SubscribeToMachineAsync("inventory-1", evt =>
            {
                if (evt.EventName == "RESERVE")
                {
                    Console.WriteLine("📥 Inventory received RESERVE event");
                    inventoryMachine.Send("RESERVE");
                }
            });

            await eventBus.SubscribeToMachineAsync("order-1", evt =>
            {
                if (evt.EventName == "STOCK_CONFIRMED")
                {
                    Console.WriteLine("📥 Order received STOCK_CONFIRMED event");
                    orderMachine.Send("SHIP");
                }
            });

            // Start services
            await orderNotifications.StartAsync();
            await inventoryNotifications.StartAsync();
            orderMachine.Start();
            inventoryMachine.Start();

            // Process order
            Console.WriteLine("\n🛒 Starting order processing...");
            orderMachine.Send("APPROVE");
            await Task.Delay(1000);

            orderMachine.Send("DELIVER");
            await Task.Delay(500);

            // Clean up
            await orderNotifications.StopAsync();
            await inventoryNotifications.StopAsync();
            orderMachine.Stop();
            inventoryMachine.Stop();
        }

        /// <summary>
        /// Event aggregation example
        /// </summary>
        private static async Task RunEventAggregationExample(IStateMachineEventBus eventBus, ILoggerFactory loggerFactory)
        {
            Console.WriteLine("\n--- Event Aggregation Example ---");

            // Create a simple counter machine
            var counterJson = @"{
                'id': 'counter',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'INCREMENT': {
                                'target': 'idle',
                                'actions': ['increment']
                            },
                            'DECREMENT': {
                                'target': 'idle',
                                'actions': ['decrement']
                            }
                        }
                    }
                }
            }";

            var counter = 0;
            var actionMap = new ActionMap
            {
                ["increment"] = new List<NamedAction> {
                    new NamedAction("increment", sm => {
                        counter++;
                        Console.WriteLine($"➕ Counter: {counter}");
                    })
                },
                ["decrement"] = new List<NamedAction> {
                    new NamedAction("decrement", sm => {
                        counter--;
                        Console.WriteLine($"➖ Counter: {counter}");
                    })
                }
            };

            var counterMachine = StateMachineFactory.CreateFromScript(counterJson, false, true, actionMap);
#if false
            var notificationService = new EventNotificationService(
                counterMachine, eventBus, "counter-1",
                loggerFactory.CreateLogger<EventNotificationService>());
#else
            var notificationService = new OptimizedEventNotificationService(
                counterMachine, eventBus, "counter-1", null,
                loggerFactory.CreateLogger<OptimizedEventNotificationService>());
#endif

            // Create event aggregator - batch events every 2 seconds or when 5 events accumulate
            var aggregator = notificationService.CreateAggregator<ActionExecutedNotification>(
                TimeSpan.FromSeconds(2),
                maxBatchSize: 5,
                batch =>
                {
                    Console.WriteLine($"\n📊 Batch of {batch.Count} actions:");
                    foreach (var action in batch)
                    {
                        Console.WriteLine($"   - {action.ActionName} at {action.Timestamp:HH:mm:ss.fff}");
                    }
                    Console.WriteLine($"   Total batch span: {(batch[^1].Timestamp - batch[0].Timestamp).TotalMilliseconds}ms\n");
                });

            // Subscribe to action events and feed to aggregator
            await eventBus.SubscribeToPatternAsync("counter-1.*", evt =>
            {
                if (evt is ActionExecutedNotification action)
                {
                    aggregator.Add(action);
                }
            });

            // Start services
            await notificationService.StartAsync();
            counterMachine.Start();

            // Generate events rapidly
            Console.WriteLine("🚀 Generating rapid events...");
            for (int i = 0; i < 12; i++)
            {
                counterMachine.Send(i % 2 == 0 ? "INCREMENT" : "DECREMENT");
                await Task.Delay(300);
            }

            // Wait for final batch
            await Task.Delay(2500);

            // Clean up
            aggregator.Dispose();
            await notificationService.StopAsync();
            counterMachine.Stop();
        }
    }

    /// <summary>
    /// Program entry point for running the example
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "ResilienceExample")
            {
                await ResilienceExample.RunExample();
            }
            else
            {
                await PubSubExample.RunExample();
            }
        }
    }
}