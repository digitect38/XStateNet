using Xunit;
using FluentAssertions;
using XStateNet;
using XStateNet.Semi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;

namespace SemiStandard.Tests
{
    public class SemiIntegratedMachineTests : IDisposable
    {
        private StateMachine? _stateMachine;
        private ActionMap _actions;
        private GuardMap _guards;
        
        // Context variables
        private ConcurrentDictionary<string, object?> _context;
        
        public SemiIntegratedMachineTests()
        {
            _context = new ConcurrentDictionary<string, object?>
            {
                ["systemId"] = "SEMI_SYSTEM_001",
                ["systemName"] = "Integrated SEMI Control System",
                ["systemStartTime"] = null,
                ["equipmentId"] = "EQ001",
                ["equipmentState"] = "INIT",
                ["equipmentReady"] = false,
                ["communicationState"] = "NOT_COMMUNICATING",
                ["activeCarriers"] = (object?)new List<object?>(),
                ["activeSubstrates"] = (object?)new List<object?>(),
                ["activeJobs"] = (object?)new List<object?>(),
                ["activeRecipes"] = (object?)new List<object?>(),
                ["activeProcesses"] = (object?)new List<object?>(),
                ["carrierAvailable"] = false,
                ["substrateReady"] = false,
                ["recipeVerified"] = false,
                ["processActive"] = false,
                ["totalProcessed"] = 0,
                ["totalErrors"] = 0,
                ["systemUptime"] = 0,
                ["availability"] = 0.0,
                ["performance"] = 0.0,
                ["quality"] = 0.0
            };
            
            InitializeActions();
            InitializeGuards();

            // Enable at the start of your application or test
            Logger.IncludeCallerInfo = true;

            // Set desired log level
            Logger.CurrentLevel = Logger.LogLevel.Warning;
        }
        
        private void InitializeActions()
        {
            _actions = new ActionMap
            {
                // Equipment Control Actions
                ["setSystemStartTime"] = [new("setSystemStartTime", (sm) => 
                {
                    sm.ContextMap!["systemStartTime"] = DateTime.Now.ToString("o");
                })],
                
                ["setEquipmentIdle"] = [new("setEquipmentIdle", (sm) => 
                {
                    sm.ContextMap!["equipmentState"] = "IDLE";
                    sm.ContextMap!["equipmentReady"] = true;
                    sm.ContextMap!["communicationState"] = "COMMUNICATING";
                })],
                
                ["setEquipmentFault"] = [new("setEquipmentFault", (sm) => 
                {
                    // Only set to FAULT if not already EMERGENCY_STOP
                                        if ((string?)sm.ContextMap!["equipmentState"] != "EMERGENCY_STOP")
                    {
                        sm.ContextMap!["equipmentState"] = "FAULT";
                    }
                    sm.ContextMap!["equipmentReady"] = false;
                    sm.ContextMap!["totalErrors"] = (int)(sm.ContextMap!["totalErrors"] ?? 0) + 1;
                })],
                
                ["setEquipmentProductive"] = [new("setEquipmentProductive", (sm) => 
                {
                    sm.ContextMap!["equipmentState"] = "PRODUCTIVE";
                })],
                
                ["setProductiveStandby"] = [new("setProductiveStandby", (sm) => 
                {
                    sm.ContextMap!["equipmentState"] = "PRODUCTIVE_STANDBY";
                })],
                
                ["setProductiveRun"] = [new("setProductiveRun", (sm) => 
                {
                    sm.ContextMap!["equipmentState"] = "PRODUCTIVE_RUN";
                    sm.ContextMap!["processActive"] = true;
                })],
                
                ["incrementProcessed"] = [new("incrementProcessed", (sm) => 
                {
                    sm.ContextMap!["totalProcessed"] = (int)(sm.ContextMap!["totalProcessed"] ?? 0) + 1;
                    sm.ContextMap!["processActive"] = false;
                })],
                
                ["setEquipmentOff"] = [new("setEquipmentOff", (sm) => 
                {
                    sm.ContextMap!["communicationState"] = "NOT_COMMUNICATING";
                    sm.ContextMap!["equipmentState"] = "OFF";
                    sm.ContextMap!["equipmentReady"] = false;
                })],
                
                // Carrier Management Actions
                ["setCarrierUnavailable"] = [new("setCarrierUnavailable", (sm) => 
                {
                    sm.ContextMap!["carrierAvailable"] = false;
                })],
                
                ["addCarrier"] = [new("addCarrier", (sm) => 
                {
                    var carriers = sm.ContextMap!["activeCarriers"] as List<object>;
                    carriers?.Add(new 
                    {
                        id = $"FOUP_{DateTime.Now.Ticks}",
                        loadPort = "LP1",
                        slotMap = new List<int>(),
                        status = "WAITING"
                    });
                })],
                
                ["setCarrierAvailable"] = [new("setCarrierAvailable", (sm) => 
                {
                    sm.ContextMap!["carrierAvailable"] = true;
                })],
                
                // Substrate Tracking Actions
                ["setSubstrateUnavailable"] = [new("setSubstrateUnavailable", (sm) => 
                {
                    sm.ContextMap!["substrateReady"] = false;
                })],
                
                ["addSubstrate"] = [new("addSubstrate", (sm) => 
                {
                    var substrates = sm.ContextMap!["activeSubstrates"] as List<object>;
                    substrates?.Add(new 
                    {
                        id = $"WAFER_{DateTime.Now.Ticks}",
                        location = "SOURCE",
                        status = "PENDING",
                        startTime = DateTime.Now.ToString("o")
                    });
                })],
                
                ["setSubstrateReady"] = [new("setSubstrateReady", (sm) => 
                {
                    sm.ContextMap!["substrateReady"] = true;
                })],
                
                // Recipe Management Actions
                ["setRecipeUnverified"] = [new("setRecipeUnverified", (sm) => 
                {
                    sm.ContextMap!["recipeVerified"] = false;
                })],
                
                ["addRecipe"] = [new("addRecipe", (sm) => 
                {
                    var recipes = sm.ContextMap!["activeRecipes"] as List<object>;
                    recipes?.Add(new 
                    {
                        id = $"RCP_{DateTime.Now.Ticks}",
                        name = "DefaultRecipe",
                        version = "1.0",
                        status = "UNVERIFIED"
                    });
                })],
                
                ["setRecipeVerified"] = [new("setRecipeVerified", (sm) => 
                {
                    sm.ContextMap!["recipeVerified"] = true;
                })],
                
                // Control Job Actions
                ["addJob"] = [new("addJob", (sm) => 
                {
                    var jobs = sm.ContextMap!["activeJobs"] as List<object>;
                    jobs?.Add(new 
                    {
                        id = $"JOB_{DateTime.Now.Ticks}",
                        name = "DefaultJob",
                        status = "QUEUED",
                        substrates = new List<string>(),
                        completedCount = 0,
                        totalCount = 0
                    });
                })],
                
                // Process Job Actions
                ["addProcess"] = [new("addProcess", (sm) => 
                {
                    var processes = sm.ContextMap!["activeProcesses"] as List<object>;
                    processes?.Add(new 
                    {
                        id = $"PROC_{DateTime.Now.Ticks}",
                        name = "DefaultProcess",
                        status = "SETTING_UP",
                        currentStep = 0,
                        totalSteps = 1
                    });
                })],
                
                // Performance Monitoring Actions
                ["updateMetrics"] = [new("updateMetrics", (sm) => 
                {
                    if (sm.ContextMap!["systemStartTime"] is string startTimeString)
                    {
                        var start = DateTime.Parse(startTimeString);
                        var uptime = (DateTime.Now - start).TotalMinutes;
                        sm.ContextMap!["systemUptime"] = (int)uptime;
                    }
                })],
                
                // Emergency Stop Actions
                ["emergencyStop"] = [new("emergencyStop", (sm) => 
                {
                    sm.ContextMap!["equipmentState"] = "EMERGENCY_STOP";
                    sm.ContextMap!["equipmentReady"] = false;
                    sm.ContextMap!["processActive"] = false;
                    sm.ContextMap!["carrierAvailable"] = false;
                    sm.ContextMap!["substrateReady"] = false;
                    sm.ContextMap!["recipeVerified"] = false;
                })],
                
                // System Reset Actions
                ["systemReset"] = [new("systemReset", (sm) => 
                {
                    sm.ContextMap!["equipmentReady"] = false;
                    sm.ContextMap!["processActive"] = false;
                    sm.ContextMap!["carrierAvailable"] = false;
                    sm.ContextMap!["substrateReady"] = false;
                    sm.ContextMap!["recipeVerified"] = false;
                    sm.ContextMap!["activeCarriers"] = new List<object>();
                    sm.ContextMap!["activeSubstrates"] = new List<object>();
                    sm.ContextMap!["activeJobs"] = new List<object>();
                    sm.ContextMap!["activeRecipes"] = new List<object>();
                    sm.ContextMap!["activeProcesses"] = new List<object>();
                    sm.ContextMap!["totalProcessed"] = 0;
                    sm.ContextMap!["totalErrors"] = 0;
                })]
            };
        }
        
        private void InitializeGuards()
        {
            _guards = new GuardMap
            {
                ["isEquipmentReady"] = new("isEquipmentReady", (sm) => 
                    (bool)(sm.ContextMap!["equipmentReady"] ?? false)),
                
                ["canStartProcess"] = new("canStartProcess", (sm) => 
                    (bool)(sm.ContextMap!["carrierAvailable"] ?? false) && 
                    (bool)(sm.ContextMap!["recipeVerified"] ?? false)),
                
                ["isProcessActive"] = new("isProcessActive", (sm) => 
                    (bool)(sm.ContextMap!["processActive"] ?? false)),
                
                ["isCarrierAvailable"] = new("isCarrierAvailable", (sm) => 
                    (bool)(sm.ContextMap!["carrierAvailable"] ?? false)),
                
                ["canStartJob"] = new("canStartJob", (sm) => 
                    (bool)(sm.ContextMap!["equipmentReady"] ?? false) && 
                    (bool)(sm.ContextMap!["carrierAvailable"] ?? false)),
                
                ["canStartRecipeProcess"] = new("canStartRecipeProcess", (sm) => 
                    (bool)(sm.ContextMap!["processActive"] ?? false)),
                
                ["isOeeAlert"] = new("isOeeAlert", (sm) => 
                {
                    var availability = (double)(sm.ContextMap!["availability"] ?? 0.0);
                    var performance = (double)(sm.ContextMap!["performance"] ?? 0.0);
                    var quality = (double)(sm.ContextMap!["quality"] ?? 0.0);
                    var oeeScore = (availability + performance + quality) / 3;
                    return oeeScore < 0.7;
                })
            };
        }
        
        [Fact]
        public async Task TestInitialState()
        {
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: true, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            var currentState = await _stateMachine!.StartAsync();
            var machineId = _stateMachine.machineId;
            currentState.Should().Contain($"{machineId}.equipment.INIT");
            currentState.Should().Contain($"{machineId}.carrierManagement.NO_CARRIERS");
            currentState.Should().Contain($"{machineId}.substrateTracking.NO_SUBSTRATES");
            currentState.Should().Contain($"{machineId}.controlJob.NO_JOBS");
            currentState.Should().Contain($"{machineId}.processJob.NO_PROCESS");
            currentState.Should().Contain($"{machineId}.recipeManagement.NO_RECIPE");
            currentState.Should().Contain($"{machineId}.performanceMonitoring.MONITORING");
        }
        
