using XStateNet.InterProcess.TestClient;

static string Timestamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

Console.WriteLine("╔═══════════════════════════════════════════════════════╗");
Console.WriteLine("║  XStateNet InterProcess Service - Test Client        ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════╝");
Console.WriteLine();

// Check command line arguments
if (args.Length > 0 && args[0] == "--help")
{
    ShowHelp();
    return;
}

// Determine test mode
var mode = args.Length > 0 ? args[0] : "menu";

switch (mode.ToLower())
{
    case "ping":
        await RunPingPongTest(args);
        break;

    case "multi":
        await RunMultiClientTest(args);
        break;

    case "stress":
        await RunStressTest(args);
        break;

    case "menu":
    default:
        await ShowInteractiveMenu();
        break;
}

static void ShowHelp()
{
    Console.WriteLine("Usage: XStateNet.InterProcess.TestClient [mode] [options]");
    Console.WriteLine();
    Console.WriteLine("Modes:");
    Console.WriteLine("  ping               - Run ping-pong test (2 clients)");
    Console.WriteLine("  multi              - Run multi-client test (5 clients)");
    Console.WriteLine("  stress             - Run stress test (100+ messages)");
    Console.WriteLine("  menu               - Interactive menu (default)");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  XStateNet.InterProcess.TestClient ping");
    Console.WriteLine("  XStateNet.InterProcess.TestClient multi");
    Console.WriteLine("  XStateNet.InterProcess.TestClient stress");
}

static async Task ShowInteractiveMenu()
{
    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("Select Test:");
        Console.WriteLine("  1. Ping-Pong Test (2 clients exchanging messages)");
        Console.WriteLine("  2. Multi-Client Test (5 clients broadcasting)");
        Console.WriteLine("  3. Stress Test (100+ messages/sec)");
        Console.WriteLine("  4. Custom Test (manual control)");
        Console.WriteLine("  5. Exit");
        Console.WriteLine();
        Console.Write("Choice [1-5]: ");

        var choice = Console.ReadLine();

        switch (choice)
        {
            case "1":
                await RunPingPongTest([]);
                break;
            case "2":
                await RunMultiClientTest([]);
                break;
            case "3":
                await RunStressTest([]);
                break;
            case "4":
                await RunCustomTest();
                break;
            case "5":
                Console.WriteLine("Goodbye!");
                return;
            default:
                Console.WriteLine("Invalid choice. Try again.");
                break;
        }
    }
}

static async Task RunPingPongTest(string[] args)
{
    Console.WriteLine();
    Console.WriteLine("═══════════════════════════════════════");
    Console.WriteLine("  Test 1: Ping-Pong (2 Clients)");
    Console.WriteLine("═══════════════════════════════════════");
    Console.WriteLine();

    var client1 = new InterProcessClient("ping-client");
    var client2 = new InterProcessClient("pong-client");

    try
    {
        // Connect both clients
        await client1.ConnectAsync();
        await client2.ConnectAsync();

        var receivedCount = 0;

        // Setup event handlers
        client1.OnEvent("PONG", evt =>
        {
            receivedCount++;
            Console.WriteLine($"[{Timestamp()}] [ping-client] Received PONG #{receivedCount}");
        });

        client2.OnEvent("PING", async evt =>
        {
            Console.WriteLine($"[{Timestamp()}] [pong-client] Received PING, sending PONG back...");
            await client2.SendEventAsync("ping-client", "PONG", new { Message = "Pong!" });
        });

        // Start ping-pong
        Console.WriteLine("Starting ping-pong...");
        for (int i = 0; i < 5; i++)
        {
            Console.WriteLine($"[{Timestamp()}] [ping-client] Sending PING #{i + 1}");
            await client1.SendEventAsync("pong-client", "PING", new { Message = $"Ping {i + 1}" });
            await Task.Delay(500);
        }

        // Wait for responses
        await Task.Delay(2000);

        Console.WriteLine();
        Console.WriteLine($"[{Timestamp()}] ✓ Test Complete! Received {receivedCount}/5 PONGs");
    }
    finally
    {
        client1.Dispose();
        client2.Dispose();
    }

    Console.WriteLine();
    Console.WriteLine("Press any key to continue...");
    Console.ReadKey();
}

