using Akka.Actor;
using FluentAssertions;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Extensions;
using XStateNet2.Tests;

namespace XStateNet2.Tests.CMPSimulator;

/// <summary>
/// Throughput tests for processing multiple successive carriers
/// Each carrier contains 25 wafers
/// Tests: 2, 5, 10, 100 carriers (50, 125, 250, 2500 wafers)
/// </summary>
public class MultiCarrierThroughputTests : XStateTestKit
{
    private const int WafersPerCarrier = 25;

    #region State Machine Definitions

    private string GetCarrierMachineJson() => """
    {
        "id": "carrier",
        "initial": "loading",
        "context": {
            "carrierId": 0,
            "totalSlots": 25,
            "unprocessedWafers": [],
            "processedWafers": [],
            "totalProcessed": 0
        },
        "states": {
            "loading": {
                "entry": ["reportLoading"],
                "on": {
                    "LOAD": {
                        "target": "processing",
                        "actions": ["loadCarrier"]
                    }
                }
            },
            "processing": {
                "entry": ["reportProcessing"],
                "on": {
                    "PICK": {
                        "actions": ["removeWafer"],
                        "cond": "hasUnprocessedWafers"
                    },
                    "PLACE": {
                        "actions": ["returnWafer", "checkCompletion"]
                    },
                    "ALL_COMPLETE": {
                        "target": "completed"
                    }
                }
            },
            "completed": {
                "entry": ["reportCompleted"],
                "on": {
                    "UNLOAD": {
                        "target": "loading",
                        "actions": ["unloadCarrier"]
                    }
                }
            }
        }
    }
    """;

    #endregion

    #region 2 Carriers Test (50 wafers)

    [Fact]
    public void MultiCarrier_TwoCarriers_50Wafers_ShouldProcessSuccessfully()
    {
        var carrierCount = 2;
        var totalWafers = carrierCount * WafersPerCarrier;
        var stats = new ProcessingStats();

        var (carrier, polisher, cleaner, buffer) = CreateCMPSystem(stats);

        // Process 2 carriers
        for (int c = 0; c < carrierCount; c++)
        {
            ProcessSingleCarrier(carrier, polisher, cleaner, buffer, c + 1, stats);
        }

        // Verify statistics
        stats.TotalCarriersProcessed.Should().Be(carrierCount);
        stats.TotalWafersProcessed.Should().Be(totalWafers);
        stats.PolisherCycles.Should().Be(totalWafers);
        stats.CleanerCycles.Should().Be(totalWafers);
        stats.BufferCycles.Should().Be(totalWafers);

        // All stations should be idle/empty
        carrier.GetStateSnapshot().CurrentState.Should().Be("loading");
        polisher.GetStateSnapshot().CurrentState.Should().Be("idle");
        cleaner.GetStateSnapshot().CurrentState.Should().Be("idle");
        buffer.GetStateSnapshot().CurrentState.Should().Be("empty");
    }

    #endregion

    #region 5 Carriers Test (125 wafers)

    [Fact]
    public void MultiCarrier_FiveCarriers_125Wafers_ShouldProcessSuccessfully()
    {
        var carrierCount = 5;
        var totalWafers = carrierCount * WafersPerCarrier;
        var stats = new ProcessingStats();

        var (carrier, polisher, cleaner, buffer) = CreateCMPSystem(stats);

        // Process 5 carriers
        for (int c = 0; c < carrierCount; c++)
        {
            ProcessSingleCarrier(carrier, polisher, cleaner, buffer, c + 1, stats);
        }

        // Verify statistics
        stats.TotalCarriersProcessed.Should().Be(carrierCount);
        stats.TotalWafersProcessed.Should().Be(totalWafers);
        stats.PolisherCycles.Should().Be(totalWafers);
        stats.CleanerCycles.Should().Be(totalWafers);
        stats.BufferCycles.Should().Be(totalWafers);

        // Verify no wafers lost
        stats.WafersPickedFromCarrier.Should().Be(totalWafers);
        stats.WafersReturnedToCarrier.Should().Be(totalWafers);
    }

    #endregion