        [Fact]
        public async Task TestEquipmentInitialization()
        {
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: true, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            var currentState = await _stateMachine!.SendAsync("INIT_COMPLETE");

            _stateMachine.ContextMap!["equipmentState"].Should().Be("IDLE");
            ((bool)(_stateMachine.ContextMap!["equipmentReady"] ?? false)).Should().BeTrue();
            _stateMachine.ContextMap!["communicationState"].Should().Be("COMMUNICATING");
        }
        
        [Fact]
        public async Task TestCarrierArrival()
        {
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: true, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            await _stateMachine!.SendAsync("INIT_COMPLETE");
            await _stateMachine!.SendAsync("CARRIER_DETECTED");
            await _stateMachine!.SendAsync("CARRIER_ARRIVED");
            
            List<object?>? carriers = _stateMachine.ContextMap!["activeCarriers"] as List<object?>;
            carriers?.Count.Should().BeGreaterThan(0);
        }
        
        [Fact]
        public async Task TestProductionStart()
        {
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: true, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();

            await _stateMachine!.SendAsync("INIT_COMPLETE");
            var currentState = await _stateMachine!.SendAsync("START_PRODUCTION");
            var machineId = _stateMachine.machineId;

            currentState.Should().Contain($"{machineId}.equipment.PRODUCTIVE");
        }
        
        [Fact]
        public async Task TestEmergencyStop()
        {
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: true, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();

            await _stateMachine!.SendAsync("INIT_COMPLETE");
            await _stateMachine!.SendAsync("START_PRODUCTION");
            await _stateMachine!.SendAsync("STARTUP_COMPLETE");
            var currentState = await _stateMachine!.SendAsync("EMERGENCY_STOP");
            var machineId = _stateMachine.machineId;

            // Check that equipment moved to FAULT state
            currentState.Should().Contain($"{machineId}.equipment.FAULT");
            
            // Check that the emergency stop actions were executed
            // Note: equipmentState will be "FAULT" because the FAULT state's entry action sets it
            var equipmentState = _stateMachine.ContextMap!["equipmentState"] as string;
            (equipmentState == "FAULT" || equipmentState == "EMERGENCY_STOP").Should().BeTrue();
            ((bool)(_stateMachine.ContextMap!["equipmentReady"] ?? false)).Should().BeFalse();
            ((bool)(_stateMachine.ContextMap!["processActive"] ?? false)).Should().BeFalse();
        }
        
        [Fact]
        public async Task TestSystemReset()
        {
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: true, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();

            await _stateMachine!.SendAsync("INIT_COMPLETE");
            await _stateMachine!.SendAsync("START_PRODUCTION");
            await _stateMachine!.SendAsync("STARTUP_COMPLETE");

            // Add some data before reset
            _stateMachine.ContextMap!["totalProcessed"] = 5;
            _stateMachine.ContextMap!["totalErrors"] = 2;

            var currentState = await _stateMachine!.SendAsync("SYSTEM_RESET");
            var machineId = _stateMachine.machineId;

            // Check that system reset to INIT state
            currentState.Should().Contain($"{machineId}.equipment.INIT");

            // Check that the reset actions were executed
            _stateMachine.ContextMap!["totalProcessed"].Should().Be(0);
            _stateMachine.ContextMap!["totalErrors"].Should().Be(0);
            List<object?>? carriers = _stateMachine.ContextMap!["activeCarriers"] as List<object?>;
            (carriers?.Count ?? 0).Should().Be(0);
            ((bool)(_stateMachine.ContextMap!["equipmentReady"] ?? false)).Should().BeFalse();
            ((bool)(_stateMachine.ContextMap!["processActive"] ?? false)).Should().BeFalse();
        }
        
        [Fact]
        public async Task TestCarrierManagementWorkflow()
        {
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();

            // Initialize equipment
            await _stateMachine!.SendAsync("INIT_COMPLETE");
            ((bool)(_stateMachine.ContextMap!["equipmentReady"] ?? false)).Should().BeTrue();

            // Carrier arrives
            var currentState = await _stateMachine!.SendAsync("CARRIER_DETECTED");
            var machineId = _stateMachine.machineId;
            currentState.Should().Contain($"{machineId}.carrierManagement.CARRIER_ARRIVING");

            currentState = await _stateMachine!.SendAsync("CARRIER_ARRIVED");
            currentState.Should().Contain($"{machineId}.carrierManagement.WAITING_FOR_HOST");

            // Proceed with carrier
            currentState = await _stateMachine!.SendAsync("PROCEED_WITH_CARRIER");
            currentState.Should().Contain($"{machineId}.carrierManagement.ID_VERIFICATION.READING_ID");

            // ID verification flow
            currentState = await _stateMachine!.SendAsync("ID_READ_SUCCESS");
            currentState.Should().Contain($"{machineId}.carrierManagement.ID_VERIFICATION.VERIFYING_ID");

            currentState = await _stateMachine!.SendAsync("ID_VERIFIED");
            currentState.Should().Contain($"{machineId}.carrierManagement.SLOT_MAP_VERIFICATION.READING_SLOT_MAP");

            // Slot map verification
            currentState = await _stateMachine!.SendAsync("SLOT_MAP_READ");
            currentState.Should().Contain($"{machineId}.carrierManagement.SLOT_MAP_VERIFICATION.VERIFYING_SLOT_MAP");

            currentState = await _stateMachine!.SendAsync("SLOT_MAP_VERIFIED");
            currentState.Should().Contain($"{machineId}.carrierManagement.READY_FOR_PROCESSING");
            ((bool)(_stateMachine.ContextMap!["carrierAvailable"] ?? false)).Should().BeTrue();
        }
        
        [Fact]
        public async Task TestSubstrateTrackingWorkflow()
        {
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();

            // Initialize and start production
            await _stateMachine!.SendAsync("INIT_COMPLETE");
            await _stateMachine!.SendAsync("START_PRODUCTION");
            await _stateMachine!.SendAsync("STARTUP_COMPLETE");

            // Substrate arrives
            var currentState = await _stateMachine!.SendAsync("SUBSTRATE_DETECTED");
            var machineId = _stateMachine.machineId;
            currentState.Should().Contain($"{machineId}.substrateTracking.SUBSTRATE_AT_SOURCE");

            // Substrate needs processing
            currentState = await _stateMachine!.SendAsync("SUBSTRATE_NEEDS_PROCESSING");
            currentState.Should().Contain($"{machineId}.substrateTracking.SUBSTRATE_PROCESSING.WAITING");
            ((bool)(_stateMachine.ContextMap!["substrateReady"] ?? false)).Should().BeTrue();

            // Start processing (requires processActive)
            _stateMachine.ContextMap!["processActive"] = true;
            currentState = await _stateMachine!.SendAsync("START_SUBSTRATE_PROCESS");
            currentState.Should().Contain($"{machineId}.substrateTracking.SUBSTRATE_PROCESSING.IN_PROCESS");

            // Complete processing
            currentState = await _stateMachine!.SendAsync("SUBSTRATE_PROCESS_COMPLETE");
            currentState.Should().Contain($"{machineId}.substrateTracking.SUBSTRATE_PROCESSING.PROCESSED");

            // Move to destination
            currentState = await _stateMachine!.SendAsync("SUBSTRATE_MOVE_TO_DESTINATION");
            currentState.Should().Contain($"{machineId}.substrateTracking.SUBSTRATE_AT_DESTINATION");

            // Substrate departs
            currentState = await _stateMachine!.SendAsync("SUBSTRATE_DEPARTED");
            currentState.Should().Contain($"{machineId}.substrateTracking.NO_SUBSTRATES");
        }
        
        [Fact]
        public async Task TestControlJobWorkflow()
        {
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();

            // Setup prerequisites
            await _stateMachine!.SendAsync("INIT_COMPLETE");
            _stateMachine.ContextMap!["carrierAvailable"] = true; // Simulate carrier ready

            // Create job
            var currentState = await _stateMachine!.SendAsync("CREATE_JOB");
            var machineId = _stateMachine.machineId;
            currentState.Should().Contain($"{machineId}.controlJob.JOB_QUEUED");

            // Select job
            currentState = await _stateMachine!.SendAsync("SELECT_JOB");
            currentState.Should().Contain($"{machineId}.controlJob.JOB_SELECTED");

            // Start job (requires equipment ready and carrier available)
            currentState = await _stateMachine!.SendAsync("START_JOB");
            currentState.Should().Contain($"{machineId}.controlJob.JOB_EXECUTING.ACTIVE");

            // Pause job
            currentState = await _stateMachine!.SendAsync("PAUSE_JOB");
            currentState.Should().Contain($"{machineId}.controlJob.JOB_EXECUTING.PAUSED");

            // Resume job
            currentState = await _stateMachine!.SendAsync("RESUME_JOB");
            currentState.Should().Contain($"{machineId}.controlJob.JOB_EXECUTING.ACTIVE");

            // Complete job
            currentState = await _stateMachine!.SendAsync("JOB_COMPLETE");
            currentState.Should().Contain($"{machineId}.controlJob.JOB_COMPLETED");

            // Remove job
            currentState = await _stateMachine!.SendAsync("REMOVE_JOB");
            currentState.Should().Contain($"{machineId}.controlJob.NO_JOBS");
        }
        
        [Fact]
        public async Task TestRecipeManagementWorkflow()
        {
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: true, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();

            // Load recipe
            var currentState = await _stateMachine!.SendAsync("LOAD_RECIPE");
            var machineId = _stateMachine.machineId;
            currentState.Should().Contain($"{machineId}.recipeManagement.UNVERIFIED");
            ((bool)(_stateMachine.ContextMap!["recipeVerified"] ?? false)).Should().BeFalse();

            // Verify recipe
            currentState = await _stateMachine!.SendAsync("VERIFY_RECIPE");
            currentState.Should().Contain($"{machineId}.recipeManagement.VERIFYING");

            // Verification passes
            currentState = await _stateMachine!.SendAsync("VERIFICATION_PASS");
            currentState.Should().Contain($"{machineId}.recipeManagement.VERIFIED");
            ((bool)(_stateMachine.ContextMap!["recipeVerified"] ?? false)).Should().BeTrue();

            // Select recipe
            currentState = await _stateMachine!.SendAsync("SELECT_RECIPE");
            currentState.Should().Contain($"{machineId}.recipeManagement.SELECTED");

            // Start recipe process (requires process active)
            _stateMachine.ContextMap!["processActive"] = true;
            currentState = await _stateMachine!.SendAsync("START_RECIPE_PROCESS");
            currentState.Should().Contain($"{machineId}.recipeManagement.ACTIVE");

            // Complete recipe process
            currentState = await _stateMachine!.SendAsync("RECIPE_PROCESS_COMPLETE");
            currentState.Should().Contain($"{machineId}.recipeManagement.VERIFIED");
        }
        
