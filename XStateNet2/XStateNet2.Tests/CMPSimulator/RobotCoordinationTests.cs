using Akka.Actor;
using FluentAssertions;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Extensions;
using XStateNet2.Tests;

namespace XStateNet2.Tests.CMPSimulator;

/// <summary>
/// Tests for Robot coordination in CMP system
/// R1: Carrier ↔ Polisher, Buffer ↔ Carrier
/// R2: Polisher ↔ Cleaner
/// R3: Cleaner ↔ Buffer
/// </summary>
public class RobotCoordinationTests : XStateTestKit
{
    #region Robot State Machine

    private string GetRobotMachineJson() => """
    {
        "id": "robot",
        "initial": "idle",
        "context": {
            "wafer": null,
            "fromStation": null,
            "toStation": null
        },
        "states": {
            "idle": {
                "entry": ["reportIdle"],
                "on": {
                    "PICKUP": {
                        "target": "carrying",
                        "actions": ["pickupWafer"]
                    }
                }
            },
            "carrying": {
                "entry": ["reportCarrying"],
                "on": {
                    "PLACE": {
                        "target": "idle",
                        "actions": ["placeWafer", "clearWafer"]
                    }
                }
            }
        }
    }
    """;

    #endregion

    #region Single Robot Operations

    [Fact]
    public void Robot_InitialState_ShouldBeIdle()
    {
        var factory = new XStateMachineFactory(Sys);
        var robot = factory.FromJson(GetRobotMachineJson()).BuildAndStart();

        WaitForStateName(robot, "idle");

        var snapshot = robot.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("idle");
        snapshot.Context["wafer"].Should().BeNull();
    }

