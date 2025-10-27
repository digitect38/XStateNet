using System.IO;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests;

/// <summary>
/// Integration tests for RobotMachine JSON definition
/// Tests the actual CMP Simulator RobotMachine.json file
/// </summary>
public class RobotMachineIntegrationTests : TestKit
{
    [Fact]
    public async Task RobotMachine_CompleteTransfer_ShouldWork()
    {
        // Arrange - Load actual RobotMachine.json from CMPSimulator
        var jsonPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..",
            "CMPSimulator", "StateMachines", "RobotMachine.json");

        if (!File.Exists(jsonPath))
        {
            // Skip test if file not found (e.g., in CI environment)
            return;
        }

        var json = await File.ReadAllTextAsync(jsonPath);

        // Create mock actors for scheduler and stations
        var scheduler = CreateTestProbe("scheduler");
        var loadPort = CreateTestProbe("LoadPort");
        var polisher = CreateTestProbe("P1");

        var factory = new XStateMachineFactory(Sys);

        bool transferInfoStored = false;
        bool pickedWafer = false;
        bool placedWafer = false;
        bool transferComplete = false;

        var robot = factory.FromJson(json)
            .WithAction("onReset", (ctx, _) => { })
            .WithAction("onIdle", (ctx, _) => { })
            .WithAction("storeTransferInfo", (ctx, data) =>
            {
                // Extract and store transfer info
                if (data != null)
                {
                    var eventData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
                        System.Text.Json.JsonSerializer.Serialize(data));

                    if (eventData != null)
                    {
                        if (eventData.TryGetValue("waferId", out var waferElem))
                            ctx.Set("heldWafer", waferElem.GetInt32());
                        if (eventData.TryGetValue("from", out var fromElem))
                            ctx.Set("pickFrom", fromElem.GetString() ?? "");
                        if (eventData.TryGetValue("to", out var toElem))
                            ctx.Set("placeTo", toElem.GetString() ?? "");
                    }
                }
                transferInfoStored = true;
            })
            .WithAction("onTransferCommand", (ctx, _) => { })
            .WithAction("onPickingUp", (ctx, _) => { })
            .WithAction("onPickedWafer", (ctx, _) =>
            {
                pickedWafer = true;
            })
            .WithAction("onHolding", (ctx, _) => { })
            .WithAction("onPlacingDown", (ctx, _) => { })
            .WithAction("onPlacedWafer", (ctx, _) =>
            {
                placedWafer = true;
            })
            .WithAction("onReturning", (ctx, _) => { })
            .WithAction("onTransferComplete", (ctx, _) =>
            {
                transferComplete = true;
            })
            .WithDelayService("moveToPickup", 100)
            .WithDelayService("moveToPlace", 100)
            .WithDelayService("returnToIdle", 50)
            .WithActor("scheduler", scheduler)
            .BuildAndStart("R1");

        // Wait for initialization to complete
        await AwaitAssertAsync(() =>
        {
            var snapshot = robot.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3)).Result;
            Assert.Equal("idle", snapshot.CurrentState);
        }, TimeSpan.FromSeconds(3));

        // Act - Send transfer command
        robot.Tell(new SendEvent("TRANSFER", new
        {
            waferId = 1,
            from = "LoadPort",
            to = "P1"
        }));

        // Wait for pickup to complete using AwaitAssert
        await AwaitAssertAsync(() =>
        {
            Assert.True(pickedWafer, "Wafer should be picked");
        }, TimeSpan.FromSeconds(3));

        // Should send ROBOT_STATUS to scheduler when holding
        var statusMsg = scheduler.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(1));
        Assert.Equal("ROBOT_STATUS", statusMsg.Type);

        // Send destination ready
        robot.Tell(new SendEvent("DESTINATION_READY", null));

        // Wait for place and return using AwaitAssert
        await AwaitAssertAsync(() =>
        {
            Assert.True(transferInfoStored, "Transfer info should be stored");
            Assert.True(placedWafer, "Wafer should be placed");
            Assert.True(transferComplete, "Transfer should be complete");
        }, TimeSpan.FromSeconds(3));
    }

    //[Fact(Skip = "Global events during invoke services not yet supported - future enhancement")]
    [Fact]
    public async Task RobotMachine_ResetCommand_ShouldClearContext()
    {
        // Arrange
        var jsonPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..",
            "CMPSimulator", "StateMachines", "RobotMachine.json");

        if (!File.Exists(jsonPath))
        {
            return;
        }

        var json = await File.ReadAllTextAsync(jsonPath);
        var scheduler = CreateTestProbe("scheduler");

        var factory = new XStateMachineFactory(Sys);

        bool resetCalled = false;
        bool idleCalled = false;

        var robot = factory.FromJson(json)
            .WithAction("onReset", (ctx, _) =>
            {
                // Verify context is cleared
                var wafer = ctx.Get<int?>("heldWafer");
                var from = ctx.Get<string>("pickFrom");
                var to = ctx.Get<string>("placeTo");

                Assert.Null(wafer);
                Assert.Null(from);
                Assert.Null(to);

                resetCalled = true;
            })
            .WithAction("onIdle", (ctx, _) =>
            {
                idleCalled = true;
            })
            .WithAction("storeTransferInfo", (ctx, data) =>
            {
                if (data != null)
                {
                    var eventData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
                        System.Text.Json.JsonSerializer.Serialize(data));

                    if (eventData != null)
                    {
                        if (eventData.TryGetValue("waferId", out var waferElem))
                            ctx.Set("heldWafer", waferElem.GetInt32());
                        if (eventData.TryGetValue("from", out var fromElem))
                            ctx.Set("pickFrom", fromElem.GetString() ?? "");
                        if (eventData.TryGetValue("to", out var toElem))
                            ctx.Set("placeTo", toElem.GetString() ?? "");
                    }
                }
            })
            .WithAction("onTransferCommand", (ctx, _) => { })
            .WithAction("onPickingUp", (ctx, _) => { })
            .WithAction("onPickedWafer", (ctx, _) => { })
            .WithAction("onHolding", (ctx, _) => { })
            .WithAction("onPlacingDown", (ctx, _) => { })
            .WithAction("onPlacedWafer", (ctx, _) => { })
            .WithAction("onReturning", (ctx, _) => { })
            .WithAction("onTransferComplete", (ctx, _) => { })
            .WithDelayService("moveToPickup", 100)
            .WithDelayService("moveToPlace", 100)
            .WithDelayService("returnToIdle", 50)
            .WithActor("scheduler", scheduler)
            .BuildAndStart("R1");

        await Task.Delay(100);

        // Act - Send transfer to set context
        robot.Tell(new SendEvent("TRANSFER", new
        {
            waferId = 1,
            from = "LoadPort",
            to = "P1"
        }));

        await Task.Delay(50);

        // Send RESET while in pickingUp state
        robot.Tell(new SendEvent("RESET", null));

        await Task.Delay(300); // Give more time for reset to process

        // Assert
        Assert.True(resetCalled, "Reset should be called");
        Assert.True(idleCalled, "Should return to idle after reset");
    }

    [Fact]
    public async Task RobotMachine_IdleState_ShouldSendStatusToScheduler()
    {
        // Arrange
        var jsonPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..",
            "CMPSimulator", "StateMachines", "RobotMachine.json");

        if (!File.Exists(jsonPath))
        {
            return;
        }

        var json = await File.ReadAllTextAsync(jsonPath);
        var scheduler = CreateTestProbe("scheduler");

        var factory = new XStateMachineFactory(Sys);

        var robot = factory.FromJson(json)
            .WithAction("onReset", (ctx, _) => { })
            .WithAction("onIdle", (ctx, _) => { })
            .WithAction("storeTransferInfo", (ctx, _) => { })
            .WithAction("onTransferCommand", (ctx, _) => { })
            .WithAction("onPickingUp", (ctx, _) => { })
            .WithAction("onPickedWafer", (ctx, _) => { })
            .WithAction("onHolding", (ctx, _) => { })
            .WithAction("onPlacingDown", (ctx, _) => { })
            .WithAction("onPlacedWafer", (ctx, _) => { })
            .WithAction("onReturning", (ctx, _) => { })
            .WithAction("onTransferComplete", (ctx, _) => { })
            .WithDelayService("moveToPickup", 100)
            .WithDelayService("moveToPlace", 100)
            .WithDelayService("returnToIdle", 50)
            .WithActor("scheduler", scheduler)
            .BuildAndStart("R1");

        // Act - Wait for initial idle state
        await Task.Delay(100);

        // Assert - Should receive ROBOT_STATUS event
        var statusEvent = scheduler.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(1));
        Assert.Equal("ROBOT_STATUS", statusEvent.Type);

        // Verify status data
        var statusData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
            System.Text.Json.JsonSerializer.Serialize(statusEvent.Data));

        Assert.NotNull(statusData);
        Assert.Equal("R1", statusData["robot"].GetString());
        Assert.Equal("idle", statusData["state"].GetString());
    }
}