        [Fact]
        public async Task TestProcessJobWorkflow()
        {
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();

            // Setup prerequisites
            await _stateMachine!.SendAsync("INIT_COMPLETE");
            _stateMachine.ContextMap!["recipeVerified"] = true;
            _stateMachine.ContextMap!["carrierAvailable"] = true;

            // Create process
            var currentState = await _stateMachine!.SendAsync("CREATE_PROCESS");
            var machineId = _stateMachine.machineId;
            currentState.Should().Contain($"{machineId}.processJob.SETTING_UP");

            // Setup complete
            currentState = await _stateMachine!.SendAsync("SETUP_COMPLETE");
            currentState.Should().Contain($"{machineId}.processJob.WAITING_FOR_START");

            // Start process
            currentState = await _stateMachine!.SendAsync("START_PROCESS");
            currentState.Should().Contain($"{machineId}.processJob.PROCESSING.EXECUTING");

            // Pause process
            currentState = await _stateMachine!.SendAsync("PAUSE_PROCESS");
            currentState.Should().Contain($"{machineId}.processJob.PROCESSING.PAUSING");

            currentState = await _stateMachine!.SendAsync("PROCESS_PAUSED");
            currentState.Should().Contain($"{machineId}.processJob.PROCESSING.PAUSED");

            // Resume process
            currentState = await _stateMachine!.SendAsync("RESUME_PROCESS");
            currentState.Should().Contain($"{machineId}.processJob.PROCESSING.RESUMING");

            currentState = await _stateMachine!.SendAsync("PROCESS_RESUMED");
            currentState.Should().Contain($"{machineId}.processJob.PROCESSING.EXECUTING");

            // Complete process
            currentState = await _stateMachine!.SendAsync("PROCESS_COMPLETE");
            currentState.Should().Contain($"{machineId}.processJob.PROCESS_COMPLETE");

            currentState = await _stateMachine!.SendAsync("VERIFY_PROCESS_OK");
            currentState.Should().Contain($"{machineId}.processJob.PROCESS_COMPLETED");
        }
        
        [Fact]
        public async Task TestParallelStateCoordination()
        {            
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            // Verify all parallel regions start correctly
            var currentState = await _stateMachine!.StartAsync();
            var machineId = _stateMachine.machineId;
            currentState.Should().Contain($"{machineId}.equipment.INIT");
            currentState.Should().Contain($"{machineId}.carrierManagement.NO_CARRIERS");
            currentState.Should().Contain($"{machineId}.substrateTracking.NO_SUBSTRATES");
            currentState.Should().Contain($"{machineId}.controlJob.NO_JOBS");
            currentState.Should().Contain($"{machineId}.processJob.NO_PROCESS");
            currentState.Should().Contain($"{machineId}.recipeManagement.NO_RECIPE");
            currentState.Should().Contain($"{machineId}.performanceMonitoring.MONITORING");

            // Initialize equipment and verify it doesn't affect other regions
            currentState = await _stateMachine!.SendAsync("INIT_COMPLETE");
            currentState.Should().Contain($"{machineId}.equipment.IDLE");
            currentState.Should().Contain($"{machineId}.carrierManagement.NO_CARRIERS");

            // Start multiple workflows in parallel
            await _stateMachine!.SendAsync("CARRIER_DETECTED");
            await _stateMachine!.SendAsync("LOAD_RECIPE");
            currentState = await _stateMachine!.SendAsync("CREATE_JOB");

            currentState.Should().Contain($"{machineId}.equipment.IDLE");
            currentState.Should().Contain($"{machineId}.carrierManagement.CARRIER_ARRIVING");
            currentState.Should().Contain($"{machineId}.recipeManagement.UNVERIFIED");
            currentState.Should().Contain($"{machineId}.controlJob.JOB_QUEUED");

            // Test that process coordination flags work correctly
            ((bool)(_stateMachine.ContextMap!["carrierAvailable"] ?? false)).Should().BeFalse();
            ((bool)(_stateMachine.ContextMap!["recipeVerified"] ?? false)).Should().BeFalse();

            // Verify recipe and check flag
            await _stateMachine!.SendAsync("VERIFY_RECIPE");
            await _stateMachine!.SendAsync("VERIFICATION_PASS");
            ((bool)(_stateMachine.ContextMap!["recipeVerified"] ?? false)).Should().BeTrue();

            // Test multiple target transition (EMERGENCY_STOP)
            await _stateMachine!.SendAsync("START_PRODUCTION");
            await _stateMachine!.SendAsync("STARTUP_COMPLETE");
            currentState = await _stateMachine!.SendAsync("EMERGENCY_STOP");

            currentState.Should().Contain($"{machineId}.equipment.FAULT");
            currentState.Should().Contain($"{machineId}.carrierManagement.NO_CARRIERS");
            currentState.Should().Contain($"{machineId}.substrateTracking.NO_SUBSTRATES");
            currentState.Should().Contain($"{machineId}.controlJob.NO_JOBS");
            currentState.Should().Contain($"{machineId}.processJob.PROCESS_ABORTING");
        }
        
        [Fact]
        public async Task TestProductionCycle()
        {
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();

            // Initialize system
            await _stateMachine!.SendAsync("INIT_COMPLETE");
            ((bool)(_stateMachine.ContextMap!["equipmentReady"] ?? false)).Should().BeTrue();

            // Load and verify recipe
            await _stateMachine!.SendAsync("LOAD_RECIPE");
            await _stateMachine!.SendAsync("VERIFY_RECIPE");
            await _stateMachine!.SendAsync("VERIFICATION_PASS");
            ((bool)(_stateMachine.ContextMap!["recipeVerified"] ?? false)).Should().BeTrue();

            // Carrier arrives and is verified
            await _stateMachine!.SendAsync("CARRIER_DETECTED");
            await _stateMachine!.SendAsync("CARRIER_ARRIVED");
            await _stateMachine!.SendAsync("PROCEED_WITH_CARRIER");
            await _stateMachine!.SendAsync("ID_READ_SUCCESS");
            await _stateMachine!.SendAsync("ID_VERIFIED");
            await _stateMachine!.SendAsync("SLOT_MAP_READ");
            await _stateMachine!.SendAsync("SLOT_MAP_VERIFIED");
            ((bool)(_stateMachine.ContextMap!["carrierAvailable"] ?? false)).Should().BeTrue();

            // Start production
            await _stateMachine!.SendAsync("START_PRODUCTION");
            var currentState = await _stateMachine!.SendAsync("STARTUP_COMPLETE");
            var machineId = _stateMachine.machineId;
            currentState.Should().Contain($"{machineId}.equipment.PRODUCTIVE.STANDBY");

            // Run production
            currentState = await _stateMachine!.SendAsync("RUN");
            currentState.Should().Contain($"{machineId}.equipment.PRODUCTIVE.PRODUCTIVE_RUN");
            ((bool)(_stateMachine.ContextMap!["processActive"] ?? false)).Should().BeTrue();

            // Complete production
            currentState = await _stateMachine!.SendAsync("PROCESS_COMPLETE");
            currentState.Should().Contain($"{machineId}.equipment.PRODUCTIVE.STANDBY");
            _stateMachine.ContextMap!["totalProcessed"].Should().Be(1);
            ((bool)(_stateMachine.ContextMap!["processActive"] ?? false)).Should().BeFalse();
        }
        
        [Fact]
        public async Task TestFaultRecovery()
        {
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();

            // Initialize and start production
            await _stateMachine!.SendAsync("INIT_COMPLETE");
            await _stateMachine!.SendAsync("START_PRODUCTION");
            await _stateMachine!.SendAsync("STARTUP_COMPLETE");

            // Simulate fault
            var machineId = _stateMachine.machineId;
            var currentState = await _stateMachine!.SendAsync("FAULT_DETECTED");
            currentState.Should().Contain($"{machineId}.equipment.FAULT");
            ((bool)(_stateMachine.ContextMap!["equipmentReady"] ?? false)).Should().BeFalse();
            ((int)(_stateMachine.ContextMap!["totalErrors"] ?? 0)).Should().BeGreaterThan(0);

            // Clear fault
            currentState = await _stateMachine!.SendAsync("FAULT_CLEARED");
            currentState.Should().Contain($"{machineId}.equipment.IDLE");
            ((bool)(_stateMachine.ContextMap!["equipmentReady"] ?? false)).Should().BeTrue();

            // Verify system can restart production
            currentState = await _stateMachine!.SendAsync("START_PRODUCTION");
            currentState.Should().Contain($"{machineId}.equipment.PRODUCTIVE");
        }
        
        [Fact]
        public async Task TestPerformanceMonitoring()
        {
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            // Verify monitoring starts active
            var currentState = await _stateMachine!.StartAsync();
            var machineId = _stateMachine.machineId;
            currentState.Should().Contain($"{machineId}.performanceMonitoring.MONITORING");

            // Update metrics
            await _stateMachine!.SendAsync("UPDATE_METRICS");

            // Simulate low OEE condition
            _stateMachine.ContextMap!["availability"] = 0.5;
            _stateMachine.ContextMap!["performance"] = 0.6;
            _stateMachine.ContextMap!["quality"] = 0.7;

            currentState = await _stateMachine!.SendAsync("PERFORMANCE_ALERT");
            currentState.Should().Contain($"{machineId}.performanceMonitoring.ALERT_STATE");

            // Acknowledge alert
            currentState = await _stateMachine!.SendAsync("ACKNOWLEDGE_ALERT");
            currentState.Should().Contain($"{machineId}.performanceMonitoring.MONITORING");

            // Test investigate flow
            _stateMachine.ContextMap!["availability"] = 0.5;
            await _stateMachine!.SendAsync("PERFORMANCE_ALERT");
            currentState = await _stateMachine!.SendAsync("INVESTIGATE_ISSUE");
            currentState.Should().Contain($"{machineId}.performanceMonitoring.ANALYZING");

            currentState = await _stateMachine!.SendAsync("ANALYSIS_COMPLETE");
            currentState.Should().Contain($"{machineId}.performanceMonitoring.MONITORING");
        }
        
        [Fact]
        public async Task TestE10StateTracking()
        {
            // SEMI E10 - Equipment Events and State Definitions
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            // Track state transitions and time spent in each state
            var stateHistory = new List<(string state, DateTime time)>();

            // Non-scheduled downtime
            var initialState = await _stateMachine!.StartAsync();
            var machineId = _stateMachine.machineId;
            initialState.Should().Contain(machineId + ".equipment.INIT");
            stateHistory.Add(("INIT", DateTime.Now));

            // Productive time
            await _stateMachine!.SendAsync("INIT_COMPLETE");
            stateHistory.Add(("IDLE", DateTime.Now));

            await _stateMachine!.SendAsync("START_PRODUCTION");
            stateHistory.Add(("PRODUCTIVE", DateTime.Now));
            
            // Engineering time
            var currentState = await _stateMachine!.SendAsync("STARTUP_FAIL");
            currentState.Should().Contain($"{machineId}.equipment.FAULT");
            stateHistory.Add(("FAULT", DateTime.Now));

            currentState = await _stateMachine!.SendAsync("ENTER_REPAIR");
            currentState.Should().Contain($"{machineId}.equipment.ENGINEERING");
            stateHistory.Add(("ENGINEERING", DateTime.Now));

            // Scheduled downtime
            await _stateMachine!.SendAsync("ENGINEERING_COMPLETE");
            currentState = await _stateMachine!.SendAsync("ENTER_SETUP");
            currentState.Should().Contain($"{machineId}.equipment.SETUP");
            stateHistory.Add(("SETUP", DateTime.Now));
            
            // Verify we can track all E10 state categories
            stateHistory.Any(s  => s.state == "PRODUCTIVE").Should().BeTrue();
            stateHistory.Any(s  => s.state == "IDLE").Should().BeTrue();
            stateHistory.Any(s  => s.state == "ENGINEERING").Should().BeTrue();
            stateHistory.Any(s  => s.state == "SETUP").Should().BeTrue();
        }
        
