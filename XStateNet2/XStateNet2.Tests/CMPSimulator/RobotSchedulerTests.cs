using Akka.Actor;
using FluentAssertions;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Extensions;
using XStateNet2.Tests;

namespace XStateNet2.Tests.CMPSimulator;

/// <summary>
/// Tests for Robot Scheduler - coordinates R1, R2, R3 robots
/// Responsibilities:
/// - R1: Carrier ↔ Polisher, Buffer ↔ Carrier
/// - R2: Polisher ↔ Cleaner
/// - R3: Cleaner ↔ Buffer
/// - Assign transfer requests to available robots
/// - Prevent robot conflicts and deadlocks
/// - Track robot states (idle/busy)
/// </summary>
public class RobotSchedulerTests : XStateTestKit
{
    #region State Machine Definition

    private string GetRobotSchedulerJson() => """
    {
        "id": "robotScheduler",
        "initial": "monitoring",
        "context": {
            "r1Status": "idle",
            "r2Status": "idle",
            "r3Status": "idle",
            "transferQueue": [],
            "activeTransfers": [],
            "totalTransfers": 0
        },
        "states": {
            "monitoring": {
                "entry": ["reportMonitoring"],
                "on": {
                    "TRANSFER_REQUEST": {
                        "actions": ["enqueueTransfer", "tryAssignRobot"]
                    },
                    "TRANSFER_COMPLETE": {
                        "actions": ["markRobotIdle", "processNextTransfer"]
                    },
                    "ROBOT_AVAILABLE": {
                        "actions": ["updateRobotStatus", "tryAssignRobot"]
                    }
                }
            }
        }
    }
    """;

    #endregion

    #region Basic Robot Assignment

    [Fact]
    public void RobotScheduler_InitialState_AllRobotsIdle()
    {
        var factory = new XStateMachineFactory(Sys);
        var scheduler = factory.FromJson(GetRobotSchedulerJson()).BuildAndStart();

        WaitForStateName(scheduler, "monitoring");

        var snapshot = scheduler.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("monitoring");

        // Handle JsonElement conversion
        var r1Status = snapshot.Context["r1Status"]?.ToString();
        var r2Status = snapshot.Context["r2Status"]?.ToString();
        var r3Status = snapshot.Context["r3Status"]?.ToString();

        r1Status.Should().Be("idle");
        r2Status.Should().Be("idle");
        r3Status.Should().Be("idle");
    }

    [Fact]
    public void RobotScheduler_R1Transfer_CarrierToPolisher()
    {
        var assignments = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = factory.FromJson(GetRobotSchedulerJson())
            .WithAction("reportMonitoring", (ctx, _) => { })
            .WithAction("enqueueTransfer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null)
                {
                    var queue = ctx.Get<List<Dictionary<string, object>>>("transferQueue") ?? new List<Dictionary<string, object>>();
                    queue.Add(data);
                    ctx.Set("transferQueue", queue);
                }
            })
            .WithAction("tryAssignRobot", (ctx, _) =>
            {
                var queue = ctx.Get<List<Dictionary<string, object>>>("transferQueue");
                if (queue != null && queue.Count > 0)
                {
                    var transfer = queue[0];
                    var from = transfer["from"]?.ToString();
                    var to = transfer["to"]?.ToString();

                    // R1 handles: Carrier ↔ Polisher, Buffer ↔ Carrier
                    if ((from == "carrier" && to == "polisher") ||
                        (from == "buffer" && to == "carrier"))
                    {
                        var r1Status = ctx.Get<object>("r1Status")?.ToString();
                        if (r1Status == "idle")
                        {
                            ctx.Set("r1Status", "busy");
                            assignments.Add("R1");
                            queue.RemoveAt(0);
                            ctx.Set("transferQueue", queue);

                            var total = ctx.Get<int>("totalTransfers");
                            ctx.Set("totalTransfers", total + 1);
                        }
                    }
                }
            })
            .WithAction("markRobotIdle", (ctx, evt) => { })
            .WithAction("processNextTransfer", (ctx, _) => { })
            .WithAction("updateRobotStatus", (ctx, _) => { })
            .BuildAndStart();

