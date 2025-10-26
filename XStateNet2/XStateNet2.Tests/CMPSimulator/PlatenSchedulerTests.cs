using Akka.Actor;
using FluentAssertions;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Extensions;
using XStateNet2.Tests;

namespace XStateNet2.Tests.CMPSimulator;

/// <summary>
/// Tests for Platen Scheduler - manages Polisher and Cleaner station assignments
/// Responsibilities:
/// - Track available polisher/cleaner stations
/// - Assign wafers to idle stations
/// - Queue wafers when all stations busy
/// - Balance load across multiple stations
/// - Track station utilization metrics
/// </summary>
public class PlatenSchedulerTests : XStateTestKit
{
    #region State Machine Definition

    private string GetPlatenSchedulerJson() => """
    {
        "id": "platenScheduler",
        "initial": "scheduling",
        "context": {
            "polisher1Status": "idle",
            "polisher2Status": "idle",
            "cleaner1Status": "idle",
            "cleaner2Status": "idle",
            "polishQueue": [],
            "cleanQueue": [],
            "totalPolishAssignments": 0,
            "totalCleanAssignments": 0
        },
        "states": {
            "scheduling": {
                "entry": ["reportScheduling"],
                "on": {
                    "POLISH_REQUEST": {
                        "actions": ["assignOrQueuePolisher"]
                    },
                    "CLEAN_REQUEST": {
                        "actions": ["assignOrQueueCleaner"]
                    },
                    "POLISH_COMPLETE": {
                        "actions": ["freePolisher", "processPolishQueue"]
                    },
                    "CLEAN_COMPLETE": {
                        "actions": ["freeCleaner", "processCleanQueue"]
                    }
                }
            }
        }
    }
    """;

    #endregion

    #region Basic Station Assignment

    [Fact]
    public void PlatenScheduler_InitialState_AllStationsIdle()
    {
        var factory = new XStateMachineFactory(Sys);
        var scheduler = factory.FromJson(GetPlatenSchedulerJson()).BuildAndStart();

        WaitForStateName(scheduler, "scheduling");

        var snapshot = scheduler.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("scheduling");

        // Handle JsonElement conversion
        var p1Status = snapshot.Context["polisher1Status"]?.ToString();
        var p2Status = snapshot.Context["polisher2Status"]?.ToString();
        var c1Status = snapshot.Context["cleaner1Status"]?.ToString();
        var c2Status = snapshot.Context["cleaner2Status"]?.ToString();

        p1Status.Should().Be("idle");
        p2Status.Should().Be("idle");
        c1Status.Should().Be("idle");
        c2Status.Should().Be("idle");
    }

