using Akka.Actor;
using FluentAssertions;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Extensions;
using XStateNet2.Tests;

namespace XStateNet2.Tests.CMPSimulator;

/// <summary>
/// Comprehensive tests for Polisher Station using XStateNet2
/// Polisher: Processes wafers with polishing operation
/// States: Idle → Processing → Done → Idle
/// </summary>
public class PolisherMachineTests : XStateTestKit
{
    #region State Machine Definition

    private string GetPolisherMachineJson() => """
    {
        "id": "polisher",
        "initial": "idle",
        "context": {
            "wafer": null,
            "processTime": 3000,
            "processingStartTime": null
        },
        "states": {
            "idle": {
                "entry": ["reportIdle"],
                "on": {
                    "PLACE": {
                        "target": "processing",
                        "actions": ["storeWafer", "startProcessing"]
                    }
                }
            },
            "processing": {
                "entry": ["reportProcessing"],
                "on": {
                    "PROCESS_COMPLETE": {
                        "target": "done",
                        "actions": ["reportProcessingDone"]
                    }
                }
            },
            "done": {
                "entry": ["reportDone"],
                "on": {
                    "PICK": {
                        "target": "idle",
                        "actions": ["clearWafer"]
                    }
                }
            }
        }
    }
    """;

    #endregion

    #region Basic State Transitions

    [Fact]
    public void Polisher_InitialState_ShouldBeIdle()
    {
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetPolisherMachineJson()).BuildAndStart();