    #region 10 Carriers Test (250 wafers)

    [Fact]
    public void MultiCarrier_TenCarriers_250Wafers_ShouldProcessSuccessfully()
    {
        var carrierCount = 10;
        var totalWafers = carrierCount * WafersPerCarrier;
        var stats = new ProcessingStats();

        var (carrier, polisher, cleaner, buffer) = CreateCMPSystem(stats);

        // Process 10 carriers
        for (int c = 0; c < carrierCount; c++)
        {
            ProcessSingleCarrier(carrier, polisher, cleaner, buffer, c + 1, stats);
        }

        // Verify statistics
        stats.TotalCarriersProcessed.Should().Be(carrierCount);
        stats.TotalWafersProcessed.Should().Be(totalWafers);
        stats.PolisherCycles.Should().Be(totalWafers);
        stats.CleanerCycles.Should().Be(totalWafers);
        stats.BufferCycles.Should().Be(totalWafers);

        // Verify wafer flow integrity
        stats.WafersPickedFromCarrier.Should().Be(totalWafers);
        stats.WafersReturnedToCarrier.Should().Be(totalWafers);

        // All stations idle
        carrier.GetStateSnapshot().CurrentState.Should().Be("loading");
        polisher.GetStateSnapshot().CurrentState.Should().Be("idle");
        cleaner.GetStateSnapshot().CurrentState.Should().Be("idle");
        buffer.GetStateSnapshot().CurrentState.Should().Be("empty");
    }

    #endregion

    #region 100 Carriers Test (2,500 wafers)

    [Fact]
    public void MultiCarrier_100Carriers_2500Wafers_ShouldProcessSuccessfully()
    {
        var carrierCount = 100;
        var totalWafers = carrierCount * WafersPerCarrier;
        var stats = new ProcessingStats();

        var (carrier, polisher, cleaner, buffer) = CreateCMPSystem(stats);

        var startTime = DateTime.UtcNow;

        // Process 100 carriers
        for (int c = 0; c < carrierCount; c++)
        {
            ProcessSingleCarrier(carrier, polisher, cleaner, buffer, c + 1, stats);

            // Log progress every 10 carriers
            if ((c + 1) % 10 == 0)
            {
                var elapsed = DateTime.UtcNow - startTime;
                var wafersProcessed = (c + 1) * WafersPerCarrier;
                System.Diagnostics.Debug.WriteLine(
                    $"Progress: {c + 1}/{carrierCount} carriers, {wafersProcessed}/{totalWafers} wafers, " +
                    $"Elapsed: {elapsed.TotalSeconds:F2}s");
            }
        }

        var totalTime = DateTime.UtcNow - startTime;

        // Verify statistics
        stats.TotalCarriersProcessed.Should().Be(carrierCount);
        stats.TotalWafersProcessed.Should().Be(totalWafers);
        stats.PolisherCycles.Should().Be(totalWafers);
        stats.CleanerCycles.Should().Be(totalWafers);
        stats.BufferCycles.Should().Be(totalWafers);

        // Verify wafer flow integrity
        stats.WafersPickedFromCarrier.Should().Be(totalWafers);
        stats.WafersReturnedToCarrier.Should().Be(totalWafers);

        // All stations idle
        carrier.GetStateSnapshot().CurrentState.Should().Be("loading");
        polisher.GetStateSnapshot().CurrentState.Should().Be("idle");
        cleaner.GetStateSnapshot().CurrentState.Should().Be("idle");
        buffer.GetStateSnapshot().CurrentState.Should().Be("empty");

        // Performance logging
        System.Diagnostics.Debug.WriteLine($"\n=== 100 Carriers Performance ===");
        System.Diagnostics.Debug.WriteLine($"Total carriers: {carrierCount}");
        System.Diagnostics.Debug.WriteLine($"Total wafers: {totalWafers}");
        System.Diagnostics.Debug.WriteLine($"Total time: {totalTime.TotalSeconds:F2}s");
        System.Diagnostics.Debug.WriteLine($"Throughput: {totalWafers / totalTime.TotalSeconds:F2} wafers/sec");
        System.Diagnostics.Debug.WriteLine($"Avg time per carrier: {totalTime.TotalMilliseconds / carrierCount:F2}ms");
        System.Diagnostics.Debug.WriteLine($"Avg time per wafer: {totalTime.TotalMilliseconds / totalWafers:F2}ms");
    }