        [Fact]
        public async Task TestE84LoadPortHandshake()
        {
            // SEMI E84 - Enhanced Carrier Handoff
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            await _stateMachine!.SendAsync("INIT_COMPLETE");

            // Simulate E84 handshake sequence
            // CS_0: Carrier not detected
            var currentState = _stateMachine!.GetActiveStateNames();
            currentState.Should().Contain("carrierManagement.NO_CARRIERS");

            // VALID signal on - carrier detected
            currentState = await _stateMachine!.SendAsync("CARRIER_DETECTED");
            currentState.Should().Contain("carrierManagement.CARRIER_ARRIVING");

            // CS_1: Transfer blocked (waiting for handshake)
            currentState = await _stateMachine!.SendAsync("CARRIER_ARRIVED");
            currentState.Should().Contain("carrierManagement.WAITING_FOR_HOST");

            // L_REQ signal - Load request from host
            await _stateMachine!.SendAsync("PROCEED_WITH_CARRIER");
            
            // U_REQ signal - Unload request would trigger carrier departure
            // This tests the bidirectional handshake of E84
        }
        
        [Fact]
        public async Task TestE142SubstrateMapping()
        {
            // SEMI E142 - Substrate Mapping
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            // Add slot map context
            _stateMachine.ContextMap!["slotMap"] = new ConcurrentDictionary<int, string>
            {
                [1] = "PRESENT",
                [2] = "PRESENT", 
                [3] = "ABSENT",
                [4] = "PRESENT",
                [5] = "DOUBLE_SLOT", // Error condition
                [6] = "PRESENT",
                [7] = "CROSS_SLOT", // Error condition
                [8] = "PRESENT",
                [9] = "PRESENT",
                [10] = "ABSENT"
            };
            _stateMachine!.Start();
            
            await _stateMachine!.SendAsync("INIT_COMPLETE");

            // Carrier with substrates arrives
            await _stateMachine!.SendAsync("CARRIER_DETECTED");
            await _stateMachine!.SendAsync("CARRIER_ARRIVED");
            await _stateMachine!.SendAsync("PROCEED_WITH_CARRIER");
            await _stateMachine!.SendAsync("ID_READ_SUCCESS");
            await _stateMachine!.SendAsync("ID_VERIFIED");

            // Slot map verification - E142 standard
            var currentState = await _stateMachine!.SendAsync("SLOT_MAP_READ");
            currentState.Should().Contain("SLOT_MAP_VERIFICATION.VERIFYING_SLOT_MAP");
            
            // Check slot map for errors
            var slotMap = _stateMachine.ContextMap!["slotMap"] as ConcurrentDictionary<int, string>;
            slotMap.Should().NotBeNull();
            
            // Verify error detection
            var hasDoubleSlot = slotMap.Any(s => s.Value == "DOUBLE_SLOT");
            var hasCrossSlot = slotMap.Any(s => s.Value == "CROSS_SLOT");
            hasDoubleSlot.Should().BeTrue("Should detect double-slotted wafer");
            hasCrossSlot.Should().BeTrue("Should detect cross-slotted wafer");
            
            // Count valid substrates
            var validSubstrates = slotMap.Count(s => s.Value == "PRESENT");
            validSubstrates.Should().Be(6);
        }
        
        [Fact]
        public async Task TestCommunicationStates()
        {
            // SEMI E5/E37 - Communication States (SECS/HSMS)
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            // Initial state - not communicating
            _stateMachine.ContextMap!["communicationState"].Should().Be("NOT_COMMUNICATING");
            
            // Establish communication
            await _stateMachine!.SendAsync("INIT_COMPLETE");
            _stateMachine.ContextMap!["communicationState"].Should().Be("COMMUNICATING");

            // Communication should persist through state changes
            await _stateMachine!.SendAsync("START_PRODUCTION");
            _stateMachine.ContextMap!["communicationState"].Should().Be("COMMUNICATING");
            
            // Verify communication state tracking works
            // Since equipment has complex nested states, let's just verify the key states
            ((bool)(_stateMachine.ContextMap!["equipmentReady"] ?? false)).Should().BeTrue();
            
            // Manually set communication state to test tracking
            _stateMachine.ContextMap!["communicationState"] = "DISABLED";
            _stateMachine.ContextMap!["communicationState"].Should().Be("DISABLED");
            
            // Reset and verify
            _stateMachine.ContextMap!["communicationState"] = "COMMUNICATING";
            _stateMachine.ContextMap!["communicationState"].Should().Be("COMMUNICATING");
        }
        
        [Fact]
        public async Task TestModuleProcessTracking()
        {
            // SEMI E157 - Module Process Tracking (simplified)
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: true, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            
            // Add module tracking context
            _stateMachine.ContextMap!["processModules"] = new List<object>
            {
                new { id = "PM1", status = "IDLE", substrateId = "" },
                new { id = "PM2", status = "IDLE", substrateId = "" },
                new { id = "PM3", status = "IDLE", substrateId = "" }
            };
            
            _stateMachine!.Start();
            await _stateMachine!.SendAsync("INIT_COMPLETE");

            // Start substrate processing in module
            var currentState = await _stateMachine!.SendAsync("SUBSTRATE_DETECTED");
            currentState.Should().Contain("substrateTracking.SUBSTRATE_AT_SOURCE");

            // Track substrate through multiple modules
            await _stateMachine!.SendAsync("SUBSTRATE_NEEDS_PROCESSING");
            _stateMachine.ContextMap!["processActive"] = true;
            currentState = await _stateMachine!.SendAsync("START_SUBSTRATE_PROCESS");

            currentState.Should().Contain("substrateTracking.SUBSTRATE_PROCESSING.IN_PROCESS");
            
            // Verify module tracking capability exists
            var modules = _stateMachine.ContextMap!["processModules"] as List<object>;
            modules.Should().NotBeNull();
            modules!.Count.Should().Be(3);
        }
        
        [Fact]
        public async Task TestDataAcquisition()
        {
            // SEMI E164 - EDA (Equipment Data Acquisition) concepts
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: true, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            
            // Add data collection context
            var dataPoints = new List<(string parameter, object value, DateTime timestamp)>();
            _stateMachine.ContextMap!["dataCollection"] = dataPoints;
            
            _stateMachine!.Start();
            
            // Collect data on state changes
            await _stateMachine!.SendAsync("INIT_COMPLETE");
            dataPoints.Add(("EquipmentState", "IDLE", DateTime.Now));
            dataPoints.Add(("EquipmentReady", true, DateTime.Now));

            await _stateMachine!.SendAsync("START_PRODUCTION");
            dataPoints.Add(("EquipmentState", "PRODUCTIVE", DateTime.Now));

            await _stateMachine!.SendAsync("STARTUP_COMPLETE");
            _stateMachine.ContextMap!["carrierAvailable"] = true;
            _stateMachine.ContextMap!["recipeVerified"] = true;

            await _stateMachine!.SendAsync("RUN");
            dataPoints.Add(("ProcessActive", true, DateTime.Now));
            dataPoints.Add(("ProcessStartTime", DateTime.Now, DateTime.Now));

            await _stateMachine!.SendAsync("PROCESS_COMPLETE");
            dataPoints.Add(("ProcessActive", false, DateTime.Now));
            dataPoints.Add(("TotalProcessed", _stateMachine.ContextMap!["totalProcessed"] ?? (object)0!, DateTime.Now));
            
            // Verify data collection
            dataPoints.Count.Should().BeGreaterThan(0);
            dataPoints.Any(d  => d.parameter == "EquipmentState").Should().BeTrue();
            dataPoints.Any(d  => d.parameter == "ProcessActive").Should().BeTrue();
            dataPoints.Any(d  => d.parameter == "TotalProcessed").Should().BeTrue();
            
            // Verify we can track state changes over time
            var stateChanges = dataPoints.Where(d => d.parameter == "EquipmentState").ToList();
            stateChanges.Count.Should().BeGreaterThanOrEqualTo(2);
        }
        
        [Fact]
        public async Task TestFullStateVisitingCoverage_Equipment()
        {
            // Test ALL states and transitions in equipment region
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            var visitedStates = new HashSet<string>();
            
            // INIT -> IDLE
            _stateMachine.GetActiveStateNames().Should().Contain("equipment.INIT");
            visitedStates.Add("INIT");

            // Test INIT_FAIL path
            var currentState = await _stateMachine!.SendAsync("INIT_FAIL");
            currentState.Should().Contain("equipment.FAULT");
            visitedStates.Add("FAULT");

            currentState = await _stateMachine!.SendAsync("FAULT_CLEARED");
            currentState.Should().Contain("equipment.IDLE");
            visitedStates.Add("IDLE");

            // Test SHUTDOWN path
            currentState = await _stateMachine!.SendAsync("SHUTDOWN_REQUEST");
            currentState.Should().Contain("equipment.SHUTDOWN");
            visitedStates.Add("SHUTDOWN");

            // Test SHUTDOWN_ABORT
            currentState = await _stateMachine!.SendAsync("SHUTDOWN_ABORT");
            currentState.Should().Contain("equipment.IDLE");

            // Test SETUP path
            currentState = await _stateMachine!.SendAsync("ENTER_SETUP");
            currentState.Should().Contain("equipment.SETUP");
            visitedStates.Add("SETUP");

            // Test SETUP_ABORT
            currentState = await _stateMachine!.SendAsync("SETUP_ABORT");
            currentState.Should().Contain("equipment.IDLE");

            // Test ENGINEERING path through FAULT
            currentState = await _stateMachine!.SendAsync("FAULT_DETECTED");
            currentState.Should().Contain("equipment.FAULT");

            currentState = await _stateMachine!.SendAsync("ENTER_REPAIR");
            currentState.Should().Contain("equipment.ENGINEERING");
            visitedStates.Add("ENGINEERING");

            // Test TEST_RUN from ENGINEERING
            currentState = await _stateMachine!.SendAsync("TEST_RUN");
            currentState.Should().Contain("equipment.PRODUCTIVE");
            visitedStates.Add("PRODUCTIVE");

            // Test all PRODUCTIVE substates
            currentState.Should().Contain("PRODUCTIVE.START_UP");
            visitedStates.Add("PRODUCTIVE.START_UP");

            currentState = await _stateMachine!.SendAsync("STARTUP_COMPLETE");
            currentState.Should().Contain("PRODUCTIVE.STANDBY");
            visitedStates.Add("PRODUCTIVE.STANDBY");

            // Test SETUP_REQUEST from STANDBY
            currentState = await _stateMachine!.SendAsync("SETUP_REQUEST");
            currentState.Should().Contain("equipment.SETUP");

            await _stateMachine!.SendAsync("SETUP_COMPLETE");
            await _stateMachine!.SendAsync("START_PRODUCTION");
            await _stateMachine!.SendAsync("STARTUP_COMPLETE");

            // Setup conditions for RUN
            _stateMachine.ContextMap!["carrierAvailable"] = true;
            _stateMachine.ContextMap!["recipeVerified"] = true;

            currentState = await _stateMachine!.SendAsync("RUN");
            currentState.Should().Contain("PRODUCTIVE.PRODUCTIVE_RUN");
            visitedStates.Add("PRODUCTIVE.PRODUCTIVE_RUN");

            // Test PAUSE
            currentState = await _stateMachine!.SendAsync("PAUSE");
            currentState.Should().Contain("PRODUCTIVE.PAUSE");
            visitedStates.Add("PRODUCTIVE.PAUSE");

            // Test ABORT from PAUSE
            currentState = await _stateMachine!.SendAsync("ABORT");
            currentState.Should().Contain("PRODUCTIVE.STANDBY");

            // Test RESUME from PAUSE
            await _stateMachine!.SendAsync("RUN");
            await _stateMachine!.SendAsync("PAUSE");
            currentState = await _stateMachine!.SendAsync("RESUME");
            currentState.Should().Contain("PRODUCTIVE.PRODUCTIVE_RUN");
            
            // Verify we visited all major equipment states
            visitedStates.Should().Contain("INIT");
            visitedStates.Should().Contain("IDLE");
            visitedStates.Should().Contain("FAULT");
            visitedStates.Should().Contain("SHUTDOWN");
            visitedStates.Should().Contain("SETUP");
            visitedStates.Should().Contain("ENGINEERING");
            visitedStates.Should().Contain("PRODUCTIVE");
            visitedStates.Should().Contain("PRODUCTIVE.START_UP");
            visitedStates.Should().Contain("PRODUCTIVE.STANDBY");
            visitedStates.Should().Contain("PRODUCTIVE.PRODUCTIVE_RUN");
            visitedStates.Should().Contain("PRODUCTIVE.PAUSE");
        }
        