        // Request R1 transfer: Carrier → Polisher
        scheduler.Tell(new SendEvent("TRANSFER_REQUEST", new Dictionary<string, object>
        {
            ["from"] = "carrier",
            ["to"] = "polisher",
            ["waferId"] = 1001
        }));

        AwaitAssert(() =>
        {
            assignments.Should().Contain("R1");
        }, TimeSpan.FromSeconds(2));

        var snapshot = scheduler.GetStateSnapshot();
        snapshot.Context["r1Status"].Should().Be("busy");
        snapshot.Context["totalTransfers"].Should().Be(1);
    }

    [Fact]
    public void RobotScheduler_R2Transfer_PolisherToCleaner()
    {
        var assignments = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreateRobotScheduler(factory, assignments);

        // Request R2 transfer: Polisher → Cleaner
        scheduler.Tell(new SendEvent("TRANSFER_REQUEST", new Dictionary<string, object>
        {
            ["from"] = "polisher",
            ["to"] = "cleaner",
            ["waferId"] = 2001
        }));

        AwaitAssert(() =>
        {
            assignments.Should().Contain("R2");
        }, TimeSpan.FromSeconds(2));

        var snapshot = scheduler.GetStateSnapshot();
        snapshot.Context["r2Status"].Should().Be("busy");
    }

    [Fact]
    public void RobotScheduler_R3Transfer_CleanerToBuffer()
    {
        var assignments = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreateRobotScheduler(factory, assignments);

        // Request R3 transfer: Cleaner → Buffer
        scheduler.Tell(new SendEvent("TRANSFER_REQUEST", new Dictionary<string, object>
        {
            ["from"] = "cleaner",
            ["to"] = "buffer",
            ["waferId"] = 3001
        }));

        AwaitAssert(() =>
        {
            assignments.Should().Contain("R3");
        }, TimeSpan.FromSeconds(2));

        var snapshot = scheduler.GetStateSnapshot();
        snapshot.Context["r3Status"].Should().Be("busy");
    }

    #endregion

    #region Transfer Queue Management

    [Fact]
    public void RobotScheduler_RobotBusy_ShouldQueueTransfer()
    {
        var assignments = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreateRobotScheduler(factory, assignments);

        // First request - should assign immediately
        scheduler.Tell(new SendEvent("TRANSFER_REQUEST", new Dictionary<string, object>
        {
            ["from"] = "carrier",
            ["to"] = "polisher",
            ["waferId"] = 1001
        }));

        AwaitAssert(() => assignments.Should().Contain("R1"), TimeSpan.FromSeconds(2));

        // Second request - R1 is busy, should queue
        scheduler.Tell(new SendEvent("TRANSFER_REQUEST", new Dictionary<string, object>
        {
            ["from"] = "carrier",
            ["to"] = "polisher",
            ["waferId"] = 1002
        }));

        AwaitAssert(() =>
        {
            var snapshot = scheduler.GetStateSnapshot();
            var queue = snapshot.Context["transferQueue"] as List<Dictionary<string, object>>;
            queue.Should().NotBeNull();
            queue.Should().HaveCount(1);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void RobotScheduler_TransferComplete_ShouldProcessQueue()
    {
        var assignments = new List<string>();
        var completions = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreateRobotSchedulerWithCompletion(factory, assignments, completions);

        // First transfer
        scheduler.Tell(new SendEvent("TRANSFER_REQUEST", new Dictionary<string, object>
        {
            ["from"] = "carrier",
            ["to"] = "polisher",
            ["waferId"] = 1001
        }));

        AwaitAssert(() => assignments.Should().Contain("R1"), TimeSpan.FromSeconds(2));

        // Second transfer (queued)
        scheduler.Tell(new SendEvent("TRANSFER_REQUEST", new Dictionary<string, object>
        {
            ["from"] = "carrier",
            ["to"] = "polisher",
            ["waferId"] = 1002
        }));

        // Complete first transfer
        scheduler.Tell(new SendEvent("TRANSFER_COMPLETE", new Dictionary<string, object>
        {
            ["robot"] = "R1",
            ["waferId"] = 1001
        }));

        AwaitAssert(() =>
        {
            completions.Should().Contain("R1");
            var snapshot = scheduler.GetStateSnapshot();

            // Handle JsonElement conversion
            var r1Status = snapshot.Context["r1Status"]?.ToString();
            var totalTransfers = Convert.ToInt32(snapshot.Context["totalTransfers"]);

            // After completing first transfer, the queued second transfer should be assigned
            // So R1 should be busy with the second transfer
            r1Status.Should().Be("busy");
            totalTransfers.Should().Be(2);
        }, TimeSpan.FromSeconds(3));
    }

    #endregion

    #region Parallel Robot Operations

    [Fact]
    public void RobotScheduler_ParallelTransfers_AllThreeRobots()
    {
        var assignments = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreateRobotScheduler(factory, assignments);

        // R1: Carrier → Polisher
        scheduler.Tell(new SendEvent("TRANSFER_REQUEST", new Dictionary<string, object>
        {
            ["from"] = "carrier",
            ["to"] = "polisher",
            ["waferId"] = 1001
        }));

        // R2: Polisher → Cleaner
        scheduler.Tell(new SendEvent("TRANSFER_REQUEST", new Dictionary<string, object>
        {
            ["from"] = "polisher",
            ["to"] = "cleaner",
            ["waferId"] = 2001
        }));

        // R3: Cleaner → Buffer
        scheduler.Tell(new SendEvent("TRANSFER_REQUEST", new Dictionary<string, object>
        {
            ["from"] = "cleaner",
            ["to"] = "buffer",
            ["waferId"] = 3001
        }));

        AwaitAssert(() =>
        {
            assignments.Should().Contain("R1");
            assignments.Should().Contain("R2");
            assignments.Should().Contain("R3");

            var snapshot = scheduler.GetStateSnapshot();
            snapshot.Context["r1Status"].Should().Be("busy");
            snapshot.Context["r2Status"].Should().Be("busy");
            snapshot.Context["r3Status"].Should().Be("busy");
            snapshot.Context["totalTransfers"].Should().Be(3);
        }, TimeSpan.FromSeconds(3));
    }

    #endregion

    #region Throughput Tests

    [Fact]
    public void RobotScheduler_HighThroughput_100Transfers()
    {
        var assignments = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var scheduler = CreateRobotSchedulerWithCompletion(factory, assignments, new List<string>());
        var startTime = DateTime.UtcNow;

        // Simulate 100 transfers (mix of R1, R2, R3)
        var transferPatterns = new[]
        {
            ("carrier", "polisher"),   // R1
            ("polisher", "cleaner"),   // R2
            ("cleaner", "buffer"),     // R3
            ("buffer", "carrier")      // R1
        };

        for (int i = 0; i < 100; i++)
        {
            var pattern = transferPatterns[i % transferPatterns.Length];
            scheduler.Tell(new SendEvent("TRANSFER_REQUEST", new Dictionary<string, object>
            {
                ["from"] = pattern.Item1,
                ["to"] = pattern.Item2,
                ["waferId"] = 1000 + i
            }));

            // Simulate completions for all robots
            if (i >= 4)
            {
                var completedPattern = transferPatterns[(i - 4) % transferPatterns.Length];
                string robotId;

                // Determine which robot handles this pattern
                if ((completedPattern.Item1 == "carrier" && completedPattern.Item2 == "polisher") ||
                    (completedPattern.Item1 == "buffer" && completedPattern.Item2 == "carrier"))
                    robotId = "R1";
                else if (completedPattern.Item1 == "polisher" && completedPattern.Item2 == "cleaner")
                    robotId = "R2";
                else // cleaner → buffer
                    robotId = "R3";

                scheduler.Tell(new SendEvent("TRANSFER_COMPLETE", new Dictionary<string, object>
                {
                    ["robot"] = robotId,
                    ["waferId"] = 1000 + i - 4
                }));
            }
        }

        AwaitAssert(() =>
        {
            assignments.Should().HaveCountGreaterOrEqualTo(10);
        }, TimeSpan.FromSeconds(5));

        var elapsed = DateTime.UtcNow - startTime;
        var throughput = assignments.Count / elapsed.TotalSeconds;

        System.Diagnostics.Debug.WriteLine($"\n=== Robot Scheduler Throughput Test ===");
        System.Diagnostics.Debug.WriteLine($"Transfers assigned: {assignments.Count}");
        System.Diagnostics.Debug.WriteLine($"Time: {elapsed.TotalSeconds:F2}s");
        System.Diagnostics.Debug.WriteLine($"Throughput: {throughput:F2} assignments/sec");

        assignments.Should().HaveCountGreaterOrEqualTo(10);
    }

    #endregion

    #region Helper Methods

    private IActorRef CreateRobotScheduler(XStateMachineFactory factory, List<string> assignments)
    {
        return factory.FromJson(GetRobotSchedulerJson())
            .WithAction("reportMonitoring", (ctx, _) => { })
            .WithAction("enqueueTransfer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null)
                {
                    var queue = ctx.Get<List<Dictionary<string, object>>>("transferQueue") ?? new List<Dictionary<string, object>>();
                    queue.Add(data);
                    ctx.Set("transferQueue", queue);
                }
            })
            .WithAction("tryAssignRobot", (ctx, _) =>
            {
                var queue = ctx.Get<List<Dictionary<string, object>>>("transferQueue");
                if (queue != null && queue.Count > 0)
                {
                    var transfer = queue[0];
                    var from = transfer.ContainsKey("from") ? transfer["from"]?.ToString() : null;
                    var to = transfer.ContainsKey("to") ? transfer["to"]?.ToString() : null;

                    // R1: Carrier ↔ Polisher, Buffer ↔ Carrier
                    if ((from == "carrier" && to == "polisher") || (from == "buffer" && to == "carrier"))
                    {
                        var r1Status = ctx.Get<object>("r1Status")?.ToString();
                        if (r1Status == "idle")
                        {
                            ctx.Set("r1Status", "busy");
                            assignments.Add("R1");
                            queue.RemoveAt(0);
                            ctx.Set("transferQueue", queue);
                            var total = ctx.Get<int>("totalTransfers");
                            ctx.Set("totalTransfers", total + 1);
                        }
                    }
                    // R2: Polisher ↔ Cleaner
                    else if (from == "polisher" && to == "cleaner")
                    {
                        var r2Status = ctx.Get<object>("r2Status")?.ToString();
                        if (r2Status == "idle")
                        {
                            ctx.Set("r2Status", "busy");
                            assignments.Add("R2");
                            queue.RemoveAt(0);
                            ctx.Set("transferQueue", queue);
                            var total = ctx.Get<int>("totalTransfers");
                            ctx.Set("totalTransfers", total + 1);
                        }
                    }
                    // R3: Cleaner ↔ Buffer
                    else if (from == "cleaner" && to == "buffer")
                    {
                        var r3Status = ctx.Get<object>("r3Status")?.ToString();
                        if (r3Status == "idle")
                        {
                            ctx.Set("r3Status", "busy");
                            assignments.Add("R3");
                            queue.RemoveAt(0);
                            ctx.Set("transferQueue", queue);
                            var total = ctx.Get<int>("totalTransfers");
                            ctx.Set("totalTransfers", total + 1);
                        }
                    }
                }
            })
            .WithAction("markRobotIdle", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("robot"))
                {
                    var robot = data["robot"]?.ToString();
                    if (robot == "R1") ctx.Set("r1Status", "idle");
                    else if (robot == "R2") ctx.Set("r2Status", "idle");
                    else if (robot == "R3") ctx.Set("r3Status", "idle");
                }
            })
            .WithAction("processNextTransfer", (ctx, _) =>
            {
                // Try to assign next transfer from queue
                var queue = ctx.Get<List<Dictionary<string, object>>>("transferQueue");
                if (queue != null && queue.Count > 0)
                {
                    var transfer = queue[0];
                    var from = transfer.ContainsKey("from") ? transfer["from"]?.ToString() : null;
                    var to = transfer.ContainsKey("to") ? transfer["to"]?.ToString() : null;

                    // R1: Carrier ↔ Polisher, Buffer ↔ Carrier
                    if ((from == "carrier" && to == "polisher") || (from == "buffer" && to == "carrier"))
                    {
                        var r1Status = ctx.Get<object>("r1Status")?.ToString();
                        if (r1Status == "idle")
                        {
                            ctx.Set("r1Status", "busy");
                            assignments.Add("R1");
                            queue.RemoveAt(0);
                            ctx.Set("transferQueue", queue);
                            var total = ctx.Get<int>("totalTransfers");
                            ctx.Set("totalTransfers", total + 1);
                        }
                    }
                    // R2: Polisher ↔ Cleaner
                    else if (from == "polisher" && to == "cleaner")
                    {
                        var r2Status = ctx.Get<object>("r2Status")?.ToString();
                        if (r2Status == "idle")
                        {
                            ctx.Set("r2Status", "busy");
                            assignments.Add("R2");
                            queue.RemoveAt(0);
                            ctx.Set("transferQueue", queue);
                            var total = ctx.Get<int>("totalTransfers");
                            ctx.Set("totalTransfers", total + 1);
                        }
                    }
                    // R3: Cleaner ↔ Buffer
                    else if (from == "cleaner" && to == "buffer")
                    {
                        var r3Status = ctx.Get<object>("r3Status")?.ToString();
                        if (r3Status == "idle")
                        {
                            ctx.Set("r3Status", "busy");
                            assignments.Add("R3");
                            queue.RemoveAt(0);
                            ctx.Set("transferQueue", queue);
                            var total = ctx.Get<int>("totalTransfers");
                            ctx.Set("totalTransfers", total + 1);
                        }
                    }
                }
            })
            .WithAction("updateRobotStatus", (ctx, _) => { })
            .BuildAndStart();
    }

    private IActorRef CreateRobotSchedulerWithCompletion(
        XStateMachineFactory factory,
        List<string> assignments,
        List<string> completions)
    {
        return factory.FromJson(GetRobotSchedulerJson())
            .WithAction("reportMonitoring", (ctx, _) => { })
            .WithAction("enqueueTransfer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null)
                {
                    var queue = ctx.Get<List<Dictionary<string, object>>>("transferQueue") ?? new List<Dictionary<string, object>>();
                    queue.Add(data);
                    ctx.Set("transferQueue", queue);
                }
            })
            .WithAction("tryAssignRobot", (ctx, _) =>
            {
                var queue = ctx.Get<List<Dictionary<string, object>>>("transferQueue");
                if (queue != null && queue.Count > 0)
                {
                    var transfer = queue[0];
                    var from = transfer.ContainsKey("from") ? transfer["from"]?.ToString() : null;
                    var to = transfer.ContainsKey("to") ? transfer["to"]?.ToString() : null;

                    if ((from == "carrier" && to == "polisher") || (from == "buffer" && to == "carrier"))
                    {
                        var r1Status = ctx.Get<object>("r1Status")?.ToString();
                        if (r1Status == "idle")
                        {
                            ctx.Set("r1Status", "busy");
                            assignments.Add("R1");
                            queue.RemoveAt(0);
                            ctx.Set("transferQueue", queue);
                            var total = ctx.Get<int>("totalTransfers");
                            ctx.Set("totalTransfers", total + 1);
                        }
                    }
                    else if (from == "polisher" && to == "cleaner")
                    {
                        var r2Status = ctx.Get<object>("r2Status")?.ToString();
                        if (r2Status == "idle")
                        {
                            ctx.Set("r2Status", "busy");
                            assignments.Add("R2");
                            queue.RemoveAt(0);
                            ctx.Set("transferQueue", queue);
                            var total = ctx.Get<int>("totalTransfers");
                            ctx.Set("totalTransfers", total + 1);
                        }
                    }
                    else if (from == "cleaner" && to == "buffer")
                    {
                        var r3Status = ctx.Get<object>("r3Status")?.ToString();
                        if (r3Status == "idle")
                        {
                            ctx.Set("r3Status", "busy");
                            assignments.Add("R3");
                            queue.RemoveAt(0);
                            ctx.Set("transferQueue", queue);
                            var total = ctx.Get<int>("totalTransfers");
                            ctx.Set("totalTransfers", total + 1);
                        }
                    }
                }
            })
            .WithAction("markRobotIdle", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("robot"))
                {
                    var robot = data["robot"]?.ToString();
                    if (robot == "R1")
                    {
                        ctx.Set("r1Status", "idle");
                        completions.Add("R1");
                    }
                    else if (robot == "R2")
                    {
                        ctx.Set("r2Status", "idle");
                        completions.Add("R2");
                    }
                    else if (robot == "R3")
                    {
                        ctx.Set("r3Status", "idle");
                        completions.Add("R3");
                    }
                }
            })
            .WithAction("processNextTransfer", (ctx, _) =>
            {
                // Try to assign next transfer from queue
                var queue = ctx.Get<List<Dictionary<string, object>>>("transferQueue");
                if (queue != null && queue.Count > 0)
                {
                    var transfer = queue[0];
                    var from = transfer.ContainsKey("from") ? transfer["from"]?.ToString() : null;
                    var to = transfer.ContainsKey("to") ? transfer["to"]?.ToString() : null;

                    // R1: Carrier ↔ Polisher, Buffer ↔ Carrier
                    if ((from == "carrier" && to == "polisher") || (from == "buffer" && to == "carrier"))
                    {
                        var r1Status = ctx.Get<object>("r1Status")?.ToString();
                        if (r1Status == "idle")
                        {
                            ctx.Set("r1Status", "busy");
                            assignments.Add("R1");
                            queue.RemoveAt(0);
                            ctx.Set("transferQueue", queue);
                            var total = ctx.Get<int>("totalTransfers");
                            ctx.Set("totalTransfers", total + 1);
                        }
                    }
                    // R2: Polisher ↔ Cleaner
                    else if (from == "polisher" && to == "cleaner")
                    {
                        var r2Status = ctx.Get<object>("r2Status")?.ToString();
                        if (r2Status == "idle")
                        {
                            ctx.Set("r2Status", "busy");
                            assignments.Add("R2");
                            queue.RemoveAt(0);
                            ctx.Set("transferQueue", queue);
                            var total = ctx.Get<int>("totalTransfers");
                            ctx.Set("totalTransfers", total + 1);
                        }
                    }
                    // R3: Cleaner ↔ Buffer
                    else if (from == "cleaner" && to == "buffer")
                    {
                        var r3Status = ctx.Get<object>("r3Status")?.ToString();
                        if (r3Status == "idle")
                        {
                            ctx.Set("r3Status", "busy");
                            assignments.Add("R3");
                            queue.RemoveAt(0);
                            ctx.Set("transferQueue", queue);
                            var total = ctx.Get<int>("totalTransfers");
                            ctx.Set("totalTransfers", total + 1);
                        }
                    }
                }
            })
            .WithAction("updateRobotStatus", (ctx, _) => { })
            .BuildAndStart();
    }

    #endregion
}
