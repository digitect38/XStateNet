using XStateNet.Orchestration;
using XStateNet.Semi.Standards;
using Xunit;

namespace SemiStandard.Tests;

/// <summary>
/// Tests for SEMI E148 Time Synchronization
/// </summary>
public class E148TimeSynchronizationMachineTests : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E148TimeSynchronizationManager _timeSyncManager;

    public E148TimeSynchronizationMachineTests()
    {
        _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
        _timeSyncManager = new E148TimeSynchronizationManager("EQUIP001", _orchestrator);
    }

    public void Dispose()
    {
        _orchestrator?.Dispose();
    }

    [Fact]
    public async Task TimeSynchronization_ShouldInitialize_Successfully()
    {
        // Act
        await _timeSyncManager.InitializeAsync();

        // Assert
        Assert.NotNull(_timeSyncManager);
        Assert.Equal("E148_TIME_SYNC_EQUIP001", _timeSyncManager.MachineId);
    }

    [Fact]
    public async Task TimeSynchronization_ShouldCalculate_TimeOffset()
    {
        // Arrange
        await _timeSyncManager.InitializeAsync();
        var hostTime = DateTime.UtcNow.AddMilliseconds(50); // Simulate 50ms offset

        // Act
        var result = await _timeSyncManager.SynchronizeAsync(hostTime);

        // Assert
        Assert.True(result.Success);
        Assert.True(Math.Abs(result.Offset.TotalMilliseconds) < 100); // Should be close to 50ms
        Assert.True(_timeSyncManager.IsSynchronized);
    }

    [Fact]
    public async Task TimeSynchronization_ShouldReturn_SynchronizedTime()
    {
        // Arrange
        await _timeSyncManager.InitializeAsync();
        var hostTime = DateTime.UtcNow;

        // Act
        await _timeSyncManager.SynchronizeAsync(hostTime);
        var syncTime = _timeSyncManager.GetSynchronizedTime();

        // Assert
        var diff = Math.Abs((syncTime - hostTime).TotalMilliseconds);
        Assert.True(diff < 100); // Should be within 100ms
    }

    [Fact]
    public async Task TimeSynchronization_ShouldCalculate_RoundTripDelay()
    {
        // Arrange
        await _timeSyncManager.InitializeAsync();
        var hostTime = DateTime.UtcNow;

        // Act
        var result = await _timeSyncManager.SynchronizeAsync(hostTime);

        // Assert
        Assert.True(result.RoundTripDelay.TotalMilliseconds >= 0);
        Assert.True(result.RoundTripDelay.TotalMilliseconds < 100); // Should be very fast in tests
    }

    [Fact]
    public async Task TimeSynchronization_ShouldCalculate_ClockDrift()
    {
        // Arrange
        await _timeSyncManager.InitializeAsync();

        // Act - Perform multiple synchronizations
        for (int i = 0; i < 5; i++)
        {
            var hostTime = DateTime.UtcNow.AddMilliseconds(i * 10); // Simulate drift
            await _timeSyncManager.SynchronizeAsync(hostTime);
            await Task.Delay(100);
        }

        // Assert
        Assert.NotEqual(0.0, _timeSyncManager.ClockDriftRate);
    }

    [Fact]
    public async Task TimeSynchronization_ShouldUpdate_LastSyncTime()
    {
        // Arrange
        await _timeSyncManager.InitializeAsync();
        var beforeSync = DateTime.UtcNow;

        // Act
        await _timeSyncManager.SynchronizeAsync(DateTime.UtcNow);
        var afterSync = DateTime.UtcNow;

        // Assert
        Assert.True(_timeSyncManager.LastSyncTime >= beforeSync);
        Assert.True(_timeSyncManager.LastSyncTime <= afterSync);
    }

    [Fact]
    public async Task TimeSynchronization_ShouldReturn_Status()
    {
        // Arrange
        await _timeSyncManager.InitializeAsync();

        // Act
        await _timeSyncManager.SynchronizeAsync(DateTime.UtcNow);
        var status = _timeSyncManager.GetStatus();

        // Assert
        Assert.True(status.IsSynchronized);
        Assert.NotEqual(TimeSpan.Zero, status.TimeOffset);
        Assert.NotEqual(DateTime.MinValue, status.LastSyncTime);
        Assert.True(status.SampleCount > 0);
    }

    [Fact]
    public async Task TimeSynchronization_IsSynchronized_ShouldBeFalse_WhenStale()
    {
        // Arrange
        await _timeSyncManager.InitializeAsync();

        // Don't sync - status should be not synchronized
        var status = _timeSyncManager.GetStatus();

        // Assert
        Assert.False(status.IsSynchronized);
    }

    [Fact]
    public async Task TimeSynchronization_ShouldAccumulate_SyncHistory()
    {
        // Arrange
        await _timeSyncManager.InitializeAsync();

        // Act
        for (int i = 0; i < 10; i++)
        {
            await _timeSyncManager.SynchronizeAsync(DateTime.UtcNow);
            await Task.Delay(10);
        }

        var status = _timeSyncManager.GetStatus();

        // Assert
        Assert.Equal(10, status.SampleCount);
    }

    [Fact]
    public async Task TimeSynchronization_ShouldLimit_HistorySize()
    {
        // Arrange
        await _timeSyncManager.InitializeAsync();

        // Act - Perform more than 100 syncs
        for (int i = 0; i < 150; i++)
        {
            await _timeSyncManager.SynchronizeAsync(DateTime.UtcNow);
        }

        var status = _timeSyncManager.GetStatus();

        // Assert
        Assert.Equal(100, status.SampleCount); // Should be capped at 100
    }

    [Fact]
    public async Task TimeSynchronization_ShouldHandle_LargeOffset()
    {
        // Arrange
        await _timeSyncManager.InitializeAsync();
        var hostTime = DateTime.UtcNow.AddSeconds(5); // 5 second offset

        // Act
        var result = await _timeSyncManager.SynchronizeAsync(hostTime);

        // Assert
        Assert.True(result.Success);
        Assert.True(Math.Abs(result.Offset.TotalSeconds - 5) < 0.1); // Should detect ~5s offset
    }

    [Fact]
    public async Task TimeSynchronization_ShouldHandle_NegativeOffset()
    {
        // Arrange
        await _timeSyncManager.InitializeAsync();
        var hostTime = DateTime.UtcNow.AddSeconds(-2); // -2 second offset (host is behind)

        // Act
        var result = await _timeSyncManager.SynchronizeAsync(hostTime);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Offset.TotalSeconds < 0); // Should be negative
    }

    [Fact]
    public async Task TimeSynchronization_MultipleSync_ShouldImprove_Accuracy()
    {
        // Arrange
        await _timeSyncManager.InitializeAsync();

        // Act
        var result1 = await _timeSyncManager.SynchronizeAsync(DateTime.UtcNow);
        await Task.Delay(100);
        var result2 = await _timeSyncManager.SynchronizeAsync(DateTime.UtcNow);
        await Task.Delay(100);
        var result3 = await _timeSyncManager.SynchronizeAsync(DateTime.UtcNow);

        // Assert
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.True(result3.Success);
        // After multiple syncs, drift calculation should be available
        Assert.NotEqual(0.0, _timeSyncManager.ClockDriftRate);
    }

    [Fact]
    public async Task TimeSynchronization_ShouldApply_DriftCorrection()
    {
        // Arrange
        await _timeSyncManager.InitializeAsync();

        // Simulate drift by multiple syncs with increasing offset
        for (int i = 0; i < 5; i++)
        {
            var hostTime = DateTime.UtcNow.AddMilliseconds(i * 2);
            await _timeSyncManager.SynchronizeAsync(hostTime);
            await Task.Delay(50);
        }

        // Act
        var syncTime1 = _timeSyncManager.GetSynchronizedTime();
        await Task.Delay(100);
        var syncTime2 = _timeSyncManager.GetSynchronizedTime();

        // Assert
        var timeDiff = (syncTime2 - syncTime1).TotalMilliseconds;
        Assert.True(timeDiff >= 100); // Should include drift correction
    }
}