        WaitForStateName(machine, "idle");

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("idle");
        snapshot.Context["wafer"].Should().BeNull();
    }

    [Fact]
    public void Polisher_PlaceWafer_ShouldStartProcessing()
    {
        var processingStarted = false;
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetPolisherMachineJson())
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("startProcessing", (ctx, _) =>
            {
                ctx.Set("processingStartTime", DateTime.UtcNow.ToString("O"));
                processingStarted = true;
            })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("reportProcessingDone", (ctx, _) => { })
            .WithAction("reportDone", (ctx, _) => { })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        SendEventAndWait(machine, "PLACE",
            s => s.CurrentState == "processing",
            "processing",
            new Dictionary<string, object> { ["wafer"] = 501 });

        processingStarted.Should().BeTrue();

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("processing");
    }

    [Fact]
    public void Polisher_ProcessComplete_ShouldTransitionToDone()
    {
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetPolisherMachineJson())
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("startProcessing", (ctx, _) =>
            {
                ctx.Set("processingStartTime", DateTime.UtcNow.ToString("O"));
            })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("reportProcessingDone", (ctx, _) => { })
            .WithAction("reportDone", (ctx, _) => { })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        // Place wafer
        SendEventAndWait(machine, "PLACE",
            s => s.CurrentState == "processing",
            "processing",
            new Dictionary<string, object> { ["wafer"] = 502 });

        // Complete processing
        SendEventAndWait(machine, "PROCESS_COMPLETE",
            s => s.CurrentState == "done",
            "done");

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("done");
    }

    [Fact]
    public void Polisher_PickFromDone_ShouldReturnToIdle()
    {
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetPolisherMachineJson())
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("startProcessing", (ctx, _) => { })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("reportProcessingDone", (ctx, _) => { })
            .WithAction("reportDone", (ctx, _) => { })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        // Complete cycle
        SendEventAndWait(machine, "PLACE",
            s => s.CurrentState == "processing",
            "processing",
            new Dictionary<string, object> { ["wafer"] = 503 });

        SendEventAndWait(machine, "PROCESS_COMPLETE",
            s => s.CurrentState == "done",
            "done");

        SendEventAndWait(machine, "PICK",
            s => s.CurrentState == "idle",
            "idle");

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("idle");
        snapshot.Context["wafer"].Should().BeNull();
    }

    #endregion

    #region Full Processing Cycle

    [Fact]
    public void Polisher_FullCycle_ShouldWork()
    {
        var actionSequence = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetPolisherMachineJson())
            .WithAction("reportIdle", (ctx, _) => actionSequence.Add("idle"))
            .WithAction("storeWafer", (ctx, evt) =>
            {
                actionSequence.Add("store");
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("startProcessing", (ctx, _) => actionSequence.Add("start"))
            .WithAction("reportProcessing", (ctx, _) => actionSequence.Add("processing"))
            .WithAction("reportProcessingDone", (ctx, _) => actionSequence.Add("processingDone"))
            .WithAction("reportDone", (ctx, _) => actionSequence.Add("done"))
            .WithAction("clearWafer", (ctx, _) =>
            {
                actionSequence.Add("clear");
                ctx.Set("wafer", null);
            })
            .BuildAndStart();

        WaitForStateName(machine, "idle");

        SendEventAndWait(machine, "PLACE",
            s => s.CurrentState == "processing",
            "processing",
            new Dictionary<string, object> { ["wafer"] = 504 });

        SendEventAndWait(machine, "PROCESS_COMPLETE",
            s => s.CurrentState == "done",
            "done");

        SendEventAndWait(machine, "PICK",
            s => s.CurrentState == "idle",
            "idle");

        actionSequence.Should().Contain("idle");
        actionSequence.Should().Contain("store");
        actionSequence.Should().Contain("start");
        actionSequence.Should().Contain("processing");
        actionSequence.Should().Contain("processingDone");
        actionSequence.Should().Contain("done");
        actionSequence.Should().Contain("clear");
    }

    [Fact]
    public void Polisher_MultipleWafers_ShouldProcessSequentially()
    {
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetPolisherMachineJson())
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("startProcessing", (ctx, _) => { })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("reportProcessingDone", (ctx, _) => { })
            .WithAction("reportDone", (ctx, _) => { })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        for (int i = 1; i <= 3; i++)
        {
            SendEventAndWait(machine, "PLACE",
                s => s.CurrentState == "processing",
                $"processing wafer {i}",
                new Dictionary<string, object> { ["wafer"] = 600 + i });

            SendEventAndWait(machine, "PROCESS_COMPLETE",
                s => s.CurrentState == "done",
                "done");

            SendEventAndWait(machine, "PICK",
                s => s.CurrentState == "idle",
                "idle");
        }

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("idle");
    }

    #endregion

    #region Invalid Operations

    [Fact]
    public void Polisher_PlaceWhenProcessing_ShouldIgnoreEvent()
    {
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetPolisherMachineJson())
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("startProcessing", (ctx, _) => { })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("reportProcessingDone", (ctx, _) => { })
            .WithAction("reportDone", (ctx, _) => { })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        // Place first wafer
        SendEventAndWait(machine, "PLACE",
            s => s.CurrentState == "processing",
            "processing",
            new Dictionary<string, object> { ["wafer"] = 701 });

        // Try to place second wafer (should be ignored)
        machine.Tell(new SendEvent("PLACE", new Dictionary<string, object> { ["wafer"] = 702 }));

        WaitForState(machine, s => s.CurrentState == "processing", "still processing");

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("processing");
    }

    [Fact]
    public void Polisher_PickWhenIdle_ShouldIgnoreEvent()
    {
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetPolisherMachineJson())
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("storeWafer", (ctx, evt) => { })
            .WithAction("startProcessing", (ctx, _) => { })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("reportProcessingDone", (ctx, _) => { })
            .WithAction("reportDone", (ctx, _) => { })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        WaitForStateName(machine, "idle");

        // Try to pick when idle
        machine.Tell(new SendEvent("PICK"));

        WaitForState(machine, s => s.CurrentState == "idle", "still idle");

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("idle");
    }

    #endregion

    #region Processing Time Tracking

    [Fact]
    public void Polisher_ShouldTrackProcessingStartTime()
    {
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetPolisherMachineJson())
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("startProcessing", (ctx, _) =>
            {
                ctx.Set("processingStartTime", DateTime.UtcNow.ToString("O"));
            })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("reportProcessingDone", (ctx, _) => { })
            .WithAction("reportDone", (ctx, _) => { })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        SendEventAndWait(machine, "PLACE",
            s => s.CurrentState == "processing",
            "processing",
            new Dictionary<string, object> { ["wafer"] = 801 });

        var snapshot = machine.GetStateSnapshot();
        snapshot.Context["processingStartTime"].Should().NotBeNull();

        var startTime = snapshot.Context["processingStartTime"]?.ToString();
        startTime.Should().NotBeNullOrEmpty();
    }

    #endregion
}
