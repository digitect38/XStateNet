using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using XStateNet;
using XStateNet.Semi;

namespace SemiStandard.Tests;

public class StateMachineIntegrationTests
{
    /// <summary>
    /// State machine coordinator for event-based communication
    /// </summary>
    public class StateMachineCoordinator
    {
        private readonly Dictionary<string, StateMachine> _machines = new();
        private readonly Dictionary<string, List<(string targetId, string eventName)>> _eventMappings = new();
        public List<string> EventLog { get; } = new();
        
        public void RegisterMachine(string id, StateMachine machine)
        {
            _machines[id] = machine;
        }
        
        public void MapEvent(string sourceMachine, string sourceState, 
                            string targetMachine, string targetEvent)
        {
            var key = $"{sourceMachine}:{sourceState}";
            if (!_eventMappings.ContainsKey(key))
            {
                _eventMappings[key] = new List<(string, string)>();
            }
            _eventMappings[key].Add((targetMachine, targetEvent));
        }
        
        public void OnStateChange(string machineId, string newState)
        {
            var key = $"{machineId}:{newState}";
            if (_eventMappings.TryGetValue(key, out var mappings))
            {
                foreach (var (targetId, eventName) in mappings)
                {
                    if (_machines.TryGetValue(targetId, out var targetMachine))
                    {
                        EventLog.Add($"{machineId}.{newState} -> {targetId}.{eventName}");
                        targetMachine.Send(eventName);
                    }
                }
            }
        }
        
        public int GetMappingCount()
        {
            int count = 0;
            foreach (var mapping in _eventMappings.Values)
            {
                count += mapping.Count;
            }
            return count;
        }
        
        public bool HasMapping(string sourceMachine, string sourceState)
        {
            return _eventMappings.ContainsKey($"{sourceMachine}:{sourceState}");
        }
    }
    
    [Fact]
    public async Task E90_E87_E94_Should_IntegrateCorrectly()
    {
        // Arrange
        var equipment = new SemiEquipmentController("EQ001");
        var jobManager = new E94ControlJobManager();
        var handoff = new E84HandoffController("LP1");
        
        // Act - Equipment initialization
        equipment.SendEvent("goRemote");
        equipment.SendEvent("initialized");
        
        // E84 handoff sequence
        handoff.SetCS0(true);
        handoff.SetValid(true);
        await Task.Delay(100);
        
        // Register carrier
        var carrier = await equipment.ProcessCarrierArrival("CAR001", "LP1");
        
        // Create and start control job
        var controlJob = jobManager.CreateControlJob("JOB001", 
            new List<string> { "CAR001" }, "RECIPE001");
        controlJob.Select();
        controlJob.Start();
        
        // Assert
        carrier.Should().BeTrue();
        equipment.GetCurrentState().Should().Contain("remote");
        controlJob.GetCurrentState().Should().Contain("executing");
        handoff.GetCurrentState().Should().NotContain("idle");
    }
    
    [Fact]
    public async Task SubstrateProcessing_Should_UpdateAllStateMachines()
    {
        // Arrange
        var equipment = new SemiEquipmentController("EQ001");
        var jobManager = new E94ControlJobManager();
        
        equipment.SendEvent("goRemote");
        await equipment.ProcessCarrierArrival("CAR001", "LP1");
        
        var controlJob = jobManager.CreateControlJob("JOB001", 
            new List<string> { "CAR001" }, "RECIPE001");
        controlJob.Select();
        controlJob.Start();
        controlJob.MaterialIn("CAR001");
        
        // Act - Process substrates
        var processedSubstrates = new List<string>();
        for (int slot = 1; slot <= 3; slot++)
        {
            var substrateid = $"CAR001_{slot:D2}";
            var substrate = equipment.SubstrateTracking.GetSubstrate(substrateid);
            
            if (substrate != null)
            {
                substrate.StateMachine.Send("SELECT_FOR_PROCESS");
                await Task.Delay(50);
                
                equipment.SubstrateTracking.UpdateLocation(substrateid, "PM1", SubstrateLocationType.ProcessModule);
                equipment.SubstrateTracking.StartProcessing(substrateid, "RECIPE001");
                controlJob.ProcessStart();
                
                await Task.Delay(100);
                
                equipment.SubstrateTracking.CompleteProcessing(substrateid, true);
                controlJob.MaterialProcessed(substrateid);
                processedSubstrates.Add(substrateid);
                
                substrate.StateMachine.Send("PLACED_IN_CARRIER");
                equipment.SubstrateTracking.UpdateLocation(substrateid, "LP1", SubstrateLocationType.Carrier);
            }
        }
        
        controlJob.ProcessComplete();
        controlJob.MaterialOut("CAR001");
        
        // Assert
        processedSubstrates.Should().HaveCount(3);
        controlJob.ProcessedSubstrates.Should().HaveCount(3);
        controlJob.GetCurrentState().Should().Contain("completed");
    }
    
    [Fact]
    public async Task E84Handoff_Should_CompleteTransferSequence()
    {
        // Arrange
        var handoff = new E84HandoffController("LP1");
        
        // Act - Complete E84 handoff sequence
        handoff.SetCS0(true);  // Carrier detected
        await Task.Delay(50);
        
        handoff.SetValid(true); // Valid carrier
        await Task.Delay(50);
        
        handoff.SetTransferRequest(true); // Transfer request from AGV
        handoff.SetBusy(true);
        await Task.Delay(50);
        
        handoff.SetBusy(false);
        handoff.SetComplete(true);
        await Task.Delay(50);
        
        var completeState = handoff.GetCurrentState();
        
        handoff.SetComplete(false);
        handoff.SetValid(false);
        handoff.SetCS0(false);
        await Task.Delay(50);
        
        var finalState = handoff.GetCurrentState();
        
        // Assert
        handoff.LoadRequest.Should().BeFalse();
        handoff.UnloadRequest.Should().BeFalse();
        handoff.Ready.Should().BeFalse();
        completeState.Should().NotBeEmpty();
        finalState.Should().NotBeEmpty();
    }
    