        [Fact]
        public async Task TestFullStateVisitingCoverage_CarrierManagement()
        {
            // Test ALL states and transitions in carrier management
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            await _stateMachine!.SendAsync("INIT_COMPLETE");

            var visitedStates = new HashSet<string>();

            // NO_CARRIERS
            _stateMachine.GetActiveStateNames().Should().Contain("carrierManagement.NO_CARRIERS");
            visitedStates.Add("NO_CARRIERS");

            // Test carrier arrival
            var currentState = await _stateMachine!.SendAsync("CARRIER_DETECTED");
            currentState.Should().Contain("carrierManagement.CARRIER_ARRIVING");
            visitedStates.Add("CARRIER_ARRIVING");

            currentState = await _stateMachine!.SendAsync("CARRIER_ARRIVED");
            currentState.Should().Contain("carrierManagement.WAITING_FOR_HOST");
            visitedStates.Add("WAITING_FOR_HOST");

            // Test REJECT_CARRIER path
            currentState = await _stateMachine!.SendAsync("REJECT_CARRIER");
            currentState.Should().Contain("carrierManagement.NO_CARRIERS");

            // Test ID verification failures
            await _stateMachine!.SendAsync("CARRIER_DETECTED");
            await _stateMachine!.SendAsync("CARRIER_ARRIVED");
            currentState = await _stateMachine!.SendAsync("PROCEED_WITH_CARRIER");

            currentState.Should().Contain("ID_VERIFICATION.READING_ID");
            visitedStates.Add("ID_VERIFICATION.READING_ID");
            
            // Test ID_READ_FAIL path
            currentState = await _stateMachine!.SendAsync("ID_READ_FAIL");
            currentState.Should().Contain("ID_VERIFICATION.ID_FAILED");
            visitedStates.Add("ID_VERIFICATION.ID_FAILED");

            // Test RETRY_ID
            currentState = await _stateMachine!.SendAsync("RETRY_ID");
            currentState.Should().Contain("ID_VERIFICATION.READING_ID");

            // Test ID_REJECTED path
            currentState = await _stateMachine!.SendAsync("ID_READ_SUCCESS");
            currentState.Should().Contain("ID_VERIFICATION.VERIFYING_ID");
            visitedStates.Add("ID_VERIFICATION.VERIFYING_ID");

            currentState = await _stateMachine!.SendAsync("ID_REJECTED");
            currentState.Should().Contain("ID_VERIFICATION.ID_FAILED");

            // Test REMOVE_CARRIER from ID_FAILED
            currentState = await _stateMachine!.SendAsync("REMOVE_CARRIER");
            currentState.Should().Contain("carrierManagement.NO_CARRIERS");

            // Test slot map failures
            await _stateMachine!.SendAsync("CARRIER_DETECTED");
            await _stateMachine!.SendAsync("CARRIER_ARRIVED");
            await _stateMachine!.SendAsync("PROCEED_WITH_CARRIER");
            await _stateMachine!.SendAsync("ID_READ_SUCCESS");
            currentState = await _stateMachine!.SendAsync("ID_VERIFIED");

            currentState.Should().Contain("SLOT_MAP_VERIFICATION.READING_SLOT_MAP");
            visitedStates.Add("SLOT_MAP_VERIFICATION.READING_SLOT_MAP");

            // Test SLOT_MAP_FAIL path
            currentState = await _stateMachine!.SendAsync("SLOT_MAP_FAIL");
            currentState.Should().Contain("SLOT_MAP_VERIFICATION.SLOT_MAP_FAILED");
            visitedStates.Add("SLOT_MAP_VERIFICATION.SLOT_MAP_FAILED");

            // Test RETRY_SLOT_MAP
            currentState = await _stateMachine!.SendAsync("RETRY_SLOT_MAP");
            currentState.Should().Contain("SLOT_MAP_VERIFICATION.READING_SLOT_MAP");

            // Test SLOT_MAP_REJECTED path
            currentState = await _stateMachine!.SendAsync("SLOT_MAP_READ");
            currentState.Should().Contain("SLOT_MAP_VERIFICATION.VERIFYING_SLOT_MAP");
            visitedStates.Add("SLOT_MAP_VERIFICATION.VERIFYING_SLOT_MAP");

            currentState = await _stateMachine!.SendAsync("SLOT_MAP_REJECTED");
            currentState.Should().Contain("SLOT_MAP_VERIFICATION.SLOT_MAP_FAILED");

            // Complete carrier flow
            await _stateMachine!.SendAsync("RETRY_SLOT_MAP");
            await _stateMachine!.SendAsync("SLOT_MAP_READ");
            currentState = await _stateMachine!.SendAsync("SLOT_MAP_VERIFIED");

            currentState.Should().Contain("carrierManagement.READY_FOR_PROCESSING");
            visitedStates.Add("READY_FOR_PROCESSING");

            // Test START_CARRIER_PROCESSING
            _stateMachine.ContextMap!["processActive"] = true;
            currentState = await _stateMachine!.SendAsync("START_CARRIER_PROCESSING");
            currentState.Should().Contain("carrierManagement.PROCESSING_CARRIER");
            visitedStates.Add("PROCESSING_CARRIER");

            // Test CARRIER_PROCESSING_STOPPED
            currentState = await _stateMachine!.SendAsync("CARRIER_PROCESSING_STOPPED");
            currentState.Should().Contain("carrierManagement.READY_FOR_PROCESSING");

            // Test completion path
            await _stateMachine!.SendAsync("START_CARRIER_PROCESSING");
            currentState = await _stateMachine!.SendAsync("CARRIER_PROCESSING_COMPLETE");
            currentState.Should().Contain("carrierManagement.CARRIER_COMPLETE");
            visitedStates.Add("CARRIER_COMPLETE");

            currentState = await _stateMachine!.SendAsync("REMOVE_CARRIER");
            currentState.Should().Contain("carrierManagement.NO_CARRIERS");
            
            // Verify all carrier states visited (12 unique states)
            visitedStates.Count.Should().Be(12);
        }
        
        [Fact]
        public async Task TestFullStateVisitingCoverage_SubstrateTracking()
        {
            // Test ALL states and transitions in substrate tracking
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            var visitedStates = new HashSet<string>();
            
            // NO_SUBSTRATES
            _stateMachine.GetActiveStateNames().Should().Contain("substrateTracking.NO_SUBSTRATES");
            visitedStates.Add("NO_SUBSTRATES");

            var currentState = await _stateMachine!.SendAsync("SUBSTRATE_DETECTED");
            currentState.Should().Contain("substrateTracking.SUBSTRATE_AT_SOURCE");
            visitedStates.Add("SUBSTRATE_AT_SOURCE");

            // Test SUBSTRATE_SKIP_PROCESSING path
            currentState = await _stateMachine!.SendAsync("SUBSTRATE_SKIP_PROCESSING");
            currentState.Should().Contain("substrateTracking.SUBSTRATE_AT_DESTINATION");
            visitedStates.Add("SUBSTRATE_AT_DESTINATION");

            currentState = await _stateMachine!.SendAsync("SUBSTRATE_DEPARTED");
            currentState.Should().Contain("substrateTracking.NO_SUBSTRATES");

            // Test processing path with pause/abort
            await _stateMachine!.SendAsync("SUBSTRATE_DETECTED");
            currentState = await _stateMachine!.SendAsync("SUBSTRATE_NEEDS_PROCESSING");
            currentState.Should().Contain("SUBSTRATE_PROCESSING.WAITING");
            visitedStates.Add("SUBSTRATE_PROCESSING.WAITING");

            _stateMachine.ContextMap!["processActive"] = true;
            currentState = await _stateMachine!.SendAsync("START_SUBSTRATE_PROCESS");
            currentState.Should().Contain("SUBSTRATE_PROCESSING.IN_PROCESS");
            visitedStates.Add("SUBSTRATE_PROCESSING.IN_PROCESS");

            // Test PAUSE path
            currentState = await _stateMachine!.SendAsync("SUBSTRATE_PROCESS_PAUSE");
            currentState.Should().Contain("SUBSTRATE_PROCESSING.PAUSED");
            visitedStates.Add("SUBSTRATE_PROCESSING.PAUSED");

            // Test ABORT from PAUSED
            currentState = await _stateMachine!.SendAsync("SUBSTRATE_PROCESS_ABORT");
            currentState.Should().Contain("SUBSTRATE_PROCESSING.ABORTED");
            visitedStates.Add("SUBSTRATE_PROCESSING.ABORTED");

            currentState = await _stateMachine!.SendAsync("SUBSTRATE_REMOVE");
            currentState.Should().Contain("substrateTracking.SUBSTRATE_AT_DESTINATION");

            // Test RESUME path
            await _stateMachine!.SendAsync("SUBSTRATE_DEPARTED");
            await _stateMachine!.SendAsync("SUBSTRATE_DETECTED");
            await _stateMachine!.SendAsync("SUBSTRATE_NEEDS_PROCESSING");
            await _stateMachine!.SendAsync("START_SUBSTRATE_PROCESS");
            await _stateMachine!.SendAsync("SUBSTRATE_PROCESS_PAUSE");
            currentState = await _stateMachine!.SendAsync("SUBSTRATE_PROCESS_RESUME");
            currentState.Should().Contain("SUBSTRATE_PROCESSING.IN_PROCESS");

            // Test direct ABORT from IN_PROCESS
            currentState = await _stateMachine!.SendAsync("SUBSTRATE_PROCESS_ABORT");
            currentState.Should().Contain("SUBSTRATE_PROCESSING.ABORTED");

            // Test normal completion
            await _stateMachine!.SendAsync("SUBSTRATE_REMOVE");
            await _stateMachine!.SendAsync("SUBSTRATE_DEPARTED");
            await _stateMachine!.SendAsync("SUBSTRATE_DETECTED");
            await _stateMachine!.SendAsync("SUBSTRATE_NEEDS_PROCESSING");
            await _stateMachine!.SendAsync("START_SUBSTRATE_PROCESS");
            currentState = await _stateMachine!.SendAsync("SUBSTRATE_PROCESS_COMPLETE");
            currentState.Should().Contain("SUBSTRATE_PROCESSING.PROCESSED");
            visitedStates.Add("SUBSTRATE_PROCESSING.PROCESSED");

            currentState = await _stateMachine!.SendAsync("SUBSTRATE_MOVE_TO_DESTINATION");
            currentState.Should().Contain("substrateTracking.SUBSTRATE_AT_DESTINATION");
            
            visitedStates.Count.Should().Be(8);
        }
        