    [Fact]
    public void PlatenScheduler_PolishRequest_AssignToPolisher1()
    {
        var assignments = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreatePlatenScheduler(factory, assignments);

        // Request polish
        scheduler.Tell(new SendEvent("POLISH_REQUEST", new Dictionary<string, object>
        {
            ["waferId"] = 1001
        }));

        AwaitAssert(() =>
        {
            assignments.Should().Contain("polisher1");
            var snapshot = scheduler.GetStateSnapshot();
            snapshot.Context["polisher1Status"].Should().Be("busy");
            snapshot.Context["totalPolishAssignments"].Should().Be(1);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void PlatenScheduler_CleanRequest_AssignToCleaner1()
    {
        var assignments = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreatePlatenScheduler(factory, assignments);

        // Request clean
        scheduler.Tell(new SendEvent("CLEAN_REQUEST", new Dictionary<string, object>
        {
            ["waferId"] = 2001
        }));

        AwaitAssert(() =>
        {
            assignments.Should().Contain("cleaner1");
            var snapshot = scheduler.GetStateSnapshot();
            snapshot.Context["cleaner1Status"].Should().Be("busy");
            snapshot.Context["totalCleanAssignments"].Should().Be(1);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void PlatenScheduler_TwoPolishRequests_LoadBalanceAcrossPolishers()
    {
        var assignments = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreatePlatenScheduler(factory, assignments);

        // First request
        scheduler.Tell(new SendEvent("POLISH_REQUEST", new Dictionary<string, object> { ["waferId"] = 1001 }));

        // Second request
        scheduler.Tell(new SendEvent("POLISH_REQUEST", new Dictionary<string, object> { ["waferId"] = 1002 }));

        AwaitAssert(() =>
        {
            assignments.Should().Contain("polisher1");
            assignments.Should().Contain("polisher2");

            var snapshot = scheduler.GetStateSnapshot();
            snapshot.Context["polisher1Status"].Should().Be("busy");
            snapshot.Context["polisher2Status"].Should().Be("busy");
            snapshot.Context["totalPolishAssignments"].Should().Be(2);
        }, TimeSpan.FromSeconds(2));
    }

    #endregion

    #region Queue Management

    [Fact]
    public void PlatenScheduler_AllPolishersBusy_ShouldQueue()
    {
        var assignments = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreatePlatenScheduler(factory, assignments);

        // Fill both polishers
        scheduler.Tell(new SendEvent("POLISH_REQUEST", new Dictionary<string, object> { ["waferId"] = 1001 }));
        scheduler.Tell(new SendEvent("POLISH_REQUEST", new Dictionary<string, object> { ["waferId"] = 1002 }));

        AwaitAssert(() => assignments.Should().HaveCount(2), TimeSpan.FromSeconds(2));

        // Third request should queue
        scheduler.Tell(new SendEvent("POLISH_REQUEST", new Dictionary<string, object> { ["waferId"] = 1003 }));

        AwaitAssert(() =>
        {
            var snapshot = scheduler.GetStateSnapshot();
            var queue = snapshot.Context["polishQueue"] as List<Dictionary<string, object>>;
            queue.Should().NotBeNull();
            queue.Should().HaveCount(1);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void PlatenScheduler_PolishComplete_ProcessQueue()
    {
        var assignments = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreatePlatenScheduler(factory, assignments);

        // Fill both polishers
        scheduler.Tell(new SendEvent("POLISH_REQUEST", new Dictionary<string, object> { ["waferId"] = 1001 }));
        scheduler.Tell(new SendEvent("POLISH_REQUEST", new Dictionary<string, object> { ["waferId"] = 1002 }));

        AwaitAssert(() => assignments.Should().HaveCount(2), TimeSpan.FromSeconds(2));

        // Queue third request
        scheduler.Tell(new SendEvent("POLISH_REQUEST", new Dictionary<string, object> { ["waferId"] = 1003 }));

        // Complete first polisher
        scheduler.Tell(new SendEvent("POLISH_COMPLETE", new Dictionary<string, object>
        {
            ["station"] = "polisher1",
            ["waferId"] = 1001
        }));

        AwaitAssert(() =>
        {
            assignments.Should().HaveCount(3);

            var snapshot = scheduler.GetStateSnapshot();
            snapshot.Context["totalPolishAssignments"].Should().Be(3);

            var queue = snapshot.Context["polishQueue"] as List<Dictionary<string, object>>;
            queue.Should().NotBeNull();
            queue.Should().HaveCount(0);
        }, TimeSpan.FromSeconds(3));
    }

    #endregion

    #region Parallel Processing

    [Fact]
    public void PlatenScheduler_ParallelPolishAndClean_IndependentQueues()
    {
        var assignments = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreatePlatenScheduler(factory, assignments);

        // Request polish on both polishers
        scheduler.Tell(new SendEvent("POLISH_REQUEST", new Dictionary<string, object> { ["waferId"] = 1001 }));
        scheduler.Tell(new SendEvent("POLISH_REQUEST", new Dictionary<string, object> { ["waferId"] = 1002 }));

        // Request clean on both cleaners
        scheduler.Tell(new SendEvent("CLEAN_REQUEST", new Dictionary<string, object> { ["waferId"] = 2001 }));
        scheduler.Tell(new SendEvent("CLEAN_REQUEST", new Dictionary<string, object> { ["waferId"] = 2002 }));

        AwaitAssert(() =>
        {
            assignments.Should().Contain("polisher1");
            assignments.Should().Contain("polisher2");
            assignments.Should().Contain("cleaner1");
            assignments.Should().Contain("cleaner2");
            assignments.Should().HaveCount(4);
        }, TimeSpan.FromSeconds(3));

        var snapshot = scheduler.GetStateSnapshot();
        snapshot.Context["totalPolishAssignments"].Should().Be(2);
        snapshot.Context["totalCleanAssignments"].Should().Be(2);
    }

    #endregion

    #region Throughput Tests

    [Fact]
    public void PlatenScheduler_HighThroughput_100Assignments()
    {
        var assignments = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreatePlatenSchedulerWithCompletion(factory, assignments);
        var startTime = DateTime.UtcNow;

        // Simulate 50 polish + 50 clean requests
        for (int i = 0; i < 50; i++)
        {
            scheduler.Tell(new SendEvent("POLISH_REQUEST", new Dictionary<string, object> { ["waferId"] = 1000 + i }));
            scheduler.Tell(new SendEvent("CLEAN_REQUEST", new Dictionary<string, object> { ["waferId"] = 2000 + i }));

            // Simulate completions
            if (i > 0 && i % 5 == 0)
            {
                scheduler.Tell(new SendEvent("POLISH_COMPLETE", new Dictionary<string, object>
                {
                    ["station"] = "polisher1",
                    ["waferId"] = 1000 + i - 5
                }));
                scheduler.Tell(new SendEvent("CLEAN_COMPLETE", new Dictionary<string, object>
                {
                    ["station"] = "cleaner1",
                    ["waferId"] = 2000 + i - 5
                }));
            }
        }

        AwaitAssert(() =>
        {
            assignments.Should().HaveCountGreaterOrEqualTo(20);
        }, TimeSpan.FromSeconds(5));

        var elapsed = DateTime.UtcNow - startTime;
        var throughput = assignments.Count / elapsed.TotalSeconds;

        System.Diagnostics.Debug.WriteLine($"\n=== Platen Scheduler Throughput Test ===");
        System.Diagnostics.Debug.WriteLine($"Assignments: {assignments.Count}");
        System.Diagnostics.Debug.WriteLine($"Time: {elapsed.TotalSeconds:F2}s");
        System.Diagnostics.Debug.WriteLine($"Throughput: {throughput:F2} assignments/sec");

        assignments.Should().HaveCountGreaterOrEqualTo(20);
    }

    [Fact]
    public void PlatenScheduler_UtilizationMetrics_TrackStationUsage()
    {
        var assignments = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreatePlatenSchedulerWithCompletion(factory, assignments);

        // Assign 10 polish requests
        for (int i = 0; i < 10; i++)
        {
            scheduler.Tell(new SendEvent("POLISH_REQUEST", new Dictionary<string, object> { ["waferId"] = 1000 + i }));

            if (i > 0 && i % 2 == 0)
            {
                scheduler.Tell(new SendEvent("POLISH_COMPLETE", new Dictionary<string, object>
                {
                    ["station"] = "polisher1",
                    ["waferId"] = 1000 + i - 2
                }));
            }
        }

        AwaitAssert(() =>
        {
            var snapshot = scheduler.GetStateSnapshot();
            if (snapshot.Context.ContainsKey("totalPolishAssignments"))
            {
                var total = Convert.ToInt32(snapshot.Context["totalPolishAssignments"]);
                total.Should().BeGreaterOrEqualTo(5);
            }
        }, TimeSpan.FromSeconds(5));

        var finalSnapshot = scheduler.GetStateSnapshot();
        var polisher1Count = assignments.Count(a => a == "polisher1");
        var polisher2Count = assignments.Count(a => a == "polisher2");

        System.Diagnostics.Debug.WriteLine($"\n=== Platen Utilization ===");
        System.Diagnostics.Debug.WriteLine($"Polisher1: {polisher1Count} assignments");
        System.Diagnostics.Debug.WriteLine($"Polisher2: {polisher2Count} assignments");
        System.Diagnostics.Debug.WriteLine($"Total Polish: {finalSnapshot.Context["totalPolishAssignments"]}");

        // Both polishers should be used (load balancing)
        polisher1Count.Should().BeGreaterThan(0);
        polisher2Count.Should().BeGreaterThan(0);
    }

    #endregion

    #region Helper Methods

    private IActorRef CreatePlatenScheduler(XStateMachineFactory factory, List<string> assignments)
    {
        return factory.FromJson(GetPlatenSchedulerJson())
            .WithAction("reportScheduling", (ctx, _) => { })
            .WithAction("assignOrQueuePolisher", (ctx, evt) =>
            {
                var p1Status = ctx.Get<object>("polisher1Status")?.ToString();
                var p2Status = ctx.Get<object>("polisher2Status")?.ToString();

                if (p1Status == "idle")
                {
                    ctx.Set("polisher1Status", "busy");
                    assignments.Add("polisher1");
                    var total = ctx.Get<int>("totalPolishAssignments");
                    ctx.Set("totalPolishAssignments", total + 1);
                }
                else if (p2Status == "idle")
                {
                    ctx.Set("polisher2Status", "busy");
                    assignments.Add("polisher2");
                    var total = ctx.Get<int>("totalPolishAssignments");
                    ctx.Set("totalPolishAssignments", total + 1);
                }
                else
                {
                    // Queue the request
                    var data = evt as Dictionary<string, object>;
                    if (data != null)
                    {
                        var queue = ctx.Get<List<Dictionary<string, object>>>("polishQueue") ?? new List<Dictionary<string, object>>();
                        queue.Add(data);
                        ctx.Set("polishQueue", queue);
                    }
                }
            })
            .WithAction("assignOrQueueCleaner", (ctx, evt) =>
            {
                var c1Status = ctx.Get<object>("cleaner1Status")?.ToString();
                var c2Status = ctx.Get<object>("cleaner2Status")?.ToString();

                if (c1Status == "idle")
                {
                    ctx.Set("cleaner1Status", "busy");
                    assignments.Add("cleaner1");
                    var total = ctx.Get<int>("totalCleanAssignments");
                    ctx.Set("totalCleanAssignments", total + 1);
                }
                else if (c2Status == "idle")
                {
                    ctx.Set("cleaner2Status", "busy");
                    assignments.Add("cleaner2");
                    var total = ctx.Get<int>("totalCleanAssignments");
                    ctx.Set("totalCleanAssignments", total + 1);
                }
                else
                {
                    var data = evt as Dictionary<string, object>;
                    if (data != null)
                    {
                        var queue = ctx.Get<List<Dictionary<string, object>>>("cleanQueue") ?? new List<Dictionary<string, object>>();
                        queue.Add(data);
                        ctx.Set("cleanQueue", queue);
                    }
                }
            })
            .WithAction("freePolisher", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("station"))
                {
                    var station = data["station"]?.ToString();
                    if (station == "polisher1") ctx.Set("polisher1Status", "idle");
                    else if (station == "polisher2") ctx.Set("polisher2Status", "idle");
                }
            })
            .WithAction("processPolishQueue", (ctx, _) =>
            {
                var queue = ctx.Get<List<Dictionary<string, object>>>("polishQueue");
                if (queue != null && queue.Count > 0)
                {
                    var p1Status = ctx.Get<string>("polisher1Status");
                    var p2Status = ctx.Get<string>("polisher2Status");

                    if (p1Status == "idle")
                    {
                        ctx.Set("polisher1Status", "busy");
                        assignments.Add("polisher1");
                        queue.RemoveAt(0);
                        ctx.Set("polishQueue", queue);
                        var total = ctx.Get<int>("totalPolishAssignments");
                        ctx.Set("totalPolishAssignments", total + 1);
                    }
                    else if (p2Status == "idle")
                    {
                        ctx.Set("polisher2Status", "busy");
                        assignments.Add("polisher2");
                        queue.RemoveAt(0);
                        ctx.Set("polishQueue", queue);
                        var total = ctx.Get<int>("totalPolishAssignments");
                        ctx.Set("totalPolishAssignments", total + 1);
                    }
                }
            })
            .WithAction("freeCleaner", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("station"))
                {
                    var station = data["station"]?.ToString();
                    if (station == "cleaner1") ctx.Set("cleaner1Status", "idle");
                    else if (station == "cleaner2") ctx.Set("cleaner2Status", "idle");
                }
            })
            .WithAction("processCleanQueue", (ctx, _) =>
            {
                var queue = ctx.Get<List<Dictionary<string, object>>>("cleanQueue");
                if (queue != null && queue.Count > 0)
                {
                    var c1Status = ctx.Get<string>("cleaner1Status");
                    var c2Status = ctx.Get<string>("cleaner2Status");

                    if (c1Status == "idle")
                    {
                        ctx.Set("cleaner1Status", "busy");
                        assignments.Add("cleaner1");
                        queue.RemoveAt(0);
                        ctx.Set("cleanQueue", queue);
                        var total = ctx.Get<int>("totalCleanAssignments");
                        ctx.Set("totalCleanAssignments", total + 1);
                    }
                    else if (c2Status == "idle")
                    {
                        ctx.Set("cleaner2Status", "busy");
                        assignments.Add("cleaner2");
                        queue.RemoveAt(0);
                        ctx.Set("cleanQueue", queue);
                        var total = ctx.Get<int>("totalCleanAssignments");
                        ctx.Set("totalCleanAssignments", total + 1);
                    }
                }
            })
            .BuildAndStart();
    }

    private IActorRef CreatePlatenSchedulerWithCompletion(XStateMachineFactory factory, List<string> assignments)
    {
        return factory.FromJson(GetPlatenSchedulerJson())
            .WithAction("reportScheduling", (ctx, _) => { })
            .WithAction("assignOrQueuePolisher", (ctx, evt) =>
            {
                var p1Status = ctx.Get<object>("polisher1Status")?.ToString();
                var p2Status = ctx.Get<object>("polisher2Status")?.ToString();

                if (p1Status == "idle")
                {
                    ctx.Set("polisher1Status", "busy");
                    assignments.Add("polisher1");
                    var total = ctx.Get<int>("totalPolishAssignments");
                    ctx.Set("totalPolishAssignments", total + 1);
                }
                else if (p2Status == "idle")
                {
                    ctx.Set("polisher2Status", "busy");
                    assignments.Add("polisher2");
                    var total = ctx.Get<int>("totalPolishAssignments");
                    ctx.Set("totalPolishAssignments", total + 1);
                }
                else
                {
                    var data = evt as Dictionary<string, object>;
                    if (data != null)
                    {
                        var queue = ctx.Get<List<Dictionary<string, object>>>("polishQueue") ?? new List<Dictionary<string, object>>();
                        queue.Add(data);
                        ctx.Set("polishQueue", queue);
                    }
                }
            })
            .WithAction("assignOrQueueCleaner", (ctx, evt) =>
            {
                var c1Status = ctx.Get<object>("cleaner1Status")?.ToString();
                var c2Status = ctx.Get<object>("cleaner2Status")?.ToString();

                if (c1Status == "idle")
                {
                    ctx.Set("cleaner1Status", "busy");
                    assignments.Add("cleaner1");
                    var total = ctx.Get<int>("totalCleanAssignments");
                    ctx.Set("totalCleanAssignments", total + 1);
                }
                else if (c2Status == "idle")
                {
                    ctx.Set("cleaner2Status", "busy");
                    assignments.Add("cleaner2");
                    var total = ctx.Get<int>("totalCleanAssignments");
                    ctx.Set("totalCleanAssignments", total + 1);
                }
                else
                {
                    var data = evt as Dictionary<string, object>;
                    if (data != null)
                    {
                        var queue = ctx.Get<List<Dictionary<string, object>>>("cleanQueue") ?? new List<Dictionary<string, object>>();
                        queue.Add(data);
                        ctx.Set("cleanQueue", queue);
                    }
                }
            })
            .WithAction("freePolisher", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("station"))
                {
                    var station = data["station"]?.ToString();
                    if (station == "polisher1")
                        ctx.Set("polisher1Status", "idle");
                    else if (station == "polisher2")
                        ctx.Set("polisher2Status", "idle");
                }
            })
            .WithAction("processPolishQueue", (ctx, evt) =>
            {
                var queue = ctx.Get<List<Dictionary<string, object>>>("polishQueue");
                if (queue != null && queue.Count > 0)
                {
                    var p1Status = ctx.Get<string>("polisher1Status");
                    var p2Status = ctx.Get<string>("polisher2Status");

                    if (p1Status == "idle")
                    {
                        ctx.Set("polisher1Status", "busy");
                        assignments.Add("polisher1");
                        queue.RemoveAt(0);
                        ctx.Set("polishQueue", queue);
                        var total = ctx.Get<int>("totalPolishAssignments");
                        ctx.Set("totalPolishAssignments", total + 1);
                    }
                    else if (p2Status == "idle")
                    {
                        ctx.Set("polisher2Status", "busy");
                        assignments.Add("polisher2");
                        queue.RemoveAt(0);
                        ctx.Set("polishQueue", queue);
                        var total = ctx.Get<int>("totalPolishAssignments");
                        ctx.Set("totalPolishAssignments", total + 1);
                    }
                }
            })
            .WithAction("freeCleaner", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("station"))
                {
                    var station = data["station"]?.ToString();
                    if (station == "cleaner1")
                        ctx.Set("cleaner1Status", "idle");
                    else if (station == "cleaner2")
                        ctx.Set("cleaner2Status", "idle");
                }
            })
            .WithAction("processCleanQueue", (ctx, evt) =>
            {
                var queue = ctx.Get<List<Dictionary<string, object>>>("cleanQueue");
                if (queue != null && queue.Count > 0)
                {
                    var c1Status = ctx.Get<string>("cleaner1Status");
                    var c2Status = ctx.Get<string>("cleaner2Status");

                    if (c1Status == "idle")
                    {
                        ctx.Set("cleaner1Status", "busy");
                        assignments.Add("cleaner1");
                        queue.RemoveAt(0);
                        ctx.Set("cleanQueue", queue);
                        var total = ctx.Get<int>("totalCleanAssignments");
                        ctx.Set("totalCleanAssignments", total + 1);
                    }
                    else if (c2Status == "idle")
                    {
                        ctx.Set("cleaner2Status", "busy");
                        assignments.Add("cleaner2");
                        queue.RemoveAt(0);
                        ctx.Set("cleanQueue", queue);
                        var total = ctx.Get<int>("totalCleanAssignments");
                        ctx.Set("totalCleanAssignments", total + 1);
                    }
                }
            })
            .BuildAndStart();
    }

    #endregion
}
