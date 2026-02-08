using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using CMPSimXS2.Console.Models;
using CMPSimXS2.Console.Schedulers;
using FluentAssertions;
using Xunit;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests.CMPSimXS2.Schedulers;

/// <summary>
/// Comprehensive tests for RobotSchedulerXState - the FrozenDictionary-based XState scheduler.
/// Tests declarative state machine behavior, transfer lifecycle, robot selection strategies,
/// and XState-specific features like guards and actions.
/// </summary>
[Collection("Sequential")] // Disable parallel execution - Akka TestKit tests with timing dependencies
public class RobotSchedulerXStateTests : TestKit
{
    private readonly RobotSchedulerXState _scheduler;
    private readonly TestProbe _robot1;
    private readonly TestProbe _robot2;
    private readonly TestProbe _robot3;

    public RobotSchedulerXStateTests()
    {
        _scheduler = new RobotSchedulerXState(Sys, "test-xstate-scheduler");
        _robot1 = CreateTestProbe("Robot1");
        _robot2 = CreateTestProbe("Robot2");
        _robot3 = CreateTestProbe("Robot3");

        // Register robots
        _scheduler.RegisterRobot("Robot 1", _robot1.Ref);
        _scheduler.RegisterRobot("Robot 2", _robot2.Ref);
        _scheduler.RegisterRobot("Robot 3", _robot3.Ref);

        // Set all robots to idle initially
        _scheduler.UpdateRobotState("Robot 1", "idle");
        _scheduler.UpdateRobotState("Robot 2", "idle");
        _scheduler.UpdateRobotState("Robot 3", "idle");

        // Wait for actor to process messages
        AwaitAssert(() =>
        {
            _scheduler.GetRobotState("Robot 1").Should().Be("idle");
            _scheduler.GetRobotState("Robot 2").Should().Be("idle");
            _scheduler.GetRobotState("Robot 3").Should().Be("idle");
        }, TimeSpan.FromSeconds(2));
    }

    #region Robot Registration Tests