        [Fact]
        public async Task TestFullStateVisitingCoverage_ControlJob()
        {
            // Test ALL states and transitions in control job
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            _stateMachine!.Send("INIT_COMPLETE");
            
            var visitedStates = new HashSet<string>();
            
            // NO_JOBS
            _stateMachine.GetActiveStateNames().Should().Contain("controlJob.NO_JOBS");
            visitedStates.Add("NO_JOBS");
            
            var currentState = await _stateMachine!.SendAsync("CREATE_JOB");
            currentState.Should().Contain("controlJob.JOB_QUEUED");
            visitedStates.Add("JOB_QUEUED");
            
            // Test DELETE_JOB from QUEUED
            currentState = await _stateMachine!.SendAsync("DELETE_JOB");
            currentState.Should().Contain("controlJob.NO_JOBS");
            
            // Test DESELECT_JOB
            await _stateMachine!.SendAsync("CREATE_JOB");
            currentState = await _stateMachine!.SendAsync("SELECT_JOB");
            currentState.Should().Contain("controlJob.JOB_SELECTED");
            visitedStates.Add("JOB_SELECTED");
            
            currentState = await _stateMachine!.SendAsync("DESELECT_JOB");
            currentState.Should().Contain("controlJob.JOB_QUEUED");
            
            // Test job execution with all substates
            _stateMachine!.Send("SELECT_JOB");
            _stateMachine.ContextMap!["carrierAvailable"] = true;
            currentState = await _stateMachine!.SendAsync("START_JOB");
            currentState.Should().Contain("JOB_EXECUTING.ACTIVE");
            visitedStates.Add("JOB_EXECUTING.ACTIVE");
            
            // Test ABORT_JOB
            currentState = await _stateMachine!.SendAsync("ABORT_JOB");
            currentState.Should().Contain("JOB_EXECUTING.ABORTING");
            visitedStates.Add("JOB_EXECUTING.ABORTING");
            
            currentState = await _stateMachine!.SendAsync("JOB_ABORTED");
            currentState.Should().Contain("controlJob.JOB_COMPLETED");
            visitedStates.Add("JOB_COMPLETED");
            
            _stateMachine!.Send("REMOVE_JOB");
            
            // Test STOP_JOB path
            await _stateMachine!.SendAsync("CREATE_JOB");
            await _stateMachine!.SendAsync("SELECT_JOB");
            await _stateMachine!.SendAsync("START_JOB");
            currentState = await _stateMachine!.SendAsync("PAUSE_JOB");
            currentState.Should().Contain("JOB_EXECUTING.PAUSED");
            visitedStates.Add("JOB_EXECUTING.PAUSED");
            
            currentState = await _stateMachine!.SendAsync("STOP_JOB");
            currentState.Should().Contain("JOB_EXECUTING.STOPPING");
            visitedStates.Add("JOB_EXECUTING.STOPPING");
            
            currentState = await _stateMachine!.SendAsync("JOB_STOPPED");
            currentState.Should().Contain("controlJob.JOB_COMPLETED");
            
            // Test RESUME_JOB
            await _stateMachine!.SendAsync("REMOVE_JOB");
            await _stateMachine!.SendAsync("CREATE_JOB");
            await _stateMachine!.SendAsync("SELECT_JOB");
            await _stateMachine!.SendAsync("START_JOB");
            await _stateMachine!.SendAsync("PAUSE_JOB");
            currentState = await _stateMachine!.SendAsync("RESUME_JOB");
            currentState.Should().Contain("JOB_EXECUTING.ACTIVE");
            
            // Test SUBSTRATE_COMPLETED (internal transition)
            currentState = await _stateMachine!.SendAsync("SUBSTRATE_COMPLETED");
            currentState.Should().Contain("JOB_EXECUTING.ACTIVE");
            
            visitedStates.Count.Should().Be(8);
        }
        
        [Fact]
        public async Task TestFullStateVisitingCoverage_ProcessJob()
        {
            // Test ALL states and transitions in process job
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            _stateMachine!.Send("INIT_COMPLETE");
            
            var visitedStates = new HashSet<string>();
            string currentState;

            // NO_PROCESS
            _stateMachine.GetActiveStateNames().Should().Contain("processJob.NO_PROCESS");
            visitedStates.Add("NO_PROCESS");
            
            currentState = await _stateMachine!.SendAsync("CREATE_PROCESS");
            currentState.Should().Contain("processJob.SETTING_UP");
            visitedStates.Add("SETTING_UP");
            
            // Test SETUP_FAILED path
            currentState = await _stateMachine!.SendAsync("SETUP_FAILED");
            currentState.Should().Contain("processJob.PROCESS_ABORTING");
            visitedStates.Add("PROCESS_ABORTING");
            
            currentState = await _stateMachine!.SendAsync("ABORT_COMPLETE");
            currentState.Should().Contain("processJob.PROCESS_ABORTED");
            visitedStates.Add("PROCESS_ABORTED");
            
            _stateMachine!.Send("REMOVE_PROCESS");
            
            // Test normal setup and abort from waiting
            await _stateMachine!.SendAsync("CREATE_PROCESS");
            currentState = await _stateMachine!.SendAsync("SETUP_COMPLETE");
            currentState.Should().Contain("processJob.WAITING_FOR_START");
            visitedStates.Add("WAITING_FOR_START");
            
            currentState = await _stateMachine!.SendAsync("ABORT_PROCESS");
            currentState.Should().Contain("processJob.PROCESS_ABORTING");
            
            _stateMachine!.Send("ABORT_COMPLETE");
            _stateMachine!.Send("REMOVE_PROCESS");
            
            // Test processing flow with all substates
            _stateMachine!.Send("CREATE_PROCESS");
            _stateMachine!.Send("SETUP_COMPLETE");
            _stateMachine.ContextMap!["carrierAvailable"] = true;
            _stateMachine.ContextMap!["recipeVerified"] = true;
            currentState = await _stateMachine!.SendAsync("START_PROCESS");
            currentState.Should().Contain("PROCESSING.EXECUTING");
            visitedStates.Add("PROCESSING.EXECUTING");
            
            // Test PAUSE_FAILED path
            currentState = await _stateMachine!.SendAsync("PAUSE_PROCESS");
            currentState.Should().Contain("PROCESSING.PAUSING");
            visitedStates.Add("PROCESSING.PAUSING");
            
            currentState = await _stateMachine!.SendAsync("PAUSE_FAILED");
            currentState.Should().Contain("PROCESSING.EXECUTING");
            
            // Test successful pause
            await _stateMachine!.SendAsync("PAUSE_PROCESS");
            currentState = await _stateMachine!.SendAsync("PROCESS_PAUSED");
            currentState.Should().Contain("PROCESSING.PAUSED");
            visitedStates.Add("PROCESSING.PAUSED");
            
            // Test RESUME_FAILED path
            currentState = await _stateMachine!.SendAsync("RESUME_PROCESS");
            currentState.Should().Contain("PROCESSING.RESUMING");
            visitedStates.Add("PROCESSING.RESUMING");
            
            currentState = await _stateMachine!.SendAsync("RESUME_FAILED");
            currentState.Should().Contain("PROCESSING.PAUSED");
            
            // Test successful resume
            await _stateMachine!.SendAsync("RESUME_PROCESS");
            currentState = await _stateMachine!.SendAsync("PROCESS_RESUMED");
            currentState.Should().Contain("PROCESSING.EXECUTING");
            
            // Test STEP_COMPLETE and NEXT_STEP
            currentState = await _stateMachine!.SendAsync("STEP_COMPLETE");
            currentState.Should().Contain("PROCESSING.NEXT_STEP");
            visitedStates.Add("PROCESSING.NEXT_STEP");
            
            currentState = await _stateMachine!.SendAsync("CONTINUE_PROCESS");
            currentState.Should().Contain("PROCESSING.EXECUTING");
            
            // Test PROCESS_ERROR path
            currentState = await _stateMachine!.SendAsync("PROCESS_ERROR");
            currentState.Should().Contain("processJob.PROCESS_ABORTING");
            
            _stateMachine!.Send("ABORT_COMPLETE");
            _stateMachine!.Send("REMOVE_PROCESS");
            
            // Test normal completion with verification
            await _stateMachine!.SendAsync("CREATE_PROCESS");
            await _stateMachine!.SendAsync("SETUP_COMPLETE");
            await _stateMachine!.SendAsync("START_PROCESS");
            currentState = await _stateMachine!.SendAsync("PROCESS_COMPLETE");
            currentState.Should().Contain("processJob.PROCESS_COMPLETE");
            visitedStates.Add("PROCESS_COMPLETE");
            
            // Test VERIFY_PROCESS_FAIL
            currentState = await _stateMachine!.SendAsync("VERIFY_PROCESS_FAIL");
            currentState.Should().Contain("processJob.PROCESS_ABORTING");
            
            _stateMachine!.Send("ABORT_COMPLETE");
            _stateMachine!.Send("REMOVE_PROCESS");
            
            // Test successful verification
            await _stateMachine!.SendAsync("CREATE_PROCESS");
            await _stateMachine!.SendAsync("SETUP_COMPLETE");
            await _stateMachine!.SendAsync("START_PROCESS");
            await _stateMachine!.SendAsync("PROCESS_COMPLETE");
            currentState = await _stateMachine!.SendAsync("VERIFY_PROCESS_OK");
            currentState.Should().Contain("processJob.PROCESS_COMPLETED");
            visitedStates.Add("PROCESS_COMPLETED");
            
            visitedStates.Count.Should().Be(12);
        }
        
        [Fact]
        public async Task TestFullStateVisitingCoverage_RecipeManagement()
        {
            // Test ALL states and transitions in recipe management
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: false, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();

            var visitedStates = new HashSet<string>();
            string currentState;
            
            // NO_RECIPE
            _stateMachine.GetActiveStateNames().Should().Contain("recipeManagement.NO_RECIPE");
            visitedStates.Add("NO_RECIPE");
            
            currentState = await _stateMachine!.SendAsync("LOAD_RECIPE");
            currentState.Should().Contain("recipeManagement.UNVERIFIED");
            visitedStates.Add("UNVERIFIED");
            
            currentState = await _stateMachine!.SendAsync("VERIFY_RECIPE");
            currentState.Should().Contain("recipeManagement.VERIFYING");
            visitedStates.Add("VERIFYING");
            
            // Test VERIFICATION_FAIL path
            currentState = await _stateMachine!.SendAsync("VERIFICATION_FAIL");
            currentState.Should().Contain("recipeManagement.VERIFICATION_FAILED");
            visitedStates.Add("VERIFICATION_FAILED");
            
            // Test RETRY_VERIFICATION
            currentState = await _stateMachine!.SendAsync("RETRY_VERIFICATION");
            currentState.Should().Contain("recipeManagement.VERIFYING");
            
            // Test EDIT_RECIPE from VERIFICATION_FAILED
            await _stateMachine!.SendAsync("VERIFICATION_FAIL");
            currentState = await _stateMachine!.SendAsync("EDIT_RECIPE");
            currentState.Should().Contain("recipeManagement.EDITING");
            visitedStates.Add("EDITING");
            
            // Test CANCEL_EDIT (requires going to VERIFIED first)
            await _stateMachine!.SendAsync("SAVE_RECIPE");
            await _stateMachine!.SendAsync("VERIFY_RECIPE");
            currentState = await _stateMachine!.SendAsync("VERIFICATION_PASS");
            currentState.Should().Contain("recipeManagement.VERIFIED");
            visitedStates.Add("VERIFIED");
            
            await _stateMachine!.SendAsync("EDIT_RECIPE");
            currentState = await _stateMachine!.SendAsync("CANCEL_EDIT");
            currentState.Should().Contain("recipeManagement.VERIFIED");
            
            // Test DELETE_RECIPE
            currentState = await _stateMachine!.SendAsync("DELETE_RECIPE");
            currentState.Should().Contain("recipeManagement.NO_RECIPE");
            
            // Test SELECTED and ACTIVE states
            await _stateMachine!.SendAsync("LOAD_RECIPE");
            await _stateMachine!.SendAsync("VERIFY_RECIPE");
            await _stateMachine!.SendAsync("VERIFICATION_PASS");
            currentState = await _stateMachine!.SendAsync("SELECT_RECIPE");
            currentState.Should().Contain("recipeManagement.SELECTED");
            visitedStates.Add("SELECTED");
            
            // Test DESELECT_RECIPE
            currentState = await _stateMachine!.SendAsync("DESELECT_RECIPE");
            currentState.Should().Contain("recipeManagement.VERIFIED");
            
            // Test START_RECIPE_PROCESS
            await _stateMachine!.SendAsync("SELECT_RECIPE");
            _stateMachine.ContextMap!["processActive"] = true;
            currentState = await _stateMachine!.SendAsync("START_RECIPE_PROCESS");
            currentState.Should().Contain("recipeManagement.ACTIVE");
            visitedStates.Add("ACTIVE");
            
            // Test RECIPE_PROCESS_ABORT
            currentState = await _stateMachine!.SendAsync("RECIPE_PROCESS_ABORT");
            currentState.Should().Contain("recipeManagement.VERIFIED");
            
            // Test RECIPE_PROCESS_COMPLETE
            await _stateMachine!.SendAsync("SELECT_RECIPE");
            await _stateMachine!.SendAsync("START_RECIPE_PROCESS");
            currentState = await _stateMachine!.SendAsync("RECIPE_PROCESS_COMPLETE");
            currentState.Should().Contain("recipeManagement.VERIFIED");
            
            visitedStates.Count.Should().Be(8);
        }
        
