using Akka.Actor;
using FluentAssertions;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Extensions;
using XStateNet2.Tests;

namespace XStateNet2.Tests.CMPSimulator;

/// <summary>
/// Integration tests for complete wafer flow through CMP system
/// Tests the entire journey: Carrier → (R1) → Polisher → (R2) → Cleaner → (R3) → Buffer → (R1) → Carrier
/// </summary>
public class WaferFlowIntegrationTests : XStateTestKit
{
    #region Complete Wafer Journey

    [Fact]
    public void WaferFlow_SingleWafer_CompleteCycle()
    {
        // Track wafer journey
        var journey = new List<string>();
        var waferId = 2001;

        var factory = new XStateMachineFactory(Sys);

        // Create all station machines
        var polisher = CreatePolisher(factory, journey);
        var cleaner = CreateCleaner(factory, journey);
        var buffer = CreateBuffer(factory, journey);

        journey.Add("START");

        // R1: Carrier → Polisher
        journey.Add("R1_PICKUP_FROM_CARRIER");
        SendEventAndWait(polisher, "PLACE",
            s => s.CurrentState == "processing",
            "polisher processing",
            new Dictionary<string, object> { ["wafer"] = waferId });

        // Polisher processes
        journey.Add("POLISHING");
        SendEventAndWait(polisher, "PROCESS_COMPLETE",
            s => s.CurrentState == "done",
            "polisher done");

        // R2: Polisher → Cleaner
        journey.Add("R2_TRANSFER_TO_CLEANER");
        SendEventAndWait(polisher, "PICK",
            s => s.CurrentState == "idle",
            "polisher idle");

        SendEventAndWait(cleaner, "PLACE",
            s => s.CurrentState == "cleaning",
            "cleaner cleaning",
            new Dictionary<string, object> { ["wafer"] = waferId });

        // Cleaner processes
        journey.Add("CLEANING");
        SendEventAndWait(cleaner, "CLEAN_COMPLETE",
            s => s.CurrentState == "done",
            "cleaner done");

        // R3: Cleaner → Buffer
        journey.Add("R3_TRANSFER_TO_BUFFER");
        SendEventAndWait(cleaner, "PICK",
            s => s.CurrentState == "idle",
            "cleaner idle");

        SendEventAndWait(buffer, "PLACE",
            s => s.CurrentState == "occupied",
            "buffer occupied",
            new Dictionary<string, object> { ["wafer"] = waferId });

        // R1: Buffer → Carrier
        journey.Add("R1_RETURN_TO_CARRIER");
        SendEventAndWait(buffer, "PICK",
            s => s.CurrentState == "empty",
            "buffer empty");

        journey.Add("END");

        // Verify complete journey
        journey.Should().ContainInOrder(
            "START",
            "R1_PICKUP_FROM_CARRIER",
            "POLISHER_PLACE",
            "POLISHING",
            "POLISHER_DONE",
            "R2_TRANSFER_TO_CLEANER",
            "POLISHER_PICK",
            "CLEANER_PLACE",
            "CLEANING",
            "CLEANER_DONE",
            "R3_TRANSFER_TO_BUFFER",
            "CLEANER_PICK",
            "BUFFER_PLACE",
            "R1_RETURN_TO_CARRIER",
            "BUFFER_PICK",
            "END"
        );
    }

    [Fact]
    public void WaferFlow_TwoWafers_Sequential()
    {
        var factory = new XStateMachineFactory(Sys);

        var polisher = CreatePolisher(factory, new List<string>());
        var cleaner = CreateCleaner(factory, new List<string>());
        var buffer = CreateBuffer(factory, new List<string>());

        // Process first wafer
        ProcessSingleWafer(polisher, cleaner, buffer, 3001);

        // Process second wafer
        ProcessSingleWafer(polisher, cleaner, buffer, 3002);

        // All stations should be idle
        var polisherState = polisher.GetStateSnapshot();
        var cleanerState = cleaner.GetStateSnapshot();
        var bufferState = buffer.GetStateSnapshot();

        polisherState.CurrentState.Should().Be("idle");
        cleanerState.CurrentState.Should().Be("idle");
        bufferState.CurrentState.Should().Be("empty");
    }

    #endregion

    #region Parallel Processing

    [Fact]
    public void WaferFlow_ParallelProcessing_TwoWafersInFlight()
    {
        var factory = new XStateMachineFactory(Sys);

        var polisher = CreatePolisher(factory, new List<string>());
        var cleaner = CreateCleaner(factory, new List<string>());

        // Start wafer 1 on polisher
        SendEventAndWait(polisher, "PLACE",
            s => s.CurrentState == "processing",
            "polisher processing wafer 1",
            new Dictionary<string, object> { ["wafer"] = 4001 });

        // Complete wafer 1 polishing and move to cleaner
        SendEventAndWait(polisher, "PROCESS_COMPLETE",
            s => s.CurrentState == "done",
            "polisher done");

        SendEventAndWait(polisher, "PICK",
            s => s.CurrentState == "idle",
            "polisher idle");

        SendEventAndWait(cleaner, "PLACE",
            s => s.CurrentState == "cleaning",
            "cleaner cleaning wafer 1",
            new Dictionary<string, object> { ["wafer"] = 4001 });

        // Now wafer 1 is cleaning, start wafer 2 on polisher (parallel!)
        SendEventAndWait(polisher, "PLACE",
            s => s.CurrentState == "processing",
            "polisher processing wafer 2",
            new Dictionary<string, object> { ["wafer"] = 4002 });

        // Both should be processing in parallel
        var polisherState = polisher.GetStateSnapshot();
        var cleanerState = cleaner.GetStateSnapshot();

        polisherState.CurrentState.Should().Be("processing");
        cleanerState.CurrentState.Should().Be("cleaning");
    }