    [Fact]
    public void StateMachineCoordinator_Should_MapEvents()
    {
        // Arrange
        var coordinator = new StateMachineCoordinator();
        
        // Act
        coordinator.MapEvent("carrier_CAR001", "WaitingForHost", "e84_LP1", "CS_0_ON");
        coordinator.MapEvent("substrate_001", "ReadyToProcess", "job_JOB001", "PROCESS_START");
        coordinator.MapEvent("job_JOB001", "completed", "carrier_CAR001", "CARRIER_REMOVED");
        coordinator.MapEvent("semi-equipment", "offline", "job_JOB001", "ABORT");
        
        // Assert
        coordinator.GetMappingCount().Should().Be(4);
        coordinator.HasMapping("carrier_CAR001", "WaitingForHost").Should().BeTrue();
        coordinator.HasMapping("job_JOB001", "completed").Should().BeTrue();
    }
    
    [Fact]
    public async Task StateMachineCoordinator_Should_TriggerMappedEvents()
    {
        // Arrange
        var coordinator = new StateMachineCoordinator();
        
        var machine1 = CreateTestMachine("machine1");
        var machine2 = CreateTestMachine("machine2");
        
        coordinator.RegisterMachine("machine1", machine1);
        coordinator.RegisterMachine("machine2", machine2);
        
        coordinator.MapEvent("machine1", "active", "machine2", "TRIGGER");
        
        // Act
        coordinator.OnStateChange("machine1", "active");
        await Task.Delay(100);
        
        // Assert
        coordinator.EventLog.Should().Contain("machine1.active -> machine2.TRIGGER");
    }
    
    [Fact]
    public async Task MultipleCarriers_Should_ProcessConcurrently()
    {
        // Arrange
        var equipment = new SemiEquipmentController("EQ001");
        var jobManager = new E94ControlJobManager();
        
        equipment.SendEvent("goRemote");
        
        // Register multiple carriers
        await equipment.ProcessCarrierArrival("CAR001", "LP1");
        await equipment.ProcessCarrierArrival("CAR002", "LP2");
        
        // Act - Create jobs for both carriers
        var job1 = jobManager.CreateControlJob("JOB001", 
            new List<string> { "CAR001" }, "RECIPE001");
        var job2 = jobManager.CreateControlJob("JOB002", 
            new List<string> { "CAR002" }, "RECIPE002");
        
        job1.Select();
        job1.Start();
        job2.Select();
        job2.Start();
        
        // Assert
        job1.GetCurrentState().Should().Contain("executing");
        job2.GetCurrentState().Should().Contain("executing");
        jobManager.GetAllJobs().Should().HaveCount(2);
    }
    
    [Fact]
    public async Task ControlJob_Should_HandlePauseResume()
    {
        // Arrange
        var jobManager = new E94ControlJobManager();
        var job = jobManager.CreateControlJob("JOB001", 
            new List<string> { "CAR001" }, "RECIPE001");
        
        // Act
        job.Select();
        job.Start();
        await Task.Delay(50);
        
        var executingState = job.GetCurrentState();
        
        job.Pause();
        await Task.Delay(50);
        
        var pausedState = job.GetCurrentState();
        
        job.Resume();
        await Task.Delay(50);
        
        var resumedState = job.GetCurrentState();
        
        // Assert
        executingState.Should().Contain("executing");
        pausedState.Should().Contain("paused");
        resumedState.Should().Contain("executing");
    }
    
    [Fact]
    public async Task Equipment_Should_HandleStateTransitions()
    {
        // Arrange
        var equipment = new SemiEquipmentController("EQ001");
        
        // Act & Assert - Test state transitions
        equipment.GetCurrentState().Should().Contain("offline");
        
        equipment.SendEvent("goLocal");
        await Task.Delay(50);
        equipment.GetCurrentState().Should().Contain("local");
        
        equipment.SendEvent("goRemote");
        await Task.Delay(50);
        equipment.GetCurrentState().Should().Contain("remote");
        
        equipment.SendEvent("initialized");
        await Task.Delay(50);
        equipment.GetCurrentState().Should().Contain("idle");
    }
    
    [Fact]
    public void JobManager_Should_DeleteJobs()
    {
        // Arrange
        var jobManager = new E94ControlJobManager();
        var job = jobManager.CreateControlJob("JOB001", 
            new List<string> { "CAR001" }, "RECIPE001");
        
        // Act
        var jobExists = jobManager.GetControlJob("JOB001");
        var deleted = jobManager.DeleteControlJob("JOB001");
        var jobAfterDelete = jobManager.GetControlJob("JOB001");
        
        // Assert
        jobExists.Should().NotBeNull();
        deleted.Should().BeTrue();
        jobAfterDelete.Should().BeNull();
    }
    
    private StateMachine CreateTestMachine(string id)
    {
        var json = @"{
            'id': 'testMachine',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'TRIGGER': 'active'
                    }
                },
                'active': {
                    'on': {
                        'RESET': 'idle'
                    }
                }
            }
        }";
        
        var machine = StateMachine.CreateFromScript(json);
        machine.Start();
        return machine;
    }
}