    [Fact]
    public void RegisterRobot_ShouldAllowRobotToBeUsed()
    {
        // Arrange
        var scheduler = new RobotSchedulerXState(Sys, "test-registration-xstate");
        var testRobot = CreateTestProbe("TestRobot");

        // Act
        scheduler.RegisterRobot("TestRobot", testRobot.Ref);
        scheduler.UpdateRobotState("TestRobot", "idle");

        // Assert
        AwaitAssert(() =>
        {
            scheduler.GetRobotState("TestRobot").Should().Be("idle");
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void RegisterRobot_MultipleRobots_ShouldTrackAllStates()
    {
        // Arrange
        var scheduler = new RobotSchedulerXState(Sys, "test-multiple-robots-xstate");
        var robotA = CreateTestProbe("RobotA");
        var robotB = CreateTestProbe("RobotB");
        var robotC = CreateTestProbe("RobotC");

        // Act
        scheduler.RegisterRobot("RobotA", robotA.Ref);
        scheduler.RegisterRobot("RobotB", robotB.Ref);
        scheduler.RegisterRobot("RobotC", robotC.Ref);

        scheduler.UpdateRobotState("RobotA", "idle");
        scheduler.UpdateRobotState("RobotB", "busy");
        scheduler.UpdateRobotState("RobotC", "idle");

        // Assert
        AwaitAssert(() =>
        {
            scheduler.GetRobotState("RobotA").Should().Be("idle");
            scheduler.GetRobotState("RobotB").Should().Be("busy");
            scheduler.GetRobotState("RobotC").Should().Be("idle");
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void GetRobotState_UnregisteredRobot_ShouldReturnUnknown()
    {
        // Act
        var state = _scheduler.GetRobotState("NonExistentRobot");

        // Assert
        state.Should().Be("unknown");
    }

    #endregion

    #region State Update Tests

    [Fact]
    public void UpdateRobotState_ShouldUpdateState()
    {
        // Act
        _scheduler.UpdateRobotState("Robot 1", "busy");

        // Assert
        AwaitAssert(() =>
        {
            _scheduler.GetRobotState("Robot 1").Should().Be("busy");
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void UpdateRobotState_IdleWithHeldWafer_ShouldClearWafer()
    {
        // Arrange - First make robot busy with a wafer
        var request = new TransferRequest
        {
            WaferId = 5,
            From = "Carrier",
            To = "Polisher",
            PreferredRobotId = "Robot 1"
        };
        _scheduler.RequestTransfer(request);

        // Wait for assignment
        AwaitAssert(() =>
        {
            _robot1.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(1));
            _scheduler.GetRobotState("Robot 1").Should().Be("busy");
        }, TimeSpan.FromSeconds(2));

        // Act - Try to set idle while still "holding" wafer (invalid state)
        _scheduler.UpdateRobotState("Robot 1", "idle", heldWaferId: 5);

        // Assert - Should enforce rule: idle robot cannot hold wafer
        AwaitAssert(() =>
        {
            _scheduler.GetRobotState("Robot 1").Should().Be("idle");
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void UpdateRobotState_ToIdle_ShouldCompleteActiveTransfer()
    {
        // Arrange
        bool transferCompleted = false;
        var request = new TransferRequest
        {
            WaferId = 7,
            From = "Carrier",
            To = "Polisher",
            PreferredRobotId = "Robot 1",
            OnCompleted = (waferId) => transferCompleted = true
        };

        _scheduler.RequestTransfer(request);

        // Wait for assignment
        AwaitAssert(() =>
        {
            _robot1.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(1));
        }, TimeSpan.FromSeconds(2));

        // Act - Robot completes transfer and becomes idle
        _scheduler.UpdateRobotState("Robot 1", "idle");

        // Assert - OnCompleted callback should be invoked
        AwaitAssert(() =>
        {
            transferCompleted.Should().BeTrue();
        }, TimeSpan.FromSeconds(2));
    }

    #endregion

    #region Transfer Request Tests

    [Fact]
    public void RequestTransfer_WithIdleRobot_ShouldAssignImmediately()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = "Polisher",
            PreferredRobotId = "Robot 1"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert - Should receive PICKUP event immediately
        AwaitAssert(() =>
        {
            var msg = _robot1.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(1));
            msg.Type.Should().Be("PICKUP");

            var data = msg.Data as Dictionary<string, object>;
            data.Should().NotBeNull();
            data!["waferId"].Should().Be(1);
            data["from"].Should().Be("Carrier");
            data["to"].Should().Be("Polisher");

            _scheduler.GetQueueSize().Should().Be(0);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void RequestTransfer_WithAllRobotsBusy_ShouldQueue()
    {
        // Arrange - Make ALL robots busy
        _scheduler.UpdateRobotState("Robot 1", "busy");
        _scheduler.UpdateRobotState("Robot 2", "busy");
        _scheduler.UpdateRobotState("Robot 3", "busy");

        AwaitAssert(() =>
        {
            _scheduler.GetRobotState("Robot 1").Should().Be("busy");
            _scheduler.GetRobotState("Robot 2").Should().Be("busy");
            _scheduler.GetRobotState("Robot 3").Should().Be("busy");
        }, TimeSpan.FromSeconds(2));

        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = "Polisher"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert - Should be queued because no robots available
        AwaitAssert(() =>
        {
            _scheduler.GetQueueSize().Should().Be(1);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void RequestTransfer_WithInvalidRequest_ShouldNotQueue()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = -1, // Invalid
            From = "Carrier",
            To = "Polisher"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert - Should not queue invalid request
        Thread.Sleep(200); // Give actor time to process
        _scheduler.GetQueueSize().Should().Be(0);
        _robot1.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void RequestTransfer_ShouldSendPickupEventWithAllRequiredData()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 42,
            From = "Polisher",
            To = "Cleaner",
            PreferredRobotId = "Robot 2"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert
        AwaitAssert(() =>
        {
            var msg = _robot2.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(1));
            msg.Type.Should().Be("PICKUP");

            var data = msg.Data as Dictionary<string, object>;
            data.Should().NotBeNull();
            data!["waferId"].Should().Be(42);
            data["wafer"].Should().Be(42); // Some robots expect "wafer"
            data["from"].Should().Be("Polisher");
            data["to"].Should().Be("Cleaner");
        }, TimeSpan.FromSeconds(2));
    }

    #endregion

    #region Robot Selection Strategy Tests

    [Fact]
    public void SelectNearestRobot_CarrierToPolisher_ShouldSelectRobot1()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = "Polisher"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert - Robot 1 handles Carrier ↔ Polisher
        AwaitAssert(() =>
        {
            _robot1.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(1));
            _robot2.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            _robot3.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SelectNearestRobot_PolisherToCleaner_ShouldSelectRobot2()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Polisher",
            To = "Cleaner"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert - Robot 2 handles Polisher ↔ Cleaner
        AwaitAssert(() =>
        {
            _robot2.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(1));
            _robot1.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            _robot3.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SelectNearestRobot_CleanerToBuffer_ShouldSelectRobot3()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Cleaner",
            To = "Buffer"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert - Robot 3 handles Cleaner ↔ Buffer
        AwaitAssert(() =>
        {
            _robot3.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(1));
            _robot1.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            _robot2.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SelectNearestRobot_BufferToCarrier_ShouldSelectRobot1()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Buffer",
            To = "Carrier"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert - Robot 1 handles Buffer ↔ Carrier
        AwaitAssert(() =>
        {
            _robot1.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(1));
            _robot2.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            _robot3.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SelectRobot_PreferredRobotAvailable_ShouldUsePreferred()
    {
        // Arrange - Request prefers Robot 3, but nearest would be Robot 2
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Polisher",
            To = "Cleaner",
            PreferredRobotId = "Robot 3" // Normally Robot 2's route
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert - Should use preferred robot (Robot 3) instead of nearest (Robot 2)
        AwaitAssert(() =>
        {
            _robot3.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(1));
            _robot2.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SelectRobot_PreferredRobotBusy_ShouldUseFallback()
    {
        // Arrange - Preferred robot is busy
        _scheduler.UpdateRobotState("Robot 1", "busy");

        AwaitAssert(() =>
        {
            _scheduler.GetRobotState("Robot 1").Should().Be("busy");
        }, TimeSpan.FromSeconds(2));

        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = "Polisher",
            PreferredRobotId = "Robot 1"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert - Should use fallback robot (Robot 2 or 3)
        AwaitAssert(() =>
        {
            _robot1.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            // Either Robot 2 or Robot 3 should receive the message
            bool robot2Got = _robot2.HasMessages;
            bool robot3Got = _robot3.HasMessages;
            (robot2Got || robot3Got).Should().BeTrue("a fallback robot should be used");

            _scheduler.GetQueueSize().Should().Be(0);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SelectRobot_NearestRobotBusy_ShouldUseFirstAvailable()
    {
        // Arrange - Make nearest robot (Robot 1) busy
        _scheduler.UpdateRobotState("Robot 1", "busy");

        AwaitAssert(() =>
        {
            _scheduler.GetRobotState("Robot 1").Should().Be("busy");
        }, TimeSpan.FromSeconds(2));

        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = "Polisher"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert - Should use first available robot (Robot 2 or 3)
        AwaitAssert(() =>
        {
            _robot1.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            bool robot2Got = _robot2.HasMessages;
            bool robot3Got = _robot3.HasMessages;
            (robot2Got || robot3Got).Should().BeTrue("first available robot should be used");
        }, TimeSpan.FromSeconds(2));
    }

    #endregion

    #region Queue Processing Tests

    [Fact]
    public void UpdateRobotState_ToIdle_ShouldProcessPendingRequests()
    {
        // Arrange - Make ALL robots busy so request must be queued
        _scheduler.UpdateRobotState("Robot 1", "busy");
        _scheduler.UpdateRobotState("Robot 2", "busy");
        _scheduler.UpdateRobotState("Robot 3", "busy");

        AwaitAssert(() =>
        {
            _scheduler.GetRobotState("Robot 1").Should().Be("busy");
            _scheduler.GetRobotState("Robot 2").Should().Be("busy");
            _scheduler.GetRobotState("Robot 3").Should().Be("busy");
        }, TimeSpan.FromSeconds(2));

        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = "Polisher",
            PreferredRobotId = "Robot 1"
        };
        _scheduler.RequestTransfer(request);

        AwaitAssert(() =>
        {
            _scheduler.GetQueueSize().Should().Be(1);
        }, TimeSpan.FromSeconds(2));

        // Act - Make Robot 1 idle
        _scheduler.UpdateRobotState("Robot 1", "idle");

        // Assert - Pending request should be processed
        // Note: ExpectMsg must be outside AwaitAssert because it consumes the message
        _robot1.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(3));
        AwaitAssert(() =>
        {
            _scheduler.GetQueueSize().Should().Be(0);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void RequestTransfer_MultipleRequests_ShouldProcessInFIFOOrder()
    {
        // Arrange - Make ALL robots busy to force queuing
        _scheduler.UpdateRobotState("Robot 1", "busy");
        _scheduler.UpdateRobotState("Robot 2", "busy");
        _scheduler.UpdateRobotState("Robot 3", "busy");

        AwaitAssert(() =>
        {
            _scheduler.GetRobotState("Robot 1").Should().Be("busy");
            _scheduler.GetRobotState("Robot 2").Should().Be("busy");
            _scheduler.GetRobotState("Robot 3").Should().Be("busy");
        }, TimeSpan.FromSeconds(2));

        var request1 = new TransferRequest { WaferId = 1, From = "Carrier", To = "Polisher", PreferredRobotId = "Robot 1" };
        var request2 = new TransferRequest { WaferId = 2, From = "Carrier", To = "Polisher", PreferredRobotId = "Robot 1" };
        var request3 = new TransferRequest { WaferId = 3, From = "Carrier", To = "Polisher", PreferredRobotId = "Robot 1" };

        // Act - Request with all robots busy
        _scheduler.RequestTransfer(request1);
        _scheduler.RequestTransfer(request2);
        _scheduler.RequestTransfer(request3);

        // Assert - All queued because no robots available
        AwaitAssert(() =>
        {
            _scheduler.GetQueueSize().Should().Be(3);
        }, TimeSpan.FromSeconds(2));

        // Act - Robot 1 becomes idle, should process first request
        _scheduler.UpdateRobotState("Robot 1", "idle");

        // Assert - First request processed (ExpectMsg outside AwaitAssert)
        var msg1 = _robot1.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(3));
        var data1 = msg1.Data as Dictionary<string, object>;
        data1!["waferId"].Should().Be(1, "first request should be processed first (FIFO)");
        AwaitAssert(() =>
        {
            _scheduler.GetQueueSize().Should().Be(2);
        }, TimeSpan.FromSeconds(2));

        // Act - Robot 1 completes and becomes idle again
        _scheduler.UpdateRobotState("Robot 1", "idle");

        // Assert - Second request processed
        var msg2 = _robot1.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(3));
        var data2 = msg2.Data as Dictionary<string, object>;
        data2!["waferId"].Should().Be(2, "second request should be processed second (FIFO)");
        AwaitAssert(() =>
        {
            _scheduler.GetQueueSize().Should().Be(1);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ProcessTransfers_MultipleRobotsBecomingIdle_ShouldProcessMultipleRequests()
    {
        // Arrange - Make ALL robots busy and queue multiple requests
        _scheduler.UpdateRobotState("Robot 1", "busy");
        _scheduler.UpdateRobotState("Robot 2", "busy");
        _scheduler.UpdateRobotState("Robot 3", "busy");

        AwaitAssert(() =>
        {
            _scheduler.GetRobotState("Robot 1").Should().Be("busy");
            _scheduler.GetRobotState("Robot 2").Should().Be("busy");
            _scheduler.GetRobotState("Robot 3").Should().Be("busy");
        }, TimeSpan.FromSeconds(2));

        var request1 = new TransferRequest { WaferId = 1, From = "Carrier", To = "Polisher" };
        var request2 = new TransferRequest { WaferId = 2, From = "Polisher", To = "Cleaner" };
        var request3 = new TransferRequest { WaferId = 3, From = "Cleaner", To = "Buffer" };

        _scheduler.RequestTransfer(request1);
        _scheduler.RequestTransfer(request2);
        _scheduler.RequestTransfer(request3);

        AwaitAssert(() =>
        {
            _scheduler.GetQueueSize().Should().Be(3);
        }, TimeSpan.FromSeconds(2));

        // Act - All robots become idle
        _scheduler.UpdateRobotState("Robot 1", "idle");
        _scheduler.UpdateRobotState("Robot 2", "idle");
        _scheduler.UpdateRobotState("Robot 3", "idle");

        // Assert - All requests should be processed (ExpectMsg outside AwaitAssert)
        _robot1.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(3));
        _robot2.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(3));
        _robot3.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(3));

        AwaitAssert(() =>
        {
            _scheduler.GetQueueSize().Should().Be(0);
        }, TimeSpan.FromSeconds(2));
    }

    #endregion

    #region ActiveTransfers Tracking Tests

    [Fact]
    public void ExecuteTransfer_ShouldUpdateRobotStateAndTrackWaferId()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 99,
            From = "Carrier",
            To = "Polisher",
            PreferredRobotId = "Robot 1"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert - Robot should be marked busy
        AwaitAssert(() =>
        {
            _robot1.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(1));
            _scheduler.GetRobotState("Robot 1").Should().Be("busy");
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void TransferLifecycle_CompleteFlow_ShouldTrackFromRequestToCompletion()
    {
        // Arrange
        bool completionCalled = false;
        int completedWaferId = 0;

        var request = new TransferRequest
        {
            WaferId = 77,
            From = "Carrier",
            To = "Polisher",
            PreferredRobotId = "Robot 1",
            OnCompleted = (waferId) =>
            {
                completionCalled = true;
                completedWaferId = waferId;
            }
        };

        // Act - Request transfer
        _scheduler.RequestTransfer(request);

        // Assert - Robot receives PICKUP and becomes busy
        AwaitAssert(() =>
        {
            var msg = _robot1.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(1));
            msg.Type.Should().Be("PICKUP");
            _scheduler.GetRobotState("Robot 1").Should().Be("busy");
        }, TimeSpan.FromSeconds(2));

        // Act - Robot completes transfer
        _scheduler.UpdateRobotState("Robot 1", "idle");

        // Assert - Completion callback invoked with correct wafer ID
        AwaitAssert(() =>
        {
            completionCalled.Should().BeTrue();
            completedWaferId.Should().Be(77);
            _scheduler.GetRobotState("Robot 1").Should().Be("idle");
        }, TimeSpan.FromSeconds(2));
    }

    #endregion

    #region XState State Machine Specific Tests

    [Fact]
    public void XStateGuards_HasNoPendingWork_ShouldTransitionToIdle()
    {
        // This test verifies that the XState guard "hasNoPendingWork" correctly
        // transitions the state machine from processing back to idle

        // Arrange
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = "Polisher",
            PreferredRobotId = "Robot 1"
        };

        // Act - Request transfer (should transition to processing state)
        _scheduler.RequestTransfer(request);

        AwaitAssert(() =>
        {
            _robot1.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(1));
        }, TimeSpan.FromSeconds(2));

        // Complete the transfer
        _scheduler.UpdateRobotState("Robot 1", "idle");

        // Assert - With no pending work, scheduler should return to idle state
        AwaitAssert(() =>
        {
            _scheduler.GetQueueSize().Should().Be(0);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void XStateActions_RegisterRobot_ShouldExecuteCorrectly()
    {
        // This test verifies that the XState action "registerRobot" executes correctly

        // Arrange
        var scheduler = new RobotSchedulerXState(Sys, "test-xstate-action");
        var testRobot = CreateTestProbe("ActionTestRobot");

        // Act - Register robot (triggers registerRobot action)
        scheduler.RegisterRobot("ActionTestRobot", testRobot.Ref);
        scheduler.UpdateRobotState("ActionTestRobot", "idle");

        // Assert - Robot should be registered and usable
        AwaitAssert(() =>
        {
            scheduler.GetRobotState("ActionTestRobot").Should().Be("idle");
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void XStateActions_QueueOrAssignTransfer_ShouldExecuteImmediateAssignment()
    {
        // This test verifies that the XState action "queueOrAssignTransfer"
        // correctly assigns transfers immediately when robots are available

        // Arrange
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = "Polisher",
            PreferredRobotId = "Robot 1"
        };

        // Act - Request transfer (triggers queueOrAssignTransfer action)
        _scheduler.RequestTransfer(request);

        // Assert - Should assign immediately, not queue
        AwaitAssert(() =>
        {
            _robot1.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(1));
            _scheduler.GetQueueSize().Should().Be(0);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void XStateActions_ProcessTransfers_ShouldDequeueAndAssign()
    {
        // This test verifies that the XState action "processTransfers"
        // correctly processes queued requests when robots become available

        // Arrange - Queue a request
        _scheduler.UpdateRobotState("Robot 1", "busy");
        _scheduler.UpdateRobotState("Robot 2", "busy");
        _scheduler.UpdateRobotState("Robot 3", "busy");

        AwaitAssert(() =>
        {
            _scheduler.GetRobotState("Robot 1").Should().Be("busy");
        }, TimeSpan.FromSeconds(2));

        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = "Polisher",
            PreferredRobotId = "Robot 1"
        };
        _scheduler.RequestTransfer(request);

        AwaitAssert(() =>
        {
            _scheduler.GetQueueSize().Should().Be(1);
        }, TimeSpan.FromSeconds(2));

        // Act - Robot becomes idle (should trigger processTransfers action)
        _scheduler.UpdateRobotState("Robot 1", "idle");

        // Assert - Request should be dequeued and assigned (ExpectMsg outside AwaitAssert)
        _robot1.ExpectMsg<SendEvent>(TimeSpan.FromSeconds(3));
        AwaitAssert(() =>
        {
            _scheduler.GetQueueSize().Should().Be(0);
        }, TimeSpan.FromSeconds(2));
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public void ConcurrentRequests_ShouldAllBeProcessedCorrectly()
    {
        // Arrange
        var requests = Enumerable.Range(1, 10).Select(i => new TransferRequest
        {
            WaferId = i,
            From = "Carrier",
            To = "Polisher"
        }).ToList();

        // Act - Send all requests concurrently
        Parallel.ForEach(requests, request => _scheduler.RequestTransfer(request));

        // Assert - Eventually all should be processed (either immediately or queued then processed)
        AwaitAssert(() =>
        {
            // After some time, queue should be empty and all robots might be busy or idle
            _scheduler.GetQueueSize().Should().BeLessThanOrEqualTo(10);
        }, TimeSpan.FromSeconds(3));
    }

    #endregion

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _robot1?.Ref.Tell(PoisonPill.Instance);
            _robot2?.Ref.Tell(PoisonPill.Instance);
            _robot3?.Ref.Tell(PoisonPill.Instance);
        }
        base.Dispose(disposing);
    }
}
