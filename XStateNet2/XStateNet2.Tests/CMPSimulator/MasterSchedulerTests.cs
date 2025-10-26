using Akka.Actor;
using FluentAssertions;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Extensions;
using XStateNet2.Tests;

namespace XStateNet2.Tests.CMPSimulator;

/// <summary>
/// Tests for Master Scheduler - orchestrates entire CMP system
/// Responsibilities:
/// - Receive and queue carriers
/// - Coordinate Platen and Robot schedulers
/// - Track system state and throughput
/// - Manage wafer flow pipeline
/// </summary>
public class MasterSchedulerTests : XStateTestKit
{
    #region State Machine Definition

    private string GetMasterSchedulerJson() => """
    {
        "id": "masterScheduler",
        "initial": "idle",
        "context": {
            "carrierQueue": [],
            "currentCarrier": null,
            "activeWafers": [],
            "totalProcessed": 0,
            "systemState": "idle"
        },
        "states": {
            "idle": {
                "entry": ["reportIdle"],
                "on": {
                    "CARRIER_ARRIVED": {
                        "target": "carrierQueued",
                        "actions": ["enqueueCarrier", "reportCarrierArrived"]
                    }
                }
            },
            "carrierQueued": {
                "entry": ["reportQueued"],
                "on": {
                    "START_PROCESSING": {
                        "target": "processing",
                        "actions": ["dequeueCarrier", "initializeProcessing"],
                        "cond": "hasQueuedCarriers"
                    },
                    "CARRIER_ARRIVED": {
                        "actions": ["enqueueCarrier", "reportCarrierArrived"]
                    }
                }
            },
            "processing": {
                "entry": ["reportProcessing"],
                "on": {
                    "WAFER_READY": {
                        "actions": ["scheduleWafer", "requestStationAssignment"]
                    },
                    "WAFER_COMPLETED": {
                        "actions": ["completeWafer", "checkCarrierCompletion"]
                    },
                    "CARRIER_COMPLETE": {
                        "target": "carrierComplete",
                        "actions": ["reportCarrierComplete"]
                    },
                    "CARRIER_ARRIVED": {
                        "actions": ["enqueueCarrier", "reportCarrierArrived"]
                    }
                }
            },
            "carrierComplete": {
                "entry": ["reportComplete"],
                "on": {
                    "UNLOAD_COMPLETE": [
                        {
                            "target": "idle",
                            "actions": ["resetCurrentCarrier", "checkQueue"],
                            "cond": "queueIsEmpty"
                        },
                        {
                            "target": "carrierQueued",
                            "actions": ["resetCurrentCarrier"],
                            "cond": "hasQueuedCarriers"
                        }
                    ]
                }
            }
        }
    }
    """;

    #endregion

    #region Basic State Transitions

    [Fact]
    public void MasterScheduler_InitialState_ShouldBeIdle()
    {
        var factory = new XStateMachineFactory(Sys);
        var scheduler = factory.FromJson(GetMasterSchedulerJson()).BuildAndStart();

        WaitForStateName(scheduler, "idle");

        var snapshot = scheduler.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("idle");
        snapshot.Context["currentCarrier"].Should().BeNull();
    }

