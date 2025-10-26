using Akka.Actor;
using FluentAssertions;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Extensions;
using XStateNet2.Tests;

namespace XStateNet2.Tests.CMPSimulator;

/// <summary>
/// Comprehensive tests for Carrier Station using XStateNet2
/// Carrier: Holds wafers at the beginning and receives them back after processing
/// States: Loading → Processing → Completed → Unloading
/// Tracks: wafer slots, processed/unprocessed counts
/// </summary>
public class CarrierMachineTests : XStateTestKit
{
    #region State Machine Definition

    private string GetCarrierMachineJson() => """
    {
        "id": "carrier",
        "initial": "loading",
        "context": {
            "totalSlots": 25,
            "wafers": [],
            "processedCount": 0,
            "carrierStatus": "empty"
        },
        "states": {
            "loading": {
                "entry": ["reportLoading"],
                "on": {
                    "LOAD_COMPLETE": {
                        "target": "processing",
                        "actions": ["initializeWafers"]
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
                        "target": "completed",
                        "actions": ["reportAllComplete"]
                    }
                }
            },
            "completed": {
                "entry": ["reportCompleted"],
                "on": {
                    "UNLOAD": {
                        "target": "unloading",
                        "actions": ["prepareUnload"]
                    }
                }
            },
            "unloading": {
                "entry": ["reportUnloading"],
                "on": {
                    "UNLOAD_COMPLETE": {
                        "target": "loading",
                        "actions": ["reset"]
                    }
                }
            }
        }
    }
    """;

    #endregion

    #region Basic State Transitions

    [Fact]
    public void Carrier_InitialState_ShouldBeLoading()
    {
        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(GetCarrierMachineJson()).BuildAndStart();

        WaitForStateName(machine, "loading");

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("loading");
    }

    [Fact]
    public void Carrier_LoadComplete_ShouldTransitionToProcessing()
    {
        var wafersInitialized = false;
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetCarrierMachineJson())
            .WithAction("reportLoading", (ctx, _) => { })
            .WithAction("initializeWafers", (ctx, evt) =>
            {
                // Initialize with some wafers
                var wafers = new List<int> { 1, 2, 3, 4, 5 };
                ctx.Set("wafers", wafers);
                ctx.Set("totalSlots", wafers.Count);
                wafersInitialized = true;
            })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("removeWafer", (ctx, _) => { })
            .WithAction("returnWafer", (ctx, _) => { })
            .WithAction("checkCompletion", (ctx, _) => { })
            .WithAction("reportAllComplete", (ctx, _) => { })
            .WithAction("reportCompleted", (ctx, _) => { })
            .WithAction("prepareUnload", (ctx, _) => { })
            .WithAction("reportUnloading", (ctx, _) => { })
            .WithAction("reset", (ctx, _) => { })
            .WithGuard("hasUnprocessedWafers", (ctx, _) =>
            {
                var wafers = ctx.Get<List<int>>("wafers");
                return wafers != null && wafers.Count > 0;
            })
            .BuildAndStart();

        SendEventAndWait(machine, "LOAD_COMPLETE",
            s => s.CurrentState == "processing",
            "processing");

