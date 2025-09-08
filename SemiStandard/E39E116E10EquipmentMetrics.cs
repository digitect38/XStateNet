using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SemiStandard;

namespace SemiStandard.E39E116E10
{
    /// <summary>
    /// SEMI E39/E116/E10 Equipment Reliability and Productivity Metrics
    /// E10: Equipment Reliability, Availability and Maintainability (RAM)
    /// E116: Equipment Performance Tracking  
    /// E39: Object Services for equipment performance tracking
    /// </summary>
    public class EquipmentMetrics
    {
        private readonly StateMachineAdapter _stateMachine;
        private readonly string _equipmentId;
        private readonly List<StateTransition> _stateHistory = new();
        private readonly Dictionary<E10State, TimeSpan> _stateDurations = new();
        private DateTime _currentStateStartTime;
        private E10State _previousState;
        
        public string EquipmentId => _equipmentId;
        public E10State CurrentState { get; private set; }
        public E116PerformanceMetrics PerformanceMetrics { get; private set; }
        
        /// <summary>
        /// SEMI E10 Equipment States
        /// Six-state model for equipment productivity
        /// </summary>
        public enum E10State
        {
            NonScheduled,           // Equipment not scheduled for production
            ScheduledDowntime,      // Planned maintenance, PM, calibration
            UnscheduledDowntime,    // Unplanned failures, breakdowns
            Engineering,            // Engineering runs, experiments, tests
            StandBy,               // Idle, waiting for work
            Productive             // Actually processing material
        }
        
        /// <summary>
        /// E10 State Categories for OEE calculation
        /// </summary>
        public enum StateCategory
        {
            TotalTime,             // Calendar time
            OperationsTime,        // Total - NonScheduled
            UpTime,               // Operations - ScheduledDowntime
            ProductionTime,       // UpTime - UnscheduledDowntime
            NetProductionTime,    // ProductionTime - Engineering
            ValueAddingTime       // Actual productive time
        }
        
        /// <summary>
        /// E116 Reason Codes for state transitions
        /// </summary>
        public enum E116ReasonCode
        {
            // Productive reasons
            ProcessingLot = 1000,
            ProcessingBatch = 1001,
            
            // StandBy reasons
            NoMaterial = 2000,
            NoOperator = 2001,
            NoRecipe = 2002,
            WaitingForCluster = 2003,
            
            // Engineering reasons  
            ProcessDevelopment = 3000,
            EquipmentTest = 3001,
            Qualification = 3002,
            
            // Unscheduled Downtime reasons
            EquipmentFailure = 4000,
            ProcessFailure = 4001,
            FacilityFailure = 4002,
            OperatorError = 4003,
            
            // Scheduled Downtime reasons
            PreventiveMaintenance = 5000,
            Calibration = 5001,
            Setup = 5002,
            
            // Non-Scheduled reasons
            NoProduction = 6000,
            Shutdown = 6001
        }

