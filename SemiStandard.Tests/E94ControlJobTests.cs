using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using XStateNet.Semi;

namespace SemiStandard.Tests;

public class E94ControlJobTests
{
    private readonly E94ControlJobManager _jobManager;
    
    public E94ControlJobTests()
    {
        _jobManager = new E94ControlJobManager();
    }
    
    [Fact]
    public void CreateControlJob_Should_InitializeInQueuedState()
    {
        // Arrange
        var jobId = "JOB001";
        var carrierIds = new List<string> { "CAR001", "CAR002" };
        var recipeId = "RECIPE001";
        
        // Act
        var job = _jobManager.CreateControlJob(jobId, carrierIds, recipeId);
        
        // Assert
        job.Should().NotBeNull();
        job.JobId.Should().Be(jobId);
        job.CarrierIds.Should().BeEquivalentTo(carrierIds);
        job.RecipeId.Should().Be(recipeId);
        job.GetCurrentState().Should().Contain("queued");
    }
    
    [Fact]
    public void SelectJob_Should_TransitionToSelected()
    {
        // Arrange
        var job = _jobManager.CreateControlJob("JOB001", new List<string> { "CAR001" });
        
        // Act
        job.Select();
        
        // Assert
        job.GetCurrentState().Should().Contain("selected");
    }
    
    [Fact]
    public void StartJob_Should_TransitionToExecuting()
    {
        // Arrange
        var job = _jobManager.CreateControlJob("JOB001", new List<string> { "CAR001" });
        job.Select();
        
        // Act
        job.Start();
        
        // Assert
        job.GetCurrentState().Should().Contain("executing");
        job.StartedTime.Should().NotBeNull();
    }
    
    [Fact]
    public void PauseAndResumeJob_Should_HandleCorrectly()
    {
        // Arrange
        var job = _jobManager.CreateControlJob("JOB001", new List<string> { "CAR001" });
        job.Select();
        job.Start();
        job.ProcessStart();
        
        // Act
        job.Pause();
        var pausedState = job.GetCurrentState();
        
        job.Resume();
        var resumedState = job.GetCurrentState();
        
        // Assert
        pausedState.Should().Contain("paused");
        resumedState.Should().Contain("executing");
    }
    
    [Fact]
    public void AbortJob_Should_TransitionToAborted()
    {
        // Arrange
        var job = _jobManager.CreateControlJob("JOB001", new List<string> { "CAR001" });
        job.Select();
        job.Start();
        
        // Act
        job.Abort();
        
        // Assert
        // Need to handle async state transition
        Task.Delay(100).Wait();
        job.GetCurrentState().Should().ContainAny("aborting", "aborted");
    }
    
    [Fact]
    public void ProcessComplete_Should_SetCompletedTime()
    {
        // Arrange
        var job = _jobManager.CreateControlJob("JOB001", new List<string> { "CAR001" });
        job.Select();
        job.Start();
        job.ProcessStart();
        
        // Act
        job.ProcessComplete();
        
        // Assert
        job.CompletedTime.Should().NotBeNull();
    }
    
    [Fact]
    public void MaterialFlow_Should_TrackCorrectly()
    {
        // Arrange
        var job = _jobManager.CreateControlJob("JOB001", new List<string> { "CAR001" });
        job.Select();
        job.Start();
        
        // Act
        job.MaterialIn("CAR001");
        job.ProcessStart();
        job.MaterialProcessed("SUB001");
        job.MaterialProcessed("SUB002");
        job.MaterialOut("CAR001");
        
        // Assert
        job.ProcessedSubstrates.Should().HaveCount(2);
        job.ProcessedSubstrates.Should().Contain("SUB001");
        job.ProcessedSubstrates.Should().Contain("SUB002");
    }
    
    [Fact]
    public void DeleteJob_Should_RemoveFromManager()
    {
        // Arrange
        var job = _jobManager.CreateControlJob("JOB001", new List<string> { "CAR001" });
        
        // Act
        var deleted = _jobManager.DeleteControlJob("JOB001");
        var retrievedJob = _jobManager.GetControlJob("JOB001");
        
        // Assert
        deleted.Should().BeTrue();
        retrievedJob.Should().BeNull();
    }
    
    [Fact]
    public void GetAllJobs_Should_ReturnActiveJobs()
    {
        // Arrange
        _jobManager.CreateControlJob("JOB001", new List<string> { "CAR001" });
        _jobManager.CreateControlJob("JOB002", new List<string> { "CAR002" });
        _jobManager.CreateControlJob("JOB003", new List<string> { "CAR003" });
        _jobManager.DeleteControlJob("JOB002");
        
        // Act
        var allJobs = _jobManager.GetAllJobs();
        
        // Assert
        allJobs.Should().HaveCount(2);
        allJobs.Select(j => j.JobId).Should().Contain("JOB001");
        allJobs.Select(j => j.JobId).Should().Contain("JOB003");
        allJobs.Select(j => j.JobId).Should().NotContain("JOB002");
    }
    
    [Theory]
    [InlineData("queued", "SELECT", "selected")]
    [InlineData("selected", "START", "executing")]
    [InlineData("selected", "DELETE", "noJob")]
    public void StateTransitions_Should_FollowE94Spec(string initialState, string event_, string expectedState)
    {
        // Arrange
        var job = _jobManager.CreateControlJob("JOB001", new List<string> { "CAR001" });
        
        // Setup initial state if needed
        if (initialState == "selected")
        {
            job.Select();
        }
        
        // Act
        switch (event_)
        {
            case "SELECT":
                job.Select();
                break;
            case "START":
                job.Start();
                break;
            case "DELETE":
                job.Delete();
                break;
        }
        
        // Assert
        job.GetCurrentState().Should().Contain(expectedState);
    }
    
    [Fact]
    public async Task ParallelJobExecution_Should_HandleConcurrency()
    {
        // Arrange
        var tasks = new Task[5];
        
        // Act
        for (int i = 0; i < 5; i++)
        {
            int index = i;
            tasks[i] = Task.Run(() =>
            {
                var jobId = $"JOB{index:D3}";
                var job = _jobManager.CreateControlJob(jobId, 
                    new List<string> { $"CAR{index:D3}" });
                
                job.Select();
                job.Start();
                job.ProcessStart();
                Task.Delay(10).Wait();
                job.ProcessComplete();
            });
        }
        
        await Task.WhenAll(tasks);
        
        // Assert
        var allJobs = _jobManager.GetAllJobs();
        allJobs.Should().HaveCount(5);
        allJobs.All(j => j.CompletedTime != null).Should().BeTrue();
    }
}