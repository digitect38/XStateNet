using Microsoft.VisualStudio.TestPlatform.Utilities;
using XStateNet.Orchestration;
using XStateNet.Semi.Standards;
using Xunit;
using Xunit.Abstractions;

namespace SemiStandard.Tests;

/// <summary>
/// Tests for SEMI E164 Enhanced Data Collection Management
/// </summary>
public class E164EnhancedDataCollectionMachineTests : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E134DataCollectionManager _dcmManager;
    private readonly E164EnhancedDataCollectionManager _enhancedDcmManager;
    private readonly ITestOutputHelper _output;
        
    public E164EnhancedDataCollectionMachineTests(ITestOutputHelper output)
    {
        _output = output;
        _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
        _dcmManager = new E134DataCollectionManager("EQUIP001", _orchestrator);
        _enhancedDcmManager = new E164EnhancedDataCollectionManager("EQUIP001", _orchestrator, _dcmManager);
    }

    public void Dispose()
    {
        _orchestrator?.Dispose();
    }

    [Fact]
    public async Task TraceDataPlan_ShouldCreate_Successfully()
    {
        // Arrange
        var planId = "TRACE001";
        var dataItems = new[] { "Temperature", "Pressure" };
        var samplePeriod = TimeSpan.FromMilliseconds(100);
        var maxSamples = 100;

        // Act
        var plan = await _enhancedDcmManager.CreateTracePlanAsync(planId, dataItems, samplePeriod, maxSamples);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(planId, plan.PlanId);
        Assert.Equal(2, plan.DataItemIds.Length);
        Assert.True(plan.IsEnabled);
    }

    [Fact]
    public async Task TraceDataPlan_ShouldCollect_Samples()
    {
        // Arrange
        var planId = "TRACE002";
        var plan = await _enhancedDcmManager.CreateTracePlanAsync(
            planId, new[] { "Speed" }, TimeSpan.FromMilliseconds(50), 10);

        await plan.StartTraceAsync();

        // Act
        for (int i = 0; i < 5; i++)
        {
            await plan.AddSampleAsync(new Dictionary<string, object> { ["Speed"] = 100 + i * 10 });
            await Task.Delay(20);
        }

        var samples = plan.GetSamples();

        // Assert
        Assert.Equal(5, samples.Length);
        Assert.Equal(100, samples[0].Data["Speed"]);
        Assert.Equal(140, samples[4].Data["Speed"]);
    }

    [Fact]
    public async Task TraceDataPlan_ShouldLimit_BufferSize()
    {
        // Arrange
        var planId = "TRACE003";
        var maxSamples = 10;
        var plan = await _enhancedDcmManager.CreateTracePlanAsync(
            planId, new[] { "Data" }, TimeSpan.FromMilliseconds(10), maxSamples);

        await plan.StartTraceAsync();

        // Act - Add more than max samples
        for (int i = 0; i < 20; i++)
        {
            await plan.AddSampleAsync(new Dictionary<string, object> { ["Data"] = i });
        }

        var samples = plan.GetSamples();

        // Assert
        Assert.Equal(maxSamples, samples.Length);
        // Should have newest samples (10-19)
        Assert.Equal(19, samples[^1].Data["Data"]);
    }

    [Fact]
    public async Task TraceDataPlan_ShouldApply_Filter()
    {
        // Arrange
        var filter = new FilterCriteria
        {
            DataItemId = "Temperature",
            MinValue = 50.0,
            MaxValue = 100.0
        };

        var plan = await _enhancedDcmManager.CreateTracePlanAsync(
            "TRACE004", new[] { "Temperature" }, TimeSpan.FromMilliseconds(10), 50, filter);

        await plan.StartTraceAsync();

        // Act
        await plan.AddSampleAsync(new Dictionary<string, object> { ["Temperature"] = 30.0 });  // Filtered out
        await plan.AddSampleAsync(new Dictionary<string, object> { ["Temperature"] = 75.0 });  // Accepted
        await plan.AddSampleAsync(new Dictionary<string, object> { ["Temperature"] = 120.0 }); // Filtered out
        await plan.AddSampleAsync(new Dictionary<string, object> { ["Temperature"] = 60.0 });  // Accepted

        var samples = plan.GetSamples();

        // Assert
        Assert.Equal(2, samples.Length); // Only 2 samples pass filter
        Assert.Equal(75.0, samples[0].Data["Temperature"]);
        Assert.Equal(60.0, samples[1].Data["Temperature"]);
    }

    [Fact]
    public async Task TraceDataPlan_ShouldStart_AndStop()
    {
        // Arrange
        var plan = await _enhancedDcmManager.CreateTracePlanAsync(
            "TRACE005", new[] { "Metric" }, TimeSpan.FromMilliseconds(100), 50);

        // Act
        await plan.StartTraceAsync();
        await Task.Delay(100);

        await plan.StopTraceAsync();
        await Task.Delay(100);

        // Assert - No exceptions
        Assert.NotNull(plan);
    }

    [Fact]
    public async Task TraceDataPlan_ShouldDisable_AndClearBuffer()
    {
        // Arrange
        var plan = await _enhancedDcmManager.CreateTracePlanAsync(
            "TRACE006", new[] { "Data" }, TimeSpan.FromMilliseconds(10), 50);

        await plan.StartTraceAsync();
        await plan.AddSampleAsync(new Dictionary<string, object> { ["Data"] = 100 });
        await plan.AddSampleAsync(new Dictionary<string, object> { ["Data"] = 200 });

        // Act
        await plan.DisableAsync();
        await Task.Delay(100);

        var samples = plan.GetSamples();

        // Assert
        Assert.False(plan.IsEnabled);
        // Buffer is cleared on disable
        Assert.Empty(samples);
    }

    [Fact]
    public async Task StreamingSession_ShouldCreate_Successfully()
    {
        // Arrange
        var sessionId = "STREAM001";
        var dataItems = new[] { "Voltage", "Current", "Power" };
        var updateRate = 100;

        // Act
        var session = await _enhancedDcmManager.StartStreamingAsync(sessionId, dataItems, updateRate);

        // Assert
        Assert.NotNull(session);
        Assert.Equal(sessionId, session.SessionId);
        Assert.Equal(3, session.DataItemIds.Length);
        Assert.True(session.IsStreaming);
    }

    [Fact]
    public async Task StreamingSession_ShouldPublish_Updates()
    {
        // Arrange
        var sessionId = "STREAM002";
        var session = await _enhancedDcmManager.StartStreamingAsync(sessionId, new[] { "Data" }, 50);

        // Act
        await Task.Delay(250); // Wait for ~5 updates

        // Assert
        Assert.True(session.UpdateCount > 0);
        Assert.True(session.IsStreaming);
    }

    [Fact]
    public async Task StreamingSession_ShouldStop_Successfully()
    {
        // Arrange
        var sessionId = "STREAM003";
        var session = await _enhancedDcmManager.StartStreamingAsync(sessionId, new[] { "Metric" }, 50);

        await Task.Delay(150); // Wait for some updates

        // Act
        var stopped = await _enhancedDcmManager.StopStreamingAsync(sessionId);

        await Task.Delay(100);

        // Assert
        Assert.True(stopped);
        Assert.False(session.IsStreaming);
    }

    [Fact]
    public async Task StreamingSession_ShouldHandle_HighUpdateRate()
    {
        // Arrange
        var sessionId = "STREAM004";
        var updateRate = 10; // Very fast - 10ms

        // Act
        var session = await _enhancedDcmManager.StartStreamingAsync(sessionId, new[] { "Fast" }, updateRate);

        // wait for completion

        await Task.Delay(100); // Should get ~10 updates

        await _enhancedDcmManager.StopStreamingAsync(sessionId);

        // Assert
        Assert.True(session.UpdateCount >= 5); // At least 5 updates in 100ms
    }

    [Fact]
    public async Task EnhancedDcmManager_ShouldGet_TracePlan()
    {
        // Arrange
        var planId = "TRACE_GET";
        var created = await _enhancedDcmManager.CreateTracePlanAsync(
            planId, new[] { "Test" }, TimeSpan.FromMilliseconds(100), 50);

        // Act
        var retrieved = _enhancedDcmManager.GetTracePlan(planId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Same(created, retrieved);
    }

    [Fact]
    public async Task EnhancedDcmManager_ShouldGet_StreamingSession()
    {
        // Arrange
        var sessionId = "STREAM_GET";
        var created = await _enhancedDcmManager.StartStreamingAsync(sessionId, new[] { "Test" });

        // Act
        var retrieved = _enhancedDcmManager.GetStreamingSession(sessionId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Same(created, retrieved);
    }

    [Fact]
    public async Task EnhancedDcmManager_ShouldReturn_Null_ForNonexistentPlan()
    {
        // Act
        var plan = _enhancedDcmManager.GetTracePlan("NONEXISTENT");

        // Assert
        Assert.Null(plan);
    }

    [Fact]
    public async Task EnhancedDcmManager_ShouldReturn_Null_ForNonexistentSession()
    {
        // Act
        var session = _enhancedDcmManager.GetStreamingSession("NONEXISTENT");

        // Assert
        Assert.Null(session);
    }

    [Fact]
    public async Task TraceDataPlan_ShouldRecord_SampleTimestamps()
    {
        // Arrange
        var plan = await _enhancedDcmManager.CreateTracePlanAsync(
            "TRACE_TIME", new[] { "Data" }, TimeSpan.FromMilliseconds(50), 10);

        await plan.StartTraceAsync();

        var beforeSample = DateTime.UtcNow;

        // Act
        await plan.AddSampleAsync(new Dictionary<string, object> { ["Data"] = 100 });

        var afterSample = DateTime.UtcNow;

        var samples = plan.GetSamples();

        // Assert
        Assert.Single(samples);
        Assert.True(samples[0].Timestamp >= beforeSample);
        Assert.True(samples[0].Timestamp <= afterSample);
    }

    [Fact]
    public async Task StreamingSession_ShouldHandle_MultipleSessions()
    {
        var completed = new TaskCompletionSource<bool>();

        // Arrange & Act
        var session1 = await _enhancedDcmManager.StartStreamingAsync("SESSION_1", new[] { "A" }, 100);
        var session2 = await _enhancedDcmManager.StartStreamingAsync("SESSION_2", new[] { "B" }, 100);
        var session3 = await _enhancedDcmManager.StartStreamingAsync("SESSION_3", new[] { "C" }, 100);

        //await Task.Delay(200);
        // Wait for completion
        
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await completed.Task.WaitAsync(cts.Token);
            //_output.WriteLine("Test completed successfully");
        }
        catch (OperationCanceledException)
        {
            _output.WriteLine("TEST FAILED: Timeout!");
        }


        // Assert
        Assert.True(session1.IsStreaming);
        Assert.True(session2.IsStreaming);
        Assert.True(session3.IsStreaming);
        Assert.True(session1.UpdateCount > 0);
        Assert.True(session2.UpdateCount > 0);
        Assert.True(session3.UpdateCount > 0);

        // Cleanup
        await _enhancedDcmManager.StopStreamingAsync("SESSION_1");
        await _enhancedDcmManager.StopStreamingAsync("SESSION_2");
        await _enhancedDcmManager.StopStreamingAsync("SESSION_3");
    }

    [Fact]
    public async Task TraceDataPlan_ShouldHandle_DuplicatePlanId()
    {
        // Arrange
        var planId = "DUPLICATE_TRACE";
        var plan1 = await _enhancedDcmManager.CreateTracePlanAsync(
            planId, new[] { "A" }, TimeSpan.FromMilliseconds(100), 50);

        // Act
        var plan2 = await _enhancedDcmManager.CreateTracePlanAsync(
            planId, new[] { "B" }, TimeSpan.FromMilliseconds(200), 100);

        // Assert
        Assert.Same(plan1, plan2); // Should return existing plan
    }

    [Fact]
    public async Task StreamingSession_ShouldHandle_DuplicateSessionId()
    {
        // Arrange
        var sessionId = "DUPLICATE_SESSION";
        var session1 = await _enhancedDcmManager.StartStreamingAsync(sessionId, new[] { "A" });

        // Act
        var session2 = await _enhancedDcmManager.StartStreamingAsync(sessionId, new[] { "B" });

        // Assert
        Assert.Same(session1, session2); // Should return existing session

        // Cleanup
        await _enhancedDcmManager.StopStreamingAsync(sessionId);
    }
}