        public EquipmentMetrics(string equipmentId)
        {
            _equipmentId = equipmentId;
            PerformanceMetrics = new E116PerformanceMetrics();
            
            // Load configuration from JSON file
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "SemiStandard.XStateScripts.E39E116E10EquipmentMetrics.json";
            string config;

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    // Fallback to file system if embedded resource not found
                    var configPath = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? "", "XStateScripts", "E39E116E10EquipmentMetrics.json");
                    config = File.ReadAllText(configPath);
                }
                else
                {
                    using (var reader = new StreamReader(stream))
                    {
                        config = reader.ReadToEnd();
                    }
                }
            }

            _stateMachine = StateMachineFactory.Create(config);
            
            // Register conditions
            _stateMachine.RegisterCondition("isScheduled", (ctx, evt) =>
            {
                return ctx["scheduled"] != null && (bool)ctx["scheduled"];
            });
            
            _stateMachine.RegisterCondition("hasWork", (ctx, evt) =>
            {
                return PerformanceMetrics.QueuedLots > 0;
            });
            
            _stateMachine.RegisterCondition("canProcess", (ctx, evt) =>
            {
                return evt.Data is ProcessingRequest req && 
                       req.MaterialAvailable && 
                       req.RecipeAvailable &&
                       req.OperatorAvailable;
            });
            
            // Register actions
            _stateMachine.RegisterAction("recordStateEntry", (ctx, evt) =>
            {
                _previousState = CurrentState;
                _currentStateStartTime = DateTime.UtcNow;
                
                int reasonCode = 0;
                try
                {
                    // Try to get Data property if it exists
                    dynamic dynamicEvt = evt;
                    if (dynamicEvt != null)
                    {
                        var dataProperty = dynamicEvt.GetType().GetProperty("Data");
                        if (dataProperty != null)
                        {
                            var data = dataProperty.GetValue(dynamicEvt);
                            if (data is StateChangeData stateData)
                            {
                                reasonCode = stateData.ReasonCode;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore if Data property doesn't exist
                }
                
                var transition = new StateTransition
                {
                    FromState = _previousState,
                    ToState = CurrentState,
                    Timestamp = _currentStateStartTime,
                    ReasonCode = reasonCode
                };
                
                _stateHistory.Add(transition);
                ctx["currentStateStartTime"] = _currentStateStartTime;
                ctx["previousState"] = _previousState.ToString();
                
                Console.WriteLine($"[E10] Equipment {_equipmentId}: {_previousState} -> {CurrentState}");
            });
            
            _stateMachine.RegisterAction("recordStateExit", (ctx, evt) =>
            {
                var duration = DateTime.UtcNow - _currentStateStartTime;
                
                if (!_stateDurations.ContainsKey(CurrentState))
                    _stateDurations[CurrentState] = TimeSpan.Zero;
                    
                _stateDurations[CurrentState] += duration;
            });
            
            _stateMachine.RegisterAction("updateMetrics", (ctx, evt) =>
            {
                CalculateMetrics();
            });
            
            _stateMachine.RegisterAction("startProcessing", (ctx, evt) =>
            {
                if (evt.Data is ProcessingRequest req)
                {
                    PerformanceMetrics.CurrentLotId = req.LotId;
                    PerformanceMetrics.ProcessingStartTime = DateTime.UtcNow;
                    Console.WriteLine($"[E116] Starting processing: Lot {req.LotId}");
                }
            });
            
            _stateMachine.RegisterAction("completeProcessing", (ctx, evt) =>
            {
                PerformanceMetrics.LotsProcessed++;
                PerformanceMetrics.ProcessingEndTime = DateTime.UtcNow;
                
                if (evt.Data is ProcessingResult result)
                {
                    PerformanceMetrics.WafersProcessed += result.WaferCount;
                    PerformanceMetrics.GoodWafers += result.GoodWafers;
                }
            });
            
            _stateMachine.RegisterAction("updateYield", (ctx, evt) =>
            {
                if (PerformanceMetrics.WafersProcessed > 0)
                {
                    PerformanceMetrics.Yield = 
                        (double)PerformanceMetrics.GoodWafers / PerformanceMetrics.WafersProcessed * 100;
                }
            });
            
            _stateMachine.RegisterAction("recordFault", (ctx, evt) =>
            {
                PerformanceMetrics.FaultCount++;
                
                if (evt.Data is FaultData fault)
                {
                    ctx["lastFault"] = fault.FaultCode;
                    Console.WriteLine($"[E10] Fault occurred: {fault.FaultCode} - {fault.Description}");
                }
            });
            
            _stateMachine.RegisterAction("recordLotComplete", (ctx, evt) =>
            {
                if (evt.Data is LotCompleteData data)
                {
                    PerformanceMetrics.LotsProcessed++;
                    Console.WriteLine($"[E116] Lot complete: {data.LotId}, Yield: {data.Yield:F2}%");
                }
            });
            
            _stateMachine.RegisterAction("notifyAlarm", (ctx, evt) =>
            {
                OnAlarmRaised?.Invoke(this, new AlarmEventArgs 
                { 
                    AlarmType = "UnscheduledDowntime",
                    Timestamp = DateTime.UtcNow 
                });
            });
            
            _stateMachine.RegisterAction("startProductionTracking", (ctx, evt) =>
            {
                PerformanceMetrics.StartProductionTracking();
            });
            
            _stateMachine.RegisterAction("stopProductionTracking", (ctx, evt) =>
            {
                PerformanceMetrics.StopProductionTracking();
            });
            
            _stateMachine.RegisterAction("notifyScheduledMaintenance", (ctx, evt) =>
            {
                Console.WriteLine($"[E39] Equipment {_equipmentId}: Scheduled maintenance notification");
            });
            
            _stateMachine.RegisterAction("recordIdleReason", (ctx, evt) =>
            {
                Console.WriteLine($"[E39] Equipment {_equipmentId}: Recording idle reason");
            });
            
            _stateMachine.RegisterAction("recordWaferComplete", (ctx, evt) =>
            {
                PerformanceMetrics.WafersProcessed++;
                Console.WriteLine($"[E39] Equipment {_equipmentId}: Wafer completed");
            });
            
            _stateMachine.RegisterAction("abortProcessing", (ctx, evt) =>
            {
                Console.WriteLine($"[E39] Equipment {_equipmentId}: Aborting processing");
            });
            
            _stateMachine.RegisterAction("logError", (ctx, evt) =>
            {
                Console.WriteLine($"[E39] Equipment {_equipmentId}: Error occurred in unscheduled downtime");
            });
            
            _stateMachine.RegisterAction("calculateMetrics", (ctx, evt) =>
            {
                Console.WriteLine($"[E39] Equipment {_equipmentId}: Calculating OEE metrics");
            });
            
            _stateMachine.RegisterAction("updateAvailability", (ctx, evt) =>
            {
                Console.WriteLine($"[E39] Equipment {_equipmentId}: Updating availability metrics");
            });
            
            _stateMachine.RegisterAction("updatePerformance", (ctx, evt) =>
            {
                Console.WriteLine($"[E39] Equipment {_equipmentId}: Updating performance metrics");
            });
            
            _stateMachine.RegisterAction("updateQuality", (ctx, evt) =>
            {
                Console.WriteLine($"[E39] Equipment {_equipmentId}: Updating quality metrics");
            });
            
            _stateMachine.RegisterAction("generateOEEReport", (ctx, evt) =>
            {
                Console.WriteLine($"[E39] Equipment {_equipmentId}: Generating OEE report");
            });
            
            _stateMachine.RegisterAction("notifyError", (ctx, evt) =>
            {
                Console.WriteLine($"[E39] Equipment {_equipmentId}: Notifying error in process state");
            });
            
            _stateMachine.Start();
            UpdateState();
        }
        
        private void UpdateState()
        {
            var state = _stateMachine.CurrentStates.FirstOrDefault()?.Name;
            if (string.IsNullOrEmpty(state) || state == "Unknown")
            {
                CurrentState = E10State.NonScheduled;
            }
            else
            {
                CurrentState = Enum.Parse<E10State>(state);
            }
        }
        
        private void CalculateMetrics()
        {
            var totalTime = GetTotalTime();
            if (totalTime.TotalSeconds == 0) return;
            
            // Calculate E10 metrics
            var operationsTime = totalTime - GetStateDuration(E10State.NonScheduled);
            var upTime = operationsTime - GetStateDuration(E10State.ScheduledDowntime);
            var productionTime = upTime - GetStateDuration(E10State.UnscheduledDowntime);
            var netProductionTime = productionTime - GetStateDuration(E10State.Engineering);
            var productiveTime = GetStateDuration(E10State.Productive);
            
            // Update performance metrics
            PerformanceMetrics.Availability = upTime.TotalSeconds / operationsTime.TotalSeconds * 100;
            PerformanceMetrics.OperationalEfficiency = productiveTime.TotalSeconds / upTime.TotalSeconds * 100;
            PerformanceMetrics.RateEfficiency = CalculateRateEfficiency();
            PerformanceMetrics.QualityRate = CalculateQualityRate();
            
            // Calculate OEE (Overall Equipment Effectiveness)
            PerformanceMetrics.OEE = (PerformanceMetrics.Availability / 100) *
                                     (PerformanceMetrics.OperationalEfficiency / 100) *
                                     (PerformanceMetrics.QualityRate / 100) * 100;
            
            // Calculate MTBF (Mean Time Between Failures)
            if (PerformanceMetrics.FaultCount > 0)
            {
                PerformanceMetrics.MTBF = upTime.TotalHours / PerformanceMetrics.FaultCount;
            }
            
            // Calculate MTTR (Mean Time To Repair)
            var repairTime = GetStateDuration(E10State.UnscheduledDowntime);
            if (PerformanceMetrics.FaultCount > 0)
            {
                PerformanceMetrics.MTTR = repairTime.TotalHours / PerformanceMetrics.FaultCount;
            }
        }
        
        private double CalculateRateEfficiency()
        {
            if (PerformanceMetrics.TheoreticalRate == 0) return 100;
            
            var actualRate = PerformanceMetrics.WafersProcessed / GetStateDuration(E10State.Productive).TotalHours;
            return actualRate / PerformanceMetrics.TheoreticalRate * 100;
        }
        
        private double CalculateQualityRate()
        {
            if (PerformanceMetrics.WafersProcessed == 0) return 100;
            return (double)PerformanceMetrics.GoodWafers / PerformanceMetrics.WafersProcessed * 100;
        }
        
        private TimeSpan GetTotalTime()
        {
            if (!_stateHistory.Any()) return TimeSpan.Zero;
            
            var firstEntry = _stateHistory.First().Timestamp;
            return DateTime.UtcNow - firstEntry;
        }
        
        private TimeSpan GetStateDuration(E10State state)
        {
            if (!_stateDurations.ContainsKey(state))
                return TimeSpan.Zero;
                
            var duration = _stateDurations[state];
            
            // Add current state duration if still in this state
            if (CurrentState == state)
            {
                duration += DateTime.UtcNow - _currentStateStartTime;
            }
            
            return duration;
        }
        
        // Public methods for state transitions
        public void Schedule()
        {
            _stateMachine.Send("SCHEDULE");
            UpdateState();
        }
        
        public void StartMaintenance(E116ReasonCode reasonCode)
        {
            _stateMachine.Send(new StateMachineEvent
            {
                Name = "MAINTENANCE_SCHEDULED",
                Data = new StateChangeData { ReasonCode = (int)reasonCode }
            });
            UpdateState();
        }
        
        public void CompleteMaintenance()
        {
            _stateMachine.Send("MAINTENANCE_COMPLETE");
            UpdateState();
        }
        
        public void ReportFault(string faultCode, string description)
        {
            _stateMachine.Send(new StateMachineEvent
            {
                Name = "FAULT",
                Data = new FaultData { FaultCode = faultCode, Description = description }
            });
            UpdateState();
        }
        
        public void CompleteRepair()
        {
            _stateMachine.Send("REPAIR_COMPLETE");
            UpdateState();
        }
        
        public void StartProcessing(string lotId, string recipeId, bool materialAvailable = true)
        {
            _stateMachine.Send(new StateMachineEvent
            {
                Name = "START_PROCESSING",
                Data = new ProcessingRequest
                {
                    LotId = lotId,
                    RecipeId = recipeId,
                    MaterialAvailable = materialAvailable,
                    RecipeAvailable = true,
                    OperatorAvailable = true
                }
            });
            UpdateState();
        }
        
        public void CompleteProcessing(int waferCount, int goodWafers)
        {
            _stateMachine.Send(new StateMachineEvent
            {
                Name = "PROCESSING_COMPLETE",
                Data = new ProcessingResult
                {
                    WaferCount = waferCount,
                    GoodWafers = goodWafers
                }
            });
            UpdateState();
        }
        
        public void StartEngineering(E116ReasonCode reasonCode)
        {
            _stateMachine.Send(new StateMachineEvent
            {
                Name = "ENGINEERING_REQUEST",
                Data = new StateChangeData { ReasonCode = (int)reasonCode }
            });
            UpdateState();
        }
        
        public void CompleteEngineering()
        {
            _stateMachine.Send("ENGINEERING_COMPLETE");
            UpdateState();
        }
        
        // Get comprehensive metrics report
        public E10MetricsReport GetMetricsReport()
        {
            CalculateMetrics();
            
            return new E10MetricsReport
            {
                EquipmentId = _equipmentId,
                CurrentState = CurrentState,
                ReportTime = DateTime.UtcNow,
                TotalTime = GetTotalTime(),
                StateDurations = new Dictionary<E10State, TimeSpan>(_stateDurations),
                PerformanceMetrics = PerformanceMetrics,
                StateHistory = _stateHistory.ToList()
            };
        }
        
        // Events
        public event EventHandler<AlarmEventArgs>? OnAlarmRaised;
        public event EventHandler<StateTransition>? OnStateChanged;
        
        // Data classes
        public class StateTransition
        {
            public E10State FromState { get; set; }
            public E10State ToState { get; set; }
            public DateTime Timestamp { get; set; }
            public int ReasonCode { get; set; }
        }
        
        public class E116PerformanceMetrics
        {
            // Production metrics
            public int LotsProcessed { get; set; }
            public int WafersProcessed { get; set; }
            public int GoodWafers { get; set; }
            public double Yield { get; set; }
            
            // Time metrics
            public DateTime? ProcessingStartTime { get; set; }
            public DateTime? ProcessingEndTime { get; set; }
            public TimeSpan CycleTime { get; set; }
            
            // Efficiency metrics
            public double Availability { get; set; }         // A in OEE
            public double OperationalEfficiency { get; set; } // P in OEE
            public double QualityRate { get; set; }          // Q in OEE
            public double OEE { get; set; }                  // Overall Equipment Effectiveness
            public double RateEfficiency { get; set; }
            
            // Reliability metrics
            public double MTBF { get; set; }  // Mean Time Between Failures (hours)
            public double MTTR { get; set; }  // Mean Time To Repair (hours)
            public int FaultCount { get; set; }
            
            // Current production
            public string? CurrentLotId { get; set; }
            public int QueuedLots { get; set; }
            public double TheoreticalRate { get; set; } = 60; // Wafers per hour
            
            private DateTime? _trackingStartTime;
            
            public void StartProductionTracking()
            {
                _trackingStartTime = DateTime.UtcNow;
            }
            
            public void StopProductionTracking()
            {
                if (_trackingStartTime.HasValue)
                {
                    CycleTime = DateTime.UtcNow - _trackingStartTime.Value;
                    _trackingStartTime = null;
                }
            }
        }
        
        public class E10MetricsReport
        {
            public string EquipmentId { get; set; } = string.Empty;
            public E10State CurrentState { get; set; }
            public DateTime ReportTime { get; set; }
            public TimeSpan TotalTime { get; set; }
            public Dictionary<E10State, TimeSpan> StateDurations { get; set; } = new();
            public E116PerformanceMetrics PerformanceMetrics { get; set; } = new();
            public List<StateTransition> StateHistory { get; set; } = new();
            
            public string GetSummary()
            {
                return $@"
Equipment Performance Report
============================
Equipment: {EquipmentId}
Current State: {CurrentState}
Report Time: {ReportTime:yyyy-MM-dd HH:mm:ss}

Time Distribution:
------------------
Total Time: {TotalTime.TotalHours:F2} hours
Productive: {(StateDurations.GetValueOrDefault(E10State.Productive).TotalHours):F2} hours
StandBy: {(StateDurations.GetValueOrDefault(E10State.StandBy).TotalHours):F2} hours
Engineering: {(StateDurations.GetValueOrDefault(E10State.Engineering).TotalHours):F2} hours
Unscheduled Downtime: {(StateDurations.GetValueOrDefault(E10State.UnscheduledDowntime).TotalHours):F2} hours
Scheduled Downtime: {(StateDurations.GetValueOrDefault(E10State.ScheduledDowntime).TotalHours):F2} hours
Non-Scheduled: {(StateDurations.GetValueOrDefault(E10State.NonScheduled).TotalHours):F2} hours

Key Performance Indicators:
---------------------------
OEE: {PerformanceMetrics.OEE:F2}%
Availability: {PerformanceMetrics.Availability:F2}%
Performance: {PerformanceMetrics.OperationalEfficiency:F2}%
Quality: {PerformanceMetrics.QualityRate:F2}%

Production Metrics:
-------------------
Lots Processed: {PerformanceMetrics.LotsProcessed}
Wafers Processed: {PerformanceMetrics.WafersProcessed}
Yield: {PerformanceMetrics.Yield:F2}%

Reliability Metrics:
--------------------
MTBF: {PerformanceMetrics.MTBF:F2} hours
MTTR: {PerformanceMetrics.MTTR:F2} hours
Fault Count: {PerformanceMetrics.FaultCount}
";
            }
        }
        
        public class StateChangeData
        {
            public int ReasonCode { get; set; }
            public string? Comments { get; set; }
        }
        
        public class ProcessingRequest
        {
            public string LotId { get; set; } = string.Empty;
            public string RecipeId { get; set; } = string.Empty;
            public bool MaterialAvailable { get; set; }
            public bool RecipeAvailable { get; set; }
            public bool OperatorAvailable { get; set; }
        }
        
        public class ProcessingResult
        {
            public int WaferCount { get; set; }
            public int GoodWafers { get; set; }
        }
        
        public class FaultData
        {
            public string FaultCode { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        }
        
        public class LotCompleteData
        {
            public string LotId { get; set; } = string.Empty;
            public double Yield { get; set; }
            public int WaferCount { get; set; }
        }
        
        public class AlarmEventArgs : EventArgs
        {
            public string AlarmType { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
        }
    }
}