    #endregion

    #region Carrier Cycle Validation

    [Fact]
    public void MultiCarrier_CarrierStateTransitions_ShouldBeCyclical()
    {
        var stats = new ProcessingStats();
        var carrierStates = new List<string>();

        var factory = new XStateMachineFactory(Sys);

        var carrier = factory.FromJson(GetCarrierMachineJson())
            .WithAction("reportLoading", (ctx, _) => carrierStates.Add("loading"))
            .WithAction("loadCarrier", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("carrierId"))
                {
                    ctx.Set("carrierId", data["carrierId"]);
                    var wafers = Enumerable.Range(1, WafersPerCarrier).ToList();
                    ctx.Set("unprocessedWafers", wafers);
                    ctx.Set("processedWafers", new List<int>());
                }
            })
            .WithAction("reportProcessing", (ctx, _) => carrierStates.Add("processing"))
            .WithAction("removeWafer", (ctx, _) => { })
            .WithAction("returnWafer", (ctx, _) => { })
            .WithAction("checkCompletion", (ctx, _) => { })
            .WithAction("reportCompleted", (ctx, _) => carrierStates.Add("completed"))
            .WithAction("unloadCarrier", (ctx, _) =>
            {
                ctx.Set("carrierId", 0);
                ctx.Set("unprocessedWafers", new List<int>());
                ctx.Set("processedWafers", new List<int>());
            })
            .WithGuard("hasUnprocessedWafers", (ctx, _) => false)
            .BuildAndStart();

        WaitForStateName(carrier, "loading");

        // Process 3 carriers to verify cycling
        for (int c = 1; c <= 3; c++)
        {
            // Load carrier
            SendEventAndWait(carrier, "LOAD",
                s => s.CurrentState == "processing",
                "processing",
                new Dictionary<string, object> { ["carrierId"] = c });

            // Complete processing
            SendEventAndWait(carrier, "ALL_COMPLETE",
                s => s.CurrentState == "completed",
                "completed");

            // Unload carrier
            SendEventAndWait(carrier, "UNLOAD",
                s => s.CurrentState == "loading",
                "loading");
        }

        // Verify state cycle pattern: loading → processing → completed → loading (repeated)
        carrierStates.Should().ContainInOrder(
            "loading", "processing", "completed",  // Carrier 1
            "loading", "processing", "completed",  // Carrier 2
            "loading", "processing", "completed",  // Carrier 3
            "loading"                              // Ready for Carrier 4
        );
    }

    #endregion

    #region Performance Benchmark

    [Fact]
    public void MultiCarrier_PerformanceBenchmark_AllScenarios()
    {
        var scenarios = new[] { 2, 5, 10, 100 };
        var results = new List<(int carriers, int wafers, double seconds, double wafersPerSec)>();

        foreach (var carrierCount in scenarios)
        {
            var totalWafers = carrierCount * WafersPerCarrier;
            var stats = new ProcessingStats();
            var (carrier, polisher, cleaner, buffer) = CreateCMPSystem(stats);

            var startTime = DateTime.UtcNow;

            for (int c = 0; c < carrierCount; c++)
            {
                ProcessSingleCarrier(carrier, polisher, cleaner, buffer, c + 1, stats);
            }

            var elapsed = DateTime.UtcNow - startTime;
            var throughput = totalWafers / elapsed.TotalSeconds;

            results.Add((carrierCount, totalWafers, elapsed.TotalSeconds, throughput));

            stats.TotalCarriersProcessed.Should().Be(carrierCount);
            stats.TotalWafersProcessed.Should().Be(totalWafers);
        }

        // Print benchmark results
        System.Diagnostics.Debug.WriteLine("\n=== CMP Throughput Benchmark ===");
        System.Diagnostics.Debug.WriteLine("Carriers | Wafers | Time(s) | Throughput(wafers/s)");
        System.Diagnostics.Debug.WriteLine("---------|--------|---------|--------------------");
        foreach (var (carriers, wafers, seconds, wps) in results)
        {
            System.Diagnostics.Debug.WriteLine($"{carriers,8} | {wafers,6} | {seconds,7:F2} | {wps,18:F2}");
        }
        System.Diagnostics.Debug.WriteLine("================================\n");

        // All scenarios should complete successfully
        results.Should().HaveCount(scenarios.Length);
    }

    #endregion

    #region Helper Methods

    private (IActorRef carrier, IActorRef polisher, IActorRef cleaner, IActorRef buffer)
        CreateCMPSystem(ProcessingStats stats, string? nameSuffix = null)
    {
        var suffix = nameSuffix ?? Guid.NewGuid().ToString("N").Substring(0, 8);
        var factory = new XStateMachineFactory(Sys);

        // Create Carrier
        var carrier = factory.FromJson(GetCarrierMachineJson())
            .WithAction("reportLoading", (ctx, _) => { })
            .WithAction("loadCarrier", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("carrierId"))
                {
                    ctx.Set("carrierId", data["carrierId"]);
                    var wafers = Enumerable.Range(1, WafersPerCarrier).ToList();
                    ctx.Set("unprocessedWafers", wafers);
                    ctx.Set("processedWafers", new List<int>());
                    ctx.Set("totalProcessed", 0);
                }
            })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("removeWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("wafer"))
                {
                    stats.WafersPickedFromCarrier++;
                }
            })
            .WithAction("returnWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("wafer"))
                {
                    var processed = ctx.Get<int>("totalProcessed");
                    ctx.Set("totalProcessed", processed + 1);
                    stats.WafersReturnedToCarrier++;
                }
            })
            .WithAction("checkCompletion", (ctx, _) =>
            {
                // Completion will be triggered externally after all wafers are processed
            })
            .WithAction("reportCompleted", (ctx, _) => { })
            .WithAction("unloadCarrier", (ctx, _) =>
            {
                stats.TotalCarriersProcessed++;
                ctx.Set("carrierId", 0);
                ctx.Set("unprocessedWafers", new List<int>());
                ctx.Set("processedWafers", new List<int>());
                ctx.Set("totalProcessed", 0);
            })
            .WithGuard("hasUnprocessedWafers", (ctx, _) =>
            {
                var processed = ctx.Get<int>("totalProcessed");
                return processed < WafersPerCarrier;
            })
            .BuildAndStart($"carrier-{suffix}");

        // Create Polisher
        var polisher = CreatePolisher(factory, stats, suffix);

        // Create Cleaner
        var cleaner = CreateCleaner(factory, stats, suffix);

        // Create Buffer
        var buffer = CreateBuffer(factory, stats, suffix);

        return (carrier, polisher, cleaner, buffer);
    }

    private IActorRef CreatePolisher(XStateMachineFactory factory, ProcessingStats stats, string suffix)
    {
        var json = """
        {
            "id": "polisher",
            "initial": "idle",
            "context": { "wafer": null },
            "states": {
                "idle": {
                    "on": { "PLACE": { "target": "processing", "actions": ["storeWafer"] } }
                },
                "processing": {
                    "on": { "COMPLETE": { "target": "done" } }
                },
                "done": {
                    "on": { "PICK": { "target": "idle", "actions": ["clearWafer"] } }
                }
            }
        }
        """;

        return factory.FromJson(json)
            .WithAction("storeWafer", (ctx, evt) =>
            {
                stats.PolisherCycles++;
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", data["wafer"]);
                }
            })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart($"polisher-{suffix}");
    }

    private IActorRef CreateCleaner(XStateMachineFactory factory, ProcessingStats stats, string suffix)
    {
        var json = """
        {
            "id": "cleaner",
            "initial": "idle",
            "context": { "wafer": null },
            "states": {
                "idle": {
                    "on": { "PLACE": { "target": "cleaning", "actions": ["storeWafer"] } }
                },
                "cleaning": {
                    "on": { "COMPLETE": { "target": "done" } }
                },
                "done": {
                    "on": { "PICK": { "target": "idle", "actions": ["clearWafer"] } }
                }
            }
        }
        """;

        return factory.FromJson(json)
            .WithAction("storeWafer", (ctx, evt) =>
            {
                stats.CleanerCycles++;
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", data["wafer"]);
                }
            })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart($"cleaner-{suffix}");
    }

    private IActorRef CreateBuffer(XStateMachineFactory factory, ProcessingStats stats, string suffix)
    {
        var json = """
        {
            "id": "buffer",
            "initial": "empty",
            "context": { "wafer": null },
            "states": {
                "empty": {
                    "on": { "PLACE": { "target": "occupied", "actions": ["storeWafer"] } }
                },
                "occupied": {
                    "on": { "PICK": { "target": "empty", "actions": ["clearWafer"] } }
                }
            }
        }
        """;

        return factory.FromJson(json)
            .WithAction("storeWafer", (ctx, evt) =>
            {
                stats.BufferCycles++;
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", data["wafer"]);
                }
            })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart($"buffer-{suffix}");
    }

    private void ProcessSingleCarrier(
        IActorRef carrier,
        IActorRef polisher,
        IActorRef cleaner,
        IActorRef buffer,
        int carrierId,
        ProcessingStats stats)
    {
        // Load carrier
        SendEventAndWait(carrier, "LOAD",
            s => s.CurrentState == "processing",
            $"carrier {carrierId} processing",
            new Dictionary<string, object> { ["carrierId"] = carrierId });

        // Process all 25 wafers
        for (int w = 1; w <= WafersPerCarrier; w++)
        {
            var waferId = (carrierId * 1000) + w;

            // R1: Carrier → Polisher
            carrier.Tell(new SendEvent("PICK", new Dictionary<string, object> { ["wafer"] = waferId }));
            SendEventAndWait(polisher, "PLACE",
                s => s.CurrentState == "processing",
                "polisher processing",
                new Dictionary<string, object> { ["wafer"] = waferId });

            // Polisher complete
            SendEventAndWait(polisher, "COMPLETE",
                s => s.CurrentState == "done",
                "polisher done");

            // R2: Polisher → Cleaner
            SendEventAndWait(polisher, "PICK",
                s => s.CurrentState == "idle",
                "polisher idle");

            SendEventAndWait(cleaner, "PLACE",
                s => s.CurrentState == "cleaning",
                "cleaner cleaning",
                new Dictionary<string, object> { ["wafer"] = waferId });

            // Cleaner complete
            SendEventAndWait(cleaner, "COMPLETE",
                s => s.CurrentState == "done",
                "cleaner done");

            // R3: Cleaner → Buffer
            SendEventAndWait(cleaner, "PICK",
                s => s.CurrentState == "idle",
                "cleaner idle");

            SendEventAndWait(buffer, "PLACE",
                s => s.CurrentState == "occupied",
                "buffer occupied",
                new Dictionary<string, object> { ["wafer"] = waferId });

            // R1: Buffer → Carrier
            SendEventAndWait(buffer, "PICK",
                s => s.CurrentState == "empty",
                "buffer empty");

            carrier.Tell(new SendEvent("PLACE", new Dictionary<string, object> { ["wafer"] = waferId }));

            stats.TotalWafersProcessed++;
        }

        // Trigger carrier completion
        SendEventAndWait(carrier, "ALL_COMPLETE",
            s => s.CurrentState == "completed",
            "carrier completed");

        // Unload carrier
        SendEventAndWait(carrier, "UNLOAD",
            s => s.CurrentState == "loading",
            $"carrier {carrierId} unloaded");
    }

    #endregion

    #region Statistics Class

    private class ProcessingStats
    {
        public int TotalCarriersProcessed { get; set; }
        public int TotalWafersProcessed { get; set; }
        public int WafersPickedFromCarrier { get; set; }
        public int WafersReturnedToCarrier { get; set; }
        public int PolisherCycles { get; set; }
        public int CleanerCycles { get; set; }
        public int BufferCycles { get; set; }
    }

    #endregion
}
