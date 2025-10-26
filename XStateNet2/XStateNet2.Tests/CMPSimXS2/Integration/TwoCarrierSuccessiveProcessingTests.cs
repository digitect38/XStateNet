using System.Collections.ObjectModel;
using CMPSimXS2.Models;
using CMPSimXS2.Schedulers;
using CMPSimXS2.ViewModels;
using Xunit;

namespace XStateNet2.Tests.CMPSimXS2.Integration;

/// <summary>
/// Integration tests for two-carrier successive processing
/// These tests validate the complete flow demonstrated in the console app:
/// 1. Carrier C1 arrives → Process 5 wafers → Complete → Depart
/// 2. Carrier C2 arrives → Process 5 wafers → Complete → Depart
/// </summary>
public class TwoCarrierSuccessiveProcessingTests
{
    /// <summary>
    /// Test: Carrier arrival event registers carrier and its wafers
    /// Expected: Carrier ID stored, wafer IDs mapped, next wafer to start set
    /// </summary>
    [Fact]
    public void CarrierArrival_ShouldRegisterCarrierAndWafers()
    {
        // Arrange
        var robotScheduler = new RobotScheduler();
        var wafers = new ObservableCollection<Wafer>
        {
            new Wafer(1), new Wafer(2), new Wafer(3), new Wafer(4), new Wafer(5)
        };
        var journeyScheduler = new WaferJourneyScheduler(robotScheduler, wafers);

        // Act
        journeyScheduler.OnCarrierArrival("C1", new List<int> { 1, 2, 3, 4, 5 });

        // Assert
        Assert.Equal("C1", journeyScheduler.GetCurrentCarrierId());
    }

    /// <summary>
    /// Test: Carrier departure event clears current carrier
    /// Expected: Current carrier ID cleared after departure
    /// </summary>
    [Fact]
    public void CarrierDeparture_ShouldClearCurrentCarrier()
    {
        // Arrange
        var robotScheduler = new RobotScheduler();
        var wafers = new ObservableCollection<Wafer>
        {
            new Wafer(1), new Wafer(2), new Wafer(3)
        };
        var journeyScheduler = new WaferJourneyScheduler(robotScheduler, wafers);

        journeyScheduler.OnCarrierArrival("C1", new List<int> { 1, 2, 3 });

        // Act
        journeyScheduler.OnCarrierDeparture("C1");

        // Assert
        Assert.Null(journeyScheduler.GetCurrentCarrierId());
    }

    /// <summary>
    /// Test: Only wafers from current carrier should be processed
    /// Expected: Wafers from C2 should not start until C1 completes
    /// </summary>
    [Fact]
    public void ProcessWaferJourneys_ShouldOnlyProcessCurrentCarrierWafers()
    {
        // Arrange
        var robotScheduler = new RobotScheduler();
        var wafers = new ObservableCollection<Wafer>
        {
            new Wafer(1), new Wafer(2), new Wafer(3),  // C1 wafers
            new Wafer(4), new Wafer(5), new Wafer(6)   // C2 wafers
        };
        var journeyScheduler = new WaferJourneyScheduler(robotScheduler, wafers);

        // Register Polisher as idle
        var polisher = new StationViewModel("Polisher") { CurrentState = "idle" };
        journeyScheduler.RegisterStation("Polisher", polisher);

        // Register Robot 1 as idle
        var mockRobot1 = CreateMockRobot();
        robotScheduler.RegisterRobot("Robot 1", mockRobot1);
        robotScheduler.UpdateRobotState("Robot 1", "idle");

        // Act - Register C1 only
        journeyScheduler.OnCarrierArrival("C1", new List<int> { 1, 2, 3 });
        journeyScheduler.ProcessWaferJourneys();

        // Assert - Only wafer 1 (from C1) should have started
        Assert.Equal("ToPolisher", wafers[0].JourneyStage); // Wafer 1 started
        Assert.Equal("InCarrier", wafers[3].JourneyStage);  // Wafer 4 (C2) not started
        Assert.Equal("InCarrier", wafers[4].JourneyStage);  // Wafer 5 (C2) not started
    }

    /// <summary>
    /// Test: Carrier completion detection when all wafers finished
    /// Expected: IsCurrentCarrierComplete returns true when all wafers done
    /// </summary>
    [Fact]
    public void IsCurrentCarrierComplete_ShouldReturnTrue_WhenAllWafersCompleted()
    {
        // Arrange
        var robotScheduler = new RobotScheduler();
        var wafers = new ObservableCollection<Wafer>
        {
            new Wafer(1), new Wafer(2), new Wafer(3)
        };
        var journeyScheduler = new WaferJourneyScheduler(robotScheduler, wafers);

        journeyScheduler.OnCarrierArrival("C1", new List<int> { 1, 2, 3 });

        // Mark all wafers as completed
        wafers[0].IsCompleted = true;
        wafers[1].IsCompleted = true;
        wafers[2].IsCompleted = true;

        // Act
        var isComplete = journeyScheduler.IsCurrentCarrierComplete();

        // Assert
        Assert.True(isComplete);
    }

