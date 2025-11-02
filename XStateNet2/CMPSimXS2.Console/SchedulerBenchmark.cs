using System.Diagnostics;
using Akka.Actor;
using CMPSimXS2.Console.Models;
using CMPSimXS2.Console.Schedulers;
using XStateNet2.Core.Messages;

namespace CMPSimXS2.Console;

/// <summary>
/// Performance benchmark comparing Lock-based vs Actor-based RobotScheduler
/// </summary>
public class SchedulerBenchmark
{
    public static async Task RunBenchmark()
    {
        System.Console.WriteLine("╔═════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║  RobotScheduler 12-Way Benchmark: Lock | Actor | XState | Array | Autonomous | Hybrid | EventDriven | ActorMailbox | Ant | PubSub | SinglePub | ArrayPub  ║");
        System.Console.WriteLine("╚═════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();

        using var actorSystem = ActorSystem.Create("Benchmark");

        // Test 1: Throughput - Sequential requests
        await TestThroughput(actorSystem);
        await Task.Delay(1000);

        // Test 2: Latency - Individual request response time
        await TestLatency(actorSystem);
        await Task.Delay(1000);

        // Test 3: Concurrent load
        await TestConcurrentLoad(actorSystem);

        await actorSystem.Terminate();
    }

    private static async Task TestThroughput(ActorSystem actorSystem)
    {
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.WriteLine("Test 1: Throughput (Sequential Requests)");
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.WriteLine();

        int iterations = 10000;

        // Lock-based
        var lockScheduler = new RobotScheduler();
        SetupScheduler(actorSystem, lockScheduler, "lock");

        var sw1 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            lockScheduler.RequestTransfer(new TransferRequest
            {
                WaferId = i % 10 + 1,
                From = "Carrier",
                To = "Polisher",
                Priority = 1
            });
        }
        sw1.Stop();

        var lockThroughput = iterations / sw1.Elapsed.TotalSeconds;
        System.Console.WriteLine($"🔒 Lock-based:");
        System.Console.WriteLine($"   Requests: {iterations:N0}");
        System.Console.WriteLine($"   Time: {sw1.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {lockThroughput:N0} requests/sec");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Actor-based
        var actorScheduler = new RobotSchedulerActorProxy(actorSystem, "throughput-scheduler");
        SetupScheduler(actorSystem, actorScheduler, "actor");

        var sw2 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            actorScheduler.RequestTransfer(new TransferRequest
            {
                WaferId = i % 10 + 1,
                From = "Carrier",
                To = "Polisher",
                Priority = 1
            });
        }
        sw2.Stop();

        var actorThroughput = iterations / sw2.Elapsed.TotalSeconds;
        System.Console.WriteLine($"🎭 Actor-based:");
        System.Console.WriteLine($"   Requests: {iterations:N0}");
        System.Console.WriteLine($"   Time: {sw2.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {actorThroughput:N0} requests/sec");
        System.Console.WriteLine();

        await Task.Delay(100);

        // XState-based
        var xstateScheduler = new RobotSchedulerXState(actorSystem, "throughput-xstate-scheduler");
        SetupScheduler(actorSystem, xstateScheduler, "xstate");

