using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace XStateNet2.Tests.AkkaNet;

/// <summary>
/// Pure Akka.NET tests to understand ActorSystem sharing characteristics.
/// Tests the fundamental behavior of individual ActorSystems under various load conditions.
///
/// GOAL: Determine reliability and performance characteristics of individual ActorSystems
/// when running in parallel (simulating xUnit parallel test execution).
///
/// APPROACH: All tests use TestKit (individual ActorSystems) to stay within framework constraints.
/// We measure timing and success rates under different parallel load scenarios.
/// </summary>
public class ActorSystemSharingTests : TestKit
{
    private readonly ITestOutputHelper _output;

    public ActorSystemSharingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Test Actor Definitions

    public class EchoActor : ReceiveActor
    {
        public EchoActor()
        {
            ReceiveAny(msg => Sender.Tell($"Echo: {msg}"));
        }
    }

    public class CounterActor : ReceiveActor
    {
        private int _count = 0;

        public class Increment { }
        public class GetCount { }

        public CounterActor()
        {
            Receive<Increment>(_ => _count++);
            Receive<GetCount>(_ => Sender.Tell(_count));
        }
    }

    #endregion

    #region Baseline Tests - Individual ActorSystem

    [Fact]
    public void Baseline_EchoActor_ShouldRespondCorrectly()
    {
        // Arrange
        var echo = Sys.ActorOf(Props.Create<EchoActor>(), "echo-baseline-1");

        // Act
        echo.Tell("Hello", TestActor);

        // Assert
        ExpectMsg<string>("Echo: Hello", TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Baseline_CounterActor_ShouldIncrementCorrectly()
    {
        // Arrange
        var counter = Sys.ActorOf(Props.Create<CounterActor>(), "counter-baseline-1");

        // Act
        counter.Tell(new CounterActor.Increment(), TestActor);
        counter.Tell(new CounterActor.Increment(), TestActor);
        counter.Tell(new CounterActor.Increment(), TestActor);
        counter.Tell(new CounterActor.GetCount(), TestActor);

        // Assert
        ExpectMsg<int>(3, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Baseline_MultipleActors_ShouldWorkIndependently()
    {
        // Arrange
        var echo1 = Sys.ActorOf(Props.Create<EchoActor>(), "echo-1");
        var echo2 = Sys.ActorOf(Props.Create<EchoActor>(), "echo-2");
        var counter = Sys.ActorOf(Props.Create<CounterActor>(), "counter-1");

        // Act & Assert - process messages from each actor independently
        echo1.Tell("Message1", TestActor);
        ExpectMsg<string>("Echo: Message1", TimeSpan.FromSeconds(1));

        echo2.Tell("Message2", TestActor);
        ExpectMsg<string>("Echo: Message2", TimeSpan.FromSeconds(1));

        counter.Tell(new CounterActor.Increment(), TestActor);
        counter.Tell(new CounterActor.GetCount(), TestActor);
        ExpectMsg<int>(1, TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Parallel Load Tests - Simulating Multiple Tests Running Concurrently

    [Fact]
    public void ParallelLoad_SimulateMultipleTestKits_Sequential()
    {
        // This test simulates what happens when multiple tests run sequentially
        // Each iteration creates a new TestKit (ActorSystem), uses it, then disposes

        var sw = Stopwatch.StartNew();
        var iterations = 10;

        for (int i = 0; i < iterations; i++)
        {
            using var testKit = new TestKit($"test-system-{i}");
            var echo = testKit.Sys.ActorOf(Props.Create<EchoActor>(), $"echo-{i}");
            echo.Tell($"Message-{i}", testKit.TestActor);
            testKit.ExpectMsg<string>($"Echo: Message-{i}", TimeSpan.FromSeconds(2));
        }

        sw.Stop();
        _output.WriteLine($"Sequential execution of {iterations} TestKits: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void ParallelLoad_SimulateMultipleTestKits_Parallel()
    {
        // This test simulates what happens when multiple tests run in parallel
        // This is what xUnit does when running tests concurrently

        var sw = Stopwatch.StartNew();
        var iterations = 10;
        var successCount = 0;
        var failures = new List<string>();

        var tasks = Enumerable.Range(0, iterations).Select(i => Task.Run(() =>
        {
            try
            {
                using var testKit = new TestKit($"parallel-test-system-{i}");
                var echo = testKit.Sys.ActorOf(Props.Create<EchoActor>(), $"echo-parallel-{i}");
                echo.Tell($"Message-{i}", testKit.TestActor);
                testKit.ExpectMsg<string>($"Echo: Message-{i}", TimeSpan.FromSeconds(3));
                return (Success: true, Error: (string?)null);
            }
            catch (Exception ex)
            {
                return (Success: false, Error: $"Iteration {i}: {ex.Message}");
            }
        })).ToArray();

        var results = Task.WhenAll(tasks).Result;
        successCount = results.Count(r => r.Success);
        failures = results.Where(r => !r.Success).Select(r => r.Error!).ToList();

        sw.Stop();

        _output.WriteLine($"Parallel execution of {iterations} TestKits: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Success rate: {successCount}/{iterations} ({100.0 * successCount / iterations:F1}%)");

        if (failures.Any())
        {
            _output.WriteLine($"Failures:");
            foreach (var failure in failures)
            {
                _output.WriteLine($"  - {failure}");
            }
        }

        // We expect high success rate even under parallel load
        successCount.Should().BeGreaterThanOrEqualTo((int)(iterations * 0.9),
            "at least 90% of parallel TestKit creations should succeed");
    }

    [Fact]
    public void HeavyParallelLoad_24TestKits_SimulatingFullTestSuite()
    {
        // This simulates the full test suite scenario (24 robot scheduler tests)
        // running in parallel as xUnit does by default

        var sw = Stopwatch.StartNew();
        var iterations = 24;
        var successCount = 0;
        var failures = new List<string>();

        var tasks = Enumerable.Range(0, iterations).Select(i => Task.Run(() =>
        {
            try
            {
                using var testKit = new TestKit($"heavy-test-system-{i}");
                var counter = testKit.Sys.ActorOf(Props.Create<CounterActor>(), $"counter-heavy-{i}");

                // Simulate some work
                for (int j = 0; j < 5; j++)
                {
                    counter.Tell(new CounterActor.Increment(), testKit.TestActor);
                }

                counter.Tell(new CounterActor.GetCount(), testKit.TestActor);
                testKit.ExpectMsg<int>(5, TimeSpan.FromSeconds(5));

                return (Success: true, Error: (string?)null);
            }
            catch (Exception ex)
            {
                return (Success: false, Error: $"Test {i}: {ex.Message}");
            }
        })).ToArray();

        var results = Task.WhenAll(tasks).Result;
        successCount = results.Count(r => r.Success);
        failures = results.Where(r => !r.Success).Select(r => r.Error!).ToList();

        sw.Stop();

        _output.WriteLine($"Heavy parallel execution of {iterations} TestKits: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Success rate: {successCount}/{iterations} ({100.0 * successCount / iterations:F1}%)");
        _output.WriteLine($"Thread pool stats: Worker threads available, I/O threads available");

        if (failures.Any())
        {
            _output.WriteLine($"Failures ({failures.Count}):");
            foreach (var failure in failures)
            {
                _output.WriteLine($"  - {failure}");
            }
        }

        // Document the success rate - this tells us the baseline reliability
        // The current RobotScheduler tests show 92% (22/24) pass rate
        _output.WriteLine($"\nComparison: RobotScheduler tests have 92% pass rate (22/24)");
        _output.WriteLine($"This pure Akka.NET test achieved {100.0 * successCount / iterations:F1}% pass rate");
    }

    #endregion

    #region Resource Measurement Tests

    [Fact]
    public void ResourceMeasurement_ThreadPoolUtilization()
    {
        // Measure thread pool usage before and after creating multiple ActorSystems

        ThreadPool.GetAvailableThreads(out int workerBefore, out int ioBefore);
        ThreadPool.GetMaxThreads(out int maxWorker, out int maxIo);

        _output.WriteLine($"Before: Worker threads: {workerBefore}/{maxWorker}, I/O threads: {ioBefore}/{maxIo}");

        var systems = new List<ActorSystem>();

        try
        {
            // Create 24 ActorSystems (simulating parallel test execution)
            for (int i = 0; i < 24; i++)
            {
                systems.Add(ActorSystem.Create($"resource-test-{i}"));
            }

            ThreadPool.GetAvailableThreads(out int workerDuring, out int ioDuring);
            _output.WriteLine($"During: Worker threads: {workerDuring}/{maxWorker}, I/O threads: {ioDuring}/{maxIo}");
            _output.WriteLine($"Thread consumption: {workerBefore - workerDuring} worker threads, {ioBefore - ioDuring} I/O threads");
        }
        finally
        {
            // Clean up
            foreach (var system in systems)
            {
                system.Terminate().Wait(TimeSpan.FromSeconds(5));
                system.Dispose();
            }
        }

        ThreadPool.GetAvailableThreads(out int workerAfter, out int ioAfter);
        _output.WriteLine($"After: Worker threads: {workerAfter}/{maxWorker}, I/O threads: {ioAfter}/{maxIo}");
    }

    #endregion
}
