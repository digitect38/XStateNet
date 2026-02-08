using Akka.Actor;
using Akka.Configuration;
using XStateNet2.Core.Builder;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using System.Diagnostics;

Console.WriteLine("╔══════════════════════════════════════════════════════╗");
Console.WriteLine("║   XStateNet2 Bidirectional Domino - 1000 Machines    ║");
Console.WriteLine("╚══════════════════════════════════════════════════════╝");
Console.WriteLine();

const int DOMINO_COUNT = 1000;

// Create actor system with minimal logging
var akkaConfig = ConfigurationFactory.ParseString(@"
    akka {
        loglevel = WARNING
        stdout-loglevel = WARNING
    }
");
var actorSystem = ActorSystem.Create("DominoSystem", akkaConfig);

try
{
    // Load JSON definition
    var dominoJson = await File.ReadAllTextAsync("DominoMachine.json");
    var factory = new XStateMachineFactory(actorSystem);

    Console.WriteLine($"Creating {DOMINO_COUNT} bidirectional domino state machines...");
    var creationStopwatch = Stopwatch.StartNew();

    // Store all domino machine references
    var dominoes = new IActorRef[DOMINO_COUNT];

    // Track completion for both directions
    int fallenCount = 0;
    int resetCount = 0;
    var onCompletionSource = new TaskCompletionSource<bool>();
    var offCompletionSource = new TaskCompletionSource<bool>();

    // Create all domino machines in reverse order (so each can reference the next)
    for (int i = DOMINO_COUNT - 1; i >= 0; i--)
    {
        int currentIndex = i;
        IActorRef? nextDomino = (i < DOMINO_COUNT - 1) ? dominoes[i + 1] : null;
        IActorRef? prevDomino = null; // Will be set after creation

        dominoes[i] = factory.FromJson(dominoJson)
            .WithOptimization(OptimizationLevel.Array)
            .WithContext("index", currentIndex)
            .WithAction("onOn", (ctx, _) =>
            {
                var idx = ctx.Get<int>("index");
                Interlocked.Increment(ref fallenCount);

                // Show progress every 100 dominoes
                if ((idx + 1) % 100 == 0 || idx == DOMINO_COUNT - 1)
                {
                    Console.WriteLine($"  [ON]  Domino {idx + 1} is ON! ({fallenCount}/{DOMINO_COUNT})");
                }

                // Trigger the next domino if exists
                if (nextDomino != null)
                {
                    nextDomino.Tell(new SendEvent("TRIGGER_ON", null));
                }
                else
                {
                    // This is the last domino
                    onCompletionSource.TrySetResult(true);
                }
            })
            .WithAction("onOff", (ctx, _) =>
            {
                var idx = ctx.Get<int>("index");
                var count = Interlocked.Increment(ref resetCount);

                // Skip initial entry (when machine starts)
                if (count <= DOMINO_COUNT)
                    return;

                // Show progress every 100 dominoes
                if ((DOMINO_COUNT - idx) % 100 == 0 || idx == 0)
                {
                    Console.WriteLine($"  [OFF] Domino {idx + 1} is OFF! ({count - DOMINO_COUNT}/{DOMINO_COUNT})");
                }

                // Trigger the previous domino if exists (reverse cascade)
                if (idx > 0)
                {
                    dominoes[idx - 1].Tell(new SendEvent("TRIGGER_OFF", null));
                }
                else
                {
                    // This is the first domino - reverse cascade complete
                    offCompletionSource.TrySetResult(true);
                }
            })
            .BuildAndStart($"domino-{currentIndex:D4}");
    }

    creationStopwatch.Stop();
    Console.WriteLine($"Created {DOMINO_COUNT} dominoes in {creationStopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine();

    // ========== Forward Cascade (OFF -> ON) ==========
    Console.WriteLine("╔══════════════════════════════════════════════════════╗");
    Console.WriteLine("║         Forward Cascade: OFF -> ON                   ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════╝");
    Console.WriteLine();

    var forwardStopwatch = Stopwatch.StartNew();

    // Trigger the first domino to turn ON
    dominoes[0].Tell(new SendEvent("TRIGGER_ON", null));

    // Wait for all dominoes to turn ON
    var forwardCompleted = await Task.WhenAny(
        onCompletionSource.Task,
        Task.Delay(TimeSpan.FromSeconds(60))
    );

    forwardStopwatch.Stop();

    Console.WriteLine();
    if (forwardCompleted == onCompletionSource.Task)
    {
        Console.WriteLine($"  Forward cascade complete!");
        Console.WriteLine($"  Time: {forwardStopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"  Throughput: {(DOMINO_COUNT * 1000.0 / forwardStopwatch.ElapsedMilliseconds):N0} transitions/sec");
    }
    else
    {
        Console.WriteLine($"  Timeout! Only {fallenCount}/{DOMINO_COUNT} dominoes turned ON.");
    }

    Console.WriteLine();
    await Task.Delay(500); // Brief pause between cascades

    // ========== Reverse Cascade (ON -> OFF) ==========
    Console.WriteLine("╔══════════════════════════════════════════════════════╗");
    Console.WriteLine("║         Reverse Cascade: ON -> OFF                   ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════╝");
    Console.WriteLine();

    var reverseStopwatch = Stopwatch.StartNew();

    // Trigger the last domino to turn OFF (starts reverse cascade)
    dominoes[DOMINO_COUNT - 1].Tell(new SendEvent("TRIGGER_OFF", null));

    // Wait for all dominoes to turn OFF
    var reverseCompleted = await Task.WhenAny(
        offCompletionSource.Task,
        Task.Delay(TimeSpan.FromSeconds(60))
    );

    reverseStopwatch.Stop();

    Console.WriteLine();
    if (reverseCompleted == offCompletionSource.Task)
    {
        Console.WriteLine($"  Reverse cascade complete!");
        Console.WriteLine($"  Time: {reverseStopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"  Throughput: {(DOMINO_COUNT * 1000.0 / reverseStopwatch.ElapsedMilliseconds):N0} transitions/sec");
    }
    else
    {
        Console.WriteLine($"  Timeout! Only {resetCount - DOMINO_COUNT}/{DOMINO_COUNT} dominoes turned OFF.");
    }

    // ========== Summary ==========
    Console.WriteLine();
    Console.WriteLine("╔══════════════════════════════════════════════════════╗");
    Console.WriteLine("║                    Summary                           ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════╝");
    Console.WriteLine();
    Console.WriteLine($"  Total dominoes:     {DOMINO_COUNT}");
    Console.WriteLine($"  Total transitions:  {DOMINO_COUNT * 2} (forward + reverse)");
    Console.WriteLine($"  Total time:         {forwardStopwatch.ElapsedMilliseconds + reverseStopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine($"  Avg throughput:     {(DOMINO_COUNT * 2 * 1000.0 / (forwardStopwatch.ElapsedMilliseconds + reverseStopwatch.ElapsedMilliseconds)):N0} transitions/sec");
    Console.WriteLine();

    // Cleanup
    Console.WriteLine("Stopping all domino machines...");
    foreach (var domino in dominoes)
    {
        domino.Tell(new StopMachine());
    }
    await Task.Delay(500);
}
finally
{
    await actorSystem.Terminate();
}

Console.WriteLine();
Console.WriteLine("╔══════════════════════════════════════════════════════╗");
Console.WriteLine("║        Bidirectional Domino Demo Complete!           ║");
Console.WriteLine("╚══════════════════════════════════════════════════════╝");
