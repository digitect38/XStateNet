using Akka.TestKit.Xunit2;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using Akka.Actor;

namespace XStateNet2.Tests;

/// <summary>
/// Performance comparison between Legacy XStateNet and XStateNet2
/// Tests throughput, latency, and concurrent processing
/// </summary>
public class PerformanceComparisonTests : TestKit
{
    private readonly ITestOutputHelper _output;

    public PerformanceComparisonTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Throughput_SimpleTransitions_XStateNet2()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": { "on": { "START": "running" } },
                "running": { "on": { "STOP": "idle" } }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Warm-up
        for (int i = 0; i < 100; i++)
        {
            machine.Tell(new SendEvent("START"));
            machine.Tell(new SendEvent("STOP"));
        }
        // Wait for warm-up completion using Ask pattern (deterministic)
        var warmupState = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2)).Result;

        // Act - Measure throughput
        var iterations = 10000;
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            machine.Tell(new SendEvent("START"));
            machine.Tell(new SendEvent("STOP"));
        }

        // Wait for all messages to be processed using Ask (deterministic)
        var finalState = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2)).Result;
        sw.Stop();

        // Assert
        var totalTransitions = iterations * 2;
        var throughput = totalTransitions / sw.Elapsed.TotalSeconds;

        _output.WriteLine($"XStateNet2 Throughput:");
        _output.WriteLine($"  Transitions: {totalTransitions:N0}");
        _output.WriteLine($"  Time: {sw.ElapsedMilliseconds:N0}ms");
        _output.WriteLine($"  Throughput: {throughput:N0} transitions/sec");
        _output.WriteLine($"  Avg latency: {sw.Elapsed.TotalMilliseconds / totalTransitions:F3}ms per transition");

        // Baseline: Actor model should handle at least 10,000 transitions/sec
        Assert.True(throughput > 10000, $"Throughput too low: {throughput:N0} transitions/sec");
    }

    [Fact]
    public void Latency_StateQuery_XStateNet2()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": { "on": { "START": "running" } },
                "running": { "on": { "STOP": "idle" } }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Warm-up (use longer timeout - not part of performance measurement)
        for (int i = 0; i < 100; i++)
        {
            var _ = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3)).Result;
        }

        // Act - Measure query latency
        var iterations = 1000;
        var latencies = new List<double>();

        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            var snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(1)).Result;
            sw.Stop();
            latencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        // Assert
        var avgLatency = latencies.Average();
        var p50 = latencies.OrderBy(x => x).ElementAt(iterations / 2);
        var p95 = latencies.OrderBy(x => x).ElementAt((int)(iterations * 0.95));
        var p99 = latencies.OrderBy(x => x).ElementAt((int)(iterations * 0.99));

        _output.WriteLine($"XStateNet2 Query Latency:");
        _output.WriteLine($"  Iterations: {iterations:N0}");
        _output.WriteLine($"  Average: {avgLatency:F3}ms");
        _output.WriteLine($"  P50: {p50:F3}ms");
        _output.WriteLine($"  P95: {p95:F3}ms");
        _output.WriteLine($"  P99: {p99:F3}ms");

        // Actor Ask pattern should be fast (< 5ms average)
        Assert.True(avgLatency < 5, $"Average latency too high: {avgLatency:F3}ms");
    }

    [Fact]
    public void Concurrent_MultipleActors_XStateNet2()
    {
        // Arrange - Create multiple independent state machines
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": { "on": { "WORK": "processing" } },
                "processing": {
                    "on": { "DONE": "idle" },
                    "entry": ["logProcessing"]
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var actorCount = 100;
        var machines = new List<Akka.Actor.IActorRef>();

        for (int i = 0; i < actorCount; i++)
        {
            var machine = factory.FromJson(json)
                .WithAction("logProcessing", (ctx, evt) => { /* simulate work */ })
                .BuildAndStart($"machine{i}");
            machines.Add(machine);
        }

        // Act - Send events to all actors concurrently
        var iterations = 100;
        var sw = Stopwatch.StartNew();

        var tasks = machines.Select(machine => Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                machine.Tell(new SendEvent("WORK"));
                machine.Tell(new SendEvent("DONE"));
            }
        })).ToArray();

        Task.WaitAll(tasks);
        // Wait for all machines to process messages (deterministic)
        foreach (var m in machines)
        {
            var state = m.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2)).Result;
        }
        sw.Stop();

        // Assert
        var totalTransitions = actorCount * iterations * 2;
        var throughput = totalTransitions / sw.Elapsed.TotalSeconds;

        _output.WriteLine($"XStateNet2 Concurrent Processing:");
        _output.WriteLine($"  Actors: {actorCount}");
        _output.WriteLine($"  Iterations per actor: {iterations}");
        _output.WriteLine($"  Total transitions: {totalTransitions:N0}");
        _output.WriteLine($"  Time: {sw.ElapsedMilliseconds:N0}ms");
        _output.WriteLine($"  Throughput: {throughput:N0} transitions/sec");

        // With actor model, concurrent processing should scale well
        // Baseline: 20,000 transitions/sec is reasonable for concurrent actor processing
        Assert.True(throughput > 20000, $"Concurrent throughput too low: {throughput:N0} transitions/sec");
    }

    [Fact]
    public void Memory_ActorOverhead_XStateNet2()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": { "on": { "START": "running" } },
                "running": { "on": { "STOP": "idle" } }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);

        // Measure memory before
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memoryBefore = GC.GetTotalMemory(true);

        // Act - Create many actors
        var actorCount = 1000;
        var machines = new List<Akka.Actor.IActorRef>();

        for (int i = 0; i < actorCount; i++)
        {
            var machine = factory.FromJson(json).BuildAndStart($"machine{i}");
            machines.Add(machine);
        }

        // Measure memory after
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memoryAfter = GC.GetTotalMemory(true);

        // Assert
        var memoryPerActor = (memoryAfter - memoryBefore) / actorCount;

        _output.WriteLine($"XStateNet2 Memory Usage:");
        _output.WriteLine($"  Actors created: {actorCount}");
        _output.WriteLine($"  Memory before: {memoryBefore / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"  Memory after: {memoryAfter / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"  Total overhead: {(memoryAfter - memoryBefore) / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"  Per actor: {memoryPerActor / 1024.0:F2} KB");

        // Each actor should use less than 50KB
        Assert.True(memoryPerActor < 50 * 1024, $"Memory per actor too high: {memoryPerActor / 1024.0:F2} KB");
    }

    [Fact]
    public void ComplexState_NestedTransitions_XStateNet2()
    {
        // Arrange - Complex nested state machine
        var json = """
        {
            "id": "complex",
            "initial": "level1",
            "states": {
                "level1": {
                    "initial": "level2a",
                    "states": {
                        "level2a": {
                            "initial": "level3a",
                            "states": {
                                "level3a": { "on": { "NEXT": "level3b" } },
                                "level3b": { "on": { "UP": "#complex.level1.level2b" } }
                            }
                        },
                        "level2b": {
                            "on": { "TOP": "#complex.final" }
                        }
                    }
                },
                "final": { "type": "final" }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Warm-up
        for (int i = 0; i < 100; i++)
        {
            machine.Tell(new SendEvent("NEXT"));
            machine.Tell(new SendEvent("UP"));
            machine.Tell(new SendEvent("TOP"));
        }
        // Wait for warm-up completion (deterministic)
        var warmupState = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2)).Result;

        // Act - Measure complex transitions
        var iterations = 1000;
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            machine.Tell(new SendEvent("NEXT"));
            machine.Tell(new SendEvent("UP"));
            machine.Tell(new SendEvent("TOP"));
        }

        // Wait for all messages to be processed (deterministic)
        var finalState = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2)).Result;
        sw.Stop();

        // Assert
        var totalTransitions = iterations * 3;
        var throughput = totalTransitions / sw.Elapsed.TotalSeconds;

        _output.WriteLine($"XStateNet2 Complex State Transitions:");
        _output.WriteLine($"  Iterations: {iterations:N0}");
        _output.WriteLine($"  Total transitions: {totalTransitions:N0}");
        _output.WriteLine($"  Time: {sw.ElapsedMilliseconds:N0}ms");
        _output.WriteLine($"  Throughput: {throughput:N0} transitions/sec");
        _output.WriteLine($"  Avg time per transition: {sw.Elapsed.TotalMilliseconds / totalTransitions:F3}ms");

        // Complex transitions should still be fast
        Assert.True(throughput > 5000, $"Complex transition throughput too low: {throughput:N0} transitions/sec");
    }
}