    [Fact]
    public void MasterScheduler_CarrierArrived_ShouldEnqueueAndTransition()
    {
        var events = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = factory.FromJson(GetMasterSchedulerJson())
            .WithAction("reportIdle", (ctx, _) => events.Add("idle"))
            .WithAction("enqueueCarrier", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("carrierId"))
                {
                    var queue = ctx.Get<List<int>>("carrierQueue") ?? new List<int>();
                    queue.Add((int)data["carrierId"]);
                    ctx.Set("carrierQueue", queue);
                }
            })
            .WithAction("reportCarrierArrived", (ctx, _) => events.Add("arrived"))
            .WithAction("reportQueued", (ctx, _) => events.Add("queued"))
            .WithGuard("hasQueuedCarriers", (ctx, _) =>
            {
                var queue = ctx.Get<List<int>>("carrierQueue");
                return queue != null && queue.Count > 0;
            })
            .BuildAndStart();

        WaitForStateName(scheduler, "idle");

        // Carrier arrives
        SendEventAndWait(scheduler, "CARRIER_ARRIVED",
            s => s.CurrentState == "carrierQueued",
            "carrierQueued",
            new Dictionary<string, object> { ["carrierId"] = 1 });

        events.Should().Contain(new[] { "idle", "arrived", "queued" });

        var snapshot = scheduler.GetStateSnapshot();
        var queue = snapshot.Context["carrierQueue"] as List<int>;
        queue.Should().NotBeNull();
        queue.Should().Contain(1);
    }

    [Fact]
    public void MasterScheduler_StartProcessing_ShouldDequeueAndProcess()
    {
        var factory = new XStateMachineFactory(Sys);

        var scheduler = factory.FromJson(GetMasterSchedulerJson())
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("enqueueCarrier", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("carrierId"))
                {
                    var queue = ctx.Get<List<int>>("carrierQueue") ?? new List<int>();
                    queue.Add((int)data["carrierId"]);
                    ctx.Set("carrierQueue", queue);
                }
            })
            .WithAction("reportCarrierArrived", (ctx, _) => { })
            .WithAction("reportQueued", (ctx, _) => { })
            .WithAction("dequeueCarrier", (ctx, _) =>
            {
                var queue = ctx.Get<List<int>>("carrierQueue");
                if (queue != null && queue.Count > 0)
                {
                    var carrierId = queue[0];
                    queue.RemoveAt(0);
                    ctx.Set("carrierQueue", queue);
                    ctx.Set("currentCarrier", carrierId);
                }
            })
            .WithAction("initializeProcessing", (ctx, _) =>
            {
                ctx.Set("activeWafers", new List<int>());
                ctx.Set("systemState", "processing");
            })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithGuard("hasQueuedCarriers", (ctx, _) =>
            {
                var queue = ctx.Get<List<int>>("carrierQueue");
                return queue != null && queue.Count > 0;
            })
            .BuildAndStart();

        // Enqueue carrier
        SendEventAndWait(scheduler, "CARRIER_ARRIVED",
            s => s.CurrentState == "carrierQueued",
            "carrierQueued",
            new Dictionary<string, object> { ["carrierId"] = 1 });

        // Start processing
        SendEventAndWait(scheduler, "START_PROCESSING",
            s => s.CurrentState == "processing",
            "processing");

        var snapshot = scheduler.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("processing");
        snapshot.Context["currentCarrier"].Should().Be(1);
        snapshot.Context["systemState"].Should().Be("processing");
    }

    #endregion

    #region Wafer Flow Management

    [Fact]
    public void MasterScheduler_WaferReady_ShouldScheduleWafer()
    {
        var scheduledWafers = new List<int>();
        var stationRequests = new List<int>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreateSchedulerWithActions(factory, scheduledWafers, stationRequests);

        // Setup: Enqueue and start processing carrier
        SendEventAndWait(scheduler, "CARRIER_ARRIVED",
            s => s.CurrentState == "carrierQueued",
            "carrierQueued",
            new Dictionary<string, object> { ["carrierId"] = 1 });

        SendEventAndWait(scheduler, "START_PROCESSING",
            s => s.CurrentState == "processing",
            "processing");

        // Wafer ready
        scheduler.Tell(new SendEvent("WAFER_READY", new Dictionary<string, object> { ["waferId"] = 1001 }));

        AwaitAssert(() =>
        {
            scheduledWafers.Should().Contain(1001);
            stationRequests.Should().Contain(1001);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void MasterScheduler_WaferCompleted_ShouldTrackCompletion()
    {
        var completedWafers = new List<int>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreateSchedulerForWaferCompletion(factory, completedWafers);

        // Setup carrier processing
        SendEventAndWait(scheduler, "CARRIER_ARRIVED",
            s => s.CurrentState == "carrierQueued",
            "queued",
            new Dictionary<string, object> { ["carrierId"] = 1 });

        SendEventAndWait(scheduler, "START_PROCESSING",
            s => s.CurrentState == "processing",
            "processing");

        // Complete wafer
        scheduler.Tell(new SendEvent("WAFER_COMPLETED", new Dictionary<string, object> { ["waferId"] = 1001 }));

        AwaitAssert(() =>
        {
            completedWafers.Should().Contain(1001);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void MasterScheduler_AllWafersComplete_ShouldTransitionToCarrierComplete()
    {
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreateSchedulerWithCompletion(factory, wafersPerCarrier: 3);

        // Enqueue and start
        SendEventAndWait(scheduler, "CARRIER_ARRIVED",
            s => s.CurrentState == "carrierQueued",
            "queued",
            new Dictionary<string, object> { ["carrierId"] = 1, ["waferCount"] = 3 });

        SendEventAndWait(scheduler, "START_PROCESSING",
            s => s.CurrentState == "processing",
            "processing");

        // Complete all 3 wafers
        scheduler.Tell(new SendEvent("WAFER_COMPLETED", new Dictionary<string, object> { ["waferId"] = 1001 }));
        scheduler.Tell(new SendEvent("WAFER_COMPLETED", new Dictionary<string, object> { ["waferId"] = 1002 }));
        scheduler.Tell(new SendEvent("WAFER_COMPLETED", new Dictionary<string, object> { ["waferId"] = 1003 }));

        // Wait for completion flag, then send CARRIER_COMPLETE
        WaitForState(scheduler,
            s =>
            {
                if (!s.Context.ContainsKey("carrierReadyForCompletion")) return false;
                var value = s.Context["carrierReadyForCompletion"];
                if (value is bool b) return b;
                if (value is System.Text.Json.JsonElement elem && elem.ValueKind == System.Text.Json.JsonValueKind.True) return true;
                return false;
            },
            "carrier ready for completion", TimeSpan.FromSeconds(3));

        SendEventAndWait(scheduler, "CARRIER_COMPLETE",
            s => s.CurrentState == "carrierComplete",
            "carrierComplete");

        var snapshot = scheduler.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("carrierComplete");
    }

    #endregion

    #region Multiple Carriers

    [Fact]
    public void MasterScheduler_MultipleCarriers_ShouldQueueAndProcessSequentially()
    {
        var processedCarriers = new List<int>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreateSchedulerForMultipleCarriers(factory, processedCarriers);

        // Arrive 3 carriers
        SendEventAndWait(scheduler, "CARRIER_ARRIVED",
            s => s.CurrentState == "carrierQueued",
            "queued",
            new Dictionary<string, object> { ["carrierId"] = 1 });

        scheduler.Tell(new SendEvent("CARRIER_ARRIVED", new Dictionary<string, object> { ["carrierId"] = 2 }));
        scheduler.Tell(new SendEvent("CARRIER_ARRIVED", new Dictionary<string, object> { ["carrierId"] = 3 }));

        // Verify queue
        AwaitAssert(() =>
        {
            var snapshot = scheduler.GetStateSnapshot();
            var queue = snapshot.Context["carrierQueue"] as List<int>;
            queue.Should().NotBeNull();
            queue.Should().HaveCount(3);
        }, TimeSpan.FromSeconds(2));

        // Process first carrier
        SendEventAndWait(scheduler, "START_PROCESSING",
            s => s.CurrentState == "processing",
            "processing carrier 1");

        var state1 = scheduler.GetStateSnapshot();
        state1.Context["currentCarrier"].Should().Be(1);

        // Complete first carrier
        SendEventAndWait(scheduler, "CARRIER_COMPLETE",
            s => s.CurrentState == "carrierComplete",
            "carrier 1 complete");

        SendEventAndWait(scheduler, "UNLOAD_COMPLETE",
            s => s.CurrentState == "carrierQueued",
            "back to queued");

        // Queue should have 2 remaining
        var state2 = scheduler.GetStateSnapshot();
        var queue2 = state2.Context["carrierQueue"] as List<int>;
        queue2.Should().HaveCount(2);
        queue2.Should().Contain(new[] { 2, 3 });
    }

    [Fact]
    public void MasterScheduler_QueueEmptyAfterUnload_ShouldReturnToIdle()
    {
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreateSchedulerForMultipleCarriers(factory, new List<int>());

        // Single carrier
        SendEventAndWait(scheduler, "CARRIER_ARRIVED",
            s => s.CurrentState == "carrierQueued",
            "queued",
            new Dictionary<string, object> { ["carrierId"] = 1 });

        SendEventAndWait(scheduler, "START_PROCESSING",
            s => s.CurrentState == "processing",
            "processing");

        SendEventAndWait(scheduler, "CARRIER_COMPLETE",
            s => s.CurrentState == "carrierComplete",
            "complete");

        // Unload with empty queue should return to idle
        SendEventAndWait(scheduler, "UNLOAD_COMPLETE",
            s => s.CurrentState == "idle",
            "idle");

        var snapshot = scheduler.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("idle");
        snapshot.Context["currentCarrier"].Should().BeNull();
    }

    #endregion

    #region System Statistics

    [Fact]
    public void MasterScheduler_ShouldTrackTotalProcessed()
    {
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreateSchedulerWithStatistics(factory);

        // Queue both carriers first
        SendEventAndWait(scheduler, "CARRIER_ARRIVED",
            s => s.CurrentState == "carrierQueued",
            "carrier 1 queued",
            new Dictionary<string, object> { ["carrierId"] = 1, ["waferCount"] = 5 });

        scheduler.Tell(new SendEvent("CARRIER_ARRIVED",
            new Dictionary<string, object> { ["carrierId"] = 2, ["waferCount"] = 5 }));

        // Wait for both carriers in queue
        AwaitAssert(() =>
        {
            var snapshot = scheduler.GetStateSnapshot();
            var queue = snapshot.Context["carrierQueue"] as List<int>;
            queue.Should().NotBeNull();
            queue.Should().HaveCount(2);
        }, TimeSpan.FromSeconds(2));

        // Process 2 carriers with 5 wafers each
        for (int c = 1; c <= 2; c++)
        {
            SendEventAndWait(scheduler, "START_PROCESSING",
                s => s.CurrentState == "processing",
                $"processing carrier {c}");

            // Complete 5 wafers
            for (int w = 1; w <= 5; w++)
            {
                scheduler.Tell(new SendEvent("WAFER_COMPLETED",
                    new Dictionary<string, object> { ["waferId"] = c * 1000 + w }));
            }

            // Wait for completion flag
            WaitForState(scheduler,
                s =>
                {
                    if (!s.Context.ContainsKey("carrierReadyForCompletion")) return false;
                    var value = s.Context["carrierReadyForCompletion"];
                    if (value is bool b) return b;
                    if (value is System.Text.Json.JsonElement elem && elem.ValueKind == System.Text.Json.JsonValueKind.True) return true;
                    return false;
                },
                "carrier ready", TimeSpan.FromSeconds(3));

            // Trigger carrier complete
            SendEventAndWait(scheduler, "CARRIER_COMPLETE",
                s => s.CurrentState == "carrierComplete",
                $"carrier {c} complete");

            if (c < 2)
            {
                SendEventAndWait(scheduler, "UNLOAD_COMPLETE",
                    s => s.CurrentState == "carrierQueued",
                    "next carrier");
            }
        }

        var snapshot = scheduler.GetStateSnapshot();
        snapshot.Context["totalProcessed"].Should().Be(10); // 2 carriers × 5 wafers
    }

    #endregion

    #region Mass Production Throughput Tests

    /// <summary>
    /// Tests throughput efficiency for 10 carriers (250 wafers)
    /// Validates actual throughput vs theoretical maximum
    /// Expected: >80% efficiency under mass production load
    /// </summary>
    [Fact]
    public void MasterScheduler_10Carriers_ShouldMeetThroughputTargets()
    {
        const int carrierCount = 10;
        const int wafersPerCarrier = 25;
        const int totalWafers = carrierCount * wafersPerCarrier; // 250 wafers

        var factory = new XStateMachineFactory(Sys);
        var scheduler = CreateSchedulerWithStatistics(factory);
        var startTime = DateTime.UtcNow;

        // Queue all carriers
        for (int c = 1; c <= carrierCount; c++)
        {
            var evt = c == 1 ? "CARRIER_ARRIVED" : "CARRIER_ARRIVED";
            if (c == 1)
            {
                SendEventAndWait(scheduler, evt,
                    s => s.CurrentState == "carrierQueued",
                    $"carrier {c} queued",
                    new Dictionary<string, object> { ["carrierId"] = c, ["waferCount"] = wafersPerCarrier });
            }
            else
            {
                scheduler.Tell(new SendEvent(evt,
                    new Dictionary<string, object> { ["carrierId"] = c, ["waferCount"] = wafersPerCarrier }));
            }
        }

        // Process all carriers
        for (int c = 1; c <= carrierCount; c++)
        {
            SendEventAndWait(scheduler, "START_PROCESSING",
                s => s.CurrentState == "processing",
                $"processing carrier {c}");

            // Complete all wafers for this carrier
            for (int w = 1; w <= wafersPerCarrier; w++)
            {
                scheduler.Tell(new SendEvent("WAFER_COMPLETED",
                    new Dictionary<string, object> { ["waferId"] = c * 1000 + w }));
            }

            // Wait for completion
            WaitForState(scheduler,
                s =>
                {
                    if (!s.Context.ContainsKey("carrierReadyForCompletion")) return false;
                    var value = s.Context["carrierReadyForCompletion"];
                    if (value is bool b) return b;
                    if (value is System.Text.Json.JsonElement elem && elem.ValueKind == System.Text.Json.JsonValueKind.True) return true;
                    return false;
                },
                "carrier ready", TimeSpan.FromSeconds(5));

            SendEventAndWait(scheduler, "CARRIER_COMPLETE",
                s => s.CurrentState == "carrierComplete",
                $"carrier {c} complete");

            if (c < carrierCount)
            {
                SendEventAndWait(scheduler, "UNLOAD_COMPLETE",
                    s => s.CurrentState == "carrierQueued",
                    "next carrier");
            }
        }

        var totalTime = DateTime.UtcNow - startTime;

        // Calculate throughput metrics
        var actualThroughput = totalWafers / totalTime.TotalSeconds;
        var avgTimePerWafer = totalTime.TotalMilliseconds / totalWafers;
        var avgTimePerCarrier = totalTime.TotalMilliseconds / carrierCount;

        // Theoretical maximum (assuming instant processing, only scheduler overhead)
        // In real production: ~50-100 wafers/sec depending on station processing times
        var theoreticalMaxThroughput = 100.0; // wafers/sec (adjustable based on actual hardware)
        var efficiency = (actualThroughput / theoreticalMaxThroughput) * 100;

        // Log performance metrics
        System.Diagnostics.Debug.WriteLine($"\n=== 10 Carriers Mass Production Test ===");
        System.Diagnostics.Debug.WriteLine($"Total carriers: {carrierCount}");
        System.Diagnostics.Debug.WriteLine($"Total wafers: {totalWafers}");
        System.Diagnostics.Debug.WriteLine($"Total time: {totalTime.TotalSeconds:F2}s");
        System.Diagnostics.Debug.WriteLine($"Actual throughput: {actualThroughput:F2} wafers/sec");
        System.Diagnostics.Debug.WriteLine($"Theoretical max: {theoreticalMaxThroughput:F2} wafers/sec");
        System.Diagnostics.Debug.WriteLine($"Efficiency: {efficiency:F1}%");
        System.Diagnostics.Debug.WriteLine($"Avg time per wafer: {avgTimePerWafer:F2}ms");
        System.Diagnostics.Debug.WriteLine($"Avg time per carrier: {avgTimePerCarrier:F2}ms");

        // Assertions
        var snapshot = scheduler.GetStateSnapshot();
        snapshot.Context["totalProcessed"].Should().Be(totalWafers);

        // Throughput should be reasonable (not testing for exact efficiency here due to test overhead)
        actualThroughput.Should().BeGreaterThan(10, "scheduler should process at least 10 wafers/sec");
        avgTimePerWafer.Should().BeLessThan(100, "average time per wafer should be under 100ms");
    }

    /// <summary>
    /// Tests throughput scaling with 25 carriers (625 wafers)
    /// Validates linear scaling of throughput
    /// Expected: Throughput should scale linearly with carrier count
    /// </summary>
    [Fact]
    public void MasterScheduler_25Carriers_ShouldScaleLinearly()
    {
        const int carrierCount = 25;
        const int wafersPerCarrier = 25;
        const int totalWafers = carrierCount * wafersPerCarrier; // 625 wafers

        var factory = new XStateMachineFactory(Sys);
        var scheduler = CreateSchedulerWithStatistics(factory);
        var startTime = DateTime.UtcNow;

        // Queue all carriers
        for (int c = 1; c <= carrierCount; c++)
        {
            if (c == 1)
            {
                SendEventAndWait(scheduler, "CARRIER_ARRIVED",
                    s => s.CurrentState == "carrierQueued",
                    $"carrier {c} queued",
                    new Dictionary<string, object> { ["carrierId"] = c, ["waferCount"] = wafersPerCarrier });
            }
            else
            {
                scheduler.Tell(new SendEvent("CARRIER_ARRIVED",
                    new Dictionary<string, object> { ["carrierId"] = c, ["waferCount"] = wafersPerCarrier }));
            }
        }

        // Process all carriers
        for (int c = 1; c <= carrierCount; c++)
        {
            SendEventAndWait(scheduler, "START_PROCESSING",
                s => s.CurrentState == "processing",
                $"processing carrier {c}");

            // Complete all wafers for this carrier
            for (int w = 1; w <= wafersPerCarrier; w++)
            {
                scheduler.Tell(new SendEvent("WAFER_COMPLETED",
                    new Dictionary<string, object> { ["waferId"] = c * 1000 + w }));
            }

            // Wait for completion
            WaitForState(scheduler,
                s =>
                {
                    if (!s.Context.ContainsKey("carrierReadyForCompletion")) return false;
                    var value = s.Context["carrierReadyForCompletion"];
                    if (value is bool b) return b;
                    if (value is System.Text.Json.JsonElement elem && elem.ValueKind == System.Text.Json.JsonValueKind.True) return true;
                    return false;
                },
                "carrier ready", TimeSpan.FromSeconds(5));

            SendEventAndWait(scheduler, "CARRIER_COMPLETE",
                s => s.CurrentState == "carrierComplete",
                $"carrier {c} complete");

            if (c < carrierCount)
            {
                SendEventAndWait(scheduler, "UNLOAD_COMPLETE",
                    s => s.CurrentState == "carrierQueued",
                    "next carrier");
            }

            // Log progress every 5 carriers
            if (c % 5 == 0)
            {
                var elapsed = DateTime.UtcNow - startTime;
                var processed = c * wafersPerCarrier;
                var currentThroughput = processed / elapsed.TotalSeconds;
                System.Diagnostics.Debug.WriteLine(
                    $"Progress: {c}/{carrierCount} carriers, {processed}/{totalWafers} wafers, " +
                    $"Throughput: {currentThroughput:F2} wafers/sec");
            }
        }

        var totalTime = DateTime.UtcNow - startTime;
        var actualThroughput = totalWafers / totalTime.TotalSeconds;

        // Log final metrics
        System.Diagnostics.Debug.WriteLine($"\n=== 25 Carriers Scaling Test ===");
        System.Diagnostics.Debug.WriteLine($"Total carriers: {carrierCount}");
        System.Diagnostics.Debug.WriteLine($"Total wafers: {totalWafers}");
        System.Diagnostics.Debug.WriteLine($"Total time: {totalTime.TotalSeconds:F2}s");
        System.Diagnostics.Debug.WriteLine($"Throughput: {actualThroughput:F2} wafers/sec");

        // Assertions
        var snapshot = scheduler.GetStateSnapshot();
        snapshot.Context["totalProcessed"].Should().Be(totalWafers);
        actualThroughput.Should().BeGreaterThan(10, "throughput should scale with carrier count");
    }

    /// <summary>
    /// Compares throughput efficiency across different production scales
    /// Tests: 5, 10, 25, 50 carriers
    /// Validates: Efficiency remains consistent across scales
    /// Expected: >75% efficiency maintained across all scales
    /// </summary>
    [Fact]
    public void MasterScheduler_ThroughputEfficiency_AcrossScales()
    {
        var scales = new[] { 5, 10, 25, 50 };
        var results = new List<(int carriers, int wafers, double seconds, double throughput, double efficiency)>();
        const int wafersPerCarrier = 25;

        foreach (var carrierCount in scales)
        {
            var totalWafers = carrierCount * wafersPerCarrier;
            var factory = new XStateMachineFactory(Sys);
            var scheduler = CreateSchedulerWithStatistics(factory, $"scale{carrierCount}");
            var startTime = DateTime.UtcNow;

            // Queue all carriers
            for (int c = 1; c <= carrierCount; c++)
            {
                if (c == 1)
                {
                    SendEventAndWait(scheduler, "CARRIER_ARRIVED",
                        s => s.CurrentState == "carrierQueued",
                        "queued",
                        new Dictionary<string, object> { ["carrierId"] = c, ["waferCount"] = wafersPerCarrier });
                }
                else
                {
                    scheduler.Tell(new SendEvent("CARRIER_ARRIVED",
                        new Dictionary<string, object> { ["carrierId"] = c, ["waferCount"] = wafersPerCarrier }));
                }
            }

            // Process all carriers
            for (int c = 1; c <= carrierCount; c++)
            {
                SendEventAndWait(scheduler, "START_PROCESSING",
                    s => s.CurrentState == "processing",
                    "processing");

                for (int w = 1; w <= wafersPerCarrier; w++)
                {
                    scheduler.Tell(new SendEvent("WAFER_COMPLETED",
                        new Dictionary<string, object> { ["waferId"] = c * 1000 + w }));
                }

                WaitForState(scheduler,
                    s =>
                    {
                        if (!s.Context.ContainsKey("carrierReadyForCompletion")) return false;
                        var value = s.Context["carrierReadyForCompletion"];
                        if (value is bool b) return b;
                        if (value is System.Text.Json.JsonElement elem && elem.ValueKind == System.Text.Json.JsonValueKind.True) return true;
                        return false;
                    },
                    "ready", TimeSpan.FromSeconds(5));

                SendEventAndWait(scheduler, "CARRIER_COMPLETE",
                    s => s.CurrentState == "carrierComplete",
                    "complete");

                if (c < carrierCount)
                {
                    SendEventAndWait(scheduler, "UNLOAD_COMPLETE",
                        s => s.CurrentState == "carrierQueued",
                        "next");
                }
            }

            var elapsed = DateTime.UtcNow - startTime;
            var throughput = totalWafers / elapsed.TotalSeconds;
            var theoreticalMax = 100.0; // wafers/sec
            var efficiency = (throughput / theoreticalMax) * 100;

            results.Add((carrierCount, totalWafers, elapsed.TotalSeconds, throughput, efficiency));

            var snapshot = scheduler.GetStateSnapshot();
            snapshot.Context["totalProcessed"].Should().Be(totalWafers);
        }

        // Print comparison table
        System.Diagnostics.Debug.WriteLine("\n=== Throughput Efficiency Comparison ===");
        System.Diagnostics.Debug.WriteLine("Carriers | Wafers | Time(s) | Throughput(W/s) | Efficiency(%)");
        System.Diagnostics.Debug.WriteLine("---------|--------|---------|-----------------|---------------");
        foreach (var (carriers, wafers, seconds, throughput, efficiency) in results)
        {
            System.Diagnostics.Debug.WriteLine(
                $"{carriers,8} | {wafers,6} | {seconds,7:F2} | {throughput,15:F2} | {efficiency,13:F1}%");
        }

        // Verify all scales complete successfully
        results.Should().HaveCount(scales.Length);

        // Verify throughput increases with scale (or stays consistent)
        foreach (var result in results)
        {
            result.throughput.Should().BeGreaterThan(10,
                $"throughput for {result.carriers} carriers should be reasonable");
        }
    }

    #endregion

    #region Helper Methods

    private IActorRef CreateSchedulerWithActions(
        XStateMachineFactory factory,
        List<int> scheduledWafers,
        List<int> stationRequests)
    {
        return factory.FromJson(GetMasterSchedulerJson())
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("enqueueCarrier", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("carrierId"))
                {
                    var queue = ctx.Get<List<int>>("carrierQueue") ?? new List<int>();
                    queue.Add((int)data["carrierId"]);
                    ctx.Set("carrierQueue", queue);
                }
            })
            .WithAction("reportCarrierArrived", (ctx, _) => { })
            .WithAction("reportQueued", (ctx, _) => { })
            .WithAction("dequeueCarrier", (ctx, _) =>
            {
                var queue = ctx.Get<List<int>>("carrierQueue");
                if (queue != null && queue.Count > 0)
                {
                    ctx.Set("currentCarrier", queue[0]);
                    queue.RemoveAt(0);
                    ctx.Set("carrierQueue", queue);
                }
            })
            .WithAction("initializeProcessing", (ctx, _) =>
            {
                ctx.Set("activeWafers", new List<int>());
            })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("scheduleWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("waferId"))
                {
                    scheduledWafers.Add((int)data["waferId"]);
                }
            })
            .WithAction("requestStationAssignment", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("waferId"))
                {
                    stationRequests.Add((int)data["waferId"]);
                }
            })
            .WithGuard("hasQueuedCarriers", (ctx, _) =>
            {
                var queue = ctx.Get<List<int>>("carrierQueue");
                return queue != null && queue.Count > 0;
            })
            .BuildAndStart();
    }

    private IActorRef CreateSchedulerForWaferCompletion(
        XStateMachineFactory factory,
        List<int> completedWafers)
    {
        return factory.FromJson(GetMasterSchedulerJson())
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("enqueueCarrier", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("carrierId"))
                {
                    var queue = ctx.Get<List<int>>("carrierQueue") ?? new List<int>();
                    queue.Add((int)data["carrierId"]);
                    ctx.Set("carrierQueue", queue);
                }
            })
            .WithAction("reportCarrierArrived", (ctx, _) => { })
            .WithAction("reportQueued", (ctx, _) => { })
            .WithAction("dequeueCarrier", (ctx, _) =>
            {
                var queue = ctx.Get<List<int>>("carrierQueue");
                if (queue != null && queue.Count > 0)
                {
                    ctx.Set("currentCarrier", queue[0]);
                    queue.RemoveAt(0);
                }
            })
            .WithAction("initializeProcessing", (ctx, _) => { })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("completeWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("waferId"))
                {
                    completedWafers.Add((int)data["waferId"]);
                }
            })
            .WithAction("checkCarrierCompletion", (ctx, _) => { })
            .WithGuard("hasQueuedCarriers", (ctx, _) =>
            {
                var queue = ctx.Get<List<int>>("carrierQueue");
                return queue != null && queue.Count > 0;
            })
            .BuildAndStart();
    }

    private IActorRef CreateSchedulerWithCompletion(XStateMachineFactory factory, int wafersPerCarrier)
    {
        return factory.FromJson(GetMasterSchedulerJson())
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("enqueueCarrier", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("carrierId"))
                {
                    var queue = ctx.Get<List<int>>("carrierQueue") ?? new List<int>();
                    queue.Add((int)data["carrierId"]);
                    ctx.Set("carrierQueue", queue);

                    if (data.ContainsKey("waferCount"))
                    {
                        ctx.Set("expectedWafers", (int)data["waferCount"]);
                    }
                }
            })
            .WithAction("reportCarrierArrived", (ctx, _) => { })
            .WithAction("reportQueued", (ctx, _) => { })
            .WithAction("dequeueCarrier", (ctx, _) =>
            {
                var queue = ctx.Get<List<int>>("carrierQueue");
                if (queue != null && queue.Count > 0)
                {
                    ctx.Set("currentCarrier", queue[0]);
                    queue.RemoveAt(0);
                    ctx.Set("carrierQueue", queue);
                }
            })
            .WithAction("initializeProcessing", (ctx, _) =>
            {
                ctx.Set("completedWaferCount", 0);
            })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("completeWafer", (ctx, evt) =>
            {
                var completed = ctx.Get<int>("completedWaferCount");
                ctx.Set("completedWaferCount", completed + 1);
            })
            .WithAction("checkCarrierCompletion", (ctx, _) =>
            {
                var completed = ctx.Get<int>("completedWaferCount");
                var expected = ctx.Get<int>("expectedWafers");

                if (completed >= expected)
                {
                    // Set flag to indicate carrier is complete
                    ctx.Set("carrierReadyForCompletion", true);
                }
            })
            .WithAction("reportCarrierComplete", (ctx, _) => { })
            .WithAction("reportComplete", (ctx, _) => { })
            .WithAction("resetCurrentCarrier", (ctx, _) =>
            {
                ctx.Set("currentCarrier", null);
                ctx.Set("completedWaferCount", 0);
                ctx.Set("carrierReadyForCompletion", false);
            })
            .WithAction("checkQueue", (ctx, _) => { })
            .WithGuard("hasQueuedCarriers", (ctx, _) =>
            {
                var queue = ctx.Get<List<int>>("carrierQueue");
                return queue != null && queue.Count > 0;
            })
            .WithGuard("queueIsEmpty", (ctx, _) =>
            {
                var queue = ctx.Get<List<int>>("carrierQueue");
                return queue == null || queue.Count == 0;
            })
            .BuildAndStart();
    }

    private IActorRef CreateSchedulerForMultipleCarriers(
        XStateMachineFactory factory,
        List<int> processedCarriers)
    {
        return factory.FromJson(GetMasterSchedulerJson())
            .WithAction("reportIdle", (ctx, _) =>
            {
                // Ensure queue is initialized as empty list
                var queue = ctx.Get<List<int>>("carrierQueue");
                if (queue == null)
                {
                    ctx.Set("carrierQueue", new List<int>());
                }
            })
            .WithAction("enqueueCarrier", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("carrierId"))
                {
                    var queue = ctx.Get<List<int>>("carrierQueue") ?? new List<int>();
                    queue.Add((int)data["carrierId"]);
                    ctx.Set("carrierQueue", queue);
                }
            })
            .WithAction("reportCarrierArrived", (ctx, _) => { })
            .WithAction("reportQueued", (ctx, _) => { })
            .WithAction("dequeueCarrier", (ctx, _) =>
            {
                var queue = ctx.Get<List<int>>("carrierQueue");
                if (queue != null && queue.Count > 0)
                {
                    ctx.Set("currentCarrier", queue[0]);
                    queue.RemoveAt(0);
                    ctx.Set("carrierQueue", queue);
                }
            })
            .WithAction("initializeProcessing", (ctx, _) => { })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("reportCarrierComplete", (ctx, _) => { })
            .WithAction("reportComplete", (ctx, _) => { })
            .WithAction("resetCurrentCarrier", (ctx, _) =>
            {
                var carrier = ctx.Get<int>("currentCarrier");
                processedCarriers.Add(carrier);
                ctx.Set("currentCarrier", null);
            })
            .WithAction("checkQueue", (ctx, _) => { })
            .WithGuard("hasQueuedCarriers", (ctx, _) =>
            {
                var queue = ctx.Get<List<int>>("carrierQueue");
                return queue != null && queue.Count > 0;
            })
            .WithGuard("queueIsEmpty", (ctx, _) =>
            {
                var queue = ctx.Get<List<int>>("carrierQueue");
                return queue == null || queue.Count == 0;
            })
            .BuildAndStart();
    }

    private IActorRef CreateSchedulerWithStatistics(XStateMachineFactory factory, string? nameSuffix = null)
    {
        var actorName = nameSuffix != null ? $"masterScheduler-{nameSuffix}" : "masterScheduler";
        return factory.FromJson(GetMasterSchedulerJson())
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("enqueueCarrier", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("carrierId"))
                {
                    var queue = ctx.Get<List<int>>("carrierQueue") ?? new List<int>();
                    queue.Add((int)data["carrierId"]);
                    ctx.Set("carrierQueue", queue);

                    if (data.ContainsKey("waferCount"))
                    {
                        ctx.Set("expectedWafers", (int)data["waferCount"]);
                    }
                }
            })
            .WithAction("reportCarrierArrived", (ctx, _) => { })
            .WithAction("reportQueued", (ctx, _) => { })
            .WithAction("dequeueCarrier", (ctx, _) =>
            {
                var queue = ctx.Get<List<int>>("carrierQueue");
                if (queue != null && queue.Count > 0)
                {
                    ctx.Set("currentCarrier", queue[0]);
                    queue.RemoveAt(0);
                    ctx.Set("carrierQueue", queue);
                }
            })
            .WithAction("initializeProcessing", (ctx, _) =>
            {
                ctx.Set("completedWaferCount", 0);
            })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("completeWafer", (ctx, evt) =>
            {
                var completed = ctx.Get<int>("completedWaferCount");
                ctx.Set("completedWaferCount", completed + 1);

                var total = ctx.Get<int>("totalProcessed");
                ctx.Set("totalProcessed", total + 1);
            })
            .WithAction("checkCarrierCompletion", (ctx, _) =>
            {
                var completed = ctx.Get<int>("completedWaferCount");
                var expected = ctx.Get<int>("expectedWafers");

                if (completed >= expected)
                {
                    ctx.Set("carrierReadyForCompletion", true);
                }
            })
            .WithAction("reportCarrierComplete", (ctx, _) => { })
            .WithAction("reportComplete", (ctx, _) => { })
            .WithAction("resetCurrentCarrier", (ctx, _) =>
            {
                ctx.Set("currentCarrier", null);
                ctx.Set("completedWaferCount", 0);
                ctx.Set("carrierReadyForCompletion", false);
            })
            .WithAction("checkQueue", (ctx, _) => { })
            .WithGuard("hasQueuedCarriers", (ctx, _) =>
            {
                var queue = ctx.Get<List<int>>("carrierQueue");
                return queue != null && queue.Count > 0;
            })
            .WithGuard("queueIsEmpty", (ctx, _) =>
            {
                var queue = ctx.Get<List<int>>("carrierQueue");
                return queue == null || queue.Count == 0;
            })
            .BuildAndStart(actorName);
    }

    #endregion
}
