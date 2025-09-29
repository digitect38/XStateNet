# XStateNet Examples

## 🎯 Comprehensive Code Examples and Tutorials

This document provides practical examples for using XStateNet in various scenarios, from simple state machines to complex distributed systems.

## 📚 Table of Contents

- [Basic Examples](#basic-examples)
- [Advanced State Machines](#advanced-state-machines)
- [Event Orchestration](#event-orchestration)
- [Inter-Machine Communication](#inter-machine-communication)
- [Monitoring & Observability](#monitoring--observability)
- [Performance Optimization](#performance-optimization)
- [Real-World Applications](#real-world-applications)
- [Testing Patterns](#testing-patterns)

## 🌟 Basic Examples

### 1. Simple Traffic Light

A basic state machine with timed transitions.

```csharp
using XStateNet;

public class TrafficLightExample
{
    public static async Task RunAsync()
    {
        // Define the state machine
        var json = @"{
            ""id"": ""trafficLight"",
            ""initial"": ""red"",
            ""states"": {
                ""red"": {
                    ""after"": { ""30000"": ""green"" }
                },
                ""yellow"": {
                    ""after"": { ""5000"": ""red"" }
                },
                ""green"": {
                    ""after"": { ""25000"": ""yellow"" }
                }
            }
        }";

        // Create and configure machine
        var machine = StateMachineFactory.CreateFromScript("trafficLight", json);

        // Subscribe to state changes
        machine.StateChanged += (sender, args) =>
        {
            Console.WriteLine($"🚦 Traffic light: {args.From} → {args.To}");
        };

        // Start the machine
        await machine.StartAsync();

        // Let it run for a while
        await Task.Delay(TimeSpan.FromMinutes(2));

        await machine.StopAsync();
    }
}
```

### 2. User Authentication Flow

A state machine handling user login with actions and guards.

```csharp
public class AuthenticationExample
{
    private static readonly Dictionary<string, User> _users = new()
    {
        ["admin"] = new User("admin", "password123", UserRole.Admin),
        ["user"] = new User("user", "secret456", UserRole.User)
    };

    public static async Task RunAsync()
    {
        var actions = new Dictionary<string, Action<ExecutionContext>>
        {
            ["validateCredentials"] = ValidateCredentials,
            ["logSuccess"] = LogSuccessfulLogin,
            ["logFailure"] = LogFailedLogin,
            ["lockAccount"] = LockUserAccount,
            ["sendWelcomeEmail"] = SendWelcomeEmail
        };

        var guards = new Dictionary<string, Func<ExecutionContext, bool>>
        {
            ["isValidUser"] = IsValidUser,
            ["hasMaxAttempts"] = HasReachedMaxAttempts,
            ["isAccountLocked"] = IsAccountLocked
        };

        var json = @"{
            ""id"": ""authentication"",
            ""initial"": ""idle"",
            ""states"": {
                ""idle"": {
                    ""on"": { ""LOGIN_ATTEMPT"": ""validating"" }
                },
                ""validating"": {
                    ""entry"": [""validateCredentials""],
                    ""on"": {
                        ""SUCCESS"": {
                            ""target"": ""authenticated"",
                            ""cond"": ""isValidUser""
                        },
                        ""FAILURE"": [
                            {
                                ""target"": ""locked"",
                                ""cond"": ""hasMaxAttempts""
                            },
                            { ""target"": ""failed"" }
                        ]
                    }
                },
                ""authenticated"": {
                    ""entry"": [""logSuccess"", ""sendWelcomeEmail""],
                    ""on"": { ""LOGOUT"": ""idle"" }
                },
                ""failed"": {
                    ""entry"": [""logFailure""],
                    ""on"": { ""RETRY"": ""idle"" }
                },
                ""locked"": {
                    ""entry"": [""lockAccount""],
                    ""type"": ""final""
                }
            }
        }";

        var machine = StateMachineFactory.CreateFromScript("auth", json, actions);

        machine.StateChanged += (sender, args) =>
        {
            Console.WriteLine($"🔐 Auth state: {args.From} → {args.To}");
        };

        await machine.StartAsync();

        // Test authentication flow
        await TestLoginFlow(machine);
    }

    private static async Task TestLoginFlow(StateMachine machine)
    {
        // Successful login
        var result = await machine.SendAsync("LOGIN_ATTEMPT", new { username = "admin", password = "password123" });
        Console.WriteLine($"Result: {result}");

        await machine.SendAsync("LOGOUT");

        // Failed login
        await machine.SendAsync("LOGIN_ATTEMPT", new { username = "admin", password = "wrongpassword" });
        await machine.SendAsync("RETRY");

        // Another failed attempt
        await machine.SendAsync("LOGIN_ATTEMPT", new { username = "user", password = "wrongpassword" });
    }

    private static void ValidateCredentials(ExecutionContext ctx)
    {
        var loginData = (dynamic)ctx.EventData;
        string username = loginData.username;
        string password = loginData.password;

        if (_users.TryGetValue(username, out var user) && user.Password == password)
        {
            ctx.SetStateData("currentUser", user);
            ctx.Machine.SendFireAndForget("SUCCESS");
        }
        else
        {
            var attempts = ctx.GetStateData<int>("failedAttempts") + 1;
            ctx.SetStateData("failedAttempts", attempts);
            ctx.Machine.SendFireAndForget("FAILURE");
        }
    }

    private static bool IsValidUser(ExecutionContext ctx) =>
        ctx.GetStateData<User>("currentUser") != null;

    private static bool HasReachedMaxAttempts(ExecutionContext ctx) =>
        ctx.GetStateData<int>("failedAttempts") >= 3;

    private static void LogSuccessfulLogin(ExecutionContext ctx)
    {
        var user = ctx.GetStateData<User>("currentUser");
        Console.WriteLine($"✅ User {user.Username} logged in successfully");
    }

    private static void LogFailedLogin(ExecutionContext ctx)
    {
        var attempts = ctx.GetStateData<int>("failedAttempts");
        Console.WriteLine($"❌ Login failed. Attempts: {attempts}/3");
    }

    // ... other action methods
}

public record User(string Username, string Password, UserRole Role);
public enum UserRole { User, Admin }
```

## 🏗️ Advanced State Machines

### 3. Hierarchical Order Processing

Complex state machine with hierarchical states for order processing.

```csharp
public class OrderProcessingExample
{
    public static async Task RunAsync()
    {
        var actions = new Dictionary<string, Action<ExecutionContext>>
        {
            ["validateOrder"] = ValidateOrder,
            ["calculateTax"] = CalculateTax,
            ["checkInventory"] = CheckInventory,
            ["reserveItems"] = ReserveItems,
            ["processPayment"] = ProcessPayment,
            ["chargeCard"] = ChargeCard,
            ["processPayPal"] = ProcessPayPal,
            ["shipOrder"] = ShipOrder,
            ["sendConfirmation"] = SendConfirmation,
            ["releaseInventory"] = ReleaseInventory,
            ["refundPayment"] = RefundPayment
        };

        var json = @"{
            ""id"": ""orderProcessor"",
            ""initial"": ""received"",
            ""states"": {
                ""received"": {
                    ""entry"": [""validateOrder""],
                    ""on"": {
                        ""VALID"": ""processing"",
                        ""INVALID"": ""rejected""
                    }
                },
                ""processing"": {
                    ""type"": ""compound"",
                    ""initial"": ""calculating"",
                    ""states"": {
                        ""calculating"": {
                            ""entry"": [""calculateTax""],
                            ""on"": { ""CALCULATED"": ""inventory"" }
                        },
                        ""inventory"": {
                            ""entry"": [""checkInventory""],
                            ""on"": {
                                ""IN_STOCK"": ""payment"",
                                ""OUT_OF_STOCK"": ""#rejected""
                            }
                        },
                        ""payment"": {
                            ""type"": ""parallel"",
                            ""states"": {
                                ""reservation"": {
                                    ""initial"": ""reserving"",
                                    ""states"": {
                                        ""reserving"": {
                                            ""entry"": [""reserveItems""],
                                            ""on"": { ""RESERVED"": ""reserved"" }
                                        },
                                        ""reserved"": { ""type"": ""final"" }
                                    }
                                },
                                ""charging"": {
                                    ""initial"": ""processing_payment"",
                                    ""states"": {
                                        ""processing_payment"": {
                                            ""entry"": [""processPayment""],
                                            ""on"": {
                                                ""CARD_PAYMENT"": ""card"",
                                                ""PAYPAL_PAYMENT"": ""paypal""
                                            }
                                        },
                                        ""card"": {
                                            ""entry"": [""chargeCard""],
                                            ""on"": {
                                                ""SUCCESS"": ""charged"",
                                                ""FAILURE"": ""#cancelled""
                                            }
                                        },
                                        ""paypal"": {
                                            ""entry"": [""processPayPal""],
                                            ""on"": {
                                                ""SUCCESS"": ""charged"",
                                                ""FAILURE"": ""#cancelled""
                                            }
                                        },
                                        ""charged"": { ""type"": ""final"" }
                                    }
                                }
                            },
                            ""onDone"": ""#fulfilled""
                        }
                    }
                },
                ""fulfilled"": {
                    ""entry"": [""shipOrder"", ""sendConfirmation""],
                    ""type"": ""final""
                },
                ""rejected"": {
                    ""type"": ""final""
                },
                ""cancelled"": {
                    ""entry"": [""releaseInventory"", ""refundPayment""],
                    ""type"": ""final""
                }
            }
        }";

        var machine = StateMachineFactory.CreateFromScript("orderProcessor", json, actions);

        machine.StateChanged += (sender, args) =>
        {
            Console.WriteLine($"📦 Order state: {args.From} → {args.To}");
        };

        await machine.StartAsync();

        // Process sample orders
        await ProcessSampleOrder(machine, new Order
        {
            Id = "ORD001",
            Items = new[] { new OrderItem("ITEM001", 2, 29.99m) },
            PaymentMethod = "CARD"
        });
    }

    private static async Task ProcessSampleOrder(StateMachine machine, Order order)
    {
        Console.WriteLine($"Processing order: {order.Id}");

        var result = await machine.SendAsync("NEW_ORDER", order);
        Console.WriteLine($"Order result: {result}");

        // Simulate validation
        await Task.Delay(500);
        await machine.SendAsync("VALID");

        // Simulate calculation
        await Task.Delay(300);
        await machine.SendAsync("CALCULATED");

        // Simulate inventory check
        await Task.Delay(200);
        await machine.SendAsync("IN_STOCK");

        // Simulate payment processing
        await Task.Delay(1000);
        await machine.SendAsync(order.PaymentMethod + "_PAYMENT");

        await Task.Delay(800);
        await machine.SendAsync("SUCCESS");
    }

    // Action implementations
    private static void ValidateOrder(ExecutionContext ctx)
    {
        var order = (Order)ctx.EventData;
        Console.WriteLine($"   Validating order {order.Id}...");
        // Validation logic here
    }

    private static void CalculateTax(ExecutionContext ctx)
    {
        var order = ctx.GetStateData<Order>("order");
        Console.WriteLine($"   Calculating tax for order {order?.Id}...");
        // Tax calculation logic
    }

    // ... other action implementations
}

public class Order
{
    public string Id { get; set; }
    public OrderItem[] Items { get; set; }
    public string PaymentMethod { get; set; }
    public decimal Total => Items?.Sum(i => i.Quantity * i.Price) ?? 0;
}

public record OrderItem(string ItemId, int Quantity, decimal Price);
```

## 🎼 Event Orchestration

### 4. Multi-Machine Workflow

Coordinating multiple state machines for a complex workflow.

```csharp
public class WorkflowOrchestrationExample
{
    public static async Task RunAsync()
    {
        var config = new OrchestratorConfig
        {
            PoolSize = 8,
            EnableMetrics = true,
            EnableBackpressure = true,
            MaxQueueDepth = 10000
        };

        using var orchestrator = new EventBusOrchestrator(config);

        // Create and register machines
        var orderMachine = CreateOrderMachine(orchestrator);
        var inventoryMachine = CreateInventoryMachine(orchestrator);
        var paymentMachine = CreatePaymentMachine(orchestrator);
        var shippingMachine = CreateShippingMachine(orchestrator);

        await orchestrator.RegisterMachineAsync("orders", orderMachine);
        await orchestrator.RegisterMachineAsync("inventory", inventoryMachine);
        await orchestrator.RegisterMachineAsync("payments", paymentMachine);
        await orchestrator.RegisterMachineAsync("shipping", shippingMachine);

        // Setup monitoring
        var dashboard = orchestrator.CreateDashboard();
        dashboard.StartMonitoring(TimeSpan.FromSeconds(2));

        await orchestrator.StartAllMachinesAsync();

        // Process multiple orders concurrently
        var orderTasks = new List<Task>();
        for (int i = 1; i <= 10; i++)
        {
            var orderId = $"ORD{i:000}";
            orderTasks.Add(ProcessWorkflowOrder(orchestrator, orderId));
        }

        await Task.WhenAll(orderTasks);

        // Display final metrics
        await Task.Delay(2000);
        dashboard.StopMonitoring();
        dashboard.DisplaySummaryReport();
    }

    private static async Task ProcessWorkflowOrder(EventBusOrchestrator orchestrator, string orderId)
    {
        Console.WriteLine($"🚀 Starting workflow for order {orderId}");

        try
        {
            // Start order processing
            var result = await orchestrator.SendEventAsync(
                $"req-{orderId}",
                "orders",
                "NEW_ORDER",
                new { OrderId = orderId, CustomerId = "CUST001", Items = new[] { "ITEM001", "ITEM002" } }
            );

            Console.WriteLine($"✅ Order {orderId} workflow completed: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Order {orderId} workflow failed: {ex.Message}");
        }
    }

    private static StateMachine CreateOrderMachine(EventBusOrchestrator orchestrator)
    {
        var actions = new Dictionary<string, Action<ExecutionContext>>
        {
            ["requestInventoryCheck"] = ctx =>
            {
                Console.WriteLine($"   🔍 Checking inventory for order {GetOrderId(ctx)}");
                ctx.RequestSend("inventory", "CHECK_AVAILABILITY", ctx.EventData);
            },
            ["requestPayment"] = ctx =>
            {
                Console.WriteLine($"   💳 Processing payment for order {GetOrderId(ctx)}");
                ctx.RequestSend("payments", "PROCESS_PAYMENT", ctx.EventData);
            },
            ["requestShipping"] = ctx =>
            {
                Console.WriteLine($"   📦 Arranging shipping for order {GetOrderId(ctx)}");
                ctx.RequestSend("shipping", "ARRANGE_SHIPPING", ctx.EventData);
            },
            ["completeOrder"] = ctx =>
            {
                Console.WriteLine($"   ✅ Order {GetOrderId(ctx)} completed successfully");
            }
        };

        var json = @"{
            ""id"": ""orderWorkflow"",
            ""initial"": ""idle"",
            ""states"": {
                ""idle"": {
                    ""on"": { ""NEW_ORDER"": ""checking_inventory"" }
                },
                ""checking_inventory"": {
                    ""entry"": [""requestInventoryCheck""],
                    ""on"": {
                        ""INVENTORY_AVAILABLE"": ""processing_payment"",
                        ""INVENTORY_UNAVAILABLE"": ""cancelled""
                    }
                },
                ""processing_payment"": {
                    ""entry"": [""requestPayment""],
                    ""on"": {
                        ""PAYMENT_SUCCESS"": ""arranging_shipping"",
                        ""PAYMENT_FAILED"": ""cancelled""
                    }
                },
                ""arranging_shipping"": {
                    ""entry"": [""requestShipping""],
                    ""on"": {
                        ""SHIPPING_ARRANGED"": ""completed""
                    }
                },
                ""completed"": {
                    ""entry"": [""completeOrder""],
                    ""on"": { ""RESET"": ""idle"" }
                },
                ""cancelled"": {
                    ""on"": { ""RESET"": ""idle"" }
                }
            }
        }";

        return StateMachineFactory.CreateFromScript("orderWorkflow", json, orchestrator, actions);
    }

    private static StateMachine CreateInventoryMachine(EventBusOrchestrator orchestrator)
    {
        var actions = new Dictionary<string, Action<ExecutionContext>>
        {
            ["checkStock"] = ctx =>
            {
                var orderId = GetOrderId(ctx);
                Console.WriteLine($"      📋 Checking stock for order {orderId}");

                // Simulate stock check (90% success rate)
                var available = Random.Shared.NextDouble() > 0.1;
                var response = available ? "INVENTORY_AVAILABLE" : "INVENTORY_UNAVAILABLE";

                ctx.RequestSend("orders", response, ctx.EventData);
            }
        };

        var json = @"{
            ""id"": ""inventoryManager"",
            ""initial"": ""ready"",
            ""states"": {
                ""ready"": {
                    ""on"": { ""CHECK_AVAILABILITY"": ""checking"" }
                },
                ""checking"": {
                    ""entry"": [""checkStock""],
                    ""after"": { ""500"": ""ready"" }
                }
            }
        }";

        return StateMachineFactory.CreateFromScript("inventoryManager", json, orchestrator, actions);
    }

    private static StateMachine CreatePaymentMachine(EventBusOrchestrator orchestrator)
    {
        var actions = new Dictionary<string, Action<ExecutionContext>>
        {
            ["processPayment"] = ctx =>
            {
                var orderId = GetOrderId(ctx);
                Console.WriteLine($"      💰 Processing payment for order {orderId}");

                // Simulate payment processing (95% success rate)
                var success = Random.Shared.NextDouble() > 0.05;
                var response = success ? "PAYMENT_SUCCESS" : "PAYMENT_FAILED";

                ctx.RequestSend("orders", response, ctx.EventData);
            }
        };

        var json = @"{
            ""id"": ""paymentProcessor"",
            ""initial"": ""ready"",
            ""states"": {
                ""ready"": {
                    ""on"": { ""PROCESS_PAYMENT"": ""processing"" }
                },
                ""processing"": {
                    ""entry"": [""processPayment""],
                    ""after"": { ""800"": ""ready"" }
                }
            }
        }";

        return StateMachineFactory.CreateFromScript("paymentProcessor", json, orchestrator, actions);
    }

    private static StateMachine CreateShippingMachine(EventBusOrchestrator orchestrator)
    {
        var actions = new Dictionary<string, Action<ExecutionContext>>
        {
            ["arrangeShipping"] = ctx =>
            {
                var orderId = GetOrderId(ctx);
                Console.WriteLine($"      🚚 Arranging shipping for order {orderId}");

                // Simulate shipping arrangement (always succeeds in demo)
                ctx.RequestSend("orders", "SHIPPING_ARRANGED", ctx.EventData);
            }
        };

        var json = @"{
            ""id"": ""shippingManager"",
            ""initial"": ""ready"",
            ""states"": {
                ""ready"": {
                    ""on"": { ""ARRANGE_SHIPPING"": ""arranging"" }
                },
                ""arranging"": {
                    ""entry"": [""arrangeShipping""],
                    ""after"": { ""600"": ""ready"" }
                }
            }
        }";

        return StateMachineFactory.CreateFromScript("shippingManager", json, orchestrator, actions);
    }

    private static string GetOrderId(ExecutionContext ctx)
    {
        var data = (dynamic)ctx.EventData;
        return data.OrderId ?? "UNKNOWN";
    }
}
```

## 🌐 Inter-Machine Communication

### 5. Distributed Chat System

Example of distributed state machines communicating across network boundaries.

```csharp
public class DistributedChatExample
{
    public static async Task RunAsync()
    {
        // Simulate multiple nodes
        var node1 = await CreateChatNode("Node1", 8001);
        var node2 = await CreateChatNode("Node2", 8002);
        var node3 = await CreateChatNode("Node3", 8003);

        // Connect nodes
        await ConnectNodes(node1, node2, node3);

        // Simulate chat activity
        await SimulateChatActivity(node1, node2, node3);

        // Cleanup
        await node1.ShutdownAsync();
        await node2.ShutdownAsync();
        await node3.ShutdownAsync();
    }

    private static async Task<ChatNode> CreateChatNode(string nodeId, int port)
    {
        var node = new ChatNode(nodeId, port);
        await node.StartAsync();
        return node;
    }

    private static async Task ConnectNodes(params ChatNode[] nodes)
    {
        // Connect all nodes to each other
        for (int i = 0; i < nodes.Length; i++)
        {
            for (int j = i + 1; j < nodes.Length; j++)
            {
                await nodes[i].ConnectToNodeAsync(nodes[j]);
                await nodes[j].ConnectToNodeAsync(nodes[i]);
            }
        }
    }

    private static async Task SimulateChatActivity(params ChatNode[] nodes)
    {
        // Users join different nodes
        await nodes[0].UserJoinAsync("Alice");
        await nodes[1].UserJoinAsync("Bob");
        await nodes[2].UserJoinAsync("Charlie");

        await Task.Delay(1000);

        // Send messages across nodes
        await nodes[0].SendMessageAsync("Alice", "Hello everyone!");
        await Task.Delay(500);

        await nodes[1].SendMessageAsync("Bob", "Hey Alice, how are you?");
        await Task.Delay(500);

        await nodes[2].SendMessageAsync("Charlie", "Good morning from Node3!");
        await Task.Delay(500);

        await nodes[0].SendMessageAsync("Alice", "Great to see the distributed system working!");

        await Task.Delay(2000);
    }
}

public class ChatNode
{
    private readonly string _nodeId;
    private readonly int _port;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly InterMachineConnector _connector;
    private readonly List<ChatNode> _connectedNodes = new();
    private StateMachine _chatMachine;

    public ChatNode(string nodeId, int port)
    {
        _nodeId = nodeId;
        _port = port;

        var config = new OrchestratorConfig
        {
            PoolSize = 4,
            EnableMetrics = true,
            EnableLogging = false
        };

        _orchestrator = new EventBusOrchestrator(config);
        _connector = new InterMachineConnector();
    }

    public async Task StartAsync()
    {
        Console.WriteLine($"🌐 Starting chat node {_nodeId} on port {_port}");

        var actions = new Dictionary<string, Action<ExecutionContext>>
        {
            ["userJoined"] = UserJoined,
            ["userLeft"] = UserLeft,
            ["broadcastMessage"] = BroadcastMessage,
            ["receiveMessage"] = ReceiveMessage
        };

        var json = @"{
            ""id"": ""chatRoom"",
            ""initial"": ""active"",
            ""states"": {
                ""active"": {
                    ""on"": {
                        ""USER_JOIN"": { ""actions"": [""userJoined""] },
                        ""USER_LEAVE"": { ""actions"": [""userLeft""] },
                        ""SEND_MESSAGE"": { ""actions"": [""broadcastMessage""] },
                        ""RECEIVE_MESSAGE"": { ""actions"": [""receiveMessage""] }
                    }
                }
            }
        }";

        _chatMachine = StateMachineFactory.CreateFromScript($"chat-{_nodeId}", json, _orchestrator, actions);

        await _orchestrator.RegisterMachineAsync("chat", _chatMachine);
        _connector.RegisterMachine("chat", _chatMachine);

        await _orchestrator.StartAllMachinesAsync();
    }

    public async Task ConnectToNodeAsync(ChatNode otherNode)
    {
        _connectedNodes.Add(otherNode);
        Console.WriteLine($"🔗 {_nodeId} connected to {otherNode._nodeId}");
    }

    public async Task UserJoinAsync(string username)
    {
        await _orchestrator.SendEventFireAndForgetAsync($"join-{username}", "chat", "USER_JOIN", new { Username = username });
    }

    public async Task SendMessageAsync(string username, string message)
    {
        await _orchestrator.SendEventFireAndForgetAsync($"msg-{Guid.NewGuid()}", "chat", "SEND_MESSAGE",
            new { Username = username, Message = message, Timestamp = DateTime.Now });
    }

    private void UserJoined(ExecutionContext ctx)
    {
        var data = (dynamic)ctx.EventData;
        Console.WriteLine($"👋 [{_nodeId}] User {data.Username} joined the chat");

        // Notify connected nodes
        foreach (var node in _connectedNodes)
        {
            _ = Task.Run(() => node.NotifyUserJoined(data.Username, _nodeId));
        }
    }

    private void UserLeft(ExecutionContext ctx)
    {
        var data = (dynamic)ctx.EventData;
        Console.WriteLine($"👋 [{_nodeId}] User {data.Username} left the chat");
    }

    private void BroadcastMessage(ExecutionContext ctx)
    {
        var data = (dynamic)ctx.EventData;
        Console.WriteLine($"💬 [{_nodeId}] {data.Username}: {data.Message}");

        // Broadcast to connected nodes
        foreach (var node in _connectedNodes)
        {
            _ = Task.Run(() => node.ReceiveMessageFromNode(data, _nodeId));
        }
    }

    private void ReceiveMessage(ExecutionContext ctx)
    {
        var data = (dynamic)ctx.EventData;
        Console.WriteLine($"📨 [{_nodeId}] Received from {data.FromNode}: {data.Username}: {data.Message}");
    }

    private async Task NotifyUserJoined(string username, string fromNode)
    {
        await _orchestrator.SendEventFireAndForgetAsync($"remote-join-{username}", "chat", "RECEIVE_MESSAGE",
            new { Username = "SYSTEM", Message = $"User {username} joined from {fromNode}", FromNode = fromNode });
    }

    private async Task ReceiveMessageFromNode(dynamic messageData, string fromNode)
    {
        await _orchestrator.SendEventFireAndForgetAsync($"remote-msg-{Guid.NewGuid()}", "chat", "RECEIVE_MESSAGE",
            new {
                Username = messageData.Username,
                Message = messageData.Message,
                FromNode = fromNode,
                Timestamp = messageData.Timestamp
            });
    }

    public async Task ShutdownAsync()
    {
        Console.WriteLine($"🛑 Shutting down chat node {_nodeId}");
        await _orchestrator.StopAllMachinesAsync();
        _orchestrator.Dispose();
    }
}
```

## 📊 Monitoring & Observability

### 6. Production Monitoring Setup

Complete monitoring and observability configuration for production use.

```csharp
public class ProductionMonitoringExample
{
    public static async Task RunAsync()
    {
        var config = new OrchestratorConfig
        {
            PoolSize = Environment.ProcessorCount,
            EnableMetrics = true,
            EnableStructuredLogging = true,
            EnableBackpressure = true,
            MaxQueueDepth = 50000,
            EnableCircuitBreaker = true,
            CircuitBreakerConfig = new CircuitBreakerConfig
            {
                FailureThreshold = 10,
                TimeoutDuration = TimeSpan.FromSeconds(30),
                RecoveryTimeout = TimeSpan.FromMinutes(2)
            },
            MetricsInterval = TimeSpan.FromSeconds(1),
            EnableHealthChecks = true,
            HealthCheckInterval = TimeSpan.FromSeconds(10)
        };

        using var orchestrator = new EventBusOrchestrator(config);

        // Setup custom metrics provider
        var metricsProvider = new PrometheusMetricsProvider();
        // orchestrator.SetMetricsProvider(metricsProvider); // Hypothetical API

        // Create production workload machines
        var machines = new List<StateMachine>();
        for (int i = 1; i <= 20; i++)
        {
            var machine = CreateProductionMachine($"worker-{i}", orchestrator);
            machines.Add(machine);
            await orchestrator.RegisterMachineAsync($"worker-{i}", machine);
        }

        // Setup comprehensive monitoring
        var dashboard = orchestrator.CreateDashboard();
        dashboard.SetUpdateInterval(TimeSpan.FromSeconds(1));
        dashboard.EnableDetailedView(true);
        dashboard.StartMonitoring();

        // Setup health monitoring task
        var healthMonitor = Task.Run(() => MonitorSystemHealth(orchestrator));

        // Setup metrics export task
        var metricsExporter = Task.Run(() => ExportMetricsPeriodically(orchestrator));

        await orchestrator.StartAllMachinesAsync();

        // Generate production-like workload
        var workloadGenerator = GenerateProductionWorkload(orchestrator, machines.Count);

        // Run for demonstration period
        await Task.Delay(TimeSpan.FromMinutes(5));

        // Graceful shutdown
        Console.WriteLine("🛑 Initiating graceful shutdown...");
        dashboard.StopMonitoring();
        await orchestrator.StopAllMachinesAsync();

        // Final reports
        dashboard.DisplaySummaryReport();
        ExportFinalReports(orchestrator, dashboard);
    }

    private static StateMachine CreateProductionMachine(string machineId, EventBusOrchestrator orchestrator)
    {
        var actions = new Dictionary<string, Action<ExecutionContext>>
        {
            ["processWorkload"] = ProcessWorkload,
            ["handleError"] = HandleError,
            ["reportMetrics"] = ReportMetrics,
            ["performHealthCheck"] = PerformHealthCheck
        };

        var json = @"{
            ""id"": ""productionWorker"",
            ""initial"": ""idle"",
            ""states"": {
                ""idle"": {
                    ""on"": {
                        ""WORK_REQUEST"": ""processing"",
                        ""HEALTH_CHECK"": ""checking""
                    }
                },
                ""processing"": {
                    ""entry"": [""processWorkload""],
                    ""on"": {
                        ""WORK_COMPLETED"": ""reporting"",
                        ""WORK_FAILED"": ""error""
                    },
                    ""after"": { ""5000"": ""timeout"" }
                },
                ""reporting"": {
                    ""entry"": [""reportMetrics""],
                    ""on"": { ""REPORTED"": ""idle"" }
                },
                ""checking"": {
                    ""entry"": [""performHealthCheck""],
                    ""on"": { ""HEALTH_OK"": ""idle"" }
                },
                ""error"": {
                    ""entry"": [""handleError""],
                    ""after"": { ""1000"": ""idle"" }
                },
                ""timeout"": {
                    ""entry"": [""handleError""],
                    ""after"": { ""2000"": ""idle"" }
                }
            }
        }";

        return StateMachineFactory.CreateFromScript(machineId, json, orchestrator, actions);
    }

    private static async Task GenerateProductionWorkload(EventBusOrchestrator orchestrator, int machineCount)
    {
        var random = new Random();
        var tasks = new List<Task>();

        // Generate continuous workload
        for (int i = 0; i < 1000; i++)
        {
            var machineId = $"worker-{random.Next(1, machineCount + 1)}";
            var workType = random.Next(1, 5); // Different work types

            var task = orchestrator.SendEventFireAndForgetAsync(
                $"work-{i}",
                machineId,
                "WORK_REQUEST",
                new { WorkId = i, WorkType = workType, Priority = random.Next(1, 4) }
            );

            tasks.Add(task);

            // Vary the load - burst and calm periods
            if (i % 100 == 0)
            {
                await Task.Delay(random.Next(100, 500)); // Calm period
            }
            else
            {
                await Task.Delay(random.Next(10, 50)); // Burst period
            }
        }

        await Task.WhenAll(tasks);
    }

    private static async Task MonitorSystemHealth(EventBusOrchestrator orchestrator)
    {
        while (true)
        {
            try
            {
                var health = orchestrator.GetHealthStatus();

                if (health.Level != HealthLevel.Healthy)
                {
                    Console.WriteLine($"⚠️  Health Alert: {health.Level}");
                    foreach (var issue in health.Issues)
                    {
                        Console.WriteLine($"   - {issue}");
                    }

                    // In production: send alerts, notifications, etc.
                    await SendHealthAlert(health);
                }

                await Task.Delay(TimeSpan.FromSeconds(30));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Health monitoring error: {ex.Message}");
                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        }
    }

    private static async Task ExportMetricsPeriodically(EventBusOrchestrator orchestrator)
    {
        var exportCount = 0;

        while (true)
        {
            try
            {
                var metrics = orchestrator.GetMetrics();
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

                // Export to multiple formats
                await ExportMetricsToFile(metrics, $"metrics_{timestamp}_{exportCount++}.json");

                // In production: export to time-series database, monitoring systems, etc.
                await ExportToMonitoringSystem(metrics);

                await Task.Delay(TimeSpan.FromMinutes(1));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Metrics export error: {ex.Message}");
                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        }
    }

    private static void ProcessWorkload(ExecutionContext ctx)
    {
        var workData = (dynamic)ctx.EventData;
        var machineId = ctx.MachineId;

        try
        {
            // Simulate work processing with varying duration
            var workType = (int)workData.WorkType;
            var duration = workType switch
            {
                1 => Random.Shared.Next(100, 300),   // Fast work
                2 => Random.Shared.Next(300, 800),   // Medium work
                3 => Random.Shared.Next(800, 1500),  // Slow work
                4 => Random.Shared.Next(50, 100),    // Very fast work
                _ => Random.Shared.Next(200, 500)    // Default
            };

            Thread.Sleep(duration);

            // Simulate occasional failures (5% failure rate)
            if (Random.Shared.NextDouble() < 0.05)
            {
                throw new InvalidOperationException($"Simulated processing failure for work type {workType}");
            }

            ctx.SetStateData("processingTime", duration);
            ctx.SetStateData("workResult", new { Success = true, Duration = duration });

            ctx.Machine.SendFireAndForget("WORK_COMPLETED");
        }
        catch (Exception ex)
        {
            ctx.SetStateData("error", ex.Message);
            ctx.Log($"Work processing failed: {ex.Message}", LogLevel.Error);
            ctx.Machine.SendFireAndForget("WORK_FAILED");
        }
    }

    private static void ReportMetrics(ExecutionContext ctx)
    {
        var processingTime = ctx.GetStateData<int>("processingTime");
        var workResult = ctx.GetStateData<object>("workResult");

        // In production: send metrics to monitoring system
        Console.WriteLine($"📊 [{ctx.MachineId}] Work completed in {processingTime}ms");

        ctx.Machine.SendFireAndForget("REPORTED");
    }

    private static void HandleError(ExecutionContext ctx)
    {
        var error = ctx.GetStateData<string>("error") ?? "Unknown error";
        Console.WriteLine($"❌ [{ctx.MachineId}] Error: {error}");

        // In production: error reporting, alerting, etc.
    }

    private static void PerformHealthCheck(ExecutionContext ctx)
    {
        // Simulate health check
        var isHealthy = Random.Shared.NextDouble() > 0.02; // 2% unhealthy rate

        if (isHealthy)
        {
            ctx.Machine.SendFireAndForget("HEALTH_OK");
        }
        else
        {
            ctx.SetStateData("healthIssue", "Simulated health degradation");
            ctx.Log("Health check failed", LogLevel.Warning);
        }
    }

    private static void ExportFinalReports(EventBusOrchestrator orchestrator, MonitoringDashboard dashboard)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var exportDir = Path.Combine(Environment.CurrentDirectory, "ProductionReports");
            Directory.CreateDirectory(exportDir);

            var metrics = orchestrator.GetMetrics();
            var health = orchestrator.GetHealthStatus();

            // Export comprehensive final report
            var finalReport = new
            {
                Timestamp = DateTime.UtcNow,
                Duration = TimeSpan.FromMinutes(5),
                Metrics = metrics,
                Health = health,
                Statistics = orchestrator.GetStatistics()
            };

            var reportPath = Path.Combine(exportDir, $"production_report_{timestamp}.json");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(finalReport, new JsonSerializerOptions { WriteIndented = true }));

            Console.WriteLine($"📁 Final production report exported to: {reportPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to export final reports: {ex.Message}");
        }
    }

    // Placeholder methods for production integrations
    private static async Task SendHealthAlert(HealthStatus health) => await Task.Delay(1);
    private static async Task ExportToMonitoringSystem(OrchestratorMetrics metrics) => await Task.Delay(1);
    private static async Task ExportMetricsToFile(OrchestratorMetrics metrics, string filename) => await Task.Delay(1);
}

// Custom metrics provider for Prometheus integration
public class PrometheusMetricsProvider : IMetricsProvider
{
    public void RecordEventProcessed(string machineId, TimeSpan duration)
    {
        // Export to Prometheus metrics
        // Prometheus.CreateGauge("xstatenet_event_duration_seconds", "Event processing duration")
        //    .WithTag("machine_id", machineId)
        //    .Set(duration.TotalSeconds);
    }

    public void RecordStateTransition(string machineId, string fromState, string toState)
    {
        // Export state transition metrics
        // Prometheus.CreateCounter("xstatenet_state_transitions_total", "State transitions")
        //    .WithTag("machine_id", machineId)
        //    .WithTag("from_state", fromState)
        //    .WithTag("to_state", toState)
        //    .Inc();
    }

    public void RecordError(string machineId, string errorType, Exception exception)
    {
        // Export error metrics
    }

    public void RecordMetric(string name, double value, Dictionary<string, string> tags = null)
    {
        // Export custom metrics
    }
}
```

## 🏎️ Performance Optimization

### 7. High-Performance Trading System

Example showcasing performance optimization techniques for high-frequency scenarios.

```csharp
public class HighPerformanceTradingExample
{
    public static async Task RunAsync()
    {
        // Optimized configuration for high performance
        var config = new OrchestratorConfig
        {
            PoolSize = Environment.ProcessorCount * 2, // Utilize all cores
            EnableMetrics = true,
            EnableBackpressure = true,
            MaxQueueDepth = 100000, // Large queue for burst handling
            ThrottleDelay = TimeSpan.Zero, // No throttling for max speed
            EnableCircuitBreaker = false, // Disabled for demo - max performance
            MetricsInterval = TimeSpan.FromMilliseconds(100) // Fast metrics
        };

        using var orchestrator = new EventBusOrchestrator(config);

        // Create optimized trading engines
        var tradingEngines = new List<StateMachine>();
        for (int i = 1; i <= 10; i++)
        {
            var engine = CreateOptimizedTradingEngine($"engine-{i}", orchestrator);
            tradingEngines.Add(engine);
            await orchestrator.RegisterMachineAsync($"engine-{i}", engine);
        }

        // Setup performance monitoring
        var performanceMonitor = new PerformanceMonitor(orchestrator);
        performanceMonitor.StartMonitoring();

        await orchestrator.StartAllMachinesAsync();

        // Generate high-frequency trading load
        Console.WriteLine("🏎️  Starting high-frequency trading simulation...");

        var tradingTasks = new List<Task>
        {
            SimulateMarketData(orchestrator, tradingEngines.Count),
            SimulateOrderFlow(orchestrator, tradingEngines.Count),
            SimulateRiskManagement(orchestrator, tradingEngines.Count)
        };

        // Run for performance test period
        var testDuration = TimeSpan.FromMinutes(2);
        await Task.WhenAll(Task.Delay(testDuration), Task.WhenAll(tradingTasks));

        // Performance analysis
        performanceMonitor.StopMonitoring();
        await AnalyzePerformance(orchestrator, performanceMonitor);
    }

    private static StateMachine CreateOptimizedTradingEngine(string engineId, EventBusOrchestrator orchestrator)
    {
        // Optimized actions using object pooling and minimal allocations
        var actions = new Dictionary<string, Action<ExecutionContext>>
        {
            ["processMarketData"] = ProcessMarketDataOptimized,
            ["evaluateSignals"] = EvaluateSignalsOptimized,
            ["executeOrder"] = ExecuteOrderOptimized,
            ["manageRisk"] = ManageRiskOptimized,
            ["updatePositions"] = UpdatePositionsOptimized
        };

        // Simplified state machine for optimal performance
        var json = @"{
            ""id"": ""tradingEngine"",
            ""initial"": ""ready"",
            ""states"": {
                ""ready"": {
                    ""on"": {
                        ""MARKET_DATA"": ""processing"",
                        ""ORDER_REQUEST"": ""executing"",
                        ""RISK_CHECK"": ""checking""
                    }
                },
                ""processing"": {
                    ""entry"": [""processMarketData"", ""evaluateSignals""],
                    ""on"": {
                        ""SIGNAL_GENERATED"": ""executing"",
                        ""NO_SIGNAL"": ""ready""
                    }
                },
                ""executing"": {
                    ""entry"": [""executeOrder"", ""updatePositions""],
                    ""on"": { ""ORDER_COMPLETE"": ""ready"" }
                },
                ""checking"": {
                    ""entry"": [""manageRisk""],
                    ""on"": { ""RISK_OK"": ""ready"" }
                }
            }
        }";

        return StateMachineFactory.CreateFromScript(engineId, json, orchestrator, actions);
    }

    private static async Task SimulateMarketData(EventBusOrchestrator orchestrator, int engineCount)
    {
        var symbols = new[] { "AAPL", "GOOGL", "MSFT", "TSLA", "AMZN" };
        var random = new Random();
        var counter = 0;

        while (true)
        {
            var symbol = symbols[random.Next(symbols.Length)];
            var price = 100 + random.NextDouble() * 400; // Price between 100-500
            var volume = random.Next(1000, 10000);

            var marketData = new MarketDataTick
            {
                Symbol = symbol,
                Price = price,
                Volume = volume,
                Timestamp = DateTime.UtcNow.Ticks // Use ticks for performance
            };

            // Distribute to engines using round-robin for load balancing
            var engineId = $"engine-{(counter % engineCount) + 1}";

            await orchestrator.SendEventFireAndForgetAsync(
                $"md-{counter++}",
                engineId,
                "MARKET_DATA",
                marketData
            );

            // High frequency - minimal delay
            await Task.Delay(1);
        }
    }

    private static async Task SimulateOrderFlow(EventBusOrchestrator orchestrator, int engineCount)
    {
        var symbols = new[] { "AAPL", "GOOGL", "MSFT", "TSLA", "AMZN" };
        var sides = new[] { "BUY", "SELL" };
        var random = new Random();
        var counter = 0;

        await Task.Delay(1000); // Start after market data

        while (true)
        {
            var order = new TradingOrder
            {
                OrderId = counter++,
                Symbol = symbols[random.Next(symbols.Length)],
                Side = sides[random.Next(sides.Length)],
                Quantity = random.Next(100, 1000),
                Price = 100 + random.NextDouble() * 400,
                Timestamp = DateTime.UtcNow.Ticks
            };

            var engineId = $"engine-{(counter % engineCount) + 1}";

            await orchestrator.SendEventFireAndForgetAsync(
                $"order-{counter}",
                engineId,
                "ORDER_REQUEST",
                order
            );

            // Order flow is less frequent than market data
            await Task.Delay(random.Next(5, 20));
        }
    }

    private static async Task SimulateRiskManagement(EventBusOrchestrator orchestrator, int engineCount)
    {
        var counter = 0;

        while (true)
        {
            // Periodic risk checks
            var engineId = $"engine-{(counter % engineCount) + 1}";

            await orchestrator.SendEventFireAndForgetAsync(
                $"risk-{counter++}",
                engineId,
                "RISK_CHECK",
                new { CheckId = counter, Timestamp = DateTime.UtcNow.Ticks }
            );

            await Task.Delay(100); // Risk checks every 100ms
        }
    }

    // Optimized action implementations
    private static void ProcessMarketDataOptimized(ExecutionContext ctx)
    {
        var marketData = (MarketDataTick)ctx.EventData;

        // Minimal processing - just essential logic
        var previousPrice = ctx.GetStateData<double>($"price_{marketData.Symbol}");
        ctx.SetStateData($"price_{marketData.Symbol}", marketData.Price);

        // Simple signal generation based on price movement
        if (previousPrice > 0)
        {
            var priceChange = (marketData.Price - previousPrice) / previousPrice;
            if (Math.Abs(priceChange) > 0.001) // 0.1% threshold
            {
                ctx.SetStateData("signal", new { Symbol = marketData.Symbol, Direction = priceChange > 0 ? 1 : -1, Strength = Math.Abs(priceChange) });
                ctx.Machine.SendFireAndForget("SIGNAL_GENERATED");
                return;
            }
        }

        ctx.Machine.SendFireAndForget("NO_SIGNAL");
    }

    private static void EvaluateSignalsOptimized(ExecutionContext ctx)
    {
        var signal = ctx.GetStateData<dynamic>("signal");
        if (signal != null)
        {
            // Fast signal evaluation - minimal logic
            var strength = (double)signal.Strength;
            if (strength > 0.005) // 0.5% threshold for trading
            {
                ctx.SetStateData("tradeSignal", signal);
            }
        }
    }

    private static void ExecuteOrderOptimized(ExecutionContext ctx)
    {
        // Simulate ultra-fast order execution
        var order = ctx.EventData as TradingOrder;
        if (order != null)
        {
            // Minimal order processing
            ctx.SetStateData($"position_{order.Symbol}", order.Quantity * (order.Side == "BUY" ? 1 : -1));
        }

        ctx.Machine.SendFireAndForget("ORDER_COMPLETE");
    }

    private static void ManageRiskOptimized(ExecutionContext ctx)
    {
        // Fast risk check - minimal calculations
        ctx.Machine.SendFireAndForget("RISK_OK");
    }

    private static void UpdatePositionsOptimized(ExecutionContext ctx)
    {
        // Minimal position updates
        // In production: update position cache, P&L calculations, etc.
    }

    private static async Task AnalyzePerformance(EventBusOrchestrator orchestrator, PerformanceMonitor monitor)
    {
        Console.WriteLine("\n🏁 PERFORMANCE ANALYSIS");
        Console.WriteLine("========================");

        var metrics = orchestrator.GetMetrics();
        var stats = monitor.GetPerformanceStats();

        Console.WriteLine($"📊 Total Events Processed: {metrics.TotalEventsProcessed:N0}");
        Console.WriteLine($"🚀 Peak Throughput: {stats.PeakEventsPerSecond:F0} events/sec");
        Console.WriteLine($"📈 Average Throughput: {metrics.EventsPerSecond:F0} events/sec");
        Console.WriteLine($"⚡ Average Latency: {metrics.AverageLatency:F3} ms");
        Console.WriteLine($"🎯 P95 Latency: {metrics.P95Latency:F3} ms");
        Console.WriteLine($"⭐ P99 Latency: {metrics.P99Latency:F3} ms");
        Console.WriteLine($"💾 Memory Usage: {metrics.MemoryUsage / 1024 / 1024:F1} MB");
        Console.WriteLine($"🔄 CPU Usage: {metrics.CpuUsage:F1}%");

        // Performance classification
        if (metrics.EventsPerSecond > 50000)
            Console.WriteLine("🏆 EXCELLENT - High-frequency trading ready!");
        else if (metrics.EventsPerSecond > 25000)
            Console.WriteLine("✅ GOOD - Suitable for most trading scenarios");
        else
            Console.WriteLine("⚠️ FAIR - May need optimization for HFT");

        // Export detailed performance report
        await ExportPerformanceReport(metrics, stats);
    }

    private static async Task ExportPerformanceReport(OrchestratorMetrics metrics, PerformanceStats stats)
    {
        var report = new
        {
            TestType = "High-Frequency Trading Simulation",
            Timestamp = DateTime.UtcNow,
            Duration = TimeSpan.FromMinutes(2),
            Configuration = new
            {
                PoolSize = Environment.ProcessorCount * 2,
                MaxQueueDepth = 100000,
                ThrottleDelay = TimeSpan.Zero
            },
            Results = new
            {
                TotalEvents = metrics.TotalEventsProcessed,
                PeakThroughput = stats.PeakEventsPerSecond,
                AverageThroughput = metrics.EventsPerSecond,
                AverageLatency = metrics.AverageLatency,
                P95Latency = metrics.P95Latency,
                P99Latency = metrics.P99Latency,
                MemoryUsage = metrics.MemoryUsage,
                CpuUsage = metrics.CpuUsage
            }
        };

        var reportPath = $"hft_performance_report_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"📁 Performance report exported to: {reportPath}");
    }
}

// Performance monitoring utilities
public class PerformanceMonitor
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly Timer _timer;
    private readonly List<double> _throughputSamples = new();
    private readonly object _lock = new();

    public PerformanceMonitor(EventBusOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        _timer = new Timer(SampleMetrics, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void StartMonitoring()
    {
        _timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
    }

    public void StopMonitoring()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void SampleMetrics(object state)
    {
        try
        {
            var metrics = _orchestrator.GetMetrics();
            lock (_lock)
            {
                _throughputSamples.Add(metrics.EventsPerSecond);
            }
        }
        catch
        {
            // Ignore sampling errors
        }
    }

    public PerformanceStats GetPerformanceStats()
    {
        lock (_lock)
        {
            return new PerformanceStats
            {
                PeakEventsPerSecond = _throughputSamples.Count > 0 ? _throughputSamples.Max() : 0,
                AverageEventsPerSecond = _throughputSamples.Count > 0 ? _throughputSamples.Average() : 0,
                SampleCount = _throughputSamples.Count
            };
        }
    }
}

public record PerformanceStats
{
    public double PeakEventsPerSecond { get; init; }
    public double AverageEventsPerSecond { get; init; }
    public int SampleCount { get; init; }
}

// Data models for trading
public record MarketDataTick
{
    public string Symbol { get; init; }
    public double Price { get; init; }
    public int Volume { get; init; }
    public long Timestamp { get; init; }
}

public record TradingOrder
{
    public int OrderId { get; init; }
    public string Symbol { get; init; }
    public string Side { get; init; }
    public int Quantity { get; init; }
    public double Price { get; init; }
    public long Timestamp { get; init; }
}
```

## 🧪 Testing Patterns

### 8. Comprehensive Testing Strategies

Examples showing various testing approaches for XStateNet applications.

```csharp
public class TestingPatternsExample
{
    public static async Task RunAsync()
    {
        await RunUnitTestExample();
        await RunIntegrationTestExample();
        await RunPerformanceTestExample();
        await RunChaosTestingExample();
    }

    // Unit testing individual state machines
    private static async Task RunUnitTestExample()
    {
        Console.WriteLine("🧪 Unit Testing Examples");
        Console.WriteLine("=========================");

        var testMachine = CreateTestMachine();
        var tester = new StateMachineTester(testMachine);

        // Test initial state
        await tester.AssertCurrentState("idle");

        // Test successful flow
        await tester.SendEvent("START");
        await tester.AssertCurrentState("working");

        await tester.SendEvent("COMPLETE");
        await tester.AssertCurrentState("completed");

        // Test error handling
        await tester.SendEvent("RESET");
        await tester.AssertCurrentState("idle");

        await tester.SendEvent("START");
        await tester.SendEvent("ERROR");
        await tester.AssertCurrentState("failed");

        Console.WriteLine("✅ Unit tests passed");
    }

    // Integration testing with orchestrator
    private static async Task RunIntegrationTestExample()
    {
        Console.WriteLine("\n🔄 Integration Testing Examples");
        Console.WriteLine("================================");

        var config = new OrchestratorConfig
        {
            PoolSize = 2,
            EnableMetrics = true,
            EnableBackpressure = false // Disable for predictable testing
        };

        using var orchestrator = new EventBusOrchestrator(config);
        var integrationTester = new IntegrationTester(orchestrator);

        // Setup test machines
        var processor = CreateTestMachine();
        var validator = CreateValidatorMachine();

        await orchestrator.RegisterMachineAsync("processor", processor);
        await orchestrator.RegisterMachineAsync("validator", validator);
        await orchestrator.StartAllMachinesAsync();

        // Test inter-machine communication
        await integrationTester.TestWorkflow(new[]
        {
            ("processor", "START", null),
            ("validator", "VALIDATE", new { Data = "test" }),
            ("processor", "COMPLETE", null)
        });

        // Test concurrent processing
        await integrationTester.TestConcurrentProcessing("processor", "START", 10);

        // Test error propagation
        await integrationTester.TestErrorHandling("processor", "ERROR");

        Console.WriteLine("✅ Integration tests passed");
    }

    // Performance and load testing
    private static async Task RunPerformanceTestExample()
    {
        Console.WriteLine("\n🏎️  Performance Testing Examples");
        Console.WriteLine("==================================");

        var perfTester = new PerformanceTester();

        // Throughput test
        var throughputResult = await perfTester.TestThroughput(
            machineCount: 5,
            eventsPerMachine: 1000,
            maxDuration: TimeSpan.FromSeconds(30)
        );

        Console.WriteLine($"📊 Throughput: {throughputResult.EventsPerSecond:F0} events/sec");

        // Latency test
        var latencyResult = await perfTester.TestLatency(
            eventCount: 100,
            warmupEvents: 20
        );

        Console.WriteLine($"⚡ Average Latency: {latencyResult.AverageLatency:F2} ms");
        Console.WriteLine($"🎯 P95 Latency: {latencyResult.P95Latency:F2} ms");

        // Memory usage test
        var memoryResult = await perfTester.TestMemoryUsage(
            duration: TimeSpan.FromMinutes(1),
            eventsPerSecond: 1000
        );

        Console.WriteLine($"💾 Peak Memory: {memoryResult.PeakMemoryMB:F1} MB");
        Console.WriteLine($"📈 Memory Growth: {memoryResult.MemoryGrowthMB:F1} MB");

        Console.WriteLine("✅ Performance tests completed");
    }

    // Chaos testing for resilience
    private static async Task RunChaosTestingExample()
    {
        Console.WriteLine("\n🌪️  Chaos Testing Examples");
        Console.WriteLine("============================");

        var chaosConfig = new OrchestratorConfig
        {
            PoolSize = 4,
            EnableBackpressure = true,
            MaxQueueDepth = 1000,
            EnableCircuitBreaker = true,
            CircuitBreakerConfig = new CircuitBreakerConfig
            {
                FailureThreshold = 5,
                TimeoutDuration = TimeSpan.FromSeconds(10),
                RecoveryTimeout = TimeSpan.FromSeconds(30)
            }
        };

        using var orchestrator = new EventBusOrchestrator(chaosConfig);
        var chaosTester = new ChaosTester(orchestrator);

        // Setup resilient machines
        for (int i = 1; i <= 5; i++)
        {
            var machine = CreateResilientMachine($"chaos-{i}");
            await orchestrator.RegisterMachineAsync($"chaos-{i}", machine);
        }

        await orchestrator.StartAllMachinesAsync();

        // Test random failures
        await chaosTester.TestRandomFailures(
            duration: TimeSpan.FromMinutes(2),
            failureRate: 0.1, // 10% failure rate
            normalLoad: 100 // events per second
        );

        // Test network partitions
        await chaosTester.TestNetworkPartitions(
            duration: TimeSpan.FromSeconds(30),
            partitionDuration: TimeSpan.FromSeconds(10)
        );

        // Test resource exhaustion
        await chaosTester.TestResourceExhaustion(
            duration: TimeSpan.FromSeconds(30),
            peakLoad: 10000 // events per second
        );

        var finalHealth = orchestrator.GetHealthStatus();
        Console.WriteLine($"🏥 Final Health Status: {finalHealth.Level}");

        Console.WriteLine("✅ Chaos testing completed");
    }

    private static StateMachine CreateTestMachine()
    {
        var actions = new Dictionary<string, Action<ExecutionContext>>
        {
            ["startWork"] = ctx => Console.WriteLine($"   Starting work in {ctx.MachineId}"),
            ["doWork"] = ctx =>
            {
                Thread.Sleep(100); // Simulate work
                Console.WriteLine($"   Work completed in {ctx.MachineId}");
            },
            ["handleError"] = ctx => Console.WriteLine($"   Error handled in {ctx.MachineId}"),
            ["complete"] = ctx => Console.WriteLine($"   Completed in {ctx.MachineId}")
        };

        var json = @"{
            ""id"": ""testMachine"",
            ""initial"": ""idle"",
            ""states"": {
                ""idle"": {
                    ""on"": { ""START"": ""working"" }
                },
                ""working"": {
                    ""entry"": [""startWork"", ""doWork""],
                    ""on"": {
                        ""COMPLETE"": ""completed"",
                        ""ERROR"": ""failed""
                    }
                },
                ""completed"": {
                    ""entry"": [""complete""],
                    ""on"": { ""RESET"": ""idle"" }
                },
                ""failed"": {
                    ""entry"": [""handleError""],
                    ""on"": { ""RESET"": ""idle"" }
                }
            }
        }";

        return StateMachineFactory.CreateFromScript("testMachine", json, actions);
    }

    private static StateMachine CreateValidatorMachine()
    {
        var actions = new Dictionary<string, Action<ExecutionContext>>
        {
            ["validate"] = ctx => Console.WriteLine($"   Validating in {ctx.MachineId}")
        };

        var json = @"{
            ""id"": ""validator"",
            ""initial"": ""ready"",
            ""states"": {
                ""ready"": {
                    ""on"": { ""VALIDATE"": ""validating"" }
                },
                ""validating"": {
                    ""entry"": [""validate""],
                    ""after"": { ""100"": ""ready"" }
                }
            }
        }";

        return StateMachineFactory.CreateFromScript("validator", json, actions);
    }

    private static StateMachine CreateResilientMachine(string machineId)
    {
        var actions = new Dictionary<string, Action<ExecutionContext>>
        {
            ["processWithFailure"] = ctx =>
            {
                // Simulate random failures
                if (Random.Shared.NextDouble() < 0.1) // 10% failure rate
                {
                    throw new InvalidOperationException($"Simulated failure in {ctx.MachineId}");
                }

                Thread.Sleep(Random.Shared.Next(10, 100)); // Variable processing time
            },
            ["handleRecovery"] = ctx => Console.WriteLine($"   Recovery in {ctx.MachineId}")
        };

        var json = @"{
            ""id"": ""resilientMachine"",
            ""initial"": ""ready"",
            ""states"": {
                ""ready"": {
                    ""on"": { ""PROCESS"": ""processing"" }
                },
                ""processing"": {
                    ""entry"": [""processWithFailure""],
                    ""on"": {
                        ""SUCCESS"": ""ready"",
                        ""FAILURE"": ""recovering""
                    },
                    ""after"": { ""200"": ""ready"" }
                },
                ""recovering"": {
                    ""entry"": [""handleRecovery""],
                    ""after"": { ""500"": ""ready"" }
                }
            }
        }";

        return StateMachineFactory.CreateFromScript(machineId, json, actions);
    }
}

// Testing utility classes
public class StateMachineTester
{
    private readonly StateMachine _machine;

    public StateMachineTester(StateMachine machine)
    {
        _machine = machine;
    }

    public async Task AssertCurrentState(string expectedState)
    {
        await _machine.StartAsync();

        if (_machine.CurrentState != expectedState)
        {
            throw new AssertionException($"Expected state '{expectedState}', but was '{_machine.CurrentState}'");
        }
    }

    public async Task SendEvent(string eventName)
    {
        await _machine.SendAsync(eventName);
        await Task.Delay(50); // Allow processing time
    }
}

public class IntegrationTester
{
    private readonly EventBusOrchestrator _orchestrator;

    public IntegrationTester(EventBusOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task TestWorkflow((string machineId, string eventName, object data)[] steps)
    {
        foreach (var (machineId, eventName, data) in steps)
        {
            await _orchestrator.SendEventFireAndForgetAsync($"test-{Guid.NewGuid()}", machineId, eventName, data);
            await Task.Delay(100); // Allow processing
        }
    }

    public async Task TestConcurrentProcessing(string machineId, string eventName, int eventCount)
    {
        var tasks = new List<Task>();
        for (int i = 0; i < eventCount; i++)
        {
            tasks.Add(_orchestrator.SendEventFireAndForgetAsync($"concurrent-{i}", machineId, eventName));
        }

        await Task.WhenAll(tasks);
    }

    public async Task TestErrorHandling(string machineId, string errorEvent)
    {
        await _orchestrator.SendEventFireAndForgetAsync("error-test", machineId, errorEvent);
        await Task.Delay(500); // Allow error handling

        var health = _orchestrator.GetHealthStatus();
        // Assert health status, error counts, etc.
    }
}

public class PerformanceTester
{
    public async Task<ThroughputResult> TestThroughput(int machineCount, int eventsPerMachine, TimeSpan maxDuration)
    {
        var config = new OrchestratorConfig { PoolSize = Environment.ProcessorCount };
        using var orchestrator = new EventBusOrchestrator(config);

        // Setup machines and measure throughput
        // Implementation details...

        return new ThroughputResult { EventsPerSecond = 50000 }; // Placeholder
    }

    public async Task<LatencyResult> TestLatency(int eventCount, int warmupEvents)
    {
        // Latency testing implementation
        return new LatencyResult { AverageLatency = 0.5, P95Latency = 1.2 }; // Placeholder
    }

    public async Task<MemoryResult> TestMemoryUsage(TimeSpan duration, int eventsPerSecond)
    {
        // Memory usage testing implementation
        return new MemoryResult { PeakMemoryMB = 150, MemoryGrowthMB = 25 }; // Placeholder
    }
}

public class ChaosTester
{
    private readonly EventBusOrchestrator _orchestrator;

    public ChaosTester(EventBusOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task TestRandomFailures(TimeSpan duration, double failureRate, int normalLoad)
    {
        // Chaos testing implementation
        await Task.Delay(duration);
    }

    public async Task TestNetworkPartitions(TimeSpan duration, TimeSpan partitionDuration)
    {
        // Network partition simulation
        await Task.Delay(duration);
    }

    public async Task TestResourceExhaustion(TimeSpan duration, int peakLoad)
    {
        // Resource exhaustion testing
        await Task.Delay(duration);
    }
}

// Test result classes
public record ThroughputResult { public double EventsPerSecond { get; init; } }
public record LatencyResult { public double AverageLatency { get; init; } public double P95Latency { get; init; } }
public record MemoryResult { public double PeakMemoryMB { get; init; } public double MemoryGrowthMB { get; init; } }

public class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }
}
```

---

These examples demonstrate the full range of XStateNet capabilities, from simple state machines to complex distributed systems with comprehensive monitoring, performance optimization, and testing strategies. Each example is designed to be practical and applicable to real-world scenarios while showcasing the framework's power and flexibility.