    #endregion

    #region Helper Methods

    private IActorRef CreatePolisher(XStateMachineFactory factory, List<string> journey)
    {
        var json = """
        {
            "id": "polisher",
            "initial": "idle",
            "context": { "wafer": null },
            "states": {
                "idle": {
                    "on": {
                        "PLACE": {
                            "target": "processing",
                            "actions": ["storeWafer"]
                        }
                    }
                },
                "processing": {
                    "entry": ["onProcessing"],
                    "on": {
                        "PROCESS_COMPLETE": {
                            "target": "done",
                            "actions": ["onDone"]
                        }
                    }
                },
                "done": {
                    "on": {
                        "PICK": {
                            "target": "idle",
                            "actions": ["onPick", "clearWafer"]
                        }
                    }
                }
            }
        }
        """;

        return factory.FromJson(json)
            .WithAction("storeWafer", (ctx, evt) =>
            {
                journey.Add("POLISHER_PLACE");
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("onProcessing", (ctx, _) => { })
            .WithAction("onDone", (ctx, _) => journey.Add("POLISHER_DONE"))
            .WithAction("onPick", (ctx, _) => journey.Add("POLISHER_PICK"))
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();
    }

    private IActorRef CreateCleaner(XStateMachineFactory factory, List<string> journey)
    {
        var json = """
        {
            "id": "cleaner",
            "initial": "idle",
            "context": { "wafer": null },
            "states": {
                "idle": {
                    "on": {
                        "PLACE": {
                            "target": "cleaning",
                            "actions": ["storeWafer"]
                        }
                    }
                },
                "cleaning": {
                    "entry": ["onCleaning"],
                    "on": {
                        "CLEAN_COMPLETE": {
                            "target": "done",
                            "actions": ["onDone"]
                        }
                    }
                },
                "done": {
                    "on": {
                        "PICK": {
                            "target": "idle",
                            "actions": ["onPick", "clearWafer"]
                        }
                    }
                }
            }
        }
        """;

        return factory.FromJson(json)
            .WithAction("storeWafer", (ctx, evt) =>
            {
                journey.Add("CLEANER_PLACE");
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("onCleaning", (ctx, _) => { })
            .WithAction("onDone", (ctx, _) => journey.Add("CLEANER_DONE"))
            .WithAction("onPick", (ctx, _) => journey.Add("CLEANER_PICK"))
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();
    }

    private IActorRef CreateBuffer(XStateMachineFactory factory, List<string> journey)
    {
        var json = """
        {
            "id": "buffer",
            "initial": "empty",
            "context": { "wafer": null },
            "states": {
                "empty": {
                    "on": {
                        "PLACE": {
                            "target": "occupied",
                            "actions": ["storeWafer"]
                        }
                    }
                },
                "occupied": {
                    "on": {
                        "PICK": {
                            "target": "empty",
                            "actions": ["onPick", "clearWafer"]
                        }
                    }
                }
            }
        }
        """;

        return factory.FromJson(json)
            .WithAction("storeWafer", (ctx, evt) =>
            {
                journey.Add("BUFFER_PLACE");
                var waferData = evt as Dictionary<string, object>;
                if (waferData != null && waferData.ContainsKey("wafer"))
                {
                    ctx.Set("wafer", waferData["wafer"]);
                }
            })
            .WithAction("onPick", (ctx, _) => journey.Add("BUFFER_PICK"))
            .WithAction("clearWafer", (ctx, _) => ctx.Set("wafer", null))
            .BuildAndStart();
    }

    private void ProcessSingleWafer(IActorRef polisher, IActorRef cleaner, IActorRef buffer, int waferId)
    {
        // Polisher
        SendEventAndWait(polisher, "PLACE",
            s => s.CurrentState == "processing",
            "polisher processing",
            new Dictionary<string, object> { ["wafer"] = waferId });

        SendEventAndWait(polisher, "PROCESS_COMPLETE",
            s => s.CurrentState == "done",
            "polisher done");

        SendEventAndWait(polisher, "PICK",
            s => s.CurrentState == "idle",
            "polisher idle");

        // Cleaner
        SendEventAndWait(cleaner, "PLACE",
            s => s.CurrentState == "cleaning",
            "cleaner cleaning",
            new Dictionary<string, object> { ["wafer"] = waferId });

        SendEventAndWait(cleaner, "CLEAN_COMPLETE",
            s => s.CurrentState == "done",
            "cleaner done");

        SendEventAndWait(cleaner, "PICK",
            s => s.CurrentState == "idle",
            "cleaner idle");

        // Buffer
        SendEventAndWait(buffer, "PLACE",
            s => s.CurrentState == "occupied",
            "buffer occupied",
            new Dictionary<string, object> { ["wafer"] = waferId });

        SendEventAndWait(buffer, "PICK",
            s => s.CurrentState == "empty",
            "buffer empty");
    }

    #endregion
}
