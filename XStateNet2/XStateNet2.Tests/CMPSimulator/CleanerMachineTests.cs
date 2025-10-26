using Akka.Actor;
using FluentAssertions;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Extensions;
using XStateNet2.Tests;

namespace XStateNet2.Tests.CMPSimulator;

/// <summary>
/// Comprehensive tests for Cleaner Station using XStateNet2
/// Cleaner: Cleans wafers after polishing
/// States: Idle → Cleaning → Done → Idle
/// </summary>
public class CleanerMachineTests : XStateTestKit
{
    #region State Machine Definition

    private string GetCleanerMachineJson() => """
    {
        "id": "cleaner",
        "initial": "idle",
        "context": {
            "wafer": null,
            "cleanTime": 2000
        },
        "states": {
            "idle": {
                "entry": ["reportIdle"],
                "on": {
                    "PLACE": {
                        "target": "cleaning",
                        "actions": ["storeWafer", "startCleaning"]
                    }
                }
            },
            "cleaning": {
                "entry": ["reportCleaning"],
                "on": {
                    "CLEAN_COMPLETE": {
                        "target": "done",
                        "actions": ["reportCleaningDone"]
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
    public void Cleaner_InitialState_ShouldBeIdle()
    {
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetCleanerMachineJson()).BuildAndStart();

        WaitForStateName(machine, "idle");

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("idle");
        snapshot.Context["wafer"].Should().BeNull();
    }

    [Fact]
    public void Cleaner_PlaceWafer_ShouldStartCleaning()
    {
        var cleaningStarted = false;
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetCleanerMachineJson())
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("startCleaning", (ctx, _) => cleaningStarted = true)
            .WithAction("reportCleaning", (ctx, _) => { })
            .WithAction("reportCleaningDone", (ctx, _) => { })
            .WithAction("reportDone", (ctx, _) => { })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        SendEventAndWait(machine, "PLACE",
            s => s.CurrentState == "cleaning",
            "cleaning",
            new Dictionary<string, object> { ["wafer"] = 901 });

        cleaningStarted.Should().BeTrue();

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("cleaning");
    }

    [Fact]
    public void Cleaner_CleanComplete_ShouldTransitionToDone()
    {
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetCleanerMachineJson())
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("startCleaning", (ctx, _) => { })
            .WithAction("reportCleaning", (ctx, _) => { })
            .WithAction("reportCleaningDone", (ctx, _) => { })
            .WithAction("reportDone", (ctx, _) => { })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        // Place wafer
        SendEventAndWait(machine, "PLACE",
            s => s.CurrentState == "cleaning",
            "cleaning",
            new Dictionary<string, object> { ["wafer"] = 902 });

        // Complete cleaning
        SendEventAndWait(machine, "CLEAN_COMPLETE",
            s => s.CurrentState == "done",
            "done");

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("done");
    }

    [Fact]
    public void Cleaner_FullCycle_ShouldWork()
    {
        var actionSequence = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetCleanerMachineJson())
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
            .WithAction("startCleaning", (ctx, _) => actionSequence.Add("start"))
            .WithAction("reportCleaning", (ctx, _) => actionSequence.Add("cleaning"))
            .WithAction("reportCleaningDone", (ctx, _) => actionSequence.Add("cleaningDone"))
            .WithAction("reportDone", (ctx, _) => actionSequence.Add("done"))
            .WithAction("clearWafer", (ctx, _) =>
            {
                actionSequence.Add("clear");
                ctx.Set("wafer", null);
            })
            .BuildAndStart();

        WaitForStateName(machine, "idle");

        SendEventAndWait(machine, "PLACE",
            s => s.CurrentState == "cleaning",
            "cleaning",
            new Dictionary<string, object> { ["wafer"] = 903 });

        SendEventAndWait(machine, "CLEAN_COMPLETE",
            s => s.CurrentState == "done",
            "done");

        SendEventAndWait(machine, "PICK",
            s => s.CurrentState == "idle",
            "idle");

        actionSequence.Should().Contain(new[] { "idle", "store", "start", "cleaning", "cleaningDone", "done", "clear" });
    }

    #endregion

    #region Multiple Wafers

    [Fact]
    public void Cleaner_MultipleWafers_ShouldCleanSequentially()
    {
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetCleanerMachineJson())
            .WithAction("reportIdle", (ctx, _) => { })
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("startCleaning", (ctx, _) => { })
            .WithAction("reportCleaning", (ctx, _) => { })
            .WithAction("reportCleaningDone", (ctx, _) => { })
            .WithAction("reportDone", (ctx, _) => { })
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();

        for (int i = 1; i <= 4; i++)
        {
            SendEventAndWait(machine, "PLACE",
                s => s.CurrentState == "cleaning",
                $"cleaning wafer {i}",
                new Dictionary<string, object> { ["wafer"] = 1000 + i });

            SendEventAndWait(machine, "CLEAN_COMPLETE",
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
}