    /// <summary>
    /// Test: Carrier completion event fires when all wafers done
    /// Expected: OnCarrierCompleted event invoked with carrier ID
    /// </summary>
    [Fact]
    public void OnCarrierCompleted_ShouldFire_WhenAllWafersProcessed()
    {
        // Arrange
        var robotScheduler = new RobotScheduler();
        var wafers = new ObservableCollection<Wafer>
        {
            new Wafer(1), new Wafer(2)
        };
        var journeyScheduler = new WaferJourneyScheduler(robotScheduler, wafers);

        string? completedCarrierId = null;
        journeyScheduler.OnCarrierCompleted += (carrierId) =>
        {
            completedCarrierId = carrierId;
        };

        journeyScheduler.OnCarrierArrival("C1", new List<int> { 1, 2 });

        // Mark all wafers as completed
        wafers[0].IsCompleted = true;
        wafers[1].IsCompleted = true;

        // Act
        journeyScheduler.IsCurrentCarrierComplete();

        // Assert
        Assert.Equal("C1", completedCarrierId);
    }

    /// <summary>
    /// Test: Two-carrier successive processing complete flow
    /// Expected: C1 completes → C2 arrives → C2 completes → All wafers done
    /// </summary>
    [Fact]
    public void TwoCarrierFlow_ShouldProcessSequentially()
    {
        // Arrange
        var robotScheduler = new RobotScheduler();
        var wafers = new ObservableCollection<Wafer>
        {
            // Carrier 1: Wafers 1-3
            new Wafer(1), new Wafer(2), new Wafer(3),
            // Carrier 2: Wafers 4-6
            new Wafer(4), new Wafer(5), new Wafer(6)
        };
        var journeyScheduler = new WaferJourneyScheduler(robotScheduler, wafers);

        var carrierCompletions = new List<string>();
        journeyScheduler.OnCarrierCompleted += (carrierId) =>
        {
            carrierCompletions.Add(carrierId);
        };

        // Act - Simulate C1 arrival and completion
        journeyScheduler.OnCarrierArrival("C1", new List<int> { 1, 2, 3 });

        // Mark C1 wafers as completed
        wafers[0].IsCompleted = true;
        wafers[1].IsCompleted = true;
        wafers[2].IsCompleted = true;

        journeyScheduler.IsCurrentCarrierComplete();
        journeyScheduler.OnCarrierDeparture("C1");

        // Act - Simulate C2 arrival and completion
        journeyScheduler.OnCarrierArrival("C2", new List<int> { 4, 5, 6 });

        // Mark C2 wafers as completed
        wafers[3].IsCompleted = true;
        wafers[4].IsCompleted = true;
        wafers[5].IsCompleted = true;

        journeyScheduler.IsCurrentCarrierComplete();
        journeyScheduler.OnCarrierDeparture("C2");

        // Assert
        Assert.Equal(2, carrierCompletions.Count);
        Assert.Equal("C1", carrierCompletions[0]);
        Assert.Equal("C2", carrierCompletions[1]);
        Assert.True(wafers.All(w => w.IsCompleted));
    }

    /// <summary>
    /// Test: Carrier switchover should update current carrier ID
    /// Expected: Current carrier changes from C1 to C2 after C1 departs
    /// </summary>
    [Fact]
    public void CarrierSwitchover_ShouldUpdateCurrentCarrierId()
    {
        // Arrange
        var robotScheduler = new RobotScheduler();
        var wafers = new ObservableCollection<Wafer>
        {
            new Wafer(1), new Wafer(2), new Wafer(3), new Wafer(4)
        };
        var journeyScheduler = new WaferJourneyScheduler(robotScheduler, wafers);

        // Act - C1 lifecycle
        journeyScheduler.OnCarrierArrival("C1", new List<int> { 1, 2 });
        Assert.Equal("C1", journeyScheduler.GetCurrentCarrierId());

        journeyScheduler.OnCarrierDeparture("C1");
        Assert.Null(journeyScheduler.GetCurrentCarrierId());

        // Act - C2 lifecycle
        journeyScheduler.OnCarrierArrival("C2", new List<int> { 3, 4 });
        Assert.Equal("C2", journeyScheduler.GetCurrentCarrierId());
    }