        var sw3 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            xstateScheduler.RequestTransfer(new TransferRequest
            {
                WaferId = i % 10 + 1,
                From = "Carrier",
                To = "Polisher",
                Priority = 1
            });
        }
        sw3.Stop();

        var xstateThroughput = iterations / sw3.Elapsed.TotalSeconds;
        System.Console.WriteLine($"🔄 XState-based (FrozenDictionary):");
        System.Console.WriteLine($"   Requests: {iterations:N0}");
        System.Console.WriteLine($"   Time: {sw3.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {xstateThroughput:N0} requests/sec");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Array-based XState (Actor + byte indices)
        var arrayScheduler = new RobotSchedulerXStateArray(actorSystem, "array-scheduler");
        SetupScheduler(actorSystem, arrayScheduler, "array");

        var sw4 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            arrayScheduler.RequestTransfer(new TransferRequest
            {
                WaferId = i % 10 + 1,
                From = "Carrier",
                To = "Polisher",
                Priority = 1
            });
        }
        sw4.Stop();

        var arrayThroughput = iterations / sw4.Elapsed.TotalSeconds;
        System.Console.WriteLine($"⚡ XState-based (Array):");
        System.Console.WriteLine($"   Requests: {iterations:N0}");
        System.Console.WriteLine($"   Time: {sw4.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {arrayThroughput:N0} requests/sec");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Autonomous (polling-based)
        var autonomousScheduler = new AutonomousRobotScheduler();
        SetupScheduler(actorSystem, autonomousScheduler, "autonomous");

        var sw5 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            autonomousScheduler.RequestTransfer(new TransferRequest
            {
                WaferId = i % 10 + 1,
                From = "Carrier",
                To = "Polisher",
                Priority = 1
            });
        }
        sw5.Stop();

        var autonomousThroughput = iterations / sw5.Elapsed.TotalSeconds;
        System.Console.WriteLine($"🤖 Autonomous (Polling):");
        System.Console.WriteLine($"   Requests: {iterations:N0}");
        System.Console.WriteLine($"   Time: {sw5.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {autonomousThroughput:N0} requests/sec (queue rate)");
        System.Console.WriteLine($"   Note: Measures queue enqueue rate, not processing rate");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Hybrid (Array + Autonomous)
        var hybridScheduler = new AutonomousArrayScheduler();
        SetupScheduler(actorSystem, hybridScheduler, "hybrid");

        var sw6 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            hybridScheduler.RequestTransfer(new TransferRequest
            {
                WaferId = i % 10 + 1,
                From = "Carrier",
                To = "Polisher",
                Priority = 1
            });
        }
        sw6.Stop();

        var hybridThroughput = iterations / sw6.Elapsed.TotalSeconds;
        System.Console.WriteLine($"🚀 Hybrid (Array + Autonomous):");
        System.Console.WriteLine($"   Requests: {iterations:N0}");
        System.Console.WriteLine($"   Time: {sw6.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {hybridThroughput:N0} requests/sec (queue rate)");
        System.Console.WriteLine($"   Note: Measures queue enqueue rate with byte optimizations");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Event-Driven Hybrid (Array + Event-driven dispatch)
        var eventDrivenScheduler = new EventDrivenHybridScheduler();
        SetupScheduler(actorSystem, eventDrivenScheduler, "eventdriven");

        var sw7 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            eventDrivenScheduler.RequestTransfer(new TransferRequest
            {
                WaferId = i % 10 + 1,
                From = "Carrier",
                To = "Polisher",
                Priority = 1
            });
        }
        sw7.Stop();

        var eventDrivenThroughput = iterations / sw7.Elapsed.TotalSeconds;
        System.Console.WriteLine($"⚡🔥 Event-Driven Hybrid (Array + Event):");
        System.Console.WriteLine($"   Requests: {iterations:N0}");
        System.Console.WriteLine($"   Time: {sw7.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {eventDrivenThroughput:N0} requests/sec (dispatch rate)");
        System.Console.WriteLine($"   Note: Event-driven dispatch with byte optimizations (no polling!)");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Actor Mailbox Event-Driven (Mailbox + Array)
        var actorMailboxScheduler = new ActorMailboxEventDrivenScheduler(actorSystem, "actormailbox-throughput");
        SetupScheduler(actorSystem, actorMailboxScheduler, "actormailbox");

        var sw8 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            actorMailboxScheduler.RequestTransfer(new TransferRequest
            {
                WaferId = i % 10 + 1,
                From = "Carrier",
                To = "Polisher",
                Priority = 1
            });
        }
        sw8.Stop();

        var actorMailboxThroughput = iterations / sw8.Elapsed.TotalSeconds;
        System.Console.WriteLine($"📬⚡ Actor Mailbox Event-Driven (Mailbox + Array):");
        System.Console.WriteLine($"   Requests: {iterations:N0}");
        System.Console.WriteLine($"   Time: {sw8.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {actorMailboxThroughput:N0} requests/sec (mailbox rate)");
        System.Console.WriteLine($"   Note: Akka.NET mailbox-based dispatch with byte optimizations");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Ant Colony (Decentralized autonomous robots)
        var antScheduler = new AntColonyScheduler(actorSystem, "ant-throughput");
        SetupScheduler(actorSystem, antScheduler, "ant");

        var sw9 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            antScheduler.RequestTransfer(new TransferRequest
            {
                WaferId = i % 10 + 1,
                From = "Carrier",
                To = "Polisher",
                Priority = 1
            });
        }
        sw9.Stop();

        var antThroughput = iterations / sw9.Elapsed.TotalSeconds;
        System.Console.WriteLine($"🐜 Ant Colony (Decentralized Autonomy):");
        System.Console.WriteLine($"   Requests: {iterations:N0}");
        System.Console.WriteLine($"   Time: {sw9.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {antThroughput:N0} requests/sec (workpool rate)");
        System.Console.WriteLine($"   Note: Decentralized robots autonomously claim work (no central dispatcher!)");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Publication-Based (Dedicated scheduler per robot with pub/sub)
        var pubSubScheduler = new PublicationBasedScheduler(actorSystem, "pubsub-throughput");
        SetupScheduler(actorSystem, pubSubScheduler, "pubsub");

        var sw10 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            pubSubScheduler.RequestTransfer(new TransferRequest
            {
                WaferId = i % 10 + 1,
                From = "Carrier",
                To = "Polisher",
                Priority = 1
            });
        }
        sw10.Stop();

        var pubSubThroughput = iterations / sw10.Elapsed.TotalSeconds;
        System.Console.WriteLine($"📡 Publication-Based (Dedicated per Robot):");
        System.Console.WriteLine($"   Requests: {iterations:N0}");
        System.Console.WriteLine($"   Time: {sw10.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {pubSubThroughput:N0} requests/sec (routing rate)");
        System.Console.WriteLine($"   Note: Each robot has dedicated scheduler reacting to state publications");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Single Publication-Based (Single scheduler with pub/sub - no routing overhead)
        var singlePubScheduler = new SinglePublicationScheduler(actorSystem, "singlepub-throughput");
        SetupScheduler(actorSystem, singlePubScheduler, "singlepub");

        var sw11 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            singlePubScheduler.RequestTransfer(new TransferRequest
            {
                WaferId = i % 10 + 1,
                From = "Carrier",
                To = "Polisher",
                Priority = 1
            });
        }
        sw11.Stop();

        var singlePubThroughput = iterations / sw11.Elapsed.TotalSeconds;
        System.Console.WriteLine($"📡⚡ Single Publication-Based (No Routing!):");
        System.Console.WriteLine($"   Requests: {iterations:N0}");
        System.Console.WriteLine($"   Time: {sw11.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {singlePubThroughput:N0} requests/sec (no routing rate)");
        System.Console.WriteLine($"   Note: Single scheduler with state publications - eliminates routing overhead!");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Array-Based Single Publication (XState + Array + No Routing)
        var arrayPubScheduler = new SinglePublicationSchedulerXState(actorSystem, "arraypub-throughput");
        SetupScheduler(actorSystem, arrayPubScheduler, "arraypub");

        var sw12 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            arrayPubScheduler.RequestTransfer(new TransferRequest
            {
                WaferId = i % 10 + 1,
                From = "Carrier",
                To = "Polisher",
                Priority = 1
            });
        }
        sw12.Stop();

        var arrayPubThroughput = iterations / sw12.Elapsed.TotalSeconds;
        System.Console.WriteLine($"📡⚡🎯 Array Single Publication (XState + Array + No Routing!):");
        System.Console.WriteLine($"   Requests: {iterations:N0}");
        System.Console.WriteLine($"   Time: {sw12.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {arrayPubThroughput:N0} requests/sec (array + no routing rate)");
        System.Console.WriteLine($"   Note: XState array-based with state publications - combines array speed with pub/sub!");
        System.Console.WriteLine();

        var actorImprovement = ((actorThroughput - lockThroughput) / lockThroughput) * 100;
        var xstateImprovement = ((xstateThroughput - lockThroughput) / lockThroughput) * 100;
        var arrayImprovement = ((arrayThroughput - lockThroughput) / lockThroughput) * 100;
        var autonomousImprovement = ((autonomousThroughput - lockThroughput) / lockThroughput) * 100;
        var hybridImprovement = ((hybridThroughput - lockThroughput) / lockThroughput) * 100;
        var eventDrivenImprovement = ((eventDrivenThroughput - lockThroughput) / lockThroughput) * 100;
        var actorMailboxImprovement = ((actorMailboxThroughput - lockThroughput) / lockThroughput) * 100;
        var antImprovement = ((antThroughput - lockThroughput) / lockThroughput) * 100;
        var pubSubImprovement = ((pubSubThroughput - lockThroughput) / lockThroughput) * 100;
        var singlePubImprovement = ((singlePubThroughput - lockThroughput) / lockThroughput) * 100;
        var arrayPubImprovement = ((arrayPubThroughput - lockThroughput) / lockThroughput) * 100;
        System.Console.WriteLine($"📊 Results:");
        System.Console.WriteLine($"   Actor is {Math.Abs(actorImprovement):F1}% {(actorImprovement > 0 ? "faster" : "slower")} than Lock");
        System.Console.WriteLine($"   XState (FrozenDict) is {Math.Abs(xstateImprovement):F1}% {(xstateImprovement > 0 ? "faster" : "slower")} than Lock");
        System.Console.WriteLine($"   XState (Array) is {Math.Abs(arrayImprovement):F1}% {(arrayImprovement > 0 ? "faster" : "slower")} than Lock");
        System.Console.WriteLine($"   Autonomous is {Math.Abs(autonomousImprovement):F1}% {(autonomousImprovement > 0 ? "faster" : "slower")} than Lock (queue rate)");
        System.Console.WriteLine($"   Hybrid is {Math.Abs(hybridImprovement):F1}% {(hybridImprovement > 0 ? "faster" : "slower")} than Lock (queue rate)");
        System.Console.WriteLine($"   Event-Driven is {Math.Abs(eventDrivenImprovement):F1}% {(eventDrivenImprovement > 0 ? "faster" : "slower")} than Lock (dispatch rate)");
        System.Console.WriteLine($"   Actor Mailbox is {Math.Abs(actorMailboxImprovement):F1}% {(actorMailboxImprovement > 0 ? "faster" : "slower")} than Lock (mailbox rate)");
        System.Console.WriteLine($"   Ant Colony is {Math.Abs(antImprovement):F1}% {(antImprovement > 0 ? "faster" : "slower")} than Lock (workpool rate)");
        System.Console.WriteLine($"   Publication-Based is {Math.Abs(pubSubImprovement):F1}% {(pubSubImprovement > 0 ? "faster" : "slower")} than Lock (routing rate)");
        System.Console.WriteLine($"   Single Publication is {Math.Abs(singlePubImprovement):F1}% {(singlePubImprovement > 0 ? "faster" : "slower")} than Lock (no routing rate)");
        System.Console.WriteLine($"   Array Single Publication is {Math.Abs(arrayPubImprovement):F1}% {(arrayPubImprovement > 0 ? "faster" : "slower")} than Lock (array + no routing rate)");
        System.Console.WriteLine();
    }

    private static async Task TestLatency(ActorSystem actorSystem)
    {
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.WriteLine("Test 2: Latency (Request-Response Time)");
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.WriteLine();

        int iterations = 1000;

        // Lock-based
        var lockScheduler = new RobotScheduler();
        SetupScheduler(actorSystem, lockScheduler, "lock-latency");

        var lockLatencies = new List<double>();
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            lockScheduler.GetQueueSize();
            sw.Stop();
            lockLatencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        var lockAvg = lockLatencies.Average();
        var lockP50 = lockLatencies.OrderBy(x => x).ElementAt(iterations / 2);
        var lockP95 = lockLatencies.OrderBy(x => x).ElementAt((int)(iterations * 0.95));

        System.Console.WriteLine($"🔒 Lock-based Query Latency:");
        System.Console.WriteLine($"   Average: {lockAvg:F3}ms");
        System.Console.WriteLine($"   P50: {lockP50:F3}ms");
        System.Console.WriteLine($"   P95: {lockP95:F3}ms");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Actor-based
        var actorScheduler = new RobotSchedulerActorProxy(actorSystem, "latency-scheduler");
        SetupScheduler(actorSystem, actorScheduler, "actor-latency");

        var actorLatencies = new List<double>();
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            actorScheduler.GetQueueSize();
            sw.Stop();
            actorLatencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        var actorAvg = actorLatencies.Average();
        var actorP50 = actorLatencies.OrderBy(x => x).ElementAt(iterations / 2);
        var actorP95 = actorLatencies.OrderBy(x => x).ElementAt((int)(iterations * 0.95));

        System.Console.WriteLine($"🎭 Actor-based Query Latency:");
        System.Console.WriteLine($"   Average: {actorAvg:F3}ms");
        System.Console.WriteLine($"   P50: {actorP50:F3}ms");
        System.Console.WriteLine($"   P95: {actorP95:F3}ms");
        System.Console.WriteLine();

        await Task.Delay(100);

        // XState-based
        var xstateScheduler = new RobotSchedulerXState(actorSystem, "latency-xstate-scheduler");
        SetupScheduler(actorSystem, xstateScheduler, "xstate-latency");

        var xstateLatencies = new List<double>();
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            xstateScheduler.GetQueueSize();
            sw.Stop();
            xstateLatencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        var xstateAvg = xstateLatencies.Average();
        var xstateP50 = xstateLatencies.OrderBy(x => x).ElementAt(iterations / 2);
        var xstateP95 = xstateLatencies.OrderBy(x => x).ElementAt((int)(iterations * 0.95));

        System.Console.WriteLine($"🔄 XState-based (FrozenDict) Query Latency:");
        System.Console.WriteLine($"   Average: {xstateAvg:F3}ms");
        System.Console.WriteLine($"   P50: {xstateP50:F3}ms");
        System.Console.WriteLine($"   P95: {xstateP95:F3}ms");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Array-based (Actor + byte indices)
        var arrayScheduler = new RobotSchedulerXStateArray(actorSystem, "array-latency-scheduler");
        SetupScheduler(actorSystem, arrayScheduler, "array-latency");

        var arrayLatencies = new List<double>();
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            arrayScheduler.GetQueueSize();
            sw.Stop();
            arrayLatencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        var arrayAvg = arrayLatencies.Average();
        var arrayP50 = arrayLatencies.OrderBy(x => x).ElementAt(iterations / 2);
        var arrayP95 = arrayLatencies.OrderBy(x => x).ElementAt((int)(iterations * 0.95));

        System.Console.WriteLine($"⚡ XState-based (Array) Query Latency:");
        System.Console.WriteLine($"   Average: {arrayAvg:F3}ms");
        System.Console.WriteLine($"   P50: {arrayP50:F3}ms");
        System.Console.WriteLine($"   P95: {arrayP95:F3}ms");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Autonomous
        var autonomousScheduler = new AutonomousRobotScheduler();
        SetupScheduler(actorSystem, autonomousScheduler, "autonomous-latency");

        var autonomousLatencies = new List<double>();
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            autonomousScheduler.GetQueueSize();
            sw.Stop();
            autonomousLatencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        var autonomousAvg = autonomousLatencies.Average();
        var autonomousP50 = autonomousLatencies.OrderBy(x => x).ElementAt(iterations / 2);
        var autonomousP95 = autonomousLatencies.OrderBy(x => x).ElementAt((int)(iterations * 0.95));

        System.Console.WriteLine($"🤖 Autonomous Query Latency:");
        System.Console.WriteLine($"   Average: {autonomousAvg:F3}ms");
        System.Console.WriteLine($"   P50: {autonomousP50:F3}ms");
        System.Console.WriteLine($"   P95: {autonomousP95:F3}ms");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Hybrid
        var hybridScheduler = new AutonomousArrayScheduler();
        SetupScheduler(actorSystem, hybridScheduler, "hybrid-latency");

        var hybridLatencies = new List<double>();
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            hybridScheduler.GetQueueSize();
            sw.Stop();
            hybridLatencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        var hybridAvg = hybridLatencies.Average();
        var hybridP50 = hybridLatencies.OrderBy(x => x).ElementAt(iterations / 2);
        var hybridP95 = hybridLatencies.OrderBy(x => x).ElementAt((int)(iterations * 0.95));

        System.Console.WriteLine($"🚀 Hybrid Query Latency:");
        System.Console.WriteLine($"   Average: {hybridAvg:F3}ms");
        System.Console.WriteLine($"   P50: {hybridP50:F3}ms");
        System.Console.WriteLine($"   P95: {hybridP95:F3}ms");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Event-Driven Hybrid
        var eventDrivenScheduler = new EventDrivenHybridScheduler();
        SetupScheduler(actorSystem, eventDrivenScheduler, "eventdriven-latency");

        var eventDrivenLatencies = new List<double>();
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            eventDrivenScheduler.GetQueueSize();
            sw.Stop();
            eventDrivenLatencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        var eventDrivenAvg = eventDrivenLatencies.Average();
        var eventDrivenP50 = eventDrivenLatencies.OrderBy(x => x).ElementAt(iterations / 2);
        var eventDrivenP95 = eventDrivenLatencies.OrderBy(x => x).ElementAt((int)(iterations * 0.95));

        System.Console.WriteLine($"⚡🔥 Event-Driven Hybrid Query Latency:");
        System.Console.WriteLine($"   Average: {eventDrivenAvg:F3}ms");
        System.Console.WriteLine($"   P50: {eventDrivenP50:F3}ms");
        System.Console.WriteLine($"   P95: {eventDrivenP95:F3}ms");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Actor Mailbox Event-Driven
        var actorMailboxScheduler = new ActorMailboxEventDrivenScheduler(actorSystem, "actormailbox-latency");
        SetupScheduler(actorSystem, actorMailboxScheduler, "actormailbox-latency");

        var actorMailboxLatencies = new List<double>();
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            actorMailboxScheduler.GetQueueSize();
            sw.Stop();
            actorMailboxLatencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        var actorMailboxAvg = actorMailboxLatencies.Average();
        var actorMailboxP50 = actorMailboxLatencies.OrderBy(x => x).ElementAt(iterations / 2);
        var actorMailboxP95 = actorMailboxLatencies.OrderBy(x => x).ElementAt((int)(iterations * 0.95));

        System.Console.WriteLine($"📬⚡ Actor Mailbox Event-Driven Query Latency:");
        System.Console.WriteLine($"   Average: {actorMailboxAvg:F3}ms");
        System.Console.WriteLine($"   P50: {actorMailboxP50:F3}ms");
        System.Console.WriteLine($"   P95: {actorMailboxP95:F3}ms");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Ant Colony
        var antScheduler = new AntColonyScheduler(actorSystem, "ant-latency");
        SetupScheduler(actorSystem, antScheduler, "ant-latency");

        var antLatencies = new List<double>();
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            antScheduler.GetQueueSize();
            sw.Stop();
            antLatencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        var antAvg = antLatencies.Average();
        var antP50 = antLatencies.OrderBy(x => x).ElementAt(iterations / 2);
        var antP95 = antLatencies.OrderBy(x => x).ElementAt((int)(iterations * 0.95));

        System.Console.WriteLine($"🐜 Ant Colony Query Latency:");
        System.Console.WriteLine($"   Average: {antAvg:F3}ms");
        System.Console.WriteLine($"   P50: {antP50:F3}ms");
        System.Console.WriteLine($"   P95: {antP95:F3}ms");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Publication-Based
        var pubSubScheduler = new PublicationBasedScheduler(actorSystem, "pubsub-latency");
        SetupScheduler(actorSystem, pubSubScheduler, "pubsub-latency");

        var pubSubLatencies = new List<double>();
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            pubSubScheduler.GetQueueSize();
            sw.Stop();
            pubSubLatencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        var pubSubAvg = pubSubLatencies.Average();
        var pubSubP50 = pubSubLatencies.OrderBy(x => x).ElementAt(iterations / 2);
        var pubSubP95 = pubSubLatencies.OrderBy(x => x).ElementAt((int)(iterations * 0.95));

        System.Console.WriteLine($"📡 Publication-Based Query Latency:");
        System.Console.WriteLine($"   Average: {pubSubAvg:F3}ms");
        System.Console.WriteLine($"   P50: {pubSubP50:F3}ms");
        System.Console.WriteLine($"   P95: {pubSubP95:F3}ms");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Single Publication-Based
        var singlePubScheduler = new SinglePublicationScheduler(actorSystem, "singlepub-latency");
        SetupScheduler(actorSystem, singlePubScheduler, "singlepub-latency");

        var singlePubLatencies = new List<double>();
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            singlePubScheduler.GetQueueSize();
            sw.Stop();
            singlePubLatencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        var singlePubAvg = singlePubLatencies.Average();
        var singlePubP50 = singlePubLatencies.OrderBy(x => x).ElementAt(iterations / 2);
        var singlePubP95 = singlePubLatencies.OrderBy(x => x).ElementAt((int)(iterations * 0.95));

        System.Console.WriteLine($"📡⚡ Single Publication-Based Query Latency:");
        System.Console.WriteLine($"   Average: {singlePubAvg:F3}ms");
        System.Console.WriteLine($"   P50: {singlePubP50:F3}ms");
        System.Console.WriteLine($"   P95: {singlePubP95:F3}ms");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Array-Based Single Publication
        var arrayPubScheduler = new SinglePublicationSchedulerXState(actorSystem, "arraypub-latency");
        SetupScheduler(actorSystem, arrayPubScheduler, "arraypub-latency");

        var arrayPubLatencies = new List<double>();
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            arrayPubScheduler.GetQueueSize();
            sw.Stop();
            arrayPubLatencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        var arrayPubAvg = arrayPubLatencies.Average();
        var arrayPubP50 = arrayPubLatencies.OrderBy(x => x).ElementAt(iterations / 2);
        var arrayPubP95 = arrayPubLatencies.OrderBy(x => x).ElementAt((int)(iterations * 0.95));

        System.Console.WriteLine($"📡⚡🎯 Array Single Publication Query Latency:");
        System.Console.WriteLine($"   Average: {arrayPubAvg:F3}ms");
        System.Console.WriteLine($"   P50: {arrayPubP50:F3}ms");
        System.Console.WriteLine($"   P95: {arrayPubP95:F3}ms");
        System.Console.WriteLine();

        var actorAvgImprovement = ((lockAvg - actorAvg) / lockAvg) * 100;
        var xstateAvgImprovement = ((lockAvg - xstateAvg) / lockAvg) * 100;
        var arrayAvgImprovement = ((lockAvg - arrayAvg) / lockAvg) * 100;
        var autonomousAvgImprovement = ((lockAvg - autonomousAvg) / lockAvg) * 100;
        var hybridAvgImprovement = ((lockAvg - hybridAvg) / lockAvg) * 100;
        var eventDrivenAvgImprovement = ((lockAvg - eventDrivenAvg) / lockAvg) * 100;
        var actorMailboxAvgImprovement = ((lockAvg - actorMailboxAvg) / lockAvg) * 100;
        var antAvgImprovement = ((lockAvg - antAvg) / lockAvg) * 100;
        var pubSubAvgImprovement = ((lockAvg - pubSubAvg) / lockAvg) * 100;
        var singlePubAvgImprovement = ((lockAvg - singlePubAvg) / lockAvg) * 100;
        var arrayPubAvgImprovement = ((lockAvg - arrayPubAvg) / lockAvg) * 100;
        System.Console.WriteLine($"📊 Results:");
        System.Console.WriteLine($"   Actor average latency is {Math.Abs(actorAvgImprovement):F1}% {(actorAvgImprovement > 0 ? "lower" : "higher")} than Lock");
        System.Console.WriteLine($"   XState (FrozenDict) average latency is {Math.Abs(xstateAvgImprovement):F1}% {(xstateAvgImprovement > 0 ? "lower" : "higher")} than Lock");
        System.Console.WriteLine($"   XState (Array) average latency is {Math.Abs(arrayAvgImprovement):F1}% {(arrayAvgImprovement > 0 ? "lower" : "higher")} than Lock");
        System.Console.WriteLine($"   Autonomous average latency is {Math.Abs(autonomousAvgImprovement):F1}% {(autonomousAvgImprovement > 0 ? "lower" : "higher")} than Lock");
        System.Console.WriteLine($"   Hybrid average latency is {Math.Abs(hybridAvgImprovement):F1}% {(hybridAvgImprovement > 0 ? "lower" : "higher")} than Lock");
        System.Console.WriteLine($"   Event-Driven average latency is {Math.Abs(eventDrivenAvgImprovement):F1}% {(eventDrivenAvgImprovement > 0 ? "lower" : "higher")} than Lock");
        System.Console.WriteLine($"   Actor Mailbox average latency is {Math.Abs(actorMailboxAvgImprovement):F1}% {(actorMailboxAvgImprovement > 0 ? "lower" : "higher")} than Lock");
        System.Console.WriteLine($"   Ant Colony average latency is {Math.Abs(antAvgImprovement):F1}% {(antAvgImprovement > 0 ? "lower" : "higher")} than Lock");
        System.Console.WriteLine($"   Publication-Based average latency is {Math.Abs(pubSubAvgImprovement):F1}% {(pubSubAvgImprovement > 0 ? "lower" : "higher")} than Lock");
        System.Console.WriteLine($"   Single Publication average latency is {Math.Abs(singlePubAvgImprovement):F1}% {(singlePubAvgImprovement > 0 ? "lower" : "higher")} than Lock");
        System.Console.WriteLine($"   Array Single Publication average latency is {Math.Abs(arrayPubAvgImprovement):F1}% {(arrayPubAvgImprovement > 0 ? "lower" : "higher")} than Lock");
        System.Console.WriteLine();
    }

    private static async Task TestConcurrentLoad(ActorSystem actorSystem)
    {
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.WriteLine("Test 3: Concurrent Load (Multiple Threads)");
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.WriteLine();

        int threads = 10;
        int iterationsPerThread = 1000;

        // Lock-based
        var lockScheduler = new RobotScheduler();
        SetupScheduler(actorSystem, lockScheduler, "lock-concurrent");

        var sw1 = Stopwatch.StartNew();
        var lockTasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerThread; i++)
            {
                lockScheduler.RequestTransfer(new TransferRequest
                {
                    WaferId = (threadId * iterationsPerThread + i) % 10 + 1,
                    From = "Carrier",
                    To = "Polisher",
                    Priority = 1
                });
            }
        })).ToArray();

        Task.WaitAll(lockTasks);
        sw1.Stop();

        var lockConcurrentThroughput = (threads * iterationsPerThread) / sw1.Elapsed.TotalSeconds;
        System.Console.WriteLine($"🔒 Lock-based Concurrent:");
        System.Console.WriteLine($"   Threads: {threads}");
        System.Console.WriteLine($"   Requests per thread: {iterationsPerThread:N0}");
        System.Console.WriteLine($"   Total requests: {threads * iterationsPerThread:N0}");
        System.Console.WriteLine($"   Time: {sw1.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {lockConcurrentThroughput:N0} requests/sec");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Actor-based
        var actorScheduler = new RobotSchedulerActorProxy(actorSystem, "concurrent-scheduler");
        SetupScheduler(actorSystem, actorScheduler, "actor-concurrent");

        var sw2 = Stopwatch.StartNew();
        var actorTasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerThread; i++)
            {
                actorScheduler.RequestTransfer(new TransferRequest
                {
                    WaferId = (threadId * iterationsPerThread + i) % 10 + 1,
                    From = "Carrier",
                    To = "Polisher",
                    Priority = 1
                });
            }
        })).ToArray();

        Task.WaitAll(actorTasks);
        sw2.Stop();

        var actorConcurrentThroughput = (threads * iterationsPerThread) / sw2.Elapsed.TotalSeconds;
        System.Console.WriteLine($"🎭 Actor-based Concurrent:");
        System.Console.WriteLine($"   Threads: {threads}");
        System.Console.WriteLine($"   Requests per thread: {iterationsPerThread:N0}");
        System.Console.WriteLine($"   Total requests: {threads * iterationsPerThread:N0}");
        System.Console.WriteLine($"   Time: {sw2.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {actorConcurrentThroughput:N0} requests/sec");
        System.Console.WriteLine();

        await Task.Delay(100);

        // XState-based
        var xstateScheduler = new RobotSchedulerXState(actorSystem, "concurrent-xstate-scheduler");
        SetupScheduler(actorSystem, xstateScheduler, "xstate-concurrent");

        var sw3 = Stopwatch.StartNew();
        var xstateTasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerThread; i++)
            {
                xstateScheduler.RequestTransfer(new TransferRequest
                {
                    WaferId = (threadId * iterationsPerThread + i) % 10 + 1,
                    From = "Carrier",
                    To = "Polisher",
                    Priority = 1
                });
            }
        })).ToArray();

        Task.WaitAll(xstateTasks);
        sw3.Stop();

        var xstateConcurrentThroughput = (threads * iterationsPerThread) / sw3.Elapsed.TotalSeconds;
        System.Console.WriteLine($"🔄 XState-based (FrozenDict) Concurrent:");
        System.Console.WriteLine($"   Threads: {threads}");
        System.Console.WriteLine($"   Requests per thread: {iterationsPerThread:N0}");
        System.Console.WriteLine($"   Total requests: {threads * iterationsPerThread:N0}");
        System.Console.WriteLine($"   Time: {sw3.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {xstateConcurrentThroughput:N0} requests/sec");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Array-based (Actor + byte indices)
        var arrayScheduler = new RobotSchedulerXStateArray(actorSystem, "array-concurrent-scheduler");
        SetupScheduler(actorSystem, arrayScheduler, "array-concurrent");

        var sw4 = Stopwatch.StartNew();
        var arrayTasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerThread; i++)
            {
                arrayScheduler.RequestTransfer(new TransferRequest
                {
                    WaferId = (threadId * iterationsPerThread + i) % 10 + 1,
                    From = "Carrier",
                    To = "Polisher",
                    Priority = 1
                });
            }
        })).ToArray();

        Task.WaitAll(arrayTasks);
        sw4.Stop();

        var arrayConcurrentThroughput = (threads * iterationsPerThread) / sw4.Elapsed.TotalSeconds;
        System.Console.WriteLine($"⚡ XState-based (Array) Concurrent:");
        System.Console.WriteLine($"   Threads: {threads}");
        System.Console.WriteLine($"   Requests per thread: {iterationsPerThread:N0}");
        System.Console.WriteLine($"   Total requests: {threads * iterationsPerThread:N0}");
        System.Console.WriteLine($"   Time: {sw4.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {arrayConcurrentThroughput:N0} requests/sec");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Autonomous
        var autonomousScheduler = new AutonomousRobotScheduler();
        SetupScheduler(actorSystem, autonomousScheduler, "autonomous-concurrent");

        var sw5 = Stopwatch.StartNew();
        var autonomousTasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerThread; i++)
            {
                autonomousScheduler.RequestTransfer(new TransferRequest
                {
                    WaferId = (threadId * iterationsPerThread + i) % 10 + 1,
                    From = "Carrier",
                    To = "Polisher",
                    Priority = 1
                });
            }
        })).ToArray();

        Task.WaitAll(autonomousTasks);
        sw5.Stop();

        var autonomousConcurrentThroughput = (threads * iterationsPerThread) / sw5.Elapsed.TotalSeconds;
        System.Console.WriteLine($"🤖 Autonomous Concurrent:");
        System.Console.WriteLine($"   Threads: {threads}");
        System.Console.WriteLine($"   Requests per thread: {iterationsPerThread:N0}");
        System.Console.WriteLine($"   Total requests: {threads * iterationsPerThread:N0}");
        System.Console.WriteLine($"   Time: {sw5.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {autonomousConcurrentThroughput:N0} requests/sec (queue rate)");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Hybrid
        var hybridScheduler = new AutonomousArrayScheduler();
        SetupScheduler(actorSystem, hybridScheduler, "hybrid-concurrent");

        var sw6 = Stopwatch.StartNew();
        var hybridTasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerThread; i++)
            {
                hybridScheduler.RequestTransfer(new TransferRequest
                {
                    WaferId = (threadId * iterationsPerThread + i) % 10 + 1,
                    From = "Carrier",
                    To = "Polisher",
                    Priority = 1
                });
            }
        })).ToArray();

        Task.WaitAll(hybridTasks);
        sw6.Stop();

        var hybridConcurrentThroughput = (threads * iterationsPerThread) / sw6.Elapsed.TotalSeconds;
        System.Console.WriteLine($"🚀 Hybrid Concurrent:");
        System.Console.WriteLine($"   Threads: {threads}");
        System.Console.WriteLine($"   Requests per thread: {iterationsPerThread:N0}");
        System.Console.WriteLine($"   Total requests: {threads * iterationsPerThread:N0}");
        System.Console.WriteLine($"   Time: {sw6.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {hybridConcurrentThroughput:N0} requests/sec (queue rate)");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Event-Driven Hybrid
        var eventDrivenScheduler = new EventDrivenHybridScheduler();
        SetupScheduler(actorSystem, eventDrivenScheduler, "eventdriven-concurrent");

        var sw7 = Stopwatch.StartNew();
        var eventDrivenTasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerThread; i++)
            {
                eventDrivenScheduler.RequestTransfer(new TransferRequest
                {
                    WaferId = (threadId * iterationsPerThread + i) % 10 + 1,
                    From = "Carrier",
                    To = "Polisher",
                    Priority = 1
                });
            }
        })).ToArray();

        Task.WaitAll(eventDrivenTasks);
        sw7.Stop();

        var eventDrivenConcurrentThroughput = (threads * iterationsPerThread) / sw7.Elapsed.TotalSeconds;
        System.Console.WriteLine($"⚡🔥 Event-Driven Hybrid Concurrent:");
        System.Console.WriteLine($"   Threads: {threads}");
        System.Console.WriteLine($"   Requests per thread: {iterationsPerThread:N0}");
        System.Console.WriteLine($"   Total requests: {threads * iterationsPerThread:N0}");
        System.Console.WriteLine($"   Time: {sw7.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {eventDrivenConcurrentThroughput:N0} requests/sec (dispatch rate)");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Actor Mailbox Event-Driven
        var actorMailboxScheduler = new ActorMailboxEventDrivenScheduler(actorSystem, "actormailbox-concurrent");
        SetupScheduler(actorSystem, actorMailboxScheduler, "actormailbox-concurrent");

        var sw8 = Stopwatch.StartNew();
        var actorMailboxTasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerThread; i++)
            {
                actorMailboxScheduler.RequestTransfer(new TransferRequest
                {
                    WaferId = (threadId * iterationsPerThread + i) % 10 + 1,
                    From = "Carrier",
                    To = "Polisher",
                    Priority = 1
                });
            }
        })).ToArray();

        Task.WaitAll(actorMailboxTasks);
        sw8.Stop();

        var actorMailboxConcurrentThroughput = (threads * iterationsPerThread) / sw8.Elapsed.TotalSeconds;
        System.Console.WriteLine($"📬⚡ Actor Mailbox Event-Driven Concurrent:");
        System.Console.WriteLine($"   Threads: {threads}");
        System.Console.WriteLine($"   Requests per thread: {iterationsPerThread:N0}");
        System.Console.WriteLine($"   Total requests: {threads * iterationsPerThread:N0}");
        System.Console.WriteLine($"   Time: {sw8.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {actorMailboxConcurrentThroughput:N0} requests/sec (mailbox rate)");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Ant Colony (Decentralized)
        var antScheduler = new AntColonyScheduler(actorSystem, "ant-concurrent");
        SetupScheduler(actorSystem, antScheduler, "ant-concurrent");

        var sw9 = Stopwatch.StartNew();
        var antTasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerThread; i++)
            {
                antScheduler.RequestTransfer(new TransferRequest
                {
                    WaferId = (threadId * iterationsPerThread + i) % 10 + 1,
                    From = "Carrier",
                    To = "Polisher",
                    Priority = 1
                });
            }
        })).ToArray();

        Task.WaitAll(antTasks);
        sw9.Stop();

        var antConcurrentThroughput = (threads * iterationsPerThread) / sw9.Elapsed.TotalSeconds;
        System.Console.WriteLine($"🐜 Ant Colony Concurrent:");
        System.Console.WriteLine($"   Threads: {threads}");
        System.Console.WriteLine($"   Requests per thread: {iterationsPerThread:N0}");
        System.Console.WriteLine($"   Total requests: {threads * iterationsPerThread:N0}");
        System.Console.WriteLine($"   Time: {sw9.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {antConcurrentThroughput:N0} requests/sec (workpool rate)");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Publication-Based
        var pubSubScheduler = new PublicationBasedScheduler(actorSystem, "pubsub-concurrent");
        SetupScheduler(actorSystem, pubSubScheduler, "pubsub-concurrent");

        var sw10 = Stopwatch.StartNew();
        var pubSubTasks = Enumerable.Range(0, threads).Select(t => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerThread; i++)
            {
                pubSubScheduler.RequestTransfer(new TransferRequest
                {
                    WaferId = i % 10 + 1,
                    From = "Carrier",
                    To = "Polisher",
                    Priority = 1
                });
            }
        })).ToArray();

        Task.WaitAll(pubSubTasks);
        sw10.Stop();

        var pubSubConcurrentThroughput = (threads * iterationsPerThread) / sw10.Elapsed.TotalSeconds;
        System.Console.WriteLine($"📡 Publication-Based Concurrent:");
        System.Console.WriteLine($"   Threads: {threads}");
        System.Console.WriteLine($"   Requests per thread: {iterationsPerThread:N0}");
        System.Console.WriteLine($"   Total requests: {threads * iterationsPerThread:N0}");
        System.Console.WriteLine($"   Time: {sw10.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {pubSubConcurrentThroughput:N0} requests/sec (routing rate)");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Single Publication-Based
        var singlePubScheduler = new SinglePublicationScheduler(actorSystem, "singlepub-concurrent");
        SetupScheduler(actorSystem, singlePubScheduler, "singlepub-concurrent");

        var sw11 = Stopwatch.StartNew();
        var singlePubTasks = Enumerable.Range(0, threads).Select(t => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerThread; i++)
            {
                singlePubScheduler.RequestTransfer(new TransferRequest
                {
                    WaferId = i % 10 + 1,
                    From = "Carrier",
                    To = "Polisher",
                    Priority = 1
                });
            }
        })).ToArray();

        Task.WaitAll(singlePubTasks);
        sw11.Stop();

        var singlePubConcurrentThroughput = (threads * iterationsPerThread) / sw11.Elapsed.TotalSeconds;
        System.Console.WriteLine($"📡⚡ Single Publication-Based Concurrent:");
        System.Console.WriteLine($"   Threads: {threads}");
        System.Console.WriteLine($"   Requests per thread: {iterationsPerThread:N0}");
        System.Console.WriteLine($"   Total requests: {threads * iterationsPerThread:N0}");
        System.Console.WriteLine($"   Time: {sw11.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {singlePubConcurrentThroughput:N0} requests/sec (no routing rate)");
        System.Console.WriteLine();

        await Task.Delay(100);

        // Array-Based Single Publication
        var arrayPubScheduler = new SinglePublicationSchedulerXState(actorSystem, "arraypub-concurrent");
        SetupScheduler(actorSystem, arrayPubScheduler, "arraypub-concurrent");

        var sw12 = Stopwatch.StartNew();
        var arrayPubTasks = Enumerable.Range(0, threads).Select(t => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerThread; i++)
            {
                arrayPubScheduler.RequestTransfer(new TransferRequest
                {
                    WaferId = i % 10 + 1,
                    From = "Carrier",
                    To = "Polisher",
                    Priority = 1
                });
            }
        })).ToArray();

        Task.WaitAll(arrayPubTasks);
        sw12.Stop();

        var arrayPubConcurrentThroughput = (threads * iterationsPerThread) / sw12.Elapsed.TotalSeconds;
        System.Console.WriteLine($"📡⚡🎯 Array Single Publication Concurrent:");
        System.Console.WriteLine($"   Threads: {threads}");
        System.Console.WriteLine($"   Requests per thread: {iterationsPerThread:N0}");
        System.Console.WriteLine($"   Total requests: {threads * iterationsPerThread:N0}");
        System.Console.WriteLine($"   Time: {sw12.ElapsedMilliseconds:N0}ms");
        System.Console.WriteLine($"   Throughput: {arrayPubConcurrentThroughput:N0} requests/sec (array + no routing rate)");
        System.Console.WriteLine();

        var actorImprovement = ((actorConcurrentThroughput - lockConcurrentThroughput) / lockConcurrentThroughput) * 100;
        var xstateImprovement = ((xstateConcurrentThroughput - lockConcurrentThroughput) / lockConcurrentThroughput) * 100;
        var arrayImprovement = ((arrayConcurrentThroughput - lockConcurrentThroughput) / lockConcurrentThroughput) * 100;
        var autonomousImprovement = ((autonomousConcurrentThroughput - lockConcurrentThroughput) / lockConcurrentThroughput) * 100;
        var hybridImprovement = ((hybridConcurrentThroughput - lockConcurrentThroughput) / lockConcurrentThroughput) * 100;
        var eventDrivenImprovement = ((eventDrivenConcurrentThroughput - lockConcurrentThroughput) / lockConcurrentThroughput) * 100;
        var actorMailboxImprovement = ((actorMailboxConcurrentThroughput - lockConcurrentThroughput) / lockConcurrentThroughput) * 100;
        var antImprovement = ((antConcurrentThroughput - lockConcurrentThroughput) / lockConcurrentThroughput) * 100;
        var pubSubImprovement = ((pubSubConcurrentThroughput - lockConcurrentThroughput) / lockConcurrentThroughput) * 100;
        var singlePubImprovement = ((singlePubConcurrentThroughput - lockConcurrentThroughput) / lockConcurrentThroughput) * 100;
        var arrayPubImprovement = ((arrayPubConcurrentThroughput - lockConcurrentThroughput) / lockConcurrentThroughput) * 100;
        System.Console.WriteLine($"📊 Results:");
        System.Console.WriteLine($"   Actor is {Math.Abs(actorImprovement):F1}% {(actorImprovement > 0 ? "faster" : "slower")} than Lock under concurrent load");
        System.Console.WriteLine($"   XState (FrozenDict) is {Math.Abs(xstateImprovement):F1}% {(xstateImprovement > 0 ? "faster" : "slower")} than Lock under concurrent load");
        System.Console.WriteLine($"   XState (Array) is {Math.Abs(arrayImprovement):F1}% {(arrayImprovement > 0 ? "faster" : "slower")} than Lock under concurrent load");
        System.Console.WriteLine($"   Autonomous is {Math.Abs(autonomousImprovement):F1}% {(autonomousImprovement > 0 ? "faster" : "slower")} than Lock under concurrent load (queue rate)");
        System.Console.WriteLine($"   Hybrid is {Math.Abs(hybridImprovement):F1}% {(hybridImprovement > 0 ? "faster" : "slower")} than Lock under concurrent load (queue rate)");
        System.Console.WriteLine($"   Event-Driven is {Math.Abs(eventDrivenImprovement):F1}% {(eventDrivenImprovement > 0 ? "faster" : "slower")} than Lock under concurrent load (dispatch rate)");
        System.Console.WriteLine($"   Actor Mailbox is {Math.Abs(actorMailboxImprovement):F1}% {(actorMailboxImprovement > 0 ? "faster" : "slower")} than Lock under concurrent load (mailbox rate)");
        System.Console.WriteLine($"   Ant Colony is {Math.Abs(antImprovement):F1}% {(antImprovement > 0 ? "faster" : "slower")} than Lock under concurrent load (workpool rate)");
        System.Console.WriteLine($"   Publication-Based is {Math.Abs(pubSubImprovement):F1}% {(pubSubImprovement > 0 ? "faster" : "slower")} than Lock under concurrent load (routing rate)");
        System.Console.WriteLine($"   Single Publication is {Math.Abs(singlePubImprovement):F1}% {(singlePubImprovement > 0 ? "faster" : "slower")} than Lock under concurrent load (no routing rate)");
        System.Console.WriteLine($"   Array Single Publication is {Math.Abs(arrayPubImprovement):F1}% {(arrayPubImprovement > 0 ? "faster" : "slower")} than Lock under concurrent load (array + no routing rate)");
        System.Console.WriteLine();
    }

    private static void SetupScheduler(ActorSystem system, IRobotScheduler scheduler, string prefix)
    {
        var robotIds = new[] { "Robot 1", "Robot 2", "Robot 3" };
        for (int i = 0; i < robotIds.Length; i++)
        {
            var robotActor = system.ActorOf(Props.Create(() => new BenchmarkRobotActor()), $"{prefix}-robot{i}");
            scheduler.RegisterRobot(robotIds[i], robotActor);
            scheduler.UpdateRobotState(robotIds[i], "idle");
        }
    }

    private class BenchmarkRobotActor : ReceiveActor
    {
        public BenchmarkRobotActor()
        {
            ReceiveAny(_ => { /* Ignore all messages for benchmark */ });
        }
    }
}