    [Fact]
    public void Robot_Pickup_ShouldTransitionToCarrying()
    {
        var factory = new XStateMachineFactory(Sys);

        var robot = factory.FromJson(GetRobotMachineJson())
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("pickupWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null)
                {
                    if (data.ContainsKey("wafer")) ctx.Set("wafer", data["wafer"]);
                    if (data.ContainsKey("from")) ctx.Set("fromStation", data["from"]);
                }
            })
            .WithAction("reportCarrying", (ctx, _) => { })
            .WithAction("placeWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("to"))
                {
                    ctx.Set("toStation", data["to"]);
                }
            })
            .WithAction("clearWafer", (ctx, _) =>
            {
                ctx.Set("wafer", null);
                ctx.Set("fromStation", null);
                ctx.Set("toStation", null);
            })
            .BuildAndStart();

        SendEventAndWait(robot, "PICKUP",
            s => s.CurrentState == "carrying",
            "carrying",
            new Dictionary<string, object>
            {
                ["wafer"] = 5001,
                ["from"] = "carrier"
            });

        var snapshot = robot.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("carrying");
    }

    [Fact]
    public void Robot_PickupAndPlace_CompleteCycle()
    {
        var factory = new XStateMachineFactory(Sys);

        var robot = factory.FromJson(GetRobotMachineJson())
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("pickupWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null)
                {
                    if (data.ContainsKey("wafer")) ctx.Set("wafer", data["wafer"]);
                    if (data.ContainsKey("from")) ctx.Set("fromStation", data["from"]);
                }
            })
            .WithAction("reportCarrying", (ctx, _) => { })
            .WithAction("placeWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("to"))
                {
                    ctx.Set("toStation", data["to"]);
                }
            })
            .WithAction("clearWafer", (ctx, _) =>
            {
                ctx.Set("wafer", null);
                ctx.Set("fromStation", null);
                ctx.Set("toStation", null);
            })
            .BuildAndStart();

        // Pickup from carrier
        SendEventAndWait(robot, "PICKUP",
            s => s.CurrentState == "carrying",
            "carrying",
            new Dictionary<string, object>
            {
                ["wafer"] = 5002,
                ["from"] = "carrier"
            });

        // Place to polisher
        SendEventAndWait(robot, "PLACE",
            s => s.CurrentState == "idle",
            "idle",
            new Dictionary<string, object> { ["to"] = "polisher" });

        var snapshot = robot.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("idle");
        snapshot.Context["wafer"].Should().BeNull();
    }

    #endregion

    #region Three Robot Coordination

    [Fact]
    public void ThreeRobots_IndependentOperations_NoConflicts()
    {
        var factory = new XStateMachineFactory(Sys);

        var r1 = CreateRobot(factory, "R1");
        var r2 = CreateRobot(factory, "R2");
        var r3 = CreateRobot(factory, "R3");

        // R1: Carrier → Polisher
        SendEventAndWait(r1, "PICKUP",
            s => s.CurrentState == "carrying",
            "R1 carrying",
            new Dictionary<string, object>
            {
                ["wafer"] = 6001,
                ["from"] = "carrier"
            });

        // R2: Polisher → Cleaner (while R1 is still carrying)
        SendEventAndWait(r2, "PICKUP",
            s => s.CurrentState == "carrying",
            "R2 carrying",
            new Dictionary<string, object>
            {
                ["wafer"] = 6002,
                ["from"] = "polisher"
            });

        // R3: Cleaner → Buffer (while R1 and R2 are still carrying)
        SendEventAndWait(r3, "PICKUP",
            s => s.CurrentState == "carrying",
            "R3 carrying",
            new Dictionary<string, object>
            {
                ["wafer"] = 6003,
                ["from"] = "cleaner"
            });

        // All three robots should be carrying simultaneously
        var r1State = r1.GetStateSnapshot();
        var r2State = r2.GetStateSnapshot();
        var r3State = r3.GetStateSnapshot();

        r1State.CurrentState.Should().Be("carrying");
        r2State.CurrentState.Should().Be("carrying");
        r3State.CurrentState.Should().Be("carrying");
    }

    [Fact]
    public void ThreeRobots_SequentialTransfers_PipelineFlow()
    {
        var factory = new XStateMachineFactory(Sys);

        var r1 = CreateRobot(factory, "R1");
        var r2 = CreateRobot(factory, "R2");
        var r3 = CreateRobot(factory, "R3");

        var waferId = 7001;

        // R1: Carrier → Polisher
        SendEventAndWait(r1, "PICKUP",
            s => s.CurrentState == "carrying",
            "R1 pickup from carrier",
            new Dictionary<string, object> { ["wafer"] = waferId, ["from"] = "carrier" });

        SendEventAndWait(r1, "PLACE",
            s => s.CurrentState == "idle",
            "R1 place to polisher",
            new Dictionary<string, object> { ["to"] = "polisher" });

        // Simulate polishing complete

        // R2: Polisher → Cleaner
        SendEventAndWait(r2, "PICKUP",
            s => s.CurrentState == "carrying",
            "R2 pickup from polisher",
            new Dictionary<string, object> { ["wafer"] = waferId, ["from"] = "polisher" });

        SendEventAndWait(r2, "PLACE",
            s => s.CurrentState == "idle",
            "R2 place to cleaner",
            new Dictionary<string, object> { ["to"] = "cleaner" });

        // Simulate cleaning complete

        // R3: Cleaner → Buffer
        SendEventAndWait(r3, "PICKUP",
            s => s.CurrentState == "carrying",
            "R3 pickup from cleaner",
            new Dictionary<string, object> { ["wafer"] = waferId, ["from"] = "cleaner" });

        SendEventAndWait(r3, "PLACE",
            s => s.CurrentState == "idle",
            "R3 place to buffer",
            new Dictionary<string, object> { ["to"] = "buffer" });

        // R1: Buffer → Carrier
        SendEventAndWait(r1, "PICKUP",
            s => s.CurrentState == "carrying",
            "R1 pickup from buffer",
            new Dictionary<string, object> { ["wafer"] = waferId, ["from"] = "buffer" });

        SendEventAndWait(r1, "PLACE",
            s => s.CurrentState == "idle",
            "R1 return to carrier",
            new Dictionary<string, object> { ["to"] = "carrier" });

        // All robots should be idle
        r1.GetStateSnapshot().CurrentState.Should().Be("idle");
        r2.GetStateSnapshot().CurrentState.Should().Be("idle");
        r3.GetStateSnapshot().CurrentState.Should().Be("idle");
    }

    #endregion

    #region Multi-Wafer Scenario

    [Fact]
    public void ThreeRobots_TwoWafersInPipeline_Concurrent()
    {
        var factory = new XStateMachineFactory(Sys);

        var r1 = CreateRobot(factory, "R1");
        var r2 = CreateRobot(factory, "R2");
        var r3 = CreateRobot(factory, "R3");

        // Wafer 1: R1 picks up from carrier
        SendEventAndWait(r1, "PICKUP",
            s => s.CurrentState == "carrying",
            "R1 carrying wafer 1",
            new Dictionary<string, object> { ["wafer"] = 8001, ["from"] = "carrier" });

        // Wafer 1: R1 places to polisher
        SendEventAndWait(r1, "PLACE",
            s => s.CurrentState == "idle",
            "R1 idle",
            new Dictionary<string, object> { ["to"] = "polisher" });

        // Wafer 1: After polishing, R2 picks up
        SendEventAndWait(r2, "PICKUP",
            s => s.CurrentState == "carrying",
            "R2 carrying wafer 1",
            new Dictionary<string, object> { ["wafer"] = 8001, ["from"] = "polisher" });

        // Wafer 2: While R2 is carrying wafer 1, R1 picks up wafer 2
        SendEventAndWait(r1, "PICKUP",
            s => s.CurrentState == "carrying",
            "R1 carrying wafer 2",
            new Dictionary<string, object> { ["wafer"] = 8002, ["from"] = "carrier" });

        // Both R1 and R2 should be carrying
        r1.GetStateSnapshot().CurrentState.Should().Be("carrying");
        r2.GetStateSnapshot().CurrentState.Should().Be("carrying");

        // Wafer 1: R2 places to cleaner
        SendEventAndWait(r2, "PLACE",
            s => s.CurrentState == "idle",
            "R2 idle",
            new Dictionary<string, object> { ["to"] = "cleaner" });

        // Wafer 2: R1 places to polisher
        SendEventAndWait(r1, "PLACE",
            s => s.CurrentState == "idle",
            "R1 idle",
            new Dictionary<string, object> { ["to"] = "polisher" });

        // Now polisher has wafer 2, cleaner has wafer 1
        r1.GetStateSnapshot().CurrentState.Should().Be("idle");
        r2.GetStateSnapshot().CurrentState.Should().Be("idle");
    }

    #endregion

    #region Helper Methods

    private IActorRef CreateRobot(XStateMachineFactory factory, string robotId)
    {
        var json = GetRobotMachineJson().Replace("\"id\": \"robot\"", $"\"id\": \"{robotId}\"");

        return factory.FromJson(json)
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("pickupWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null)
                {
                    if (data.ContainsKey("wafer")) ctx.Set("wafer", data["wafer"]);
                    if (data.ContainsKey("from")) ctx.Set("fromStation", data["from"]);
                }
            })
            .WithAction("reportCarrying", (ctx, _) => { })
            .WithAction("placeWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("to"))
                {
                    ctx.Set("toStation", data["to"]);
                }
            })
            .WithAction("clearWafer", (ctx, _) =>
            {
                ctx.Set("wafer", null);
                ctx.Set("fromStation", null);
                ctx.Set("toStation", null);
            })
            .BuildAndStart(robotId);
    }

    #endregion
}