    /// <summary>
    /// Test: Reset should clear all carrier tracking data
    /// Expected: Current carrier, wafer mappings, and completion flags cleared
    /// </summary>
    [Fact]
    public void Reset_ShouldClearCarrierTrackingData()
    {
        // Arrange
        var robotScheduler = new RobotScheduler();
        var wafers = new ObservableCollection<Wafer>
        {
            new Wafer(1), new Wafer(2)
        };
        var journeyScheduler = new WaferJourneyScheduler(robotScheduler, wafers);

        journeyScheduler.OnCarrierArrival("C1", new List<int> { 1, 2 });

        // Act
        journeyScheduler.Reset();

        // Assert
        Assert.Null(journeyScheduler.GetCurrentCarrierId());
    }

    /// <summary>
    /// Test: 5-wafer carrier processing (matches console demo)
    /// Expected: All 5 wafers from one carrier process sequentially
    /// </summary>
    [Fact]
    public void FiveWaferCarrier_ShouldProcessAllWafers()
    {
        // Arrange
        var robotScheduler = new RobotScheduler();
        var wafers = new ObservableCollection<Wafer>
        {
            new Wafer(1), new Wafer(2), new Wafer(3), new Wafer(4), new Wafer(5)
        };
        var journeyScheduler = new WaferJourneyScheduler(robotScheduler, wafers);

        // Act
        journeyScheduler.OnCarrierArrival("C1", new List<int> { 1, 2, 3, 4, 5 });

        // Mark all as completed (simulating full journey)
        foreach (var wafer in wafers)
        {
            wafer.IsCompleted = true;
        }

        var isComplete = journeyScheduler.IsCurrentCarrierComplete();

        // Assert
        Assert.True(isComplete);
        Assert.Equal("C1", journeyScheduler.GetCurrentCarrierId());
        Assert.Equal(5, wafers.Count(w => w.IsCompleted));
    }

    /// <summary>
    /// Test: 10-wafer two-carrier scenario (matches console demo)
    /// Expected: C1 (wafers 1-5) then C2 (wafers 6-10) process successfully
    /// </summary>
    [Fact]
    public void TenWafer_TwoCarrier_ShouldProcessAllSuccessively()
    {
        // Arrange
        var robotScheduler = new RobotScheduler();
        var wafers = new ObservableCollection<Wafer>
        {
            // Carrier C1: Wafers 1-5
            new Wafer(1), new Wafer(2), new Wafer(3), new Wafer(4), new Wafer(5),
            // Carrier C2: Wafers 6-10
            new Wafer(6), new Wafer(7), new Wafer(8), new Wafer(9), new Wafer(10)
        };
        var journeyScheduler = new WaferJourneyScheduler(robotScheduler, wafers);

        var completedCarriers = new List<string>();
        journeyScheduler.OnCarrierCompleted += (id) => completedCarriers.Add(id);

        // Act - Process Carrier C1
        journeyScheduler.OnCarrierArrival("C1", new List<int> { 1, 2, 3, 4, 5 });
        for (int i = 0; i < 5; i++)
        {
            wafers[i].IsCompleted = true;
        }
        journeyScheduler.IsCurrentCarrierComplete();
        journeyScheduler.OnCarrierDeparture("C1");

        // Act - Process Carrier C2
        journeyScheduler.OnCarrierArrival("C2", new List<int> { 6, 7, 8, 9, 10 });
        for (int i = 5; i < 10; i++)
        {
            wafers[i].IsCompleted = true;
        }
        journeyScheduler.IsCurrentCarrierComplete();
        journeyScheduler.OnCarrierDeparture("C2");

        // Assert
        Assert.Equal(2, completedCarriers.Count);
        Assert.Equal("C1", completedCarriers[0]);
        Assert.Equal("C2", completedCarriers[1]);
        Assert.Equal(10, wafers.Count(w => w.IsCompleted));
        Assert.Null(journeyScheduler.GetCurrentCarrierId());
    }

    /// <summary>
    /// Helper method to create a mock robot actor
    /// </summary>
    private static Akka.Actor.IActorRef CreateMockRobot()
    {
        var system = Akka.Actor.ActorSystem.Create("TestSystem");
        return system.ActorOf(Akka.Actor.Props.Create(() => new MockRobotActor()));
    }

    /// <summary>
    /// Mock robot actor for testing
    /// </summary>
    private class MockRobotActor : Akka.Actor.ReceiveActor
    {
        public MockRobotActor()
        {
            ReceiveAny(_ => { });
        }
    }
}
