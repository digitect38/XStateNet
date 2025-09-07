using System;
using System.Threading.Tasks;
using XStateNet.Semi;

namespace XStateNet.Examples;

/// <summary>
/// Example showing how SEMI standard state machines communicate with each other
/// </summary>
public class SemiIntegrationExample
{
    /// <summary>
    /// Demonstrates communication between E90, E87, and E94 state machines
    /// </summary>
    public static async Task RunIntegratedExample()
    {
        Console.WriteLine("=== SEMI Standards Integration Example ===\n");
        
        // Create the main equipment controller
        var equipment = new SemiEquipmentController("EQ001");
        
        // Create carrier management
        var carrierMgmt = equipment.CarrierManagement;
        
        // Create substrate tracking
        var substratTracking = equipment.SubstrateTracking;
        
        // Create control job manager
        var jobManager = new E94ControlJobManager();
        
        // Create E84 handoff controllers for load ports
        var handoffLP1 = new E84HandoffController("LP1");
        var handoffLP2 = new E84HandoffController("LP2");
        
        Console.WriteLine("Step 1: Equipment going online");
        equipment.SendEvent("goRemote");
        equipment.SendEvent("initialized");
        
        Console.WriteLine($"Equipment State: {equipment.GetCurrentState()}\n");
        
        // Simulate carrier arrival at load port 1
        Console.WriteLine("Step 2: Carrier arriving at Load Port 1");
        
        // E84 handoff sequence
        handoffLP1.SetCS0(true);  // Carrier detected
        handoffLP1.SetValid(true); // Valid carrier
        await Task.Delay(100);
        
        // Register carrier in E87
        var carrier = await equipment.ProcessCarrierArrival("CAR001", "LP1");
        Console.WriteLine($"Carrier State: {carrier ? "Registered" : "Failed"}\n");
        
        // Create control job for the carrier
        Console.WriteLine("Step 3: Creating Control Job");
        var controlJob = jobManager.CreateControlJob("JOB001", 
            new List<string> { "CAR001" }, 
            "RECIPE001");
        
        controlJob.Select();
        Console.WriteLine($"Control Job State: {controlJob.GetCurrentState()}\n");
        
        // Start processing
        Console.WriteLine("Step 4: Starting Control Job");
        controlJob.Start();
        controlJob.MaterialIn("CAR001");
        
        // Process substrates
        Console.WriteLine("Step 5: Processing Substrates");
        for (int slot = 1; slot <= 3; slot++)
        {
            var substrateid = $"CAR001_{slot:D2}";
            var substrate = substratTracking.GetSubstrate(substrateid);
            
            if (substrate != null)
            {
                Console.WriteLine($"  Processing substrate {substrateid}");
                
                // Move substrate through states
                substrate.StateMachine.Send("SELECT_FOR_PROCESS");
                await Task.Delay(100);
                
                // Simulate moving to process module
                substratTracking.UpdateLocation(substrateid, "PM1", SubstrateLocationType.ProcessModule);
                
                // Start processing
                substratTracking.StartProcessing(substrateid, "RECIPE001");
                controlJob.ProcessStart();
                
                await Task.Delay(500); // Simulate processing time
                
                // Complete processing
                substratTracking.CompleteProcessing(substrateid, true);
                controlJob.MaterialProcessed(substrateid);
                
                // Return to carrier
                substrate.StateMachine.Send("PLACED_IN_CARRIER");
                substratTracking.UpdateLocation(substrateid, "LP1", SubstrateLocationType.Carrier);
                
                Console.WriteLine($"  Substrate {substrateid} complete: {substrate.GetCurrentState()}");
            }
        }
        
        // Complete control job
        Console.WriteLine("\nStep 6: Completing Control Job");
        controlJob.ProcessComplete();
        controlJob.MaterialOut("CAR001");
        Console.WriteLine($"Control Job State: {controlJob.GetCurrentState()}");
        
        // E84 handoff sequence for carrier removal
        Console.WriteLine("\nStep 7: Carrier Removal via E84");
        handoffLP1.SetTransferRequest(true);
        handoffLP1.SetBusy(true);
        await Task.Delay(100);
        handoffLP1.SetBusy(false);
        handoffLP1.SetComplete(true);
        await Task.Delay(100);
        handoffLP1.SetComplete(false);
        handoffLP1.SetValid(false);
        handoffLP1.SetCS0(false);
        
        Console.WriteLine($"E84 Handoff State: {handoffLP1.GetCurrentState()}");
        
        Console.WriteLine("\n=== Integration Example Complete ===");
    }
    
    /// <summary>
    /// Example of state machine coordination using events
    /// </summary>
    public class StateMachineCoordinator
    {
        private readonly Dictionary<string, StateMachine> _machines = new();
        private readonly Dictionary<string, List<(string targetId, string eventName)>> _eventMappings = new();
        
        /// <summary>
        /// Register a state machine with the coordinator
        /// </summary>
        public void RegisterMachine(string id, StateMachine machine)
        {
            _machines[id] = machine;
            
            // Hook into state changes to trigger coordinated events
            // In real implementation, you'd hook into XStateNet's state change events
        }
        
        /// <summary>
        /// Map an event from one machine to trigger events in other machines
        /// </summary>
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
        
        /// <summary>
        /// Handle state change and trigger mapped events
        /// </summary>
        public void OnStateChange(string machineId, string newState)
        {
            var key = $"{machineId}:{newState}";
            if (_eventMappings.TryGetValue(key, out var mappings))
            {
                foreach (var (targetId, eventName) in mappings)
                {
                    if (_machines.TryGetValue(targetId, out var targetMachine))
                    {
                        Console.WriteLine($"Coordinator: {machineId}.{newState} -> {targetId}.{eventName}");
                        targetMachine.Send(eventName);
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Example showing how to set up coordinated SEMI state machines
    /// </summary>
    public static void SetupCoordinatedSystem()
    {
        var coordinator = new StateMachineCoordinator();
        
        // Set up event mappings between state machines
        
        // When carrier arrives (E87), start E84 handoff
        coordinator.MapEvent("carrier_CAR001", "WaitingForHost", 
                            "e84_LP1", "CS_0_ON");
        
        // When substrate is ready (E90), notify control job (E94)
        coordinator.MapEvent("substrate_001", "ReadyToProcess", 
                            "job_JOB001", "PROCESS_START");
        
        // When control job completes (E94), release carrier (E87)
        coordinator.MapEvent("job_JOB001", "completed", 
                            "carrier_CAR001", "CARRIER_REMOVED");
        
        // When equipment goes offline, abort all jobs
        coordinator.MapEvent("semi-equipment", "offline", 
                            "job_JOB001", "ABORT");
        
        Console.WriteLine("Coordinated system configured with event mappings");
    }
}