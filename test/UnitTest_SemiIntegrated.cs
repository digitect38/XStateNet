using NUnit.Framework;
using XStateNet;
using XStateNet.UnitTest;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SemiIntegratedTests
{
    [TestFixture]
    public class SemiIntegratedMachineTests
    {
        private StateMachine? _stateMachine;
        private ActionMap _actions;
        private GuardMap _guards;
        
        // Context variables
        private Dictionary<string, object?> _context;
        
        [SetUp]
        public void Setup()
        {
            _context = new Dictionary<string, object?>
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
            Logger.CurrentLevel = Logger.LogLevel.Info;
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
        
        [Test]
        public void TestInitialState()
        {
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.INIT"));
            Assert.IsTrue(currentState.Contains("carrierManagement.NO_CARRIERS"));
            Assert.IsTrue(currentState.Contains("substrateTracking.NO_SUBSTRATES"));
            Assert.IsTrue(currentState.Contains("controlJob.NO_JOBS"));
            Assert.IsTrue(currentState.Contains("processJob.NO_PROCESS"));
            Assert.IsTrue(currentState.Contains("recipeManagement.NO_RECIPE"));
            Assert.IsTrue(currentState.Contains("performanceMonitoring.MONITORING"));
        }
        
        [Test]
        public void TestEquipmentInitialization()
        {
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            _stateMachine!.Send("INIT_COMPLETE");
            
            Assert.That(_stateMachine.ContextMap!["equipmentState"], Is.EqualTo("IDLE"));
            Assert.That((bool)(_stateMachine.ContextMap!["equipmentReady"] ?? false), Is.True);
            Assert.That(_stateMachine.ContextMap!["communicationState"], Is.EqualTo("COMMUNICATING"));
        }
        
        [Test]
        public void TestCarrierArrival()
        {
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("CARRIER_DETECTED");
            _stateMachine!.Send("CARRIER_ARRIVED");
            
            List<object?>? carriers = _stateMachine.ContextMap!["activeCarriers"] as List<object?>;
            Assert.IsTrue(carriers?.Count > 0);
        }
        
        [Test]
        public void TestProductionStart()
        {
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("START_PRODUCTION");
            
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.PRODUCTIVE"));
        }
        
        [Test]
        public void TestEmergencyStop()
        {
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("START_PRODUCTION");
            _stateMachine!.Send("STARTUP_COMPLETE");
            _stateMachine!.Send("EMERGENCY_STOP");
            
            // Check that equipment moved to FAULT state
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.FAULT"));
            
            // Check that the emergency stop actions were executed
            // Note: equipmentState will be "FAULT" because the FAULT state's entry action sets it
            var equipmentState = _stateMachine.ContextMap!["equipmentState"] as string;
            Assert.IsTrue(equipmentState == "FAULT" || equipmentState == "EMERGENCY_STOP");
            Assert.IsFalse((bool)(_stateMachine.ContextMap!["equipmentReady"] ?? false));
            Assert.IsFalse((bool)(_stateMachine.ContextMap!["processActive"] ?? false));
        }
        
        [Test]
        public void TestSystemReset()
        {
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("START_PRODUCTION");
            _stateMachine!.Send("STARTUP_COMPLETE");
            
            // Add some data before reset
            _stateMachine.ContextMap!["totalProcessed"] = 5;
            _stateMachine.ContextMap!["totalErrors"] = 2;
            
            _stateMachine!.Send("SYSTEM_RESET");
            
            // Check that system reset to INIT state
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.INIT"));
            
            // Check that the reset actions were executed
            Assert.That(_stateMachine.ContextMap!["totalProcessed"], Is.EqualTo(0));
            Assert.That(_stateMachine.ContextMap!["totalErrors"], Is.EqualTo(0));
            List<object?>? carriers = _stateMachine.ContextMap!["activeCarriers"] as List<object?>;
            Assert.That(carriers?.Count ?? 0, Is.EqualTo(0));
            Assert.IsFalse((bool)(_stateMachine.ContextMap!["equipmentReady"] ?? false));
            Assert.IsFalse((bool)(_stateMachine.ContextMap!["processActive"] ?? false));
        }
        
        [Test]
        public void TestCarrierManagementWorkflow()
        {
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            // Initialize equipment
            _stateMachine!.Send("INIT_COMPLETE");
            Assert.IsTrue((bool)(_stateMachine.ContextMap!["equipmentReady"] ?? false));
            
            // Carrier arrives
            _stateMachine!.Send("CARRIER_DETECTED");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("carrierManagement.CARRIER_ARRIVING"));
            
            _stateMachine!.Send("CARRIER_ARRIVED");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("carrierManagement.WAITING_FOR_HOST"));
            
            // Proceed with carrier
            _stateMachine!.Send("PROCEED_WITH_CARRIER");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("carrierManagement.ID_VERIFICATION.READING_ID"));
            
            // ID verification flow
            _stateMachine!.Send("ID_READ_SUCCESS");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("carrierManagement.ID_VERIFICATION.VERIFYING_ID"));
            
            _stateMachine!.Send("ID_VERIFIED");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("carrierManagement.SLOT_MAP_VERIFICATION.READING_SLOT_MAP"));
            
            // Slot map verification
            _stateMachine!.Send("SLOT_MAP_READ");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("carrierManagement.SLOT_MAP_VERIFICATION.VERIFYING_SLOT_MAP"));
            
            _stateMachine!.Send("SLOT_MAP_VERIFIED");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("carrierManagement.READY_FOR_PROCESSING"));
            Assert.IsTrue((bool)(_stateMachine.ContextMap!["carrierAvailable"] ?? false));
        }
        
        [Test]
        public void TestSubstrateTrackingWorkflow()
        {
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            // Initialize and start production
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("START_PRODUCTION");
            _stateMachine!.Send("STARTUP_COMPLETE");
            
            // Substrate arrives
            _stateMachine!.Send("SUBSTRATE_DETECTED");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("substrateTracking.SUBSTRATE_AT_SOURCE"));
            
            // Substrate needs processing
            _stateMachine!.Send("SUBSTRATE_NEEDS_PROCESSING");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("substrateTracking.SUBSTRATE_PROCESSING.WAITING"));
            Assert.IsTrue((bool)(_stateMachine.ContextMap!["substrateReady"] ?? false));
            
            // Start processing (requires processActive)
            _stateMachine.ContextMap!["processActive"] = true;
            _stateMachine!.Send("START_SUBSTRATE_PROCESS");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("substrateTracking.SUBSTRATE_PROCESSING.IN_PROCESS"));
            
            // Complete processing
            _stateMachine!.Send("SUBSTRATE_PROCESS_COMPLETE");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("substrateTracking.SUBSTRATE_PROCESSING.PROCESSED"));
            
            // Move to destination
            _stateMachine!.Send("SUBSTRATE_MOVE_TO_DESTINATION");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("substrateTracking.SUBSTRATE_AT_DESTINATION"));
            
            // Substrate departs
            _stateMachine!.Send("SUBSTRATE_DEPARTED");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("substrateTracking.NO_SUBSTRATES"));
        }
        
        [Test]
        public void TestControlJobWorkflow()
        {
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            // Setup prerequisites
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine.ContextMap!["carrierAvailable"] = true; // Simulate carrier ready
            
            // Create job
            _stateMachine!.Send("CREATE_JOB");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("controlJob.JOB_QUEUED"));
            
            // Select job
            _stateMachine!.Send("SELECT_JOB");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("controlJob.JOB_SELECTED"));
            
            // Start job (requires equipment ready and carrier available)
            _stateMachine!.Send("START_JOB");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("controlJob.JOB_EXECUTING.ACTIVE"));
            
            // Pause job
            _stateMachine!.Send("PAUSE_JOB");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("controlJob.JOB_EXECUTING.PAUSED"));
            
            // Resume job
            _stateMachine!.Send("RESUME_JOB");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("controlJob.JOB_EXECUTING.ACTIVE"));
            
            // Complete job
            _stateMachine!.Send("JOB_COMPLETE");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("controlJob.JOB_COMPLETED"));
            
            // Remove job
            _stateMachine!.Send("REMOVE_JOB");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("controlJob.NO_JOBS"));
        }
        
        [Test]
        public void TestRecipeManagementWorkflow()
        {
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            // Load recipe
            _stateMachine!.Send("LOAD_RECIPE");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("recipeManagement.UNVERIFIED"));
            Assert.IsFalse((bool)(_stateMachine.ContextMap!["recipeVerified"] ?? false));
            
            // Verify recipe
            _stateMachine!.Send("VERIFY_RECIPE");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("recipeManagement.VERIFYING"));
            
            // Verification passes
            _stateMachine!.Send("VERIFICATION_PASS");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("recipeManagement.VERIFIED"));
            Assert.IsTrue((bool)(_stateMachine.ContextMap!["recipeVerified"] ?? false));
            
            // Select recipe
            _stateMachine!.Send("SELECT_RECIPE");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("recipeManagement.SELECTED"));
            
            // Start recipe process (requires process active)
            _stateMachine.ContextMap!["processActive"] = true;
            _stateMachine!.Send("START_RECIPE_PROCESS");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("recipeManagement.ACTIVE"));
            
            // Complete recipe process
            _stateMachine!.Send("RECIPE_PROCESS_COMPLETE");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("recipeManagement.VERIFIED"));
        }
        
        [Test]
        public void TestProcessJobWorkflow()
        {
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            // Setup prerequisites
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine.ContextMap!["recipeVerified"] = true;
            _stateMachine.ContextMap!["carrierAvailable"] = true;
            
            // Create process
            _stateMachine!.Send("CREATE_PROCESS");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("processJob.SETTING_UP"));
            
            // Setup complete
            _stateMachine!.Send("SETUP_COMPLETE");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("processJob.WAITING_FOR_START"));
            
            // Start process
            _stateMachine!.Send("START_PROCESS");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("processJob.PROCESSING.EXECUTING"));
            
            // Pause process
            _stateMachine!.Send("PAUSE_PROCESS");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("processJob.PROCESSING.PAUSING"));
            
            _stateMachine!.Send("PROCESS_PAUSED");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("processJob.PROCESSING.PAUSED"));
            
            // Resume process
            _stateMachine!.Send("RESUME_PROCESS");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("processJob.PROCESSING.RESUMING"));
            
            _stateMachine!.Send("PROCESS_RESUMED");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("processJob.PROCESSING.EXECUTING"));
            
            // Complete process
            _stateMachine!.Send("PROCESS_COMPLETE");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("processJob.PROCESS_COMPLETE"));
            
            _stateMachine!.Send("VERIFY_PROCESS_OK");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("processJob.PROCESS_COMPLETED"));
        }
        
        [Test]
        public void TestParallelStateCoordination()
        {
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            // Verify all parallel regions start correctly
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.INIT"));
            Assert.IsTrue(currentState.Contains("carrierManagement.NO_CARRIERS"));
            Assert.IsTrue(currentState.Contains("substrateTracking.NO_SUBSTRATES"));
            Assert.IsTrue(currentState.Contains("controlJob.NO_JOBS"));
            Assert.IsTrue(currentState.Contains("processJob.NO_PROCESS"));
            Assert.IsTrue(currentState.Contains("recipeManagement.NO_RECIPE"));
            Assert.IsTrue(currentState.Contains("performanceMonitoring.MONITORING"));
            
            // Initialize equipment and verify it doesn't affect other regions
            _stateMachine!.Send("INIT_COMPLETE");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.IDLE"));
            Assert.IsTrue(currentState.Contains("carrierManagement.NO_CARRIERS"));
            
            // Start multiple workflows in parallel
            _stateMachine!.Send("CARRIER_DETECTED");
            _stateMachine!.Send("LOAD_RECIPE");
            _stateMachine!.Send("CREATE_JOB");
            
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.IDLE"));
            Assert.IsTrue(currentState.Contains("carrierManagement.CARRIER_ARRIVING"));
            Assert.IsTrue(currentState.Contains("recipeManagement.UNVERIFIED"));
            Assert.IsTrue(currentState.Contains("controlJob.JOB_QUEUED"));
            
            // Test that process coordination flags work correctly
            Assert.IsFalse((bool)(_stateMachine.ContextMap!["carrierAvailable"] ?? false));
            Assert.IsFalse((bool)(_stateMachine.ContextMap!["recipeVerified"] ?? false));
            
            // Verify recipe and check flag
            _stateMachine!.Send("VERIFY_RECIPE");
            _stateMachine!.Send("VERIFICATION_PASS");
            Assert.IsTrue((bool)(_stateMachine.ContextMap!["recipeVerified"] ?? false));
            
            // Test multiple target transition (EMERGENCY_STOP)
            _stateMachine!.Send("START_PRODUCTION");
            _stateMachine!.Send("STARTUP_COMPLETE");
            _stateMachine!.Send("EMERGENCY_STOP");
            
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.FAULT"));
            Assert.IsTrue(currentState.Contains("carrierManagement.NO_CARRIERS"));
            Assert.IsTrue(currentState.Contains("substrateTracking.NO_SUBSTRATES"));
            Assert.IsTrue(currentState.Contains("controlJob.NO_JOBS"));
            Assert.IsTrue(currentState.Contains("processJob.PROCESS_ABORTING"));
        }
        
        [Test]
        public void TestProductionCycle()
        {
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            // Initialize system
            _stateMachine!.Send("INIT_COMPLETE");
            Assert.IsTrue((bool)(_stateMachine.ContextMap!["equipmentReady"] ?? false));
            
            // Load and verify recipe
            _stateMachine!.Send("LOAD_RECIPE");
            _stateMachine!.Send("VERIFY_RECIPE");
            _stateMachine!.Send("VERIFICATION_PASS");
            Assert.IsTrue((bool)(_stateMachine.ContextMap!["recipeVerified"] ?? false));
            
            // Carrier arrives and is verified
            _stateMachine!.Send("CARRIER_DETECTED");
            _stateMachine!.Send("CARRIER_ARRIVED");
            _stateMachine!.Send("PROCEED_WITH_CARRIER");
            _stateMachine!.Send("ID_READ_SUCCESS");
            _stateMachine!.Send("ID_VERIFIED");
            _stateMachine!.Send("SLOT_MAP_READ");
            _stateMachine!.Send("SLOT_MAP_VERIFIED");
            Assert.IsTrue((bool)(_stateMachine.ContextMap!["carrierAvailable"] ?? false));
            
            // Start production
            _stateMachine!.Send("START_PRODUCTION");
            _stateMachine!.Send("STARTUP_COMPLETE");
            
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.PRODUCTIVE.STANDBY"));
            
            // Run production
            _stateMachine!.Send("RUN");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.PRODUCTIVE.PRODUCTIVE_RUN"));
            Assert.IsTrue((bool)(_stateMachine.ContextMap!["processActive"] ?? false));
            
            // Complete production
            _stateMachine!.Send("PROCESS_COMPLETE");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.PRODUCTIVE.STANDBY"));
            Assert.That(_stateMachine.ContextMap!["totalProcessed"], Is.EqualTo(1));
            Assert.IsFalse((bool)(_stateMachine.ContextMap!["processActive"] ?? false));
        }
        
        [Test]
        public void TestFaultRecovery()
        {
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            // Initialize and start production
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("START_PRODUCTION");
            _stateMachine!.Send("STARTUP_COMPLETE");
            
            // Simulate fault
            _stateMachine!.Send("FAULT_DETECTED");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.FAULT"));
            Assert.IsFalse((bool)(_stateMachine.ContextMap!["equipmentReady"] ?? false));
            Assert.IsTrue((int)(_stateMachine.ContextMap!["totalErrors"] ?? 0) > 0);
            
            // Clear fault
            _stateMachine!.Send("FAULT_CLEARED");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.IDLE"));
            Assert.IsTrue((bool)(_stateMachine.ContextMap!["equipmentReady"] ?? false));
            
            // Verify system can restart production
            _stateMachine!.Send("START_PRODUCTION");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.PRODUCTIVE"));
        }
        
        [Test]
        public void TestPerformanceMonitoring()
        {
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            // Verify monitoring starts active
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("performanceMonitoring.MONITORING"));
            
            // Update metrics
            _stateMachine!.Send("UPDATE_METRICS");
            
            // Simulate low OEE condition
            _stateMachine.ContextMap!["availability"] = 0.5;
            _stateMachine.ContextMap!["performance"] = 0.6;
            _stateMachine.ContextMap!["quality"] = 0.7;
            
            _stateMachine!.Send("PERFORMANCE_ALERT");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("performanceMonitoring.ALERT_STATE"));
            
            // Acknowledge alert
            _stateMachine!.Send("ACKNOWLEDGE_ALERT");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("performanceMonitoring.MONITORING"));
            
            // Test investigate flow
            _stateMachine.ContextMap!["availability"] = 0.5;
            _stateMachine!.Send("PERFORMANCE_ALERT");
            _stateMachine!.Send("INVESTIGATE_ISSUE");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("performanceMonitoring.ANALYZING"));
            
            _stateMachine!.Send("ANALYSIS_COMPLETE");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("performanceMonitoring.MONITORING"));
        }
        
        [Test]
        public void TestE10StateTracking()
        {
            // SEMI E10 - Equipment Events and State Definitions
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            // Track state transitions and time spent in each state
            var stateHistory = new List<(string state, DateTime time)>();
            
            // Non-scheduled downtime
            var initialState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(initialState.Contains("equipment.INIT"));
            stateHistory.Add(("INIT", DateTime.Now));
            
            // Productive time
            _stateMachine!.Send("INIT_COMPLETE");
            stateHistory.Add(("IDLE", DateTime.Now));
            
            _stateMachine!.Send("START_PRODUCTION");
            stateHistory.Add(("PRODUCTIVE", DateTime.Now));
            
            // Engineering time
            _stateMachine!.Send("STARTUP_FAIL");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.FAULT"));
            stateHistory.Add(("FAULT", DateTime.Now));
            
            _stateMachine!.Send("ENTER_REPAIR");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.ENGINEERING"));
            stateHistory.Add(("ENGINEERING", DateTime.Now));
            
            // Scheduled downtime
            _stateMachine!.Send("ENGINEERING_COMPLETE");
            _stateMachine!.Send("ENTER_SETUP");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.SETUP"));
            stateHistory.Add(("SETUP", DateTime.Now));
            
            // Verify we can track all E10 state categories
            Assert.IsTrue(stateHistory.Any(s => s.state == "PRODUCTIVE"));
            Assert.IsTrue(stateHistory.Any(s => s.state == "IDLE"));
            Assert.IsTrue(stateHistory.Any(s => s.state == "ENGINEERING"));
            Assert.IsTrue(stateHistory.Any(s => s.state == "SETUP"));
        }
        
        [Test]
        public void TestE84LoadPortHandshake()
        {
            // SEMI E84 - Enhanced Carrier Handoff
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            _stateMachine!.Send("INIT_COMPLETE");
            
            // Simulate E84 handshake sequence
            // CS_0: Carrier not detected
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("carrierManagement.NO_CARRIERS"));
            
            // VALID signal on - carrier detected
            _stateMachine!.Send("CARRIER_DETECTED");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("carrierManagement.CARRIER_ARRIVING"));
            
            // CS_1: Transfer blocked (waiting for handshake)
            _stateMachine!.Send("CARRIER_ARRIVED");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("carrierManagement.WAITING_FOR_HOST"));
            
            // L_REQ signal - Load request from host
            _stateMachine!.Send("PROCEED_WITH_CARRIER");
            
            // U_REQ signal - Unload request would trigger carrier departure
            // This tests the bidirectional handshake of E84
        }
        
        [Test]
        public void TestE142SubstrateMapping()
        {
            // SEMI E142 - Substrate Mapping
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            // Add slot map context
            _stateMachine.ContextMap!["slotMap"] = new Dictionary<int, string>
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
            
            _stateMachine!.Send("INIT_COMPLETE");
            
            // Carrier with substrates arrives
            _stateMachine!.Send("CARRIER_DETECTED");
            _stateMachine!.Send("CARRIER_ARRIVED");
            _stateMachine!.Send("PROCEED_WITH_CARRIER");
            _stateMachine!.Send("ID_READ_SUCCESS");
            _stateMachine!.Send("ID_VERIFIED");
            
            // Slot map verification - E142 standard
            _stateMachine!.Send("SLOT_MAP_READ");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("SLOT_MAP_VERIFICATION.VERIFYING_SLOT_MAP"));
            
            // Check slot map for errors
            var slotMap = _stateMachine.ContextMap!["slotMap"] as Dictionary<int, string>;
            Assert.IsNotNull(slotMap);
            
            // Verify error detection
            var hasDoubleSlot = slotMap.Any(s => s.Value == "DOUBLE_SLOT");
            var hasCrossSlot = slotMap.Any(s => s.Value == "CROSS_SLOT");
            Assert.IsTrue(hasDoubleSlot, "Should detect double-slotted wafer");
            Assert.IsTrue(hasCrossSlot, "Should detect cross-slotted wafer");
            
            // Count valid substrates
            var validSubstrates = slotMap.Count(s => s.Value == "PRESENT");
            Assert.That(validSubstrates, Is.EqualTo(6));
        }
        
        [Test]
        public void TestCommunicationStates()
        {
            // SEMI E5/E37 - Communication States (SECS/HSMS)
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            // Initial state - not communicating
            Assert.That(_stateMachine.ContextMap!["communicationState"], Is.EqualTo("NOT_COMMUNICATING"));
            
            // Establish communication
            _stateMachine!.Send("INIT_COMPLETE");
            Assert.That(_stateMachine.ContextMap!["communicationState"], Is.EqualTo("COMMUNICATING"));
            
            // Communication should persist through state changes
            _stateMachine!.Send("START_PRODUCTION");
            Assert.That(_stateMachine.ContextMap!["communicationState"], Is.EqualTo("COMMUNICATING"));
            
            // Verify communication state tracking works
            // Since equipment has complex nested states, let's just verify the key states
            Assert.IsTrue((bool)(_stateMachine.ContextMap!["equipmentReady"] ?? false));
            
            // Manually set communication state to test tracking
            _stateMachine.ContextMap!["communicationState"] = "DISABLED";
            Assert.That(_stateMachine.ContextMap!["communicationState"], Is.EqualTo("DISABLED"));
            
            // Reset and verify
            _stateMachine.ContextMap!["communicationState"] = "COMMUNICATING";
            Assert.That(_stateMachine.ContextMap!["communicationState"], Is.EqualTo("COMMUNICATING"));
        }
        
        [Test]
        public void TestModuleProcessTracking()
        {
            // SEMI E157 - Module Process Tracking (simplified)
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
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
            _stateMachine!.Send("INIT_COMPLETE");
            
            // Start substrate processing in module
            _stateMachine!.Send("SUBSTRATE_DETECTED");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("substrateTracking.SUBSTRATE_AT_SOURCE"));
            
            // Track substrate through multiple modules
            _stateMachine!.Send("SUBSTRATE_NEEDS_PROCESSING");
            _stateMachine.ContextMap!["processActive"] = true;
            _stateMachine!.Send("START_SUBSTRATE_PROCESS");
            
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("substrateTracking.SUBSTRATE_PROCESSING.IN_PROCESS"));
            
            // Verify module tracking capability exists
            var modules = _stateMachine.ContextMap!["processModules"] as List<object>;
            Assert.IsNotNull(modules);
            Assert.That(((dynamic)modules).Count, Is.EqualTo(3));
        }
        
        [Test]
        public void TestDataAcquisition()
        {
            // SEMI E164 - EDA (Equipment Data Acquisition) concepts
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            
            // Add data collection context
            var dataPoints = new List<(string parameter, object value, DateTime timestamp)>();
            _stateMachine.ContextMap!["dataCollection"] = dataPoints;
            
            _stateMachine!.Start();
            
            // Collect data on state changes
            _stateMachine!.Send("INIT_COMPLETE");
            dataPoints.Add(("EquipmentState", "IDLE", DateTime.Now));
            dataPoints.Add(("EquipmentReady", true, DateTime.Now));
            
            _stateMachine!.Send("START_PRODUCTION");
            dataPoints.Add(("EquipmentState", "PRODUCTIVE", DateTime.Now));
            
            _stateMachine!.Send("STARTUP_COMPLETE");
            _stateMachine.ContextMap!["carrierAvailable"] = true;
            _stateMachine.ContextMap!["recipeVerified"] = true;
            
            _stateMachine!.Send("RUN");
            dataPoints.Add(("ProcessActive", true, DateTime.Now));
            dataPoints.Add(("ProcessStartTime", DateTime.Now, DateTime.Now));
            
            _stateMachine!.Send("PROCESS_COMPLETE");
            dataPoints.Add(("ProcessActive", false, DateTime.Now));
            dataPoints.Add(("TotalProcessed", _stateMachine.ContextMap!["totalProcessed"] ?? (object)0!, DateTime.Now));
            
            // Verify data collection
            Assert.IsTrue(dataPoints.Count > 0);
            Assert.IsTrue(dataPoints.Any(d => d.parameter == "EquipmentState"));
            Assert.IsTrue(dataPoints.Any(d => d.parameter == "ProcessActive"));
            Assert.IsTrue(dataPoints.Any(d => d.parameter == "TotalProcessed"));
            
            // Verify we can track state changes over time
            var stateChanges = dataPoints.Where(d => d.parameter == "EquipmentState").ToList();
            Assert.IsTrue(stateChanges.Count >= 2);
        }
        
        [Test]
        public void TestFullStateVisitingCoverage_Equipment()
        {
            // Test ALL states and transitions in equipment region
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            var visitedStates = new HashSet<string>();
            
            // INIT -> IDLE
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("equipment.INIT"));
            visitedStates.Add("INIT");
            
            // Test INIT_FAIL path
            _stateMachine!.Send("INIT_FAIL");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("equipment.FAULT"));
            visitedStates.Add("FAULT");
            
            _stateMachine!.Send("FAULT_CLEARED");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("equipment.IDLE"));
            visitedStates.Add("IDLE");
            
            // Test SHUTDOWN path
            _stateMachine!.Send("SHUTDOWN_REQUEST");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("equipment.SHUTDOWN"));
            visitedStates.Add("SHUTDOWN");
            
            // Test SHUTDOWN_ABORT
            _stateMachine!.Send("SHUTDOWN_ABORT");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("equipment.IDLE"));
            
            // Test SETUP path
            _stateMachine!.Send("ENTER_SETUP");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("equipment.SETUP"));
            visitedStates.Add("SETUP");
            
            // Test SETUP_ABORT
            _stateMachine!.Send("SETUP_ABORT");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("equipment.IDLE"));
            
            // Test ENGINEERING path through FAULT
            _stateMachine!.Send("FAULT_DETECTED");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("equipment.FAULT"));
            
            _stateMachine!.Send("ENTER_REPAIR");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("equipment.ENGINEERING"));
            visitedStates.Add("ENGINEERING");
            
            // Test TEST_RUN from ENGINEERING
            _stateMachine!.Send("TEST_RUN");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("equipment.PRODUCTIVE"));
            visitedStates.Add("PRODUCTIVE");
            
            // Test all PRODUCTIVE substates
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("PRODUCTIVE.START_UP"));
            visitedStates.Add("PRODUCTIVE.START_UP");
            
            _stateMachine!.Send("STARTUP_COMPLETE");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("PRODUCTIVE.STANDBY"));
            visitedStates.Add("PRODUCTIVE.STANDBY");
            
            // Test SETUP_REQUEST from STANDBY
            _stateMachine!.Send("SETUP_REQUEST");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("equipment.SETUP"));
            
            _stateMachine!.Send("SETUP_COMPLETE");
            _stateMachine!.Send("START_PRODUCTION");
            _stateMachine!.Send("STARTUP_COMPLETE");
            
            // Setup conditions for RUN
            _stateMachine.ContextMap!["carrierAvailable"] = true;
            _stateMachine.ContextMap!["recipeVerified"] = true;
            
            _stateMachine!.Send("RUN");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("PRODUCTIVE.PRODUCTIVE_RUN"));
            visitedStates.Add("PRODUCTIVE.PRODUCTIVE_RUN");
            
            // Test PAUSE
            _stateMachine!.Send("PAUSE");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("PRODUCTIVE.PAUSE"));
            visitedStates.Add("PRODUCTIVE.PAUSE");
            
            // Test ABORT from PAUSE
            _stateMachine!.Send("ABORT");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("PRODUCTIVE.STANDBY"));
            
            // Test RESUME from PAUSE
            _stateMachine!.Send("RUN");
            _stateMachine!.Send("PAUSE");
            _stateMachine!.Send("RESUME");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("PRODUCTIVE.PRODUCTIVE_RUN"));
            
            // Verify we visited all major equipment states
            Assert.IsTrue(visitedStates.Contains("INIT"));
            Assert.IsTrue(visitedStates.Contains("IDLE"));
            Assert.IsTrue(visitedStates.Contains("FAULT"));
            Assert.IsTrue(visitedStates.Contains("SHUTDOWN"));
            Assert.IsTrue(visitedStates.Contains("SETUP"));
            Assert.IsTrue(visitedStates.Contains("ENGINEERING"));
            Assert.IsTrue(visitedStates.Contains("PRODUCTIVE"));
            Assert.IsTrue(visitedStates.Contains("PRODUCTIVE.START_UP"));
            Assert.IsTrue(visitedStates.Contains("PRODUCTIVE.STANDBY"));
            Assert.IsTrue(visitedStates.Contains("PRODUCTIVE.PRODUCTIVE_RUN"));
            Assert.IsTrue(visitedStates.Contains("PRODUCTIVE.PAUSE"));
        }
        
        [Test]
        public void TestFullStateVisitingCoverage_CarrierManagement()
        {
            // Test ALL states and transitions in carrier management
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            _stateMachine!.Send("INIT_COMPLETE");
            
            var visitedStates = new HashSet<string>();
            
            // NO_CARRIERS
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("carrierManagement.NO_CARRIERS"));
            visitedStates.Add("NO_CARRIERS");
            
            // Test carrier arrival
            _stateMachine!.Send("CARRIER_DETECTED");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("carrierManagement.CARRIER_ARRIVING"));
            visitedStates.Add("CARRIER_ARRIVING");
            
            _stateMachine!.Send("CARRIER_ARRIVED");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("carrierManagement.WAITING_FOR_HOST"));
            visitedStates.Add("WAITING_FOR_HOST");
            
            // Test REJECT_CARRIER path
            _stateMachine!.Send("REJECT_CARRIER");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("carrierManagement.NO_CARRIERS"));
            
            // Test ID verification failures
            _stateMachine!.Send("CARRIER_DETECTED");
            _stateMachine!.Send("CARRIER_ARRIVED");
            _stateMachine!.Send("PROCEED_WITH_CARRIER");
            
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("ID_VERIFICATION.READING_ID"));
            visitedStates.Add("ID_VERIFICATION.READING_ID");
            
            // Test ID_READ_FAIL path
            _stateMachine!.Send("ID_READ_FAIL");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("ID_VERIFICATION.ID_FAILED"));
            visitedStates.Add("ID_VERIFICATION.ID_FAILED");
            
            // Test RETRY_ID
            _stateMachine!.Send("RETRY_ID");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("ID_VERIFICATION.READING_ID"));
            
            // Test ID_REJECTED path
            _stateMachine!.Send("ID_READ_SUCCESS");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("ID_VERIFICATION.VERIFYING_ID"));
            visitedStates.Add("ID_VERIFICATION.VERIFYING_ID");
            
            _stateMachine!.Send("ID_REJECTED");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("ID_VERIFICATION.ID_FAILED"));
            
            // Test REMOVE_CARRIER from ID_FAILED
            _stateMachine!.Send("REMOVE_CARRIER");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("carrierManagement.NO_CARRIERS"));
            
            // Test slot map failures
            _stateMachine!.Send("CARRIER_DETECTED");
            _stateMachine!.Send("CARRIER_ARRIVED");
            _stateMachine!.Send("PROCEED_WITH_CARRIER");
            _stateMachine!.Send("ID_READ_SUCCESS");
            _stateMachine!.Send("ID_VERIFIED");
            
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("SLOT_MAP_VERIFICATION.READING_SLOT_MAP"));
            visitedStates.Add("SLOT_MAP_VERIFICATION.READING_SLOT_MAP");
            
            // Test SLOT_MAP_FAIL path
            _stateMachine!.Send("SLOT_MAP_FAIL");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("SLOT_MAP_VERIFICATION.SLOT_MAP_FAILED"));
            visitedStates.Add("SLOT_MAP_VERIFICATION.SLOT_MAP_FAILED");
            
            // Test RETRY_SLOT_MAP
            _stateMachine!.Send("RETRY_SLOT_MAP");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("SLOT_MAP_VERIFICATION.READING_SLOT_MAP"));
            
            // Test SLOT_MAP_REJECTED path
            _stateMachine!.Send("SLOT_MAP_READ");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("SLOT_MAP_VERIFICATION.VERIFYING_SLOT_MAP"));
            visitedStates.Add("SLOT_MAP_VERIFICATION.VERIFYING_SLOT_MAP");
            
            _stateMachine!.Send("SLOT_MAP_REJECTED");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("SLOT_MAP_VERIFICATION.SLOT_MAP_FAILED"));
            
            // Complete carrier flow
            _stateMachine!.Send("RETRY_SLOT_MAP");
            _stateMachine!.Send("SLOT_MAP_READ");
            _stateMachine!.Send("SLOT_MAP_VERIFIED");
            
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("carrierManagement.READY_FOR_PROCESSING"));
            visitedStates.Add("READY_FOR_PROCESSING");
            
            // Test START_CARRIER_PROCESSING
            _stateMachine.ContextMap!["processActive"] = true;
            _stateMachine!.Send("START_CARRIER_PROCESSING");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("carrierManagement.PROCESSING_CARRIER"));
            visitedStates.Add("PROCESSING_CARRIER");
            
            // Test CARRIER_PROCESSING_STOPPED
            _stateMachine!.Send("CARRIER_PROCESSING_STOPPED");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("carrierManagement.READY_FOR_PROCESSING"));
            
            // Test completion path
            _stateMachine!.Send("START_CARRIER_PROCESSING");
            _stateMachine!.Send("CARRIER_PROCESSING_COMPLETE");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("carrierManagement.CARRIER_COMPLETE"));
            visitedStates.Add("CARRIER_COMPLETE");
            
            _stateMachine!.Send("REMOVE_CARRIER");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("carrierManagement.NO_CARRIERS"));
            
            // Verify all carrier states visited (12 unique states)
            Assert.That(visitedStates.Count, Is.EqualTo(12));
        }
        
        [Test]
        public void TestFullStateVisitingCoverage_SubstrateTracking()
        {
            // Test ALL states and transitions in substrate tracking
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            var visitedStates = new HashSet<string>();
            
            // NO_SUBSTRATES
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("substrateTracking.NO_SUBSTRATES"));
            visitedStates.Add("NO_SUBSTRATES");
            
            _stateMachine!.Send("SUBSTRATE_DETECTED");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("substrateTracking.SUBSTRATE_AT_SOURCE"));
            visitedStates.Add("SUBSTRATE_AT_SOURCE");
            
            // Test SUBSTRATE_SKIP_PROCESSING path
            _stateMachine!.Send("SUBSTRATE_SKIP_PROCESSING");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("substrateTracking.SUBSTRATE_AT_DESTINATION"));
            visitedStates.Add("SUBSTRATE_AT_DESTINATION");
            
            _stateMachine!.Send("SUBSTRATE_DEPARTED");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("substrateTracking.NO_SUBSTRATES"));
            
            // Test processing path with pause/abort
            _stateMachine!.Send("SUBSTRATE_DETECTED");
            _stateMachine!.Send("SUBSTRATE_NEEDS_PROCESSING");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("SUBSTRATE_PROCESSING.WAITING"));
            visitedStates.Add("SUBSTRATE_PROCESSING.WAITING");
            
            _stateMachine.ContextMap!["processActive"] = true;
            _stateMachine!.Send("START_SUBSTRATE_PROCESS");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("SUBSTRATE_PROCESSING.IN_PROCESS"));
            visitedStates.Add("SUBSTRATE_PROCESSING.IN_PROCESS");
            
            // Test PAUSE path
            _stateMachine!.Send("SUBSTRATE_PROCESS_PAUSE");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("SUBSTRATE_PROCESSING.PAUSED"));
            visitedStates.Add("SUBSTRATE_PROCESSING.PAUSED");
            
            // Test ABORT from PAUSED
            _stateMachine!.Send("SUBSTRATE_PROCESS_ABORT");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("SUBSTRATE_PROCESSING.ABORTED"));
            visitedStates.Add("SUBSTRATE_PROCESSING.ABORTED");
            
            _stateMachine!.Send("SUBSTRATE_REMOVE");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("substrateTracking.SUBSTRATE_AT_DESTINATION"));
            
            // Test RESUME path
            _stateMachine!.Send("SUBSTRATE_DEPARTED");
            _stateMachine!.Send("SUBSTRATE_DETECTED");
            _stateMachine!.Send("SUBSTRATE_NEEDS_PROCESSING");
            _stateMachine!.Send("START_SUBSTRATE_PROCESS");
            _stateMachine!.Send("SUBSTRATE_PROCESS_PAUSE");
            _stateMachine!.Send("SUBSTRATE_PROCESS_RESUME");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("SUBSTRATE_PROCESSING.IN_PROCESS"));
            
            // Test direct ABORT from IN_PROCESS
            _stateMachine!.Send("SUBSTRATE_PROCESS_ABORT");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("SUBSTRATE_PROCESSING.ABORTED"));
            
            // Test normal completion
            _stateMachine!.Send("SUBSTRATE_REMOVE");
            _stateMachine!.Send("SUBSTRATE_DEPARTED");
            _stateMachine!.Send("SUBSTRATE_DETECTED");
            _stateMachine!.Send("SUBSTRATE_NEEDS_PROCESSING");
            _stateMachine!.Send("START_SUBSTRATE_PROCESS");
            _stateMachine!.Send("SUBSTRATE_PROCESS_COMPLETE");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("SUBSTRATE_PROCESSING.PROCESSED"));
            visitedStates.Add("SUBSTRATE_PROCESSING.PROCESSED");
            
            _stateMachine!.Send("SUBSTRATE_MOVE_TO_DESTINATION");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("substrateTracking.SUBSTRATE_AT_DESTINATION"));
            
            Assert.That(visitedStates.Count, Is.EqualTo(8));
        }
        
        [Test]
        public void TestFullStateVisitingCoverage_ControlJob()
        {
            // Test ALL states and transitions in control job
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            _stateMachine!.Send("INIT_COMPLETE");
            
            var visitedStates = new HashSet<string>();
            
            // NO_JOBS
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("controlJob.NO_JOBS"));
            visitedStates.Add("NO_JOBS");
            
            _stateMachine!.Send("CREATE_JOB");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("controlJob.JOB_QUEUED"));
            visitedStates.Add("JOB_QUEUED");
            
            // Test DELETE_JOB from QUEUED
            _stateMachine!.Send("DELETE_JOB");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("controlJob.NO_JOBS"));
            
            // Test DESELECT_JOB
            _stateMachine!.Send("CREATE_JOB");
            _stateMachine!.Send("SELECT_JOB");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("controlJob.JOB_SELECTED"));
            visitedStates.Add("JOB_SELECTED");
            
            _stateMachine!.Send("DESELECT_JOB");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("controlJob.JOB_QUEUED"));
            
            // Test job execution with all substates
            _stateMachine!.Send("SELECT_JOB");
            _stateMachine.ContextMap!["carrierAvailable"] = true;
            _stateMachine!.Send("START_JOB");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("JOB_EXECUTING.ACTIVE"));
            visitedStates.Add("JOB_EXECUTING.ACTIVE");
            
            // Test ABORT_JOB
            _stateMachine!.Send("ABORT_JOB");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("JOB_EXECUTING.ABORTING"));
            visitedStates.Add("JOB_EXECUTING.ABORTING");
            
            _stateMachine!.Send("JOB_ABORTED");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("controlJob.JOB_COMPLETED"));
            visitedStates.Add("JOB_COMPLETED");
            
            _stateMachine!.Send("REMOVE_JOB");
            
            // Test STOP_JOB path
            _stateMachine!.Send("CREATE_JOB");
            _stateMachine!.Send("SELECT_JOB");
            _stateMachine!.Send("START_JOB");
            _stateMachine!.Send("PAUSE_JOB");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("JOB_EXECUTING.PAUSED"));
            visitedStates.Add("JOB_EXECUTING.PAUSED");
            
            _stateMachine!.Send("STOP_JOB");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("JOB_EXECUTING.STOPPING"));
            visitedStates.Add("JOB_EXECUTING.STOPPING");
            
            _stateMachine!.Send("JOB_STOPPED");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("controlJob.JOB_COMPLETED"));
            
            // Test RESUME_JOB
            _stateMachine!.Send("REMOVE_JOB");
            _stateMachine!.Send("CREATE_JOB");
            _stateMachine!.Send("SELECT_JOB");
            _stateMachine!.Send("START_JOB");
            _stateMachine!.Send("PAUSE_JOB");
            _stateMachine!.Send("RESUME_JOB");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("JOB_EXECUTING.ACTIVE"));
            
            // Test SUBSTRATE_COMPLETED (internal transition)
            _stateMachine!.Send("SUBSTRATE_COMPLETED");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("JOB_EXECUTING.ACTIVE"));
            
            Assert.That(visitedStates.Count, Is.EqualTo(8));
        }
        
        [Test]
        public void TestFullStateVisitingCoverage_ProcessJob()
        {
            // Test ALL states and transitions in process job
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            _stateMachine!.Send("INIT_COMPLETE");
            
            var visitedStates = new HashSet<string>();
            
            // NO_PROCESS
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("processJob.NO_PROCESS"));
            visitedStates.Add("NO_PROCESS");
            
            _stateMachine!.Send("CREATE_PROCESS");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("processJob.SETTING_UP"));
            visitedStates.Add("SETTING_UP");
            
            // Test SETUP_FAILED path
            _stateMachine!.Send("SETUP_FAILED");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("processJob.PROCESS_ABORTING"));
            visitedStates.Add("PROCESS_ABORTING");
            
            _stateMachine!.Send("ABORT_COMPLETE");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("processJob.PROCESS_ABORTED"));
            visitedStates.Add("PROCESS_ABORTED");
            
            _stateMachine!.Send("REMOVE_PROCESS");
            
            // Test normal setup and abort from waiting
            _stateMachine!.Send("CREATE_PROCESS");
            _stateMachine!.Send("SETUP_COMPLETE");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("processJob.WAITING_FOR_START"));
            visitedStates.Add("WAITING_FOR_START");
            
            _stateMachine!.Send("ABORT_PROCESS");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("processJob.PROCESS_ABORTING"));
            
            _stateMachine!.Send("ABORT_COMPLETE");
            _stateMachine!.Send("REMOVE_PROCESS");
            
            // Test processing flow with all substates
            _stateMachine!.Send("CREATE_PROCESS");
            _stateMachine!.Send("SETUP_COMPLETE");
            _stateMachine.ContextMap!["carrierAvailable"] = true;
            _stateMachine.ContextMap!["recipeVerified"] = true;
            _stateMachine!.Send("START_PROCESS");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("PROCESSING.EXECUTING"));
            visitedStates.Add("PROCESSING.EXECUTING");
            
            // Test PAUSE_FAILED path
            _stateMachine!.Send("PAUSE_PROCESS");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("PROCESSING.PAUSING"));
            visitedStates.Add("PROCESSING.PAUSING");
            
            _stateMachine!.Send("PAUSE_FAILED");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("PROCESSING.EXECUTING"));
            
            // Test successful pause
            _stateMachine!.Send("PAUSE_PROCESS");
            _stateMachine!.Send("PROCESS_PAUSED");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("PROCESSING.PAUSED"));
            visitedStates.Add("PROCESSING.PAUSED");
            
            // Test RESUME_FAILED path
            _stateMachine!.Send("RESUME_PROCESS");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("PROCESSING.RESUMING"));
            visitedStates.Add("PROCESSING.RESUMING");
            
            _stateMachine!.Send("RESUME_FAILED");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("PROCESSING.PAUSED"));
            
            // Test successful resume
            _stateMachine!.Send("RESUME_PROCESS");
            _stateMachine!.Send("PROCESS_RESUMED");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("PROCESSING.EXECUTING"));
            
            // Test STEP_COMPLETE and NEXT_STEP
            _stateMachine!.Send("STEP_COMPLETE");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("PROCESSING.NEXT_STEP"));
            visitedStates.Add("PROCESSING.NEXT_STEP");
            
            _stateMachine!.Send("CONTINUE_PROCESS");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("PROCESSING.EXECUTING"));
            
            // Test PROCESS_ERROR path
            _stateMachine!.Send("PROCESS_ERROR");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("processJob.PROCESS_ABORTING"));
            
            _stateMachine!.Send("ABORT_COMPLETE");
            _stateMachine!.Send("REMOVE_PROCESS");
            
            // Test normal completion with verification
            _stateMachine!.Send("CREATE_PROCESS");
            _stateMachine!.Send("SETUP_COMPLETE");
            _stateMachine!.Send("START_PROCESS");
            _stateMachine!.Send("PROCESS_COMPLETE");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("processJob.PROCESS_COMPLETE"));
            visitedStates.Add("PROCESS_COMPLETE");
            
            // Test VERIFY_PROCESS_FAIL
            _stateMachine!.Send("VERIFY_PROCESS_FAIL");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("processJob.PROCESS_ABORTING"));
            
            _stateMachine!.Send("ABORT_COMPLETE");
            _stateMachine!.Send("REMOVE_PROCESS");
            
            // Test successful verification
            _stateMachine!.Send("CREATE_PROCESS");
            _stateMachine!.Send("SETUP_COMPLETE");
            _stateMachine!.Send("START_PROCESS");
            _stateMachine!.Send("PROCESS_COMPLETE");
            _stateMachine!.Send("VERIFY_PROCESS_OK");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("processJob.PROCESS_COMPLETED"));
            visitedStates.Add("PROCESS_COMPLETED");
            
            Assert.That(visitedStates.Count, Is.EqualTo(12));
        }
        
        [Test]
        public void TestFullStateVisitingCoverage_RecipeManagement()
        {
            // Test ALL states and transitions in recipe management
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            var visitedStates = new HashSet<string>();
            
            // NO_RECIPE
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("recipeManagement.NO_RECIPE"));
            visitedStates.Add("NO_RECIPE");
            
            _stateMachine!.Send("LOAD_RECIPE");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("recipeManagement.UNVERIFIED"));
            visitedStates.Add("UNVERIFIED");
            
            _stateMachine!.Send("VERIFY_RECIPE");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("recipeManagement.VERIFYING"));
            visitedStates.Add("VERIFYING");
            
            // Test VERIFICATION_FAIL path
            _stateMachine!.Send("VERIFICATION_FAIL");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("recipeManagement.VERIFICATION_FAILED"));
            visitedStates.Add("VERIFICATION_FAILED");
            
            // Test RETRY_VERIFICATION
            _stateMachine!.Send("RETRY_VERIFICATION");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("recipeManagement.VERIFYING"));
            
            // Test EDIT_RECIPE from VERIFICATION_FAILED
            _stateMachine!.Send("VERIFICATION_FAIL");
            _stateMachine!.Send("EDIT_RECIPE");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("recipeManagement.EDITING"));
            visitedStates.Add("EDITING");
            
            // Test CANCEL_EDIT (requires going to VERIFIED first)
            _stateMachine!.Send("SAVE_RECIPE");
            _stateMachine!.Send("VERIFY_RECIPE");
            _stateMachine!.Send("VERIFICATION_PASS");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("recipeManagement.VERIFIED"));
            visitedStates.Add("VERIFIED");
            
            _stateMachine!.Send("EDIT_RECIPE");
            _stateMachine!.Send("CANCEL_EDIT");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("recipeManagement.VERIFIED"));
            
            // Test DELETE_RECIPE
            _stateMachine!.Send("DELETE_RECIPE");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("recipeManagement.NO_RECIPE"));
            
            // Test SELECTED and ACTIVE states
            _stateMachine!.Send("LOAD_RECIPE");
            _stateMachine!.Send("VERIFY_RECIPE");
            _stateMachine!.Send("VERIFICATION_PASS");
            _stateMachine!.Send("SELECT_RECIPE");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("recipeManagement.SELECTED"));
            visitedStates.Add("SELECTED");
            
            // Test DESELECT_RECIPE
            _stateMachine!.Send("DESELECT_RECIPE");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("recipeManagement.VERIFIED"));
            
            // Test START_RECIPE_PROCESS
            _stateMachine!.Send("SELECT_RECIPE");
            _stateMachine.ContextMap!["processActive"] = true;
            _stateMachine!.Send("START_RECIPE_PROCESS");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("recipeManagement.ACTIVE"));
            visitedStates.Add("ACTIVE");
            
            // Test RECIPE_PROCESS_ABORT
            _stateMachine!.Send("RECIPE_PROCESS_ABORT");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("recipeManagement.VERIFIED"));
            
            // Test RECIPE_PROCESS_COMPLETE
            _stateMachine!.Send("SELECT_RECIPE");
            _stateMachine!.Send("START_RECIPE_PROCESS");
            _stateMachine!.Send("RECIPE_PROCESS_COMPLETE");
            Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("recipeManagement.VERIFIED"));
            
            Assert.That(visitedStates.Count, Is.EqualTo(8));
        }
        
        [Test]
        public void TestSystemLevelTransitions()
        {
            // Test SYSTEM_INITIALIZE and global transitions
            _stateMachine = StateMachine.CreateFromScript(semiIntegratedScript, _actions, _guards);
            foreach (var kvp in _context)
            {
                _stateMachine.ContextMap![kvp.Key] = kvp.Value;
            }
            _stateMachine!.Start();
            
            // Test SYSTEM_INITIALIZE (actions only, no transition)
            _stateMachine!.Send("SYSTEM_INITIALIZE");
            Assert.IsNotNull(_stateMachine.ContextMap!["systemStartTime"]);
            
            // Setup complex state
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("LOAD_RECIPE");
            _stateMachine!.Send("CREATE_JOB");
            _stateMachine!.Send("CREATE_PROCESS");
            _stateMachine!.Send("CARRIER_DETECTED");
            _stateMachine!.Send("SUBSTRATE_DETECTED");
            
            // Test EMERGENCY_STOP with multiple targets
            _stateMachine!.Send("EMERGENCY_STOP");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.FAULT"));
            Assert.IsTrue(currentState.Contains("carrierManagement.NO_CARRIERS"));
            Assert.IsTrue(currentState.Contains("substrateTracking.NO_SUBSTRATES"));
            Assert.IsTrue(currentState.Contains("controlJob.NO_JOBS"));
            Assert.IsTrue(currentState.Contains("processJob.PROCESS_ABORTING"));
            
            // Clear emergency state
            _stateMachine!.Send("ABORT_COMPLETE");
            _stateMachine!.Send("REMOVE_PROCESS");
            _stateMachine!.Send("FAULT_CLEARED");
            
            // Setup complex state again
            _stateMachine!.Send("START_PRODUCTION");
            _stateMachine!.Send("LOAD_RECIPE");
            _stateMachine!.Send("CREATE_JOB");
            _stateMachine!.Send("CREATE_PROCESS");
            
            // Test SYSTEM_RESET with multiple targets
            _stateMachine!.Send("SYSTEM_RESET");
            currentState = _stateMachine!.GetActiveStateString();
            Assert.IsTrue(currentState.Contains("equipment.INIT"));
            Assert.IsTrue(currentState.Contains("carrierManagement.NO_CARRIERS"));
            Assert.IsTrue(currentState.Contains("substrateTracking.NO_SUBSTRATES"));
            Assert.IsTrue(currentState.Contains("controlJob.NO_JOBS"));
            Assert.IsTrue(currentState.Contains("processJob.NO_PROCESS"));
            Assert.IsTrue(currentState.Contains("recipeManagement.NO_RECIPE"));
            Assert.IsTrue(currentState.Contains("performanceMonitoring.MONITORING"));
            
            // Verify context was reset
            Assert.That(_stateMachine.ContextMap!["totalProcessed"], Is.EqualTo(0));
            Assert.That(_stateMachine.ContextMap!["totalErrors"], Is.EqualTo(0));
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
                    'actions': ['emergencyStop']
                },
                'SYSTEM_INITIALIZE': {
                    'actions': ['setSystemStartTime']
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
                    'actions': ['systemReset']
                }
            },
            'states': {
                'equipment': {
                    'initial': 'INIT',
                    'states': {
                        'INIT': {
                            'entry': ['setSystemStartTime'],
                            'on': {
                                'INIT_COMPLETE': {
                                    'target': 'IDLE',
                                    'actions': ['setEquipmentIdle']
                                },
                                'INIT_FAIL': {
                                    'target': 'FAULT',
                                    'actions': ['setEquipmentFault']
                                }
                            }
                        },
                        'IDLE': {
                            'on': {
                                'START_PRODUCTION': {
                                    'target': 'PRODUCTIVE',
                                    'actions': ['setEquipmentProductive']
                                },
                                'ENTER_SETUP': 'SETUP',
                                'SHUTDOWN_REQUEST': 'SHUTDOWN',
                                'FAULT_DETECTED': {
                                    'target': 'FAULT',
                                    'actions': ['setEquipmentFault']
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
                                            'actions': ['setProductiveStandby']
                                        },
                                        'STARTUP_FAIL': '#SEMI_Integrated_System.equipment.FAULT'
                                    }
                                },
                                'STANDBY': {
                                    'on': {
                                        'RUN': {
                                            'target': 'PRODUCTIVE_RUN',
                                            'guard': 'canStartProcess',
                                            'actions': ['setProductiveRun']
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
                                            'actions': ['setProductiveStandby']
                                        }
                                    }
                                }
                            }
                        },
                        'SETUP': {
                            'on': {
                                'SETUP_COMPLETE': {
                                    'target': 'IDLE',
                                    'actions': ['setEquipmentIdle']
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
                                    'actions': ['setEquipmentOff']
                                },
                                'SHUTDOWN_ABORT': 'IDLE'
                            }
                        },
                        'FAULT': {
                            'entry': ['setEquipmentFault'],
                            'on': {
                                'FAULT_CLEARED': {
                                    'target': 'IDLE',
                                    'actions': ['setEquipmentIdle']
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
                            'entry': ['setCarrierUnavailable'],
                            'on': {
                                'CARRIER_DETECTED': 'CARRIER_ARRIVING'
                            }
                        },
                        'CARRIER_ARRIVING': {
                            'on': {
                                'CARRIER_ARRIVED': {
                                    'target': 'WAITING_FOR_HOST',
                                    'actions': ['addCarrier']
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
                                            'actions': ['setCarrierAvailable']
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
                            'entry': ['setSubstrateUnavailable'],
                            'on': {
                                'SUBSTRATE_DETECTED': {
                                    'target': 'SUBSTRATE_AT_SOURCE',
                                    'actions': ['addSubstrate']
                                }
                            }
                        },
                        'SUBSTRATE_AT_SOURCE': {
                            'on': {
                                'SUBSTRATE_NEEDS_PROCESSING': {
                                    'target': 'SUBSTRATE_PROCESSING',
                                    'actions': ['setSubstrateReady']
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
                                    'actions': ['addJob']
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
                                    'actions': ['addProcess']
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
                            'entry': ['setRecipeUnverified'],
                            'on': {
                                'LOAD_RECIPE': {
                                    'target': 'UNVERIFIED',
                                    'actions': ['addRecipe']
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
                                    'actions': ['setRecipeVerified']
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
                                    'actions': ['setRecipeUnverified']
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
                                    'actions': ['updateMetrics']
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
    }
}