using XStateNet.Orchestration;
using XStateNet.Semi.Standards;
using Xunit;

namespace SemiStandard.Tests;

/// <summary>
/// Tests for SEMI E134 Data Collection Management
/// </summary>
public class E134DataCollectionMachineTests : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E134DataCollectionManager _dcmManager;

    public E134DataCollectionMachineTests()
    {
        _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
        _dcmManager = new E134DataCollectionManager("EQUIP001", _orchestrator);
    }

    public void Dispose()
    {
        _orchestrator?.Dispose();
    }

    [Fact]
    public async Task DataCollectionPlan_ShouldCreate_Successfully()
    {
        // Arrange
        var planId = "PLAN001";
        var dataItems = new[] { "Temperature", "Pressure", "FlowRate" };

        // Act
        var plan = await _dcmManager.CreatePlanAsync(planId, dataItems, CollectionTrigger.Event);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(planId, plan.PlanId);
        Assert.Equal(3, plan.DataItemIds.Length);
        Assert.True(plan.IsEnabled);
    }

    [Fact]
    public async Task DataCollectionPlan_ShouldCollect_WithEventTrigger()
    {
        // Arrange
        var planId = "PLAN002";
        var dataItems = new[] { "Voltage", "Current" };
        var plan = await _dcmManager.CreatePlanAsync(planId, dataItems, CollectionTrigger.Event);

        // Act
        var collectedData = new Dictionary<string, object>
        {
            ["Voltage"] = 220.5,
            ["Current"] = 15.2
        };

        var report = await _dcmManager.CollectDataAsync(planId, collectedData);

        // Assert
        Assert.NotNull(report);
        Assert.Equal(planId, report.PlanId);
        Assert.Equal(2, report.Data.Count);
        Assert.Equal(220.5, report.Data["Voltage"]);
        Assert.Equal(1, plan.CollectionCount);
    }

    [Fact]
    public async Task DataCollectionPlan_ShouldPause_AndResume()
    {
        // Arrange
        var planId = "PLAN003";
        var plan = await _dcmManager.CreatePlanAsync(planId, new[] { "Speed" }, CollectionTrigger.Timer);

        // Act
        await plan.PauseAsync();
        await Task.Delay(100);

        // Resume
        await plan.ResumeAsync();
        await Task.Delay(100);

        // Assert
        Assert.True(plan.IsEnabled);
    }

    [Fact]
    public async Task DataCollectionPlan_ShouldDisable_Successfully()
    {
        // Arrange
        var planId = "PLAN004";
        var plan = await _dcmManager.CreatePlanAsync(planId, new[] { "Humidity" }, CollectionTrigger.StateChange);

        // Act
        await plan.DisableAsync();
        await Task.Delay(100);

        // Assert
        Assert.False(plan.IsEnabled);
    }

    [Fact]
    public async Task DataCollectionManager_ShouldGet_ActivePlans()
    {
        // Arrange
        await _dcmManager.CreatePlanAsync("PLAN_A", new[] { "A" }, CollectionTrigger.Event);
        await _dcmManager.CreatePlanAsync("PLAN_B", new[] { "B" }, CollectionTrigger.Timer);
        var planC = await _dcmManager.CreatePlanAsync("PLAN_C", new[] { "C" }, CollectionTrigger.Manual);

        // Disable one plan
        await planC.DisableAsync();
        await Task.Delay(100);

        // Act
        var activePlans = _dcmManager.GetActivePlans().ToList();

        // Assert
        Assert.Equal(2, activePlans.Count);
        Assert.DoesNotContain(planC, activePlans);
    }

    [Fact]
    public async Task DataCollectionManager_ShouldStore_MultipleReports()
    {
        // Arrange
        var planId = "PLAN005";
        await _dcmManager.CreatePlanAsync(planId, new[] { "Power" }, CollectionTrigger.Event);

        // Act - Collect multiple times
        await _dcmManager.CollectDataAsync(planId, new Dictionary<string, object> { ["Power"] = 100 });
        await Task.Delay(50);
        await _dcmManager.CollectDataAsync(planId, new Dictionary<string, object> { ["Power"] = 150 });
        await Task.Delay(50);
        await _dcmManager.CollectDataAsync(planId, new Dictionary<string, object> { ["Power"] = 120 });

        var reports = _dcmManager.GetReports(planId).ToList();

        // Assert
        Assert.Equal(3, reports.Count);
        Assert.Equal(100, reports[0].Data["Power"]);
        Assert.Equal(150, reports[1].Data["Power"]);
        Assert.Equal(120, reports[2].Data["Power"]);
    }

    [Fact]
    public async Task DataCollectionManager_ShouldFilter_ReportsByTime()
    {
        // Arrange
        var planId = "PLAN006";
        await _dcmManager.CreatePlanAsync(planId, new[] { "Metric" }, CollectionTrigger.Event);

        var startTime = DateTime.UtcNow;

        await _dcmManager.CollectDataAsync(planId, new Dictionary<string, object> { ["Metric"] = 1 });
        await Task.Delay(100);

        var filterTime = DateTime.UtcNow;

        await _dcmManager.CollectDataAsync(planId, new Dictionary<string, object> { ["Metric"] = 2 });
        await _dcmManager.CollectDataAsync(planId, new Dictionary<string, object> { ["Metric"] = 3 });

        // Act
        var recentReports = _dcmManager.GetReports(planId, filterTime).ToList();

        // Assert
        Assert.Equal(2, recentReports.Count);
        Assert.All(recentReports, r => Assert.True(r.Timestamp >= filterTime));
    }

    [Fact]
    public async Task DataCollectionManager_ShouldRemove_Plan()
    {
        // Arrange
        var planId = "PLAN007";
        await _dcmManager.CreatePlanAsync(planId, new[] { "Test" }, CollectionTrigger.Event);

        // Act
        var removed = await _dcmManager.RemovePlanAsync(planId);
        var plan = _dcmManager.GetPlan(planId);

        // Assert
        Assert.True(removed);
        Assert.Null(plan);
    }

    [Fact]
    public async Task DataCollectionPlan_ShouldTrack_CollectionCount()
    {
        // Arrange
        var planId = "PLAN008";
        var plan = await _dcmManager.CreatePlanAsync(planId, new[] { "Counter" }, CollectionTrigger.Event);

        // Act
        for (int i = 0; i < 5; i++)
        {
            await _dcmManager.CollectDataAsync(planId, new Dictionary<string, object> { ["Counter"] = i });
            await Task.Delay(20);
        }

        // Assert
        Assert.Equal(5, plan.CollectionCount);
    }

    [Fact]
    public async Task DataCollectionPlan_ShouldRecord_LastCollectionTime()
    {
        // Arrange
        var planId = "PLAN009";
        var plan = await _dcmManager.CreatePlanAsync(planId, new[] { "Time" }, CollectionTrigger.Event);

        var beforeCollection = DateTime.UtcNow;

        // Act
        await _dcmManager.CollectDataAsync(planId, new Dictionary<string, object> { ["Time"] = "test" });
        await Task.Delay(50);

        var afterCollection = DateTime.UtcNow;

        // Assert
        Assert.NotNull(plan.LastCollectionTime);
        Assert.True(plan.LastCollectionTime >= beforeCollection);
        Assert.True(plan.LastCollectionTime <= afterCollection);
    }

    [Fact]
    public async Task DataCollectionPlan_WithTimerTrigger_ShouldCreate()
    {
        // Arrange & Act
        var plan = await _dcmManager.CreatePlanAsync("TIMER_PLAN", new[] { "Data" }, CollectionTrigger.Timer);

        // Assert
        Assert.NotNull(plan);
        Assert.True(plan.IsEnabled);
    }

    [Fact]
    public async Task DataCollectionPlan_WithThresholdTrigger_ShouldCreate()
    {
        // Arrange & Act
        var plan = await _dcmManager.CreatePlanAsync("THRESHOLD_PLAN", new[] { "Temp" }, CollectionTrigger.Threshold);

        // Assert
        Assert.NotNull(plan);
        Assert.True(plan.IsEnabled);
    }

    [Fact]
    public async Task DataCollectionPlan_WithManualTrigger_ShouldCreate()
    {
        // Arrange & Act
        var plan = await _dcmManager.CreatePlanAsync("MANUAL_PLAN", new[] { "Manual" }, CollectionTrigger.Manual);

        // Assert
        Assert.NotNull(plan);
        Assert.True(plan.IsEnabled);
    }

    [Fact]
    public async Task DataCollectionManager_ShouldHandle_DuplicatePlanId()
    {
        // Arrange
        var planId = "DUPLICATE_PLAN";
        var plan1 = await _dcmManager.CreatePlanAsync(planId, new[] { "A" }, CollectionTrigger.Event);

        // Act
        var plan2 = await _dcmManager.CreatePlanAsync(planId, new[] { "B" }, CollectionTrigger.Timer);

        // Assert
        Assert.Same(plan1, plan2); // Should return the same instance
    }
}
