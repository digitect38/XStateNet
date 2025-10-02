using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using XStateNet.Orchestration;
using XStateNet.Semi.Standards;
using static SemiStandard.Tests.StateMachineTestHelpers;

namespace SemiStandard.Tests;

public class E94ControlJobMachineTests : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E94ControlJobManager _controlJobMgr;

    public E94ControlJobMachineTests()
    {
        var config = new OrchestratorConfig { EnableLogging = true, PoolSize = 4, EnableMetrics = false };
        _orchestrator = new EventBusOrchestrator(config);
        _controlJobMgr = new E94ControlJobManager("EQ001", _orchestrator);
    }

    public void Dispose() => _orchestrator?.Dispose();

    [Fact]
    public async Task E94_Should_Create_ControlJob()
    {
        var controlJob = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");

        Assert.NotNull(controlJob);
        Assert.Equal("CJ001", controlJob.JobId);
        Assert.Equal("RECIPE001", controlJob.RecipeId);
        Assert.Contains("noJob", controlJob.GetCurrentState());
    }

    [Fact]
    public async Task E94_Should_Complete_Full_ControlJob_Lifecycle()
    {
        var controlJob = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");

        // Create
        var result = await controlJob.CreateAsync();
        AssertState(result, "queued");

        // Select
        result = await controlJob.SelectAsync();
        AssertState(result, "selected");

        // Start execution
        result = await controlJob.StartExecutionAsync();
        AssertState(result, "executing");
        Assert.NotNull(controlJob.StartedTime);

        // Process start
        result = await controlJob.ProcessStartAsync();
        AssertState(result, "active");

        // Material flow
        result = await controlJob.MaterialInAsync();
        AssertState(result, "materialAtSource");

        result = await controlJob.MaterialOutAsync();
        AssertState(result, "materialAtDestination");

        result = await controlJob.MaterialProcessedAsync();
        AssertState(result, "materialComplete");

        // Process complete
        result = await controlJob.ProcessCompleteAsync();
        AssertState(result, "completed");
        Assert.NotNull(controlJob.CompletedTime);
        Assert.True(controlJob.CompletedTime >= controlJob.StartedTime);
    }

    [Fact]
    public async Task E94_Should_Handle_Select_Deselect()
    {
        var controlJob = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");

        var result = await controlJob.CreateAsync();
        AssertState(result, "queued");

        // Select
        result = await controlJob.SelectAsync();
        AssertState(result, "selected");

        // Deselect
        result = await controlJob.DeselectAsync();
        AssertState(result, "queued");
    }

    [Fact]
    public async Task E94_Should_Handle_Pause_Resume_During_Execution()
    {
        var controlJob = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");

        // Get to executing state
        await controlJob.CreateAsync();
        await controlJob.SelectAsync();
        await controlJob.StartExecutionAsync();
        await controlJob.ProcessStartAsync();

        // Pause from executing
        var result = await controlJob.PauseAsync();
        AssertState(result, "paused");

        // Resume
        result = await controlJob.ResumeAsync();
        AssertState(result, "executing");
    }

    [Fact]
    public async Task E94_Should_Handle_Stop_From_Execution()
    {
        var controlJob = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");

        // Get to executing state
        await controlJob.CreateAsync();
        await controlJob.SelectAsync();
        await controlJob.StartExecutionAsync();
        await controlJob.ProcessStartAsync();

        // Stop
        var result = await controlJob.StopAsync();
        AssertState(result, "stopping");

        result = await controlJob.StoppedAsync();
        AssertState(result, "stopped");
        Assert.NotNull(controlJob.CompletedTime);
    }

    [Fact]
    public async Task E94_Should_Handle_Abort_From_Selected()
    {
        var controlJob = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");

        await controlJob.CreateAsync();
        await controlJob.SelectAsync();

        // Abort from selected
        var result = await controlJob.AbortAsync();
        AssertState(result, "aborting");

        result = await controlJob.AbortedAsync();
        AssertState(result, "aborted");
        Assert.NotNull(controlJob.CompletedTime);
    }

    [Fact]
    public async Task E94_Should_Handle_Abort_From_Execution()
    {
        var controlJob = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");

        // Get to executing state
        await controlJob.CreateAsync();
        await controlJob.SelectAsync();
        await controlJob.StartExecutionAsync();
        await controlJob.ProcessStartAsync();

        // Abort from executing
        var result = await controlJob.AbortAsync();
        AssertState(result, "aborting");

        result = await controlJob.AbortedAsync();
        AssertState(result, "aborted");
    }

    [Fact]
    public async Task E94_Should_Delete_ControlJob_From_Queued()
    {
        var controlJob = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");

        await controlJob.CreateAsync();

        // Delete from queued
        var result = await controlJob.DeleteAsync();
        AssertState(result, "noJob");
    }

    [Fact]
    public async Task E94_Should_Delete_ControlJob_After_Completion()
    {
        var controlJob = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");

        // Complete the job
        await controlJob.CreateAsync();
        await controlJob.SelectAsync();
        await controlJob.StartExecutionAsync();
        await controlJob.ProcessStartAsync();
        await controlJob.MaterialInAsync();
        await controlJob.MaterialOutAsync();
        await controlJob.MaterialProcessedAsync();
        await controlJob.ProcessCompleteAsync();

        // Delete after completion
        var result = await controlJob.DeleteAsync();
        AssertState(result, "noJob");
    }

    [Fact]
    public async Task E94_Should_Get_Selected_ControlJob()
    {
        var cj1 = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");
        var cj2 = await _controlJobMgr.CreateControlJobAsync("CJ002", new List<string> { "CARRIER002" }, "RECIPE002");

        // Create and select CJ001
        await cj1.CreateAsync();
        await cj1.SelectAsync();

        // Create CJ002 but don't select
        await cj2.CreateAsync();

        var selected = _controlJobMgr.GetSelectedControlJob();
        Assert.NotNull(selected);
        Assert.Equal("CJ001", selected.JobId);
    }

    [Fact]
    public async Task E94_Should_Get_Executing_Jobs()
    {
        var cj1 = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");
        var cj2 = await _controlJobMgr.CreateControlJobAsync("CJ002", new List<string> { "CARRIER002" }, "RECIPE002");

        // Start CJ001 executing
        await cj1.CreateAsync();
        await cj1.SelectAsync();
        await cj1.StartExecutionAsync();

        // Keep CJ002 in queued
        await cj2.CreateAsync();

        var executing = _controlJobMgr.GetExecutingJobs().ToList();
        Assert.Single(executing);
        Assert.Equal("CJ001", executing[0].JobId);
    }

    [Fact]
    public async Task E94_Should_Get_All_ControlJobs()
    {
        await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");
        await _controlJobMgr.CreateControlJobAsync("CJ002", new List<string> { "CARRIER002" }, "RECIPE002");
        await _controlJobMgr.CreateControlJobAsync("CJ003", new List<string> { "CARRIER003" }, "RECIPE003");

        var jobs = _controlJobMgr.GetAllControlJobs().ToList();
        Assert.Equal(3, jobs.Count);
    }

    [Fact]
    public async Task E94_Should_Have_Correct_MachineId()
    {
        var controlJob = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");

        Assert.StartsWith("E94_CONTROLJOB_CJ001_", controlJob.MachineId);
        Assert.Equal("E94_CONTROLJOB_MGR_EQ001", _controlJobMgr.MachineId);
    }

    [Fact]
    public async Task E94_Should_Track_Multiple_Carriers()
    {
        var carriers = new List<string> { "CARRIER001", "CARRIER002", "CARRIER003" };
        var controlJob = await _controlJobMgr.CreateControlJobAsync("CJ001", carriers, "RECIPE001");

        Assert.Equal(3, controlJob.CarrierIds.Count);
        Assert.Equal(carriers, controlJob.CarrierIds);
    }

    [Fact]
    public async Task E94_Should_Track_ProcessedSubstrates()
    {
        var controlJob = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");

        controlJob.AddProcessedSubstrate("SUBSTRATE001");
        controlJob.AddProcessedSubstrate("SUBSTRATE002");
        controlJob.AddProcessedSubstrate("SUBSTRATE003");

        Assert.Equal(3, controlJob.ProcessedSubstrates.Count);
        Assert.Contains("SUBSTRATE001", controlJob.ProcessedSubstrates);
        Assert.Contains("SUBSTRATE002", controlJob.ProcessedSubstrates);
        Assert.Contains("SUBSTRATE003", controlJob.ProcessedSubstrates);
    }

    [Fact]
    public async Task E94_Should_Handle_Pause_Resume_In_Paused_State()
    {
        var controlJob = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");

        // Get to paused state
        await controlJob.CreateAsync();
        await controlJob.SelectAsync();
        await controlJob.StartExecutionAsync();
        await controlJob.PauseAsync();

        // Stop from paused
        var result = await controlJob.StopAsync();
        AssertState(result, "stopping");

        result = await controlJob.StoppedAsync();
        AssertState(result, "stopped");
    }

    [Fact]
    public async Task E94_Should_Handle_Abort_From_Paused()
    {
        var controlJob = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");

        // Get to paused state
        await controlJob.CreateAsync();
        await controlJob.SelectAsync();
        await controlJob.StartExecutionAsync();
        await controlJob.PauseAsync();

        // Abort from paused
        var result = await controlJob.AbortAsync();
        AssertState(result, "aborting");

        result = await controlJob.AbortedAsync();
        AssertState(result, "aborted");
    }

    [Fact]
    public async Task E94_Two_ControlJobs_Should_Not_Interfere()
    {
        // Test for race condition similar to E42
        var cj1 = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");
        var cj2 = await _controlJobMgr.CreateControlJobAsync("CJ002", new List<string> { "CARRIER002" }, "RECIPE002");

        // Create CJ001 only
        var result = await cj1.CreateAsync();
        AssertState(result, "queued");

        // CJ002 should still be noJob
        Assert.Contains("noJob", cj2.GetCurrentState());
    }

    [Fact]
    public async Task E94_Should_Delete_ControlJob_From_Stopped()
    {
        var controlJob = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");

        // Get to stopped state
        await controlJob.CreateAsync();
        await controlJob.SelectAsync();
        await controlJob.StartExecutionAsync();
        await controlJob.StopAsync();
        await controlJob.StoppedAsync();

        // Delete from stopped
        var result = await controlJob.DeleteAsync();
        AssertState(result, "noJob");
    }

    [Fact]
    public async Task E94_Should_Delete_ControlJob_From_Aborted()
    {
        var controlJob = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");

        // Get to aborted state
        await controlJob.CreateAsync();
        await controlJob.SelectAsync();
        await controlJob.AbortAsync();
        await controlJob.AbortedAsync();

        // Delete from aborted
        var result = await controlJob.DeleteAsync();
        AssertState(result, "noJob");
    }

    [Fact]
    public async Task E94_Should_Track_CreatedTime()
    {
        var before = DateTime.UtcNow;
        var controlJob = await _controlJobMgr.CreateControlJobAsync("CJ001", new List<string> { "CARRIER001" }, "RECIPE001");
        var after = DateTime.UtcNow;

        Assert.True(controlJob.CreatedTime >= before);
        Assert.True(controlJob.CreatedTime <= after);
    }
}