static async Task RunMultiClientTest(string[] args)
{
    Console.WriteLine();
    Console.WriteLine("═══════════════════════════════════════");
    Console.WriteLine("  Test 2: Multi-Client (5 Clients)");
    Console.WriteLine("═══════════════════════════════════════");
    Console.WriteLine();

    var clients = new List<InterProcessClient>();
    var receivedCounts = new Dictionary<string, int>();

    try
    {
        // Create and connect 5 clients
        for (int i = 1; i <= 5; i++)
        {
            var machineId = $"client-{i}";
            var client = new InterProcessClient(machineId);
            await client.ConnectAsync();
            clients.Add(client);
            receivedCounts[machineId] = 0;

            // Each client listens for BROADCAST events
            client.OnEvent("BROADCAST", evt =>
            {
                receivedCounts[machineId]++;
                Console.WriteLine($"[{Timestamp()}] [{machineId}] Received broadcast from {evt.SourceMachineId}: {evt.Payload}");
            });
        }

        Console.WriteLine($"[{Timestamp()}] ✓ All {clients.Count} clients connected");
        Console.WriteLine();

        // Each client broadcasts to all others
        foreach (var sender in clients)
        {
            Console.WriteLine($"[{Timestamp()}] [{sender.MachineId}] Broadcasting message...");

            foreach (var receiver in clients)
            {
                if (receiver.MachineId != sender.MachineId)
                {
                    await sender.SendEventAsync(
                        receiver.MachineId,
                        "BROADCAST",
                        new { From = sender.MachineId, Message = $"Hello from {sender.MachineId}!" });
                }
            }

            await Task.Delay(200);
        }

        // Wait for all messages
        await Task.Delay(2000);

        Console.WriteLine();
        Console.WriteLine("Results:");
        foreach (var kvp in receivedCounts)
        {
            Console.WriteLine($"  {kvp.Key}: received {kvp.Value}/4 broadcasts");
        }

        var totalExpected = 5 * 4; // 5 clients, each receives 4 messages
        var totalReceived = receivedCounts.Values.Sum();
        Console.WriteLine($"[{Timestamp()}] ✓ Total: {totalReceived}/{totalExpected} messages delivered");
    }
    finally
    {
        foreach (var client in clients)
        {
            client.Dispose();
        }
    }

    Console.WriteLine();
    Console.WriteLine("Press any key to continue...");
    Console.ReadKey();
}

static async Task RunStressTest(string[] args)
{
    Console.WriteLine();
    Console.WriteLine("═══════════════════════════════════════");
    Console.WriteLine("  Test 3: Stress Test");
    Console.WriteLine("═══════════════════════════════════════");
    Console.WriteLine();

    var sender = new InterProcessClient("stress-sender");
    var receiver = new InterProcessClient("stress-receiver");

    try
    {
        await sender.ConnectAsync();
        await receiver.ConnectAsync();

        var receivedCount = 0;
        var messageCount = 100;

        receiver.OnEvent("STRESS_EVENT", evt =>
        {
            Interlocked.Increment(ref receivedCount);
        });

        Console.WriteLine($"[{Timestamp()}] Sending {messageCount} messages...");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < messageCount; i++)
        {
            await sender.SendEventAsync("stress-receiver", "STRESS_EVENT", new { Index = i });

            if ((i + 1) % 20 == 0)
            {
                Console.Write(".");
            }
        }

        sw.Stop();
        Console.WriteLine();

        // Wait for all to be received
        await Task.Delay(2000);

        var throughput = messageCount / sw.Elapsed.TotalSeconds;

        Console.WriteLine();
        Console.WriteLine($"[{Timestamp()}] ✓ Sent: {messageCount} messages");
        Console.WriteLine($"[{Timestamp()}] ✓ Received: {receivedCount} messages");
        Console.WriteLine($"[{Timestamp()}] ✓ Time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"[{Timestamp()}] ✓ Throughput: {throughput:F0} msg/sec");
        Console.WriteLine($"[{Timestamp()}] ✓ Avg Latency: {(double)sw.ElapsedMilliseconds / messageCount:F2}ms per message");
    }
    finally
    {
        sender.Dispose();
        receiver.Dispose();
    }

    Console.WriteLine();
    Console.WriteLine("Press any key to continue...");
    Console.ReadKey();
}

static async Task RunCustomTest()
{
    Console.WriteLine();
    Console.WriteLine("═══════════════════════════════════════");
    Console.WriteLine("  Test 4: Custom Test");
    Console.WriteLine("═══════════════════════════════════════");
    Console.WriteLine();

    Console.Write("Enter your machine ID: ");
    var machineId = Console.ReadLine() ?? "custom-client";

    var client = new InterProcessClient(machineId);

    try
    {
        await client.ConnectAsync();

        client.OnEvent("*", evt =>
        {
            Console.WriteLine($"[{Timestamp()}] ✓ Received: {evt.EventName} from {evt.SourceMachineId}");
        });

        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  send <target> <event> [message]  - Send event");
        Console.WriteLine("  quit                             - Exit");
        Console.WriteLine();

        while (true)
        {
            Console.Write($"[{machineId}]> ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLower();

            switch (command)
            {
                case "send":
                    if (parts.Length < 3)
                    {
                        Console.WriteLine("Usage: send <target> <event> [message]");
                        break;
                    }

                    var target = parts[1];
                    var eventName = parts[2];
                    var message = parts.Length > 3 ? string.Join(" ", parts.Skip(3)) : "";

                    await client.SendEventAsync(target, eventName, new { Message = message });
                    Console.WriteLine($"[{Timestamp()}] ✓ Sent {eventName} to {target}");
                    break;

                case "quit":
                case "exit":
                    return;

                default:
                    Console.WriteLine($"Unknown command: {command}");
                    break;
            }
        }
    }
    finally
    {
        client.Dispose();
    }
}