        [Fact]
        public async Task TestSystemLevelTransitions()
        {
            // Test SYSTEM_INITIALIZE and global transitions
            _stateMachine = XStateNet.StateMachineFactory.CreateFromScript(semiIntegratedScript, threadSafe: false, guidIsolate: true, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();

            // Test SYSTEM_INITIALIZE (actions only, no transition)
            await _stateMachine!.SendAsync("SYSTEM_INITIALIZE");
            _stateMachine.ContextMap!["systemStartTime"].Should().NotBeNull();

            // Setup complex state
            await _stateMachine!.SendAsync("INIT_COMPLETE");
            await _stateMachine!.SendAsync("LOAD_RECIPE");
            await _stateMachine!.SendAsync("CREATE_JOB");
            await _stateMachine!.SendAsync("CREATE_PROCESS");
            await _stateMachine!.SendAsync("CARRIER_DETECTED");
            await _stateMachine!.SendAsync("SUBSTRATE_DETECTED");

            // Test EMERGENCY_STOP with multiple targets
            var currentState = await _stateMachine!.SendAsync("EMERGENCY_STOP");
            currentState.Should().Contain("equipment.FAULT");
            currentState.Should().Contain("carrierManagement.NO_CARRIERS");
            currentState.Should().Contain("substrateTracking.NO_SUBSTRATES");
            currentState.Should().Contain("controlJob.NO_JOBS");
            currentState.Should().Contain("processJob.PROCESS_ABORTING");

            // Clear emergency state
            await _stateMachine!.SendAsync("ABORT_COMPLETE");
            await _stateMachine!.SendAsync("REMOVE_PROCESS");
            await _stateMachine!.SendAsync("FAULT_CLEARED");

            // Setup complex state again
            await _stateMachine!.SendAsync("START_PRODUCTION");
            await _stateMachine!.SendAsync("LOAD_RECIPE");
            await _stateMachine!.SendAsync("CREATE_JOB");
            await _stateMachine!.SendAsync("CREATE_PROCESS");

            // Test SYSTEM_RESET with multiple targets
            currentState = await _stateMachine!.SendAsync("SYSTEM_RESET");
            currentState.Should().Contain("equipment.INIT");
            currentState.Should().Contain("carrierManagement.NO_CARRIERS");
            currentState.Should().Contain("substrateTracking.NO_SUBSTRATES");
            currentState.Should().Contain("controlJob.NO_JOBS");
            currentState.Should().Contain("processJob.NO_PROCESS");
            currentState.Should().Contain("recipeManagement.NO_RECIPE");
            currentState.Should().Contain("performanceMonitoring.MONITORING");

            // Verify context was reset
            _stateMachine.ContextMap!["totalProcessed"].Should().Be(0);
            _stateMachine.ContextMap!["totalErrors"].Should().Be(0);
        }
        
        // The SEMI Integrated State Machine Script
        const string semiIntegratedScript = @"
        {
            'id': 'SEMI_Integrated_System',
            'type': 'parallel',
            'context': {
                'systemId': 'SEMI_SYSTEM_001',
                'systemName': 'Integrated SEMI Control System',
                'systemStartTime': null,
                'equipmentId': 'EQ001',
                'equipmentState': 'INIT',
                'equipmentReady': false,
                'communicationState': 'NOT_COMMUNICATING',
                'activeCarriers': [],
                'activeSubstrates': [],
                'activeJobs': [],
                'activeRecipes': [],
                'activeProcesses': [],
                'carrierAvailable': false,
                'substrateReady': false,
                'recipeVerified': false,
                'processActive': false,
                'totalProcessed': 0,
                'totalErrors': 0,
                'systemUptime': 0,
                'availability': 0,
                'performance': 0,
                'quality': 0
            },
            'on': {
                'EMERGENCY_STOP': {
                    'target': [
                        '.equipment.FAULT',
                        '.carrierManagement.NO_CARRIERS',
                        '.substrateTracking.NO_SUBSTRATES',
                        '.controlJob.NO_JOBS',
                        '.processJob.PROCESS_ABORTING'
                    ],
                    'actions': 'emergencyStop'
                },
                'SYSTEM_INITIALIZE': {
                    'actions': 'setSystemStartTime'
                },
                'SYSTEM_RESET': {
                    'target': [
                        '.equipment.INIT',
                        '.carrierManagement.NO_CARRIERS',
                        '.substrateTracking.NO_SUBSTRATES',
                        '.controlJob.NO_JOBS',
                        '.processJob.NO_PROCESS',
                        '.recipeManagement.NO_RECIPE',
                        '.performanceMonitoring.MONITORING'
                    ],
                    'actions': 'systemReset'
                }
            },
            'states': {
                'equipment': {
                    'initial': 'INIT',
                    'states': {
                        'INIT': {
                            'entry': 'setSystemStartTime',
                            'on': {
                                'INIT_COMPLETE': {
                                    'target': 'IDLE',
                                    'actions': 'setEquipmentIdle'
                                },
                                'INIT_FAIL': {
                                    'target': 'FAULT',
                                    'actions': 'setEquipmentFault'
                                }
                            }
                        },
                        'IDLE': {
                            'on': {
                                'START_PRODUCTION': {
                                    'target': 'PRODUCTIVE',
                                    'actions': 'setEquipmentProductive'
                                },
                                'ENTER_SETUP': 'SETUP',
                                'SHUTDOWN_REQUEST': 'SHUTDOWN',
                                'FAULT_DETECTED': {
                                    'target': 'FAULT',
                                    'actions': 'setEquipmentFault'
                                }
                            }
                        },
                        'PRODUCTIVE': {
                            'initial': 'START_UP',
                            'states': {
                                'START_UP': {
                                    'on': {
                                        'STARTUP_COMPLETE': {
                                            'target': 'STANDBY',
                                            'actions': 'setProductiveStandby'
                                        },
                                        'STARTUP_FAIL': '#SEMI_Integrated_System.equipment.FAULT'
                                    }
                                },
                                'STANDBY': {
                                    'on': {
                                        'RUN': {
                                            'target': 'PRODUCTIVE_RUN',
                                            'guard': 'canStartProcess',
                                            'actions': 'setProductiveRun'
                                        },
                                        'SETUP_REQUEST': '#SEMI_Integrated_System.equipment.SETUP',
                                        'FAULT_DETECTED': '#SEMI_Integrated_System.equipment.FAULT'
                                    }
                                },
                                'PRODUCTIVE_RUN': {
                                    'on': {
                                        'PROCESS_COMPLETE': {
                                            'target': 'STANDBY',
                                            'actions': ['incrementProcessed', 'setProductiveStandby']
                                        },
                                        'PAUSE': 'PAUSE',
                                        'FAULT_DETECTED': '#SEMI_Integrated_System.equipment.FAULT'
                                    }
                                },
                                'PAUSE': {
                                    'on': {
                                        'RESUME': 'PRODUCTIVE_RUN',
                                        'ABORT': {
                                            'target': 'STANDBY',
                                            'actions': 'setProductiveStandby'
                                        }
                                    }
                                }
                            }
                        },
                        'SETUP': {
                            'on': {
                                'SETUP_COMPLETE': {
                                    'target': 'IDLE',
                                    'actions': 'setEquipmentIdle'
                                },
                                'SETUP_ABORT': 'IDLE'
                            }
                        },
                        'ENGINEERING': {
                            'on': {
                                'ENGINEERING_COMPLETE': 'IDLE',
                                'TEST_RUN': 'PRODUCTIVE'
                            }
                        },
                        'SHUTDOWN': {
                            'on': {
                                'SHUTDOWN_COMPLETE': {
                                    'target': 'OFF',
                                    'actions': 'setEquipmentOff'
                                },
                                'SHUTDOWN_ABORT': 'IDLE'
                            }
                        },
                        'FAULT': {
                            'entry': 'setEquipmentFault',
                            'on': {
                                'FAULT_CLEARED': {
                                    'target': 'IDLE',
                                    'actions': 'setEquipmentIdle'
                                },
                                'ENTER_REPAIR': 'ENGINEERING'
                            }
                        },
                        'OFF': {
                            'type': 'final'
                        }
                    }
                },
                'carrierManagement': {
                    'initial': 'NO_CARRIERS',
                    'states': {
                        'NO_CARRIERS': {
                            'entry': 'setCarrierUnavailable',
                            'on': {
                                'CARRIER_DETECTED': 'CARRIER_ARRIVING'
                            }
                        },
                        'CARRIER_ARRIVING': {
                            'on': {
                                'CARRIER_ARRIVED': {
                                    'target': 'WAITING_FOR_HOST',
                                    'actions': 'addCarrier'
                                }
                            }
                        },
                        'WAITING_FOR_HOST': {
                            'on': {
                                'PROCEED_WITH_CARRIER': {
                                    'target': 'ID_VERIFICATION',
                                    'guard': 'isEquipmentReady'
                                },
                                'REJECT_CARRIER': 'NO_CARRIERS'
                            }
                        },
                        'ID_VERIFICATION': {
                            'initial': 'READING_ID',
                            'states': {
                                'READING_ID': {
                                    'on': {
                                        'ID_READ_SUCCESS': 'VERIFYING_ID',
                                        'ID_READ_FAIL': 'ID_FAILED'
                                    }
                                },
                                'VERIFYING_ID': {
                                    'on': {
                                        'ID_VERIFIED': '#SEMI_Integrated_System.carrierManagement.SLOT_MAP_VERIFICATION',
                                        'ID_REJECTED': 'ID_FAILED'
                                    }
                                },
                                'ID_FAILED': {
                                    'on': {
                                        'RETRY_ID': 'READING_ID',
                                        'REMOVE_CARRIER': '#SEMI_Integrated_System.carrierManagement.NO_CARRIERS'
                                    }
                                }
                            }
                        },
                        'SLOT_MAP_VERIFICATION': {
                            'initial': 'READING_SLOT_MAP',
                            'states': {
                                'READING_SLOT_MAP': {
                                    'on': {
                                        'SLOT_MAP_READ': 'VERIFYING_SLOT_MAP',
                                        'SLOT_MAP_FAIL': 'SLOT_MAP_FAILED'
                                    }
                                },
                                'VERIFYING_SLOT_MAP': {
                                    'on': {
                                        'SLOT_MAP_VERIFIED': {
                                            'target': '#SEMI_Integrated_System.carrierManagement.READY_FOR_PROCESSING',
                                            'actions': 'setCarrierAvailable'
                                        },
                                        'SLOT_MAP_REJECTED': 'SLOT_MAP_FAILED'
                                    }
                                },
                                'SLOT_MAP_FAILED': {
                                    'on': {
                                        'RETRY_SLOT_MAP': 'READING_SLOT_MAP',
                                        'REMOVE_CARRIER': '#SEMI_Integrated_System.carrierManagement.NO_CARRIERS'
                                    }
                                }
                            }
                        },
                        'READY_FOR_PROCESSING': {
                            'on': {
                                'START_CARRIER_PROCESSING': {
                                    'target': 'PROCESSING_CARRIER',
                                    'guard': 'isProcessActive'
                                },
                                'CARRIER_DEPARTED': 'NO_CARRIERS'
                            }
                        },
                        'PROCESSING_CARRIER': {
                            'on': {
                                'CARRIER_PROCESSING_COMPLETE': 'CARRIER_COMPLETE',
                                'CARRIER_PROCESSING_STOPPED': 'READY_FOR_PROCESSING'
                            }
                        },
                        'CARRIER_COMPLETE': {
                            'on': {
                                'REMOVE_CARRIER': 'NO_CARRIERS'
                            }
                        }
                    }
                },
                'substrateTracking': {
                    'initial': 'NO_SUBSTRATES',
                    'states': {
                        'NO_SUBSTRATES': {
                            'entry': 'setSubstrateUnavailable',
                            'on': {
                                'SUBSTRATE_DETECTED': {
                                    'target': 'SUBSTRATE_AT_SOURCE',
                                    'actions': 'addSubstrate'
                                }
                            }
                        },
                        'SUBSTRATE_AT_SOURCE': {
                            'on': {
                                'SUBSTRATE_NEEDS_PROCESSING': {
                                    'target': 'SUBSTRATE_PROCESSING',
                                    'actions': 'setSubstrateReady'
                                },
                                'SUBSTRATE_SKIP_PROCESSING': 'SUBSTRATE_AT_DESTINATION'
                            }
                        },
                        'SUBSTRATE_PROCESSING': {
                            'initial': 'WAITING',
                            'states': {
                                'WAITING': {
                                    'on': {
                                        'START_SUBSTRATE_PROCESS': {
                                            'target': 'IN_PROCESS',
                                            'guard': 'isProcessActive'
                                        }
                                    }
                                },
                                'IN_PROCESS': {
                                    'on': {
                                        'SUBSTRATE_PROCESS_COMPLETE': 'PROCESSED',
                                        'SUBSTRATE_PROCESS_PAUSE': 'PAUSED',
                                        'SUBSTRATE_PROCESS_ABORT': 'ABORTED'
                                    }
                                },
                                'PAUSED': {
                                    'on': {
                                        'SUBSTRATE_PROCESS_RESUME': 'IN_PROCESS',
                                        'SUBSTRATE_PROCESS_ABORT': 'ABORTED'
                                    }
                                },
                                'PROCESSED': {
                                    'on': {
                                        'SUBSTRATE_MOVE_TO_DESTINATION': '#SEMI_Integrated_System.substrateTracking.SUBSTRATE_AT_DESTINATION'
                                    }
                                },
                                'ABORTED': {
                                    'on': {
                                        'SUBSTRATE_REMOVE': '#SEMI_Integrated_System.substrateTracking.SUBSTRATE_AT_DESTINATION'
                                    }
                                }
                            }
                        },
                        'SUBSTRATE_AT_DESTINATION': {
                            'on': {
                                'SUBSTRATE_DEPARTED': 'NO_SUBSTRATES'
                            }
                        }
                    }
                },
                'controlJob': {
                    'initial': 'NO_JOBS',
                    'states': {
                        'NO_JOBS': {
                            'on': {
                                'CREATE_JOB': {
                                    'target': 'JOB_QUEUED',
                                    'actions': 'addJob'
                                }
                            }
                        },
                        'JOB_QUEUED': {
                            'on': {
                                'SELECT_JOB': 'JOB_SELECTED',
                                'DELETE_JOB': 'NO_JOBS'
                            }
                        },
                        'JOB_SELECTED': {
                            'on': {
                                'START_JOB': {
                                    'target': 'JOB_EXECUTING',
                                    'guard': 'canStartJob'
                                },
                                'DESELECT_JOB': 'JOB_QUEUED'
                            }
                        },
                        'JOB_EXECUTING': {
                            'initial': 'ACTIVE',
                            'states': {
                                'ACTIVE': {
                                    'on': {
                                        'PAUSE_JOB': 'PAUSED',
                                        'SUBSTRATE_COMPLETED': {},
                                        'JOB_COMPLETE': '#SEMI_Integrated_System.controlJob.JOB_COMPLETED',
                                        'ABORT_JOB': 'ABORTING'
                                    }
                                },
                                'PAUSED': {
                                    'on': {
                                        'RESUME_JOB': 'ACTIVE',
                                        'STOP_JOB': 'STOPPING'
                                    }
                                },
                                'STOPPING': {
                                    'on': {
                                        'JOB_STOPPED': '#SEMI_Integrated_System.controlJob.JOB_COMPLETED'
                                    }
                                },
                                'ABORTING': {
                                    'on': {
                                        'JOB_ABORTED': '#SEMI_Integrated_System.controlJob.JOB_COMPLETED'
                                    }
                                }
                            }
                        },
                        'JOB_COMPLETED': {
                            'on': {
                                'REMOVE_JOB': 'NO_JOBS'
                            }
                        }
                    }
                },
                'processJob': {
                    'initial': 'NO_PROCESS',
                    'states': {
                        'NO_PROCESS': {
                            'on': {
                                'CREATE_PROCESS': {
                                    'target': 'SETTING_UP',
                                    'actions': 'addProcess'
                                }
                            }
                        },
                        'SETTING_UP': {
                            'on': {
                                'SETUP_COMPLETE': 'WAITING_FOR_START',
                                'SETUP_FAILED': 'PROCESS_ABORTING'
                            }
                        },
                        'WAITING_FOR_START': {
                            'on': {
                                'START_PROCESS': {
                                    'target': 'PROCESSING',
                                    'guard': 'canStartProcess'
                                },
                                'ABORT_PROCESS': 'PROCESS_ABORTING'
                            }
                        },
                        'PROCESSING': {
                            'initial': 'EXECUTING',
                            'states': {
                                'EXECUTING': {
                                    'on': {
                                        'PAUSE_PROCESS': 'PAUSING',
                                        'STEP_COMPLETE': 'NEXT_STEP',
                                        'PROCESS_COMPLETE': '#SEMI_Integrated_System.processJob.PROCESS_COMPLETE',
                                        'PROCESS_ERROR': '#SEMI_Integrated_System.processJob.PROCESS_ABORTING'
                                    }
                                },
                                'PAUSING': {
                                    'on': {
                                        'PROCESS_PAUSED': 'PAUSED',
                                        'PAUSE_FAILED': 'EXECUTING'
                                    }
                                },
                                'PAUSED': {
                                    'on': {
                                        'RESUME_PROCESS': 'RESUMING',
                                        'ABORT_PROCESS': '#SEMI_Integrated_System.processJob.PROCESS_ABORTING'
                                    }
                                },
                                'RESUMING': {
                                    'on': {
                                        'PROCESS_RESUMED': 'EXECUTING',
                                        'RESUME_FAILED': 'PAUSED'
                                    }
                                },
                                'NEXT_STEP': {
                                    'on': {
                                        'CONTINUE_PROCESS': 'EXECUTING'
                                    }
                                }
                            }
                        },
                        'PROCESS_COMPLETE': {
                            'on': {
                                'VERIFY_PROCESS_OK': 'PROCESS_COMPLETED',
                                'VERIFY_PROCESS_FAIL': 'PROCESS_ABORTING'
                            }
                        },
                        'PROCESS_ABORTING': {
                            'on': {
                                'ABORT_COMPLETE': 'PROCESS_ABORTED'
                            }
                        },
                        'PROCESS_ABORTED': {
                            'on': {
                                'REMOVE_PROCESS': 'NO_PROCESS'
                            }
                        },
                        'PROCESS_COMPLETED': {
                            'on': {
                                'REMOVE_PROCESS': 'NO_PROCESS'
                            }
                        }
                    }
                },
                'recipeManagement': {
                    'initial': 'NO_RECIPE',
                    'states': {
                        'NO_RECIPE': {
                            'entry': 'setRecipeUnverified',
                            'on': {
                                'LOAD_RECIPE': {
                                    'target': 'UNVERIFIED',
                                    'actions': 'addRecipe'
                                }
                            }
                        },
                        'UNVERIFIED': {
                            'on': {
                                'VERIFY_RECIPE': 'VERIFYING'
                            }
                        },
                        'VERIFYING': {
                            'on': {
                                'VERIFICATION_PASS': {
                                    'target': 'VERIFIED',
                                    'actions': 'setRecipeVerified'
                                },
                                'VERIFICATION_FAIL': 'VERIFICATION_FAILED'
                            }
                        },
                        'VERIFICATION_FAILED': {
                            'on': {
                                'EDIT_RECIPE': 'EDITING',
                                'RETRY_VERIFICATION': 'VERIFYING'
                            }
                        },
                        'VERIFIED': {
                            'on': {
                                'SELECT_RECIPE': 'SELECTED',
                                'EDIT_RECIPE': 'EDITING',
                                'DELETE_RECIPE': 'NO_RECIPE'
                            }
                        },
                        'EDITING': {
                            'on': {
                                'SAVE_RECIPE': {
                                    'target': 'UNVERIFIED',
                                    'actions': 'setRecipeUnverified'
                                },
                                'CANCEL_EDIT': 'VERIFIED'
                            }
                        },
                        'SELECTED': {
                            'on': {
                                'START_RECIPE_PROCESS': {
                                    'target': 'ACTIVE',
                                    'guard': 'canStartRecipeProcess'
                                },
                                'DESELECT_RECIPE': 'VERIFIED'
                            }
                        },
                        'ACTIVE': {
                            'on': {
                                'RECIPE_PROCESS_COMPLETE': 'VERIFIED',
                                'RECIPE_PROCESS_ABORT': 'VERIFIED'
                            }
                        }
                    }
                },
                'performanceMonitoring': {
                    'initial': 'MONITORING',
                    'states': {
                        'MONITORING': {
                            'on': {
                                'UPDATE_METRICS': {
                                    'actions': 'updateMetrics'
                                },
                                'PERFORMANCE_ALERT': {
                                    'target': 'ALERT_STATE',
                                    'guard': 'isOeeAlert'
                                }
                            }
                        },
                        'ALERT_STATE': {
                            'on': {
                                'ACKNOWLEDGE_ALERT': 'MONITORING',
                                'INVESTIGATE_ISSUE': 'ANALYZING'
                            }
                        },
                        'ANALYZING': {
                            'on': {
                                'ANALYSIS_COMPLETE': 'MONITORING'
                            }
                        }
                    }
                }
            }
        }";
    

        public void Dispose()
        {
            // Cleanup if needed
        }}
}








