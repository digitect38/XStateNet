using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using XStateNet.Orchestration;
using XStateNet.Semi.Standards;
using static SemiStandard.Tests.StateMachineTestHelpers;

namespace SemiStandard.Tests;

public class E40ProcessJobMachineTests : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E40ProcessJobManager _processJobMgr;

    public E40ProcessJobMachineTests()
    {
        var config = new OrchestratorConfig { EnableLogging = true, PoolSize = 4, EnableMetrics = false };
        _orchestrator = new EventBusOrchestrator(config);
        _processJobMgr = new E40ProcessJobManager("EQ001", _orchestrator);
    }

    public void Dispose() => _orchestrator?.Dispose();

    [Fact]
    public async Task E40_Should_Create_ProcessJob()
    {
        var processJob = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001", "MAT002" });

        Assert.NotNull(processJob);
        Assert.Equal("PJ001", processJob.ProcessJobId);
        Assert.Equal("RECIPE001", processJob.RecipeId);
        Assert.Contains("NoState", processJob.GetCurrentState());
    }

    [Fact]
    public async Task E40_Should_Complete_Full_ProcessJob_Lifecycle()
    {
        var processJob = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001", "MAT002" });

        // Create
        var result = await processJob.CreateAsync();
        AssertState(result, "Queued");

        // Setup
        result = await processJob.SetupAsync();
        AssertState(result, "SettingUp");

        result = await processJob.SetupCompleteAsync();
        AssertState(result, "WaitingForStart");

        // Start Processing
        result = await processJob.StartProcessingAsync();
        AssertState(result, "Processing");
        Assert.NotNull(processJob.StartTime);

        // Complete Processing
        result = await processJob.ProcessingCompleteAsync();
        AssertState(result, "ProcessingComplete");
        Assert.NotNull(processJob.EndTime);
        Assert.True(processJob.EndTime >= processJob.StartTime);
    }

    [Fact]
    public async Task E40_Should_Handle_Pause_Resume()
    {
        var processJob = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001" });

        // Get to Processing state
        var result = await processJob.CreateAsync();
        result = await processJob.SetupAsync();
        result = await processJob.SetupCompleteAsync();
        result = await processJob.StartProcessingAsync();

        // Pause
        result = await processJob.PauseRequestAsync();
        AssertState(result, "Pausing");

        result = await processJob.PauseCompleteAsync();
        AssertState(result, "Paused");

        // Resume
        result = await processJob.ResumeAsync();
        AssertState(result, "Processing");
    }

    [Fact]
    public async Task E40_Should_Handle_Stop()
    {
        var processJob = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001" });

        // Get to Processing state
        var result = await processJob.CreateAsync();
        result = await processJob.SetupAsync();
        result = await processJob.SetupCompleteAsync();
        result = await processJob.StartProcessingAsync();

        // Stop
        result = await processJob.StopAsync();
        AssertState(result, "Stopping");

        result = await processJob.StopCompleteAsync();
        AssertState(result, "Stopped");
        Assert.NotNull(processJob.EndTime);
    }

    [Fact]
    public async Task E40_Should_Handle_Abort_From_Processing()
    {
        var processJob = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001" });

        // Get to Processing state
        var result = await processJob.CreateAsync();
        result = await processJob.SetupAsync();
        result = await processJob.SetupCompleteAsync();
        result = await processJob.StartProcessingAsync();

        // Abort
        result = await processJob.AbortAsync();
        AssertState(result, "Aborting");

        result = await processJob.AbortCompleteAsync();
        AssertState(result, "Aborted");
        Assert.NotNull(processJob.EndTime);
    }

    [Fact]
    public async Task E40_Should_Handle_Abort_From_Queued()
    {
        var processJob = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001" });

        var result = await processJob.CreateAsync();
        AssertState(result, "Queued");

        result = await processJob.AbortAsync();
        AssertState(result, "Aborting");

        result = await processJob.AbortCompleteAsync();
        AssertState(result, "Aborted");
    }

    [Fact]
    public async Task E40_Should_Handle_Error_During_Processing()
    {
        var processJob = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001" });

        // Get to Processing state
        var result = await processJob.CreateAsync();
        result = await processJob.SetupAsync();
        result = await processJob.SetupCompleteAsync();
        result = await processJob.StartProcessingAsync();

        // Error
        result = await processJob.ErrorAsync("PROCESS_ERROR_001");
        AssertState(result, "Aborting");
        Assert.Equal("PROCESS_ERROR_001", processJob.ErrorCode);
    }

    [Fact]
    public async Task E40_Should_Handle_Setup_Failure()
    {
        var processJob = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001" });

        var result = await processJob.CreateAsync();
        result = await processJob.SetupAsync();

        result = await processJob.SetupFailedAsync();
        AssertState(result, "Queued");
    }

    [Fact]
    public async Task E40_Should_Handle_Pause_Failure()
    {
        var processJob = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001" });

        // Get to Processing state
        var result = await processJob.CreateAsync();
        result = await processJob.SetupAsync();
        result = await processJob.SetupCompleteAsync();
        result = await processJob.StartProcessingAsync();

        // Pause request
        result = await processJob.PauseRequestAsync();

        // Pause failed
        result = await processJob.PauseFailedAsync();
        AssertState(result, "Processing");
    }

    [Fact]
    public async Task E40_Should_Remove_Completed_Job()
    {
        var processJob = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001" });

        // Complete the job
        var result = await processJob.CreateAsync();
        result = await processJob.SetupAsync();
        result = await processJob.SetupCompleteAsync();
        result = await processJob.StartProcessingAsync();
        result = await processJob.ProcessingCompleteAsync();

        // Remove
        result = await processJob.RemoveAsync();
        AssertState(result, "NoState");
    }

    [Fact]
    public async Task E40_Should_Restart_Completed_Job()
    {
        var processJob = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001" });

        // Complete the job
        var result = await processJob.CreateAsync();
        result = await processJob.SetupAsync();
        result = await processJob.SetupCompleteAsync();
        result = await processJob.StartProcessingAsync();
        result = await processJob.ProcessingCompleteAsync();

        // Restart
        result = await processJob.RestartAsync();
        AssertState(result, "Queued");
    }

    [Fact]
    public async Task E40_Should_Restart_Stopped_Job()
    {
        var processJob = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001" });

        // Get to Stopped state
        var result = await processJob.CreateAsync();
        result = await processJob.SetupAsync();
        result = await processJob.SetupCompleteAsync();
        result = await processJob.StartProcessingAsync();
        result = await processJob.StopAsync();
        result = await processJob.StopCompleteAsync();

        // Restart
        result = await processJob.RestartAsync();
        AssertState(result, "Queued");
    }

    [Fact]
    public async Task E40_Should_Get_Processing_Jobs()
    {
        var pj1 = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001" });
        var pj2 = await _processJobMgr.CreateProcessJobAsync("PJ002", "RECIPE002", new List<string> { "MAT002" });

        // Start PJ001
        var result = await pj1.CreateAsync();
        result = await pj1.SetupAsync();
        result = await pj1.SetupCompleteAsync();
        result = await pj1.StartProcessingAsync();

        // Keep PJ002 in Queued
        result = await pj2.CreateAsync();

        var processingJobs = _processJobMgr.GetProcessingJobs();
        Assert.Single(processingJobs);
        Assert.Equal("PJ001", processingJobs.First().ProcessJobId);
    }

    [Fact]
    public async Task E40_Should_Get_All_ProcessJobs()
    {
        await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001" });
        await _processJobMgr.CreateProcessJobAsync("PJ002", "RECIPE002", new List<string> { "MAT002" });
        await _processJobMgr.CreateProcessJobAsync("PJ003", "RECIPE003", new List<string> { "MAT003" });

        var jobs = _processJobMgr.GetAllProcessJobs().ToList();
        Assert.Equal(3, jobs.Count);
    }

    [Fact]
    public async Task E40_Should_Have_Correct_MachineId()
    {
        var processJob = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001" });

        Assert.StartsWith("E40_PROCESSJOB_PJ001_", processJob.MachineId);
        Assert.Equal("E40_PROCESSJOB_MGR_EQ001", _processJobMgr.MachineId);
    }

    [Fact]
    public async Task E40_Should_Track_Multiple_Materials()
    {
        var materials = new List<string> { "MAT001", "MAT002", "MAT003", "MAT004" };
        var processJob = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", materials);

        Assert.Equal(4, processJob.MaterialIds.Count);
        Assert.Equal(materials, processJob.MaterialIds);
    }

    [Fact]
    public async Task E40_Should_Handle_Stop_From_Paused()
    {
        var processJob = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001" });

        // Get to Paused state
        var result = await processJob.CreateAsync();
        result = await processJob.SetupAsync();
        result = await processJob.SetupCompleteAsync();
        result = await processJob.StartProcessingAsync();
        result = await processJob.PauseRequestAsync();
        result = await processJob.PauseCompleteAsync();

        // Stop from Paused
        result = await processJob.StopAsync();
        AssertState(result, "Stopping");

        result = await processJob.StopCompleteAsync();
        AssertState(result, "Stopped");
    }

    [Fact]
    public async Task E40_Should_Handle_Abort_From_Paused()
    {
        var processJob = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001" });

        // Get to Paused state
        var result = await processJob.CreateAsync();
        result = await processJob.SetupAsync();
        result = await processJob.SetupCompleteAsync();
        result = await processJob.StartProcessingAsync();
        result = await processJob.PauseRequestAsync();
        result = await processJob.PauseCompleteAsync();

        // Abort from Paused
        result = await processJob.AbortAsync();
        AssertState(result, "Aborting");

        result = await processJob.AbortCompleteAsync();
        AssertState(result, "Aborted");
    }

    [Fact]
    public async Task E40_Two_ProcessJobs_Should_Not_Interfere()
    {
        // Test for race condition similar to E42
        var pj1 = await _processJobMgr.CreateProcessJobAsync("PJ001", "RECIPE001", new List<string> { "MAT001" });
        var pj2 = await _processJobMgr.CreateProcessJobAsync("PJ002", "RECIPE002", new List<string> { "MAT002" });

        // Create PJ001 only
        var result = await pj1.CreateAsync();
        AssertState(result, "Queued");

        // PJ001 should be Queued, PJ002 should still be NoState
        Assert.Contains("Queued", pj1.GetCurrentState());
        Assert.Contains("NoState", pj2.GetCurrentState());
    }
}