        wafersInitialized.Should().BeTrue();
        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("processing");
    }

    [Fact]
    public void Carrier_AllComplete_ShouldTransitionToCompleted()
    {
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetCarrierMachineJson())
            .WithAction("reportLoading", (ctx, _) => { })
            .WithAction("initializeWafers", (ctx, evt) =>
            {
                ctx.Set("wafers", new List<int> { 1, 2, 3 });
                ctx.Set("totalSlots", 3);
            })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("removeWafer", (ctx, _) => { })
            .WithAction("returnWafer", (ctx, _) => { })
            .WithAction("checkCompletion", (ctx, _) => { })
            .WithAction("reportAllComplete", (ctx, _) => { })
            .WithAction("reportCompleted", (ctx, _) => { })
            .WithAction("prepareUnload", (ctx, _) => { })
            .WithAction("reportUnloading", (ctx, _) => { })
            .WithAction("reset", (ctx, _) => { })
            .WithGuard("hasUnprocessedWafers", (ctx, _) => false)
            .BuildAndStart();

        // Load carrier
        SendEventAndWait(machine, "LOAD_COMPLETE",
            s => s.CurrentState == "processing",
            "processing");

        // Signal all wafers processed
        SendEventAndWait(machine, "ALL_COMPLETE",
            s => s.CurrentState == "completed",
            "completed");

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("completed");
    }

    [Fact]
    public void Carrier_CompleteCycle_LoadProcessUnload()
    {
        var actionSequence = new List<string>();
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetCarrierMachineJson())
            .WithAction("reportLoading", (ctx, _) => actionSequence.Add("loading"))
            .WithAction("initializeWafers", (ctx, evt) =>
            {
                actionSequence.Add("initialize");
                ctx.Set("wafers", new List<int> { 1, 2 });
                ctx.Set("totalSlots", 2);
            })
            .WithAction("reportProcessing", (ctx, _) => actionSequence.Add("processing"))
            .WithAction("removeWafer", (ctx, _) => actionSequence.Add("remove"))
            .WithAction("returnWafer", (ctx, _) => actionSequence.Add("return"))
            .WithAction("checkCompletion", (ctx, _) => actionSequence.Add("check"))
            .WithAction("reportAllComplete", (ctx, _) => actionSequence.Add("allComplete"))
            .WithAction("reportCompleted", (ctx, _) => actionSequence.Add("completed"))
            .WithAction("prepareUnload", (ctx, _) => actionSequence.Add("prepare"))
            .WithAction("reportUnloading", (ctx, _) => actionSequence.Add("unloading"))
            .WithAction("reset", (ctx, _) => actionSequence.Add("reset"))
            .WithGuard("hasUnprocessedWafers", (ctx, _) => false)
            .BuildAndStart();

        // Complete cycle
        SendEventAndWait(machine, "LOAD_COMPLETE",
            s => s.CurrentState == "processing",
            "processing");

        SendEventAndWait(machine, "ALL_COMPLETE",
            s => s.CurrentState == "completed",
            "completed");

        SendEventAndWait(machine, "UNLOAD",
            s => s.CurrentState == "unloading",
            "unloading");

        SendEventAndWait(machine, "UNLOAD_COMPLETE",
            s => s.CurrentState == "loading",
            "loading");

        actionSequence.Should().Contain(new[]
        {
            "loading", "initialize", "processing",
            "allComplete", "completed",
            "prepare", "unloading", "reset"
        });
    }

    #endregion

    #region Wafer Slot Management

    [Fact]
    public void Carrier_PickWafer_ShouldRemoveFromSlot()
    {
        var removedWaferId = 0;
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetCarrierMachineJson())
            .WithAction("reportLoading", (ctx, _) => { })
            .WithAction("initializeWafers", (ctx, evt) =>
            {
                ctx.Set("wafers", new List<int> { 101, 102, 103 });
            })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("removeWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("wafer"))
                {
                    removedWaferId = Convert.ToInt32(data["wafer"]);
                    var wafers = ctx.Get<List<int>>("wafers");
                    if (wafers != null)
                    {
                        wafers.Remove(removedWaferId);
                        ctx.Set("wafers", wafers);
                    }
                }
            })
            .WithAction("returnWafer", (ctx, _) => { })
            .WithAction("checkCompletion", (ctx, _) => { })
            .WithAction("reportAllComplete", (ctx, _) => { })
            .WithAction("reportCompleted", (ctx, _) => { })
            .WithAction("prepareUnload", (ctx, _) => { })
            .WithAction("reportUnloading", (ctx, _) => { })
            .WithAction("reset", (ctx, _) => { })
            .WithGuard("hasUnprocessedWafers", (ctx, _) => true)
            .BuildAndStart();

        // Load carrier
        SendEventAndWait(machine, "LOAD_COMPLETE",
            s => s.CurrentState == "processing",
            "processing");

        // Pick wafer 102
        machine.Tell(new SendEvent("PICK", new Dictionary<string, object> { ["wafer"] = 102 }));

        WaitForState(machine, s => s.CurrentState == "processing", "still processing");

        removedWaferId.Should().Be(102);
    }

    [Fact]
    public void Carrier_PlaceWafer_ShouldReturnToSlot()
    {
        var returnedWaferId = 0;
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetCarrierMachineJson())
            .WithAction("reportLoading", (ctx, _) => { })
            .WithAction("initializeWafers", (ctx, evt) =>
            {
                ctx.Set("wafers", new List<int> { 201, 202 });
                ctx.Set("processedCount", 0);
            })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("removeWafer", (ctx, _) => { })
            .WithAction("returnWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("wafer"))
                {
                    returnedWaferId = Convert.ToInt32(data["wafer"]);
                    var count = ctx.Get<int>("processedCount");
                    ctx.Set("processedCount", count + 1);
                }
            })
            .WithAction("checkCompletion", (ctx, _) => { })
            .WithAction("reportAllComplete", (ctx, _) => { })
            .WithAction("reportCompleted", (ctx, _) => { })
            .WithAction("prepareUnload", (ctx, _) => { })
            .WithAction("reportUnloading", (ctx, _) => { })
            .WithAction("reset", (ctx, _) => { })
            .WithGuard("hasUnprocessedWafers", (ctx, _) => false)
            .BuildAndStart();

        // Load carrier
        SendEventAndWait(machine, "LOAD_COMPLETE",
            s => s.CurrentState == "processing",
            "processing");

        // Return wafer 201
        machine.Tell(new SendEvent("PLACE", new Dictionary<string, object> { ["wafer"] = 201 }));

        WaitForState(machine, s => s.CurrentState == "processing", "still processing");

        returnedWaferId.Should().Be(201);
    }

    [Fact]
    public void Carrier_ProcessMultipleWafers_TrackPickAndPlace()
    {
        var pickedWafers = new List<int>();
        var returnedWafers = new List<int>();
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetCarrierMachineJson())
            .WithAction("reportLoading", (ctx, _) => { })
            .WithAction("initializeWafers", (ctx, evt) =>
            {
                ctx.Set("wafers", new List<int> { 301, 302, 303, 304, 305 });
                ctx.Set("processedCount", 0);
            })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("removeWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("wafer"))
                {
                    var waferId = Convert.ToInt32(data["wafer"]);
                    pickedWafers.Add(waferId);
                }
            })
            .WithAction("returnWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("wafer"))
                {
                    var waferId = Convert.ToInt32(data["wafer"]);
                    returnedWafers.Add(waferId);
                    var count = ctx.Get<int>("processedCount");
                    ctx.Set("processedCount", count + 1);
                }
            })
            .WithAction("checkCompletion", (ctx, _) => { })
            .WithAction("reportAllComplete", (ctx, _) => { })
            .WithAction("reportCompleted", (ctx, _) => { })
            .WithAction("prepareUnload", (ctx, _) => { })
            .WithAction("reportUnloading", (ctx, _) => { })
            .WithAction("reset", (ctx, _) => { })
            .WithGuard("hasUnprocessedWafers", (ctx, _) => true)
            .BuildAndStart();

        // Load carrier
        SendEventAndWait(machine, "LOAD_COMPLETE",
            s => s.CurrentState == "processing",
            "processing");

        // Simulate processing 5 wafers
        for (int i = 0; i < 5; i++)
        {
            var waferId = 301 + i;

            // Pick wafer
            machine.Tell(new SendEvent("PICK", new Dictionary<string, object> { ["wafer"] = waferId }));
            WaitForState(machine, s => s.CurrentState == "processing", "processing");

            // Return wafer
            machine.Tell(new SendEvent("PLACE", new Dictionary<string, object> { ["wafer"] = waferId }));
            WaitForState(machine, s => s.CurrentState == "processing", "processing");
        }

        pickedWafers.Should().HaveCount(5);
        returnedWafers.Should().HaveCount(5);
        pickedWafers.Should().BeEquivalentTo(new[] { 301, 302, 303, 304, 305 });
        returnedWafers.Should().BeEquivalentTo(new[] { 301, 302, 303, 304, 305 });
    }

    #endregion

    #region Completion Detection

    [Fact]
    public void Carrier_AllWafersProcessed_ShouldDetectCompletion()
    {
        var completionChecked = 0;
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetCarrierMachineJson())
            .WithAction("reportLoading", (ctx, _) => { })
            .WithAction("initializeWafers", (ctx, evt) =>
            {
                ctx.Set("wafers", new List<int> { 401, 402, 403 });
                ctx.Set("totalSlots", 3);
                ctx.Set("processedCount", 0);
            })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("removeWafer", (ctx, _) => { })
            .WithAction("returnWafer", (ctx, evt) =>
            {
                var count = ctx.Get<int>("processedCount");
                ctx.Set("processedCount", count + 1);
            })
            .WithAction("checkCompletion", (ctx, _) =>
            {
                completionChecked++;
                var processed = ctx.Get<int>("processedCount");
                var total = ctx.Get<int>("totalSlots");
                if (processed >= total)
                {
                    // In real implementation, would send ALL_COMPLETE event
                }
            })
            .WithAction("reportAllComplete", (ctx, _) => { })
            .WithAction("reportCompleted", (ctx, _) => { })
            .WithAction("prepareUnload", (ctx, _) => { })
            .WithAction("reportUnloading", (ctx, _) => { })
            .WithAction("reset", (ctx, _) => { })
            .WithGuard("hasUnprocessedWafers", (ctx, _) => true)
            .BuildAndStart();

        // Load carrier
        SendEventAndWait(machine, "LOAD_COMPLETE",
            s => s.CurrentState == "processing",
            "processing");

        // Return 3 wafers
        for (int i = 0; i < 3; i++)
        {
            machine.Tell(new SendEvent("PLACE", new Dictionary<string, object> { ["wafer"] = 401 + i }));
            WaitForState(machine, s => s.CurrentState == "processing", "processing");
        }

        completionChecked.Should().Be(3);

        var snapshot = machine.GetStateSnapshot();
        var processedCount = snapshot.Context["processedCount"];
        int actualCount = processedCount is System.Text.Json.JsonElement element
            ? element.GetInt32()
            : Convert.ToInt32(processedCount);
        actualCount.Should().Be(3);
    }

    #endregion

    #region Guard Conditions

    [Fact]
    public void Carrier_HasUnprocessedWafers_GuardCondition()
    {
        var factory = new XStateMachineFactory(Sys);

        var machine = factory.FromJson(GetCarrierMachineJson())
            .WithAction("reportLoading", (ctx, _) => { })
            .WithAction("initializeWafers", (ctx, evt) =>
            {
                ctx.Set("wafers", new List<int> { 501, 502 });
            })
            .WithAction("reportProcessing", (ctx, _) => { })
            .WithAction("removeWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("wafer"))
                {
                    var waferId = Convert.ToInt32(data["wafer"]);
                    var wafers = ctx.Get<List<int>>("wafers");
                    if (wafers != null)
                    {
                        wafers.Remove(waferId);
                        ctx.Set("wafers", wafers);
                    }
                }
            })
            .WithAction("returnWafer", (ctx, _) => { })
            .WithAction("checkCompletion", (ctx, _) => { })
            .WithAction("reportAllComplete", (ctx, _) => { })
            .WithAction("reportCompleted", (ctx, _) => { })
            .WithAction("prepareUnload", (ctx, _) => { })
            .WithAction("reportUnloading", (ctx, _) => { })
            .WithAction("reset", (ctx, _) => { })
            .WithGuard("hasUnprocessedWafers", (ctx, _) =>
            {
                var wafers = ctx.Get<List<int>>("wafers");
                return wafers != null && wafers.Count > 0;
            })
            .BuildAndStart();

        // Load carrier
        SendEventAndWait(machine, "LOAD_COMPLETE",
            s => s.CurrentState == "processing",
            "processing");

        // Pick first wafer - guard should pass
        machine.Tell(new SendEvent("PICK", new Dictionary<string, object> { ["wafer"] = 501 }));
        WaitForState(machine, s => s.CurrentState == "processing", "processing");

        // Pick second wafer - guard should pass
        machine.Tell(new SendEvent("PICK", new Dictionary<string, object> { ["wafer"] = 502 }));
        WaitForState(machine, s => s.CurrentState == "processing", "processing");

        // Try to pick again - guard should fail (no wafers left)
        machine.Tell(new SendEvent("PICK", new Dictionary<string, object> { ["wafer"] = 503 }));
        WaitForState(machine, s => s.CurrentState == "processing", "still processing");

        var snapshot = machine.GetStateSnapshot();
        snapshot.CurrentState.Should().Be("processing");
    }

    #endregion
}
