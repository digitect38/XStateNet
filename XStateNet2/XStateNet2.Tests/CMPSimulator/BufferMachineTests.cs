using Akka.Actor;
using FluentAssertions;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Extensions;
using XStateNet2.Tests;

namespace XStateNet2.Tests.CMPSimulator;

/// <summary>
/// Comprehensive tests for Buffer Station using XStateNet2
/// Buffer: Simple storage station that holds wafers temporarily
/// States: Empty → Occupied → Empty
/// </summary>
public class BufferMachineTests : XStateTestKit
{
    #region State Machine Definition

    private string GetBufferMachineJson() => """
    {
        "id": "buffer",
        "initial": "empty",
        "context": {
            "wafer": null,
            "capacity": 1
        },
        "states": {
            "empty": {
                "entry": ["reportEmpty"],
                "on": {
                    "PLACE": {
                        "target": "occupied",
                        "actions": ["storeWafer", "reportOccupied"]
                    }
                }
            },
            "occupied": {
                "on": {
                    "PICK": {
                        "target": "empty",
                        "actions": ["clearWafer", "reportEmpty"]
                    }
                }
            }
        }
    }
    """;

    #endregion

    #region Basic State Transitions

    [Fact]
    public void Buffer_InitialState_ShouldBeEmpty()
    {
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetBufferMachineJson()).BuildAndStart();

        WaitForStateName(machine, "empty");

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("empty");
        snapshot.Context["wafer"].Should().BeNull();
    }

    [Fact]
    public void Buffer_PlaceWafer_ShouldTransitionToOccupied()
    {
        var waferPlaced = false;
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetBufferMachineJson())
            .WithAction("reportEmpty", (ctx, _) => { })
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                    waferPlaced = true;
                }
            })
            .WithAction("reportOccupied", (ctx, _) => { })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        WaitForStateName(machine, "empty");

        SendEventAndWait(machine, "PLACE",
            s => s.CurrentState == "occupied",
            "occupied after PLACE",
            new Dictionary<string, object> { ["wafer"] = 101 });

        waferPlaced.Should().BeTrue();

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("occupied");
    }

    [Fact]
    public void Buffer_PickWafer_ShouldTransitionToEmpty()
    {
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetBufferMachineJson())
            .WithAction("reportEmpty", (ctx, _) => { })
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("reportOccupied", (ctx, _) => { })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        WaitForStateName(machine, "empty");

        // Place wafer
        SendEventAndWait(machine, "PLACE",
            s => s.CurrentState == "occupied",
            "occupied",
            new Dictionary<string, object> { ["wafer"] = 102 });

        // Pick wafer
        SendEventAndWait(machine, "PICK",
            s => s.CurrentState == "empty",
            "empty after PICK");

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("empty");
        snapshot.Context["wafer"].Should().BeNull();
    }

    #endregion

    #region Wafer Tracking

    [Fact]
    public void Buffer_ShouldTrackWaferId()
    {
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetBufferMachineJson())
            .WithAction("reportEmpty", (ctx, _) => { })
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("reportOccupied", (ctx, _) => { })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        WaitForStateName(machine, "empty");

        // Place wafer 203
        SendEventAndWait(machine, "PLACE",
            s => s.CurrentState == "occupied",
            "occupied",
            new Dictionary<string, object> { ["wafer"] = 203 });

        WaitForContextValue(machine, "wafer", 203);

        var snapshot = machine.GetStateSnapshot();
        var waferValue = snapshot.Context["wafer"];

        // Handle JsonElement
        int actualWafer;
        if (waferValue is System.Text.Json.JsonElement element)
        {
            actualWafer = element.GetInt32();
        }
        else
        {
            actualWafer = Convert.ToInt32(waferValue);
        }

        actualWafer.Should().Be(203);
    }

    [Fact]
    public void Buffer_MultiplePlacePickCycles_ShouldWork()
    {
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetBufferMachineJson())
            .WithAction("reportEmpty", (ctx, _) => { })
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("reportOccupied", (ctx, _) => { })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        for (int i = 1; i <= 5; i++)
        {
            // Place wafer
            SendEventAndWait(machine, "PLACE",
                s => s.CurrentState == "occupied",
                $"occupied with wafer {i}",
                new Dictionary<string, object> { ["wafer"] = i });

            // Pick wafer
            SendEventAndWait(machine, "PICK",
                s => s.CurrentState == "empty",
                $"empty after picking wafer {i}");
        }

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("empty");
    }

    #endregion

    #region Invalid Operations

    [Fact]
    public void Buffer_PlaceWhenOccupied_ShouldIgnoreEvent()
    {
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetBufferMachineJson())
            .WithAction("reportEmpty", (ctx, _) => { })
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("reportOccupied", (ctx, _) => { })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        // Place first wafer
        SendEventAndWait(machine, "PLACE",
            s => s.CurrentState == "occupied",
            "occupied",
            new Dictionary<string, object> { ["wafer"] = 301 });

        // Try to place second wafer (should be ignored)
        machine.Tell(new SendEvent("PLACE", new Dictionary<string, object> { ["wafer"] = 302 }));

        // Wait a bit and verify state hasn't changed
        WaitForState(machine, s => s.CurrentState == "occupied", "still occupied");

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("occupied");
    }

    [Fact]
    public void Buffer_PickWhenEmpty_ShouldIgnoreEvent()
    {
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetBufferMachineJson())
            .WithAction("reportEmpty", (ctx, _) => { })
            .WithAction("storeWafer", (ctx, evt) => { })
            .WithAction("reportOccupied", (ctx, _) => { })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        WaitForStateName(machine, "empty");

        // Try to pick from empty buffer
        machine.Tell(new SendEvent("PICK"));

        // Wait and verify still empty
        WaitForState(machine, s => s.CurrentState == "empty", "still empty");

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("empty");
    }

    #endregion

    #region Action Callbacks

    [Fact]
    public void Buffer_ShouldCallReportEmptyOnEntry()
    {
        var reportEmptyCalled = false;
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetBufferMachineJson())
            .WithAction("reportEmpty", (ctx, _) => reportEmptyCalled = true)
            .WithAction("storeWafer", (ctx, evt) => { })
            .WithAction("reportOccupied", (ctx, _) => { })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        WaitForStateName(machine, "empty");

        reportEmptyCalled.Should().BeTrue();
    }

    [Fact]
    public void Buffer_ShouldCallReportOccupiedOnEntry()
    {
        var reportOccupiedCalled = false;
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetBufferMachineJson())
            .WithAction("reportEmpty", (ctx, _) => { })
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("reportOccupied", (ctx, _) => reportOccupiedCalled = true)
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        SendEventAndWait(machine, "PLACE",
            s => s.CurrentState == "occupied",
            "occupied",
            new Dictionary<string, object> { ["wafer"] = 401 });

        reportOccupiedCalled.Should().BeTrue();
    }

    #endregion
}
