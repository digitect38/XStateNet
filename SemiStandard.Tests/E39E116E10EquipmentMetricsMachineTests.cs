using XStateNet.Orchestration;
using XStateNet.Semi.Standards;
using Xunit;
using static SemiStandard.Tests.StateMachineTestHelpers;
using static XStateNet.Semi.Standards.E39E116E10EquipmentMetricsMachine;

namespace SemiStandard.Tests;

public class E39E116E10EquipmentMetricsMachineTests : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E39E116E10EquipmentMetricsMachine _metrics;

    public E39E116E10EquipmentMetricsMachineTests()
    {
        var config = new OrchestratorConfig { EnableLogging = true, PoolSize = 4, EnableMetrics = false };
        _orchestrator = new EventBusOrchestrator(config);
        _metrics = new E39E116E10EquipmentMetricsMachine("EQ001", _orchestrator);
    }

    public void Dispose() => _orchestrator?.Dispose();

    [Fact]
    public async Task E39_Should_Start_In_NonScheduled_State()
    {
        await _metrics.StartAsync();

        Assert.Contains("NonScheduled", _metrics.GetCurrentState());
        Assert.Equal(E10State.NonScheduled, _metrics.CurrentMetricState);
    }

    [Fact]
    public async Task E39_Should_Transition_To_StandBy_When_Scheduled()
    {
        await _metrics.StartAsync();

        var result = await _metrics.ScheduleAsync();

        AssertState(result, "StandBy");
        Assert.Equal(E10State.StandBy, _metrics.CurrentMetricState);
    }

    [Fact]
    public async Task E39_Should_Transition_To_Productive_When_Processing()
    {
        await _metrics.StartAsync();

        await _metrics.ScheduleAsync();

        var result = await _metrics.StartProcessingAsync("LOT001", "RECIPE001");

        AssertState(result, "Productive");
        Assert.Equal(E10State.Productive, _metrics.CurrentMetricState);
        Assert.Equal("LOT001", _metrics.Metrics.CurrentLotId);
    }

    [Fact]
    public async Task E39_Should_Complete_Processing_And_Return_To_StandBy()
    {
        await _metrics.StartAsync();

        await _metrics.ScheduleAsync();

        await _metrics.StartProcessingAsync("LOT001", "RECIPE001");

        var result = await _metrics.CompleteProcessingAsync(25, 24);

        AssertState(result, "StandBy");
        Assert.Equal(1, _metrics.Metrics.LotsProcessed);
        Assert.Equal(25, _metrics.Metrics.WafersProcessed);
        Assert.Equal(24, _metrics.Metrics.GoodWafers);
    }

    [Fact]
    public async Task E39_Should_Calculate_Yield()
    {
        await _metrics.StartAsync();

        await _metrics.ScheduleAsync();

        await _metrics.StartProcessingAsync("LOT001", "RECIPE001");

        await _metrics.CompleteProcessingAsync(100, 95);

        Assert.Equal(95.0, _metrics.Metrics.Yield);
    }

    [Fact]
    public async Task E39_Should_Transition_To_ScheduledDowntime_For_Maintenance()
    {
        await _metrics.StartAsync();

        await _metrics.ScheduleAsync();

        var result = await _metrics.StartMaintenanceAsync(E116ReasonCode.PreventiveMaintenance);

        AssertState(result, "ScheduledDowntime");
        Assert.Equal(E10State.ScheduledDowntime, _metrics.CurrentMetricState);
    }

    [Fact]
    public async Task E39_Should_Complete_Maintenance_And_Return_To_StandBy()
    {
        await _metrics.StartAsync();

        await _metrics.ScheduleAsync();

        await _metrics.StartMaintenanceAsync(E116ReasonCode.Calibration);

        var result = await _metrics.CompleteMaintenanceAsync();

        AssertState(result, "StandBy");
    }

    [Fact]
    public async Task E39_Should_Transition_To_UnscheduledDowntime_On_Fault()
    {
        await _metrics.StartAsync();

        await _metrics.ScheduleAsync();

        var result = await _metrics.ReportFaultAsync("FAULT_001", "Equipment failure");

        AssertState(result, "UnscheduledDowntime");
        Assert.Equal(E10State.UnscheduledDowntime, _metrics.CurrentMetricState);
        Assert.Equal(1, _metrics.Metrics.FaultCount);
    }

    [Fact]
    public async Task E39_Should_Complete_Repair_And_Return_To_StandBy()
    {
        await _metrics.StartAsync();

        await _metrics.ScheduleAsync();

        await _metrics.ReportFaultAsync("FAULT_001", "Equipment failure");

        var result = await _metrics.CompleteRepairAsync();

        AssertState(result, "StandBy");
    }

    [Fact]
    public async Task E39_Should_Handle_Fault_During_Processing()
    {
        await _metrics.StartAsync();

        await _metrics.ScheduleAsync();

        await _metrics.StartProcessingAsync("LOT001", "RECIPE001");

        var result = await _metrics.ReportFaultAsync("PROC_FAULT", "Process failure");

        AssertState(result, "UnscheduledDowntime");
        Assert.Equal(1, _metrics.Metrics.FaultCount);
    }

    [Fact]
    public async Task E39_Should_Transition_To_Engineering_State()
    {
        await _metrics.StartAsync();

        await _metrics.ScheduleAsync();

        var result = await _metrics.StartEngineeringAsync(E116ReasonCode.ProcessDevelopment);

        AssertState(result, "Engineering");
        Assert.Equal(E10State.Engineering, _metrics.CurrentMetricState);
    }

    [Fact]
    public async Task E39_Should_Complete_Engineering_And_Return_To_StandBy()
    {
        await _metrics.StartAsync();

        await _metrics.ScheduleAsync();

        await _metrics.StartEngineeringAsync(E116ReasonCode.EquipmentTest);

        var result = await _metrics.CompleteEngineeringAsync();

        AssertState(result, "StandBy");
    }

    [Fact]
    public async Task E39_Should_Pause_Processing()
    {
        await _metrics.StartAsync();

        await _metrics.ScheduleAsync();

        await _metrics.StartProcessingAsync("LOT001", "RECIPE001");

        var result = await _metrics.PauseProcessingAsync();

        AssertState(result, "StandBy");
    }

    [Fact]
    public async Task E39_Should_Unschedule_And_Return_To_NonScheduled()
    {
        await _metrics.StartAsync();

        await _metrics.ScheduleAsync();

        var result = await _metrics.UnscheduleAsync();

        AssertState(result, "NonScheduled");
    }

    [Fact]
    public async Task E39_Should_Generate_Metrics_Report()
    {
        await _metrics.StartAsync();

        await _metrics.ScheduleAsync();

        await _metrics.StartProcessingAsync("LOT001", "RECIPE001");

        await _metrics.CompleteProcessingAsync(50, 48);

        var report = _metrics.GetMetricsReport();

        Assert.NotNull(report);
        Assert.Equal("EQ001", report.EquipmentId);
        Assert.Equal(1, report.Metrics.LotsProcessed);
        Assert.Equal(50, report.Metrics.WafersProcessed);
        Assert.True(report.Metrics.Yield > 0);
        Assert.True(report.TotalTime.TotalSeconds > 0);
    }

    [Fact]
    public async Task E39_Should_Track_Multiple_Processing_Cycles()
    {
        await _metrics.StartAsync();

        await _metrics.ScheduleAsync();

        // First cycle
        await _metrics.StartProcessingAsync("LOT001", "RECIPE001");
        await _metrics.CompleteProcessingAsync(25, 24);

        // Second cycle
        await _metrics.StartProcessingAsync("LOT002", "RECIPE001");
        await _metrics.CompleteProcessingAsync(30, 29);

        Assert.Equal(2, _metrics.Metrics.LotsProcessed);
        Assert.Equal(55, _metrics.Metrics.WafersProcessed);
        Assert.Equal(53, _metrics.Metrics.GoodWafers);
    }

    [Fact]
    public async Task E39_Should_Calculate_OEE_Metrics()
    {
        await _metrics.StartAsync();

        await _metrics.ScheduleAsync();

        await _metrics.StartProcessingAsync("LOT001", "RECIPE001");

        await _metrics.CompleteProcessingAsync(100, 95);

        var report = _metrics.GetMetricsReport();

        Assert.True(report.Metrics.OEE >= 0);
        Assert.True(report.Metrics.Availability >= 0);
        Assert.True(report.Metrics.OperationalEfficiency >= 0);
        Assert.True(report.Metrics.QualityRate >= 0);
    }

    [Fact]
    public async Task E39_Should_Have_Correct_MachineId()
    {
        Assert.StartsWith("E39_E116_E10_METRICS_EQ001_", _metrics.MachineId);
    }

    [Fact]
    public async Task E39_Should_Track_State_History()
    {
        await _metrics.StartAsync();

        await _metrics.ScheduleAsync();

        await _metrics.StartProcessingAsync("LOT001", "RECIPE001");

        await _metrics.CompleteProcessingAsync(25, 24);

        var report = _metrics.GetMetricsReport();

        Assert.True(report.StateHistory.Count >= 3); // NonScheduled → StandBy → Productive → StandBy
    }

    [Fact]
    public async Task E39_Should_Calculate_MTTR_After_Fault()
    {
        await _metrics.StartAsync();

        await _metrics.ScheduleAsync();

        await _metrics.ReportFaultAsync("FAULT_001", "Test fault");

        await _metrics.CompleteRepairAsync();

        var report = _metrics.GetMetricsReport();

        Assert.True(report.Metrics.MTTR > 0);
        Assert.Equal(1, report.Metrics.FaultCount);
    }
}
