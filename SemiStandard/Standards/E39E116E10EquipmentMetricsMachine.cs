using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Standards;

/// <summary>
/// SEMI E39/E116/E10 Equipment Reliability and Productivity Metrics
/// E10: Equipment Reliability, Availability and Maintainability (RAM) - 6-state model
/// E116: Equipment Performance Tracking - reason codes and performance metrics
/// E39: Object Services - equipment performance tracking and OEE calculation
/// </summary>
public class E39E116E10EquipmentMetricsMachine
{
    private readonly string _equipmentId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IPureStateMachine _machine;
    private readonly PerformanceMetrics _metrics;
    private readonly List<StateTransition> _stateHistory = new();
    private readonly ConcurrentDictionary<E10State, TimeSpan> _stateDurations = new();
    private DateTime _currentStateStartTime;
    private E10State _previousState;
    private readonly string _instanceId;

    public string MachineId => $"E39_E116_E10_METRICS_{_equipmentId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;
    public PerformanceMetrics Metrics => _metrics;
    public E10State CurrentMetricState { get; private set; }

    /// <summary>
    /// SEMI E10 Equipment States - Six-state model for equipment productivity
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

    public E39E116E10EquipmentMetricsMachine(string equipmentId, EventBusOrchestrator orchestrator)
    {
        _equipmentId = equipmentId;
        _orchestrator = orchestrator;
        _metrics = new PerformanceMetrics();
        CurrentMetricState = E10State.NonScheduled;
        _currentStateStartTime = DateTime.UtcNow;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);

        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'NonScheduled',
            states: {
                NonScheduled: {
                    entry: 'logNonScheduled',
                    on: {
                        SCHEDULE: {
                            target: 'StandBy',
                            actions: ['recordStateExit', 'recordStateEntry', 'startTracking']
                        }
                    }
                },
                StandBy: {
                    entry: 'logStandBy',
                    on: {
                        START_PROCESSING: {
                            target: 'Productive',
                            actions: ['recordStateExit', 'recordStateEntry', 'startProcessing']
                        },
                        START_ENGINEERING: {
                            target: 'Engineering',
                            actions: ['recordStateExit', 'recordStateEntry']
                        },
                        MAINTENANCE_SCHEDULED: {
                            target: 'ScheduledDowntime',
                            actions: ['recordStateExit', 'recordStateEntry', 'notifyMaintenance']
                        },
                        FAULT: {
                            target: 'UnscheduledDowntime',
                            actions: ['recordStateExit', 'recordStateEntry', 'recordFault', 'raiseAlarm']
                        },
                        UNSCHEDULE: {
                            target: 'NonScheduled',
                            actions: ['recordStateExit', 'recordStateEntry', 'stopTracking']
                        }
                    }
                },
                Productive: {
                    entry: 'logProductive',
                    on: {
                        PROCESSING_COMPLETE: {
                            target: 'StandBy',
                            actions: ['recordStateExit', 'recordStateEntry', 'completeProcessing', 'updateMetrics']
                        },
                        FAULT: {
                            target: 'UnscheduledDowntime',
                            actions: ['recordStateExit', 'recordStateEntry', 'recordFault', 'raiseAlarm', 'abortProcessing']
                        },
                        PAUSE: {
                            target: 'StandBy',
                            actions: ['recordStateExit', 'recordStateEntry']
                        }
                    }
                },
                Engineering: {
                    entry: 'logEngineering',
                    on: {
                        ENGINEERING_COMPLETE: {
                            target: 'StandBy',
                            actions: ['recordStateExit', 'recordStateEntry']
                        },
                        FAULT: {
                            target: 'UnscheduledDowntime',
                            actions: ['recordStateExit', 'recordStateEntry', 'recordFault', 'raiseAlarm']
                        }
                    }
                },
                ScheduledDowntime: {
                    entry: 'logScheduledDowntime',
                    on: {
                        MAINTENANCE_COMPLETE: {
                            target: 'StandBy',
                            actions: ['recordStateExit', 'recordStateEntry', 'updateAvailability']
                        }
                    }
                },
                UnscheduledDowntime: {
                    entry: 'logUnscheduledDowntime',
                    on: {
                        REPAIR_COMPLETE: {
                            target: 'StandBy',
                            actions: ['recordStateExit', 'recordStateEntry', 'updateMTTR']
                        }
                    }
                }
            }
        }
        """;

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logNonScheduled"] = (ctx) =>
            {
                CurrentMetricState = E10State.NonScheduled;
                Console.WriteLine($"[{MachineId}] 🔴 NonScheduled - Equipment not in production schedule");
            },

            ["logStandBy"] = (ctx) =>
            {
                CurrentMetricState = E10State.StandBy;
                Console.WriteLine($"[{MachineId}] ⏸️ StandBy - Idle, waiting for work");
            },

            ["logProductive"] = (ctx) =>
            {
                CurrentMetricState = E10State.Productive;
                Console.WriteLine($"[{MachineId}] 🔧 Productive - Processing material");
            },

            ["logEngineering"] = (ctx) =>
            {
                CurrentMetricState = E10State.Engineering;
                Console.WriteLine($"[{MachineId}] 🔬 Engineering - Development/test runs");
            },

            ["logScheduledDowntime"] = (ctx) =>
            {
                CurrentMetricState = E10State.ScheduledDowntime;
                Console.WriteLine($"[{MachineId}] 🛠️ ScheduledDowntime - Planned maintenance");
            },

            ["logUnscheduledDowntime"] = (ctx) =>
            {
                CurrentMetricState = E10State.UnscheduledDowntime;
                Console.WriteLine($"[{MachineId}] ❌ UnscheduledDowntime - Unplanned failure");
            },

            ["recordStateEntry"] = (ctx) =>
            {
                _previousState = CurrentMetricState;
                _currentStateStartTime = DateTime.UtcNow;

                var transition = new StateTransition
                {
                    FromState = _previousState,
                    ToState = CurrentMetricState,
                    Timestamp = _currentStateStartTime,
                    ReasonCode = 0
                };

                _stateHistory.Add(transition);
                Console.WriteLine($"[{MachineId}] 📊 State transition: {_previousState} → {CurrentMetricState}");
            },

            ["recordStateExit"] = (ctx) =>
            {
                var duration = DateTime.UtcNow - _currentStateStartTime;

                if (!_stateDurations.ContainsKey(CurrentMetricState))
                    _stateDurations[CurrentMetricState] = TimeSpan.Zero;

                _stateDurations[CurrentMetricState] += duration;
            },

            ["startTracking"] = (ctx) =>
            {
                _metrics.StartProductionTracking();
                Console.WriteLine($"[{MachineId}] 📈 Production tracking started");
            },

            ["stopTracking"] = (ctx) =>
            {
                _metrics.StopProductionTracking();
                Console.WriteLine($"[{MachineId}] 📉 Production tracking stopped");
            },

            ["startProcessing"] = (ctx) =>
            {
                _metrics.ProcessingStartTime = DateTime.UtcNow;
                Console.WriteLine($"[{MachineId}] 🚀 Processing started");

                ctx.RequestSend("E40_PROCESS_JOB", "EQUIPMENT_PROCESSING", new JObject
                {
                    ["equipmentId"] = _equipmentId,
                    ["lotId"] = _metrics.CurrentLotId,
                    ["startTime"] = _metrics.ProcessingStartTime
                });
            },

            ["completeProcessing"] = (ctx) =>
            {
                _metrics.IncrementLotsProcessed();
                _metrics.ProcessingEndTime = DateTime.UtcNow;

                var processingTime = _metrics.ProcessingEndTime.Value - _metrics.ProcessingStartTime!.Value;
                Console.WriteLine($"[{MachineId}] ✅ Processing complete ({processingTime.TotalSeconds:F1}s)");

                ctx.RequestSend("E40_PROCESS_JOB", "EQUIPMENT_PROCESSING_COMPLETE", new JObject
                {
                    ["equipmentId"] = _equipmentId,
                    ["lotId"] = _metrics.CurrentLotId,
                    ["processingTime"] = processingTime.TotalSeconds
                });
            },

            ["abortProcessing"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⛔ Processing aborted due to fault");
            },

            ["recordFault"] = (ctx) =>
            {
                _metrics.IncrementFaultCount();
                _metrics.LastFaultTime = DateTime.UtcNow;
                Console.WriteLine($"[{MachineId}] ⚠️ Fault recorded (Total: {_metrics.FaultCount})");

                ctx.RequestSend("ALARM_SYSTEM", "EQUIPMENT_FAULT", new JObject
                {
                    ["equipmentId"] = _equipmentId,
                    ["faultTime"] = _metrics.LastFaultTime,
                    ["faultCount"] = _metrics.FaultCount
                });
            },

            ["raiseAlarm"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🚨 Alarm raised - Unscheduled downtime");
            },

            ["notifyMaintenance"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔔 Scheduled maintenance notification");

                ctx.RequestSend("MAINTENANCE_SYSTEM", "MAINTENANCE_STARTED", new JObject
                {
                    ["equipmentId"] = _equipmentId,
                    ["startTime"] = DateTime.UtcNow
                });
            },

            ["updateMetrics"] = (ctx) =>
            {
                CalculateMetrics();
                Console.WriteLine($"[{MachineId}] 📊 Metrics updated - OEE: {_metrics.OEE:F2}%");
            },

            ["updateAvailability"] = (ctx) =>
            {
                CalculateMetrics();
                Console.WriteLine($"[{MachineId}] 📈 Availability: {_metrics.Availability:F2}%");
            },

            ["updateMTTR"] = (ctx) =>
            {
                CalculateMetrics();
                Console.WriteLine($"[{MachineId}] ⏱️ MTTR: {_metrics.MTTR:F2} hours");
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: MachineId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions
        );
    }

    public async Task<string> StartAsync()
    {
        return await _machine.StartAsync();
    }

    public string GetCurrentState()
    {
        return _machine.CurrentState;
    }

    // Public API methods
    public async Task<EventResult> ScheduleAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SCHEDULE", null);
        return result;
    }

    public async Task<EventResult> UnscheduleAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "UNSCHEDULE", null);
        return result;
    }

    public async Task<EventResult> StartProcessingAsync(string lotId, string recipeId)
    {
        _metrics.CurrentLotId = lotId;
        _metrics.CurrentRecipeId = recipeId;
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "START_PROCESSING", new JObject
        {
            ["lotId"] = lotId,
            ["recipeId"] = recipeId
        });
        return result;
    }

    public async Task<EventResult> CompleteProcessingAsync(int waferCount, int goodWafers)
    {
        _metrics.AddWafersProcessed(waferCount);
        _metrics.AddGoodWafers(goodWafers);
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PROCESSING_COMPLETE", new JObject
        {
            ["waferCount"] = waferCount,
            ["goodWafers"] = goodWafers
        });
        return result;
    }

    public async Task<EventResult> PauseProcessingAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PAUSE", null);
        return result;
    }

    public async Task<EventResult> StartEngineeringAsync(E116ReasonCode reasonCode)
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "START_ENGINEERING", new JObject
        {
            ["reasonCode"] = (int)reasonCode
        });
        return result;
    }

    public async Task<EventResult> CompleteEngineeringAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "ENGINEERING_COMPLETE", null);
        return result;
    }

    public async Task<EventResult> StartMaintenanceAsync(E116ReasonCode reasonCode)
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "MAINTENANCE_SCHEDULED", new JObject
        {
            ["reasonCode"] = (int)reasonCode
        });
        return result;
    }

    public async Task<EventResult> CompleteMaintenanceAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "MAINTENANCE_COMPLETE", null);
        return result;
    }

    public async Task<EventResult> ReportFaultAsync(string faultCode, string description)
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "FAULT", new JObject
        {
            ["faultCode"] = faultCode,
            ["description"] = description
        });
        return result;
    }

    public async Task<EventResult> CompleteRepairAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "REPAIR_COMPLETE", null);
        return result;
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
        if (operationsTime.TotalSeconds > 0)
            _metrics.Availability = upTime.TotalSeconds / operationsTime.TotalSeconds * 100;

        if (upTime.TotalSeconds > 0)
            _metrics.OperationalEfficiency = productiveTime.TotalSeconds / upTime.TotalSeconds * 100;

        _metrics.RateEfficiency = CalculateRateEfficiency();
        _metrics.QualityRate = CalculateQualityRate();

        // Calculate OEE (Overall Equipment Effectiveness)
        _metrics.OEE = (_metrics.Availability / 100) *
                       (_metrics.OperationalEfficiency / 100) *
                       (_metrics.QualityRate / 100) * 100;

        // Calculate MTBF (Mean Time Between Failures)
        if (_metrics.FaultCount > 0)
        {
            _metrics.MTBF = upTime.TotalHours / _metrics.FaultCount;
        }

        // Calculate MTTR (Mean Time To Repair)
        var repairTime = GetStateDuration(E10State.UnscheduledDowntime);
        if (_metrics.FaultCount > 0)
        {
            _metrics.MTTR = repairTime.TotalHours / _metrics.FaultCount;
        }
    }

    private double CalculateRateEfficiency()
    {
        if (_metrics.TheoreticalRate == 0) return 100;

        var productiveTime = GetStateDuration(E10State.Productive);
        if (productiveTime.TotalHours == 0) return 0;

        var actualRate = _metrics.WafersProcessed / productiveTime.TotalHours;
        return actualRate / _metrics.TheoreticalRate * 100;
    }

    private double CalculateQualityRate()
    {
        if (_metrics.WafersProcessed == 0) return 100;
        return (double)_metrics.GoodWafers / _metrics.WafersProcessed * 100;
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
        if (CurrentMetricState == state)
        {
            duration += DateTime.UtcNow - _currentStateStartTime;
        }

        return duration;
    }

    public MetricsReport GetMetricsReport()
    {
        CalculateMetrics();

        return new MetricsReport
        {
            EquipmentId = _equipmentId,
            CurrentState = CurrentMetricState,
            ReportTime = DateTime.UtcNow,
            TotalTime = GetTotalTime(),
            StateDurations = new Dictionary<E10State, TimeSpan>(_stateDurations),
            Metrics = _metrics,
            StateHistory = _stateHistory.ToList()
        };
    }

    // Data classes
    public class StateTransition
    {
        public E10State FromState { get; set; }
        public E10State ToState { get; set; }
        public DateTime Timestamp { get; set; }
        public int ReasonCode { get; set; }
    }

    public class PerformanceMetrics
    {
        // Production metrics
        private int _lotsProcessed;
        private int _wafersProcessed;
        private int _goodWafers;
        private int _faultCount;

        public int LotsProcessed => _lotsProcessed;
        public int WafersProcessed => _wafersProcessed;
        public int GoodWafers => _goodWafers;
        public double Yield => WafersProcessed > 0 ? (double)GoodWafers / WafersProcessed * 100 : 0;

        // Time metrics
        public DateTime? ProcessingStartTime { get; set; }
        public DateTime? ProcessingEndTime { get; set; }
        public DateTime? LastFaultTime { get; set; }

        // Efficiency metrics
        public double Availability { get; set; }         // A in OEE
        public double OperationalEfficiency { get; set; } // P in OEE
        public double QualityRate { get; set; }          // Q in OEE
        public double OEE { get; set; }                  // Overall Equipment Effectiveness
        public double RateEfficiency { get; set; }

        // Reliability metrics
        public double MTBF { get; set; }  // Mean Time Between Failures (hours)
        public double MTTR { get; set; }  // Mean Time To Repair (hours)
        public int FaultCount => _faultCount;

        // Current production
        public string? CurrentLotId { get; set; }
        public string? CurrentRecipeId { get; set; }
        public double TheoreticalRate { get; set; } = 60; // Wafers per hour

        private DateTime? _trackingStartTime;

        public void IncrementLotsProcessed() => Interlocked.Increment(ref _lotsProcessed);
        public void AddWafersProcessed(int count) => Interlocked.Add(ref _wafersProcessed, count);
        public void AddGoodWafers(int count) => Interlocked.Add(ref _goodWafers, count);
        public void IncrementFaultCount() => Interlocked.Increment(ref _faultCount);

        public void StartProductionTracking()
        {
            _trackingStartTime = DateTime.UtcNow;
        }

        public void StopProductionTracking()
        {
            _trackingStartTime = null;
        }
    }

    public class MetricsReport
    {
        public string EquipmentId { get; set; } = string.Empty;
        public E10State CurrentState { get; set; }
        public DateTime ReportTime { get; set; }
        public TimeSpan TotalTime { get; set; }
        public Dictionary<E10State, TimeSpan> StateDurations { get; set; } = new();
        public PerformanceMetrics Metrics { get; set; } = new();
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
Productive: {StateDurations.GetValueOrDefault(E10State.Productive).TotalHours:F2} hours
StandBy: {StateDurations.GetValueOrDefault(E10State.StandBy).TotalHours:F2} hours
Engineering: {StateDurations.GetValueOrDefault(E10State.Engineering).TotalHours:F2} hours
Unscheduled Downtime: {StateDurations.GetValueOrDefault(E10State.UnscheduledDowntime).TotalHours:F2} hours
Scheduled Downtime: {StateDurations.GetValueOrDefault(E10State.ScheduledDowntime).TotalHours:F2} hours
Non-Scheduled: {StateDurations.GetValueOrDefault(E10State.NonScheduled).TotalHours:F2} hours

Key Performance Indicators:
---------------------------
OEE: {Metrics.OEE:F2}%
Availability: {Metrics.Availability:F2}%
Performance: {Metrics.OperationalEfficiency:F2}%
Quality: {Metrics.QualityRate:F2}%

Production Metrics:
-------------------
Lots Processed: {Metrics.LotsProcessed}
Wafers Processed: {Metrics.WafersProcessed}
Yield: {Metrics.Yield:F2}%

Reliability Metrics:
--------------------
MTBF: {Metrics.MTBF:F2} hours
MTTR: {Metrics.MTTR:F2} hours
Fault Count: {Metrics.FaultCount}
";
        }
    }
}
