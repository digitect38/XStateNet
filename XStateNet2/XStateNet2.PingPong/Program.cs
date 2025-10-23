using Akka.Actor;
using Akka.Configuration;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using System.Diagnostics;

Console.WriteLine("╔══════════════════════════════════════════════════════╗");
Console.WriteLine("║  XStateNet2 Performance Benchmark - Ping Pong Test  ║");
Console.WriteLine("╚══════════════════════════════════════════════════════╝");
Console.WriteLine();

// Create actor system with minimal logging
var akkaConfig = ConfigurationFactory.ParseString(@"
    akka {
        loglevel = WARNING
        stdout-loglevel = WARNING
    }
");
var actorSystem = ActorSystem.Create("PingPongBenchmark", akkaConfig);

try
{
    // Sequential tests with cache warming
    await RunPingPongTest(actorSystem, "Test 10K", 10_000, false);
    await RunPingPongTest(actorSystem, "Test 100K", 100_000, false);
    await RunPingPongTest(actorSystem, "Test 1M", 1_000_000, true);

    // Pure Actor baseline test (uncomment when needed for comparison)
    // Console.WriteLine();
    // Console.WriteLine("╔══════════════════════════════════════════════════════╗");
    // Console.WriteLine("║         Pure Akka.NET Actor Baseline Test           ║");
    // Console.WriteLine("╚══════════════════════════════════════════════════════╝");
    // Console.WriteLine();
    // await RunPureActorTest(actorSystem, "Pure Actor 1M", 1_000_000);
}
finally
{
    await actorSystem.Terminate();
}

Console.WriteLine();
Console.WriteLine("╔══════════════════════════════════════════════════════╗");
Console.WriteLine("║              Benchmark Complete!                     ║");
Console.WriteLine("╚══════════════════════════════════════════════════════╝");

static async Task RunPingPongTest(ActorSystem system, string testName, int messageCount, bool showProgress)
{
    Console.WriteLine($"\n┌─ {testName} ({messageCount:N0} messages) ─────────────────────");

    // Load JSON definitions
    var pingJson = await File.ReadAllTextAsync("PingMachine.json");
    var pongJson = await File.ReadAllTextAsync("PongMachine.json");

    var factory = new XStateMachineFactory(system);

    // Performance counters
    int pingCount = 0;
    int pongCount = 0;
    var startTime = DateTime.MinValue;
    var endTime = DateTime.MinValue;

    // Unique actor names for each test
    var testId = Guid.NewGuid().ToString("N").Substring(0, 8);
    var pongName = $"pong-{testId}";
    var pingName = $"ping-{testId}";

    // Placeholder for ping machine reference (will be set after ping is created)
    IActorRef? pingMachineRef = null;

    // Create Pong machine first
    var pongMachine = factory.FromJson(pongJson)
        .WithAction("onWaiting", (ctx, _) => { })
        .WithAction("incrementCount", (ctx, _) =>
        {
            var count = ctx.Get<int>("count");
            ctx.Set("count", count + 1);
            Interlocked.Increment(ref pongCount);
        })
        .WithAction("sendPong", (ctx, _) =>
        {
            // Send PONG back to ping machine
            pingMachineRef?.Tell(new SendEvent("PONG", null));
        })
        .WithAction("onDone", (ctx, _) =>
        {
            endTime = DateTime.UtcNow;
        })
        .BuildAndStart(pongName);

    // Create Ping machine
    var pingMachine = factory.FromJson(pingJson)
        .WithAction("onIdle", (ctx, _) => { })
        .WithAction("incrementCount", (ctx, _) =>
        {
            var count = ctx.Get<int>("count");
            ctx.Set("count", count + 1);

            if (startTime == DateTime.MinValue)
            {
                startTime = DateTime.UtcNow;
            }

            Interlocked.Increment(ref pingCount);

            // Show progress every 10000 messages
            if (showProgress && count > 0 && count % 10000 == 0)
            {
                var elapsed = DateTime.UtcNow - startTime;
                var throughput = (count * 2) / elapsed.TotalSeconds; // *2 because ping+pong
                Console.WriteLine($"│ Progress: {count:N0}/{messageCount:N0} rounds ({elapsed.TotalSeconds:N2}s, {throughput:N0} msg/s)");
            }
        })
        .WithAction("sendPing", (ctx, _) =>
        {
            // Send PING to pong machine
            pongMachine.Tell(new SendEvent("PING", null));
        })
        .WithAction("onDone", (ctx, _) =>
        {
            endTime = DateTime.UtcNow;

            // Stop pong machine
            pongMachine.Tell(new SendEvent("STOP", null));
        })
        .WithGuard("isMaxReached", (ctx, _) =>
        {
            var count = ctx.Get<int>("count");
            var maxCount = ctx.Get<int>("maxCount");
            return count >= maxCount;
        })
        .WithContext("maxCount", messageCount)
        .BuildAndStart(pingName);

    // Set ping machine reference for pong machine to use
    pingMachineRef = pingMachine;

    // Start the ping-pong
    pingMachine.Tell(new SendEvent("START", null));

    // Wait for completion with timeout
    var timeout = messageCount < 10_000 ? 5000 : messageCount < 100_000 ? 30000 : 120000;
    await Task.Delay(timeout);

    // Calculate and display results
    var duration = endTime - startTime;
    if (duration.TotalMilliseconds > 0)
    {
        var totalMessages = pingCount + pongCount;
        var throughput = totalMessages / duration.TotalSeconds;

        Console.WriteLine("│");
        Console.WriteLine("│ ══════════════════════════════════════════════════");
        Console.WriteLine("│   Performance Statistics");
        Console.WriteLine("│ ══════════════════════════════════════════════════");
        Console.WriteLine("│");
        Console.WriteLine($"│ Ping FSM:");
        Console.WriteLine($"│   Pings Sent:      {pingCount:N0}");
        Console.WriteLine($"│   Total Messages:  {pingCount:N0}");
        Console.WriteLine($"│   Duration:        {duration.TotalSeconds:N2} seconds");
        Console.WriteLine($"│   Throughput:      {(pingCount / duration.TotalSeconds):N0} msg/sec");
        Console.WriteLine("│");
        Console.WriteLine($"│ Pong FSM:");
        Console.WriteLine($"│   Pongs Sent:      {pongCount:N0}");
        Console.WriteLine($"│   Total Messages:  {pongCount:N0}");
        Console.WriteLine($"│   Duration:        {duration.TotalSeconds:N2} seconds");
        Console.WriteLine($"│   Throughput:      {(pongCount / duration.TotalSeconds):N0} msg/sec");
        Console.WriteLine("│");
        Console.WriteLine($"│ Overall:");
        Console.WriteLine($"│   Total Messages:  {totalMessages:N0}");
        Console.WriteLine($"│   Avg Throughput:  {throughput:N0} msg/sec");
        Console.WriteLine($"│   Round Trips:     {messageCount:N0}");
    }
    else
    {
        Console.WriteLine("│ ⚠ Test did not complete in time");
    }

    Console.WriteLine("└─────────────────────────────────────────────────────");

    // Stop machines
    pingMachine.Tell(new StopMachine());
    pongMachine.Tell(new StopMachine());

    await Task.Delay(500); // Allow cleanup
}

static async Task RunPureActorTest(ActorSystem system, string testName, int messageCount)
{
    Console.WriteLine($"\n┌─ {testName} ({messageCount:N0} messages) ─────────────────────");

    int pingCount = 0;
    int pongCount = 0;
    TimeSpan duration = TimeSpan.Zero;

    var tcs = new TaskCompletionSource<bool>();

    // Extract lambda to avoid expression tree conversion error
    Action<int, int, TimeSpan> onComplete = (pc, mc, d) =>
    {
        pingCount = pc;
        pongCount = pc;
        duration = d;
        tcs.SetResult(true);
    };

    var pongActor = system.ActorOf(Props.Create(() => new PurePongActor()), "pure-pong");
    var pingActor = system.ActorOf(Props.Create(() => new PurePingActor(pongActor, messageCount, onComplete)), "pure-ping");

    pingActor.Tell(new StartMsg());

    await tcs.Task;

    var totalMessages = pingCount + pongCount;
    var throughput = totalMessages / duration.TotalSeconds;

    Console.WriteLine("│");
    Console.WriteLine("│ ══════════════════════════════════════════════════");
    Console.WriteLine("│   Performance Statistics (Pure Akka.NET)");
    Console.WriteLine("│ ══════════════════════════════════════════════════");
    Console.WriteLine("│");
    Console.WriteLine($"│ Ping Actor:");
    Console.WriteLine($"│   Pings Sent:      {pingCount:N0}");
    Console.WriteLine($"│   Total Messages:  {pingCount:N0}");
    Console.WriteLine($"│   Duration:        {duration.TotalSeconds:N2} seconds");
    Console.WriteLine($"│   Throughput:      {(pingCount / duration.TotalSeconds):N0} msg/sec");
    Console.WriteLine("│");
    Console.WriteLine($"│ Pong Actor:");
    Console.WriteLine($"│   Pongs Sent:      {pongCount:N0}");
    Console.WriteLine($"│   Total Messages:  {pongCount:N0}");
    Console.WriteLine($"│   Duration:        {duration.TotalSeconds:N2} seconds");
    Console.WriteLine($"│   Throughput:      {(pongCount / duration.TotalSeconds):N0} msg/sec");
    Console.WriteLine("│");
    Console.WriteLine($"│ Overall:");
    Console.WriteLine($"│   Total Messages:  {totalMessages:N0}");
    Console.WriteLine($"│   Avg Throughput:  {throughput:N0} msg/sec");
    Console.WriteLine($"│   Round Trips:     {messageCount:N0}");
    Console.WriteLine("└─────────────────────────────────────────────────────");

    system.Stop(pingActor);
    system.Stop(pongActor);

    await Task.Delay(500);
}
