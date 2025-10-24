using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Monitoring;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using LoggerHelper;

namespace CMPSimulator.StateMachines;

/// <summary>
/// Carrier State Machine (SEMI E87 Carrier Management)
/// Full E87 implementation with complete state lifecycle
/// States: NotPresent → WaitingForHost → Mapping → MappingVerification →
///         ReadyToAccess → InAccess → AccessPaused → Complete → CarrierOut
/// </summary>
public class CarrierMachine
{
    private readonly IPureStateMachine _machine;
    private readonly StateMachineMonitor _monitor;
    private readonly EventBusOrchestrator _orchestrator;
    private StateMachine? _underlyingMachine;

    public string CarrierId { get; }
    public string CurrentState => _machine?.CurrentState ?? "initializing";
    public List<int> WaferIds { get; private set; }
    public List<int> CompletedWafers { get; private set; }

    // E87 Slot mapping
    public ConcurrentDictionary<int, SlotState> SlotMap { get; }
    public int SubstrateCount { get; set; }
    public DateTime ArrivedTime { get; }
    public DateTime? MappingCompleteTime { get; set; }
    public DateTime? DepartedTime { get; set; }

    // Expose StateChanged event for Pub/Sub
    public event EventHandler<StateTransitionEventArgs>? StateChanged
    {
        add => _monitor.StateTransitioned += value;
        remove => _monitor.StateTransitioned -= value;
    }

    public CarrierMachine(string carrierId, List<int> waferIds, EventBusOrchestrator orchestrator)
    {
        CarrierId = carrierId;
        WaferIds = waferIds;
        CompletedWafers = new List<int>();
        SlotMap = new ConcurrentDictionary<int, SlotState>();
        SubstrateCount = waferIds.Count;
        ArrivedTime = DateTime.Now;
        _orchestrator = orchestrator;

        // Initialize slot map - all slots have wafers present
        for (int i = 0; i < waferIds.Count; i++)
        {
            SlotMap[i + 1] = SlotState.Present;
        }

        // Full E87 Carrier State Machine Definition
        var definition = $$"""
        {
            "id": "{{carrierId}}",
            "initial": "NotPresent",
            "states": {
                "NotPresent": {
                    "entry": ["logNotPresent"],
                    "on": {
                        "CARRIER_DETECTED": {
                            "target": "WaitingForHost",
                            "actions": ["onCarrierDetected"]
                        }
                    }
                },
                "WaitingForHost": {
                    "entry": ["logWaitingForHost"],
                    "on": {
                        "HOST_PROCEED": {
                            "target": "Mapping",
                            "actions": ["onHostProceed"]
                        },
                        "HOST_CANCEL": {
                            "target": "CarrierOut",
                            "actions": ["onHostCancel"]
                        }
                    }
                },
                "Mapping": {
                    "entry": ["startMapping"],
                    "on": {
                        "MAPPING_COMPLETE": {
                            "target": "MappingVerification",
                            "actions": ["onMappingComplete"]
                        },
                        "MAPPING_ERROR": {
                            "target": "WaitingForHost",
                            "actions": ["onMappingError"]
                        }
                    }
                },
                "MappingVerification": {
                    "entry": ["logMappingVerification"],
                    "on": {
                        "VERIFY_OK": {
                            "target": "ReadyToAccess",
                            "actions": ["onVerifyOk"]
                        },
                        "VERIFY_FAIL": {
                            "target": "Mapping",
                            "actions": ["onVerifyFail"]
                        }
                    },
                    "after": {
                        "500": {
                            "target": "ReadyToAccess"
                        }
                    }
                },
                "ReadyToAccess": {
                    "entry": ["logReadyToAccess"],
                    "on": {
                        "START_ACCESS": {
                            "target": "InAccess",
                            "actions": ["onStartAccess"]
                        },
                        "HOST_CANCEL": {
                            "target": "Complete",
                            "actions": ["onHostCancel"]
                        }
                    }
                },
                "InAccess": {
                    "entry": ["logInAccess"],
                    "on": {
                        "WAFER_COMPLETED": {
                            "actions": ["onWaferCompleted"]
                        },
                        "ACCESS_COMPLETE": {
                            "target": "Complete",
                            "actions": ["onAccessComplete"]
                        },
                        "ACCESS_ERROR": {
                            "target": "AccessPaused",
                            "actions": ["onAccessError"]
                        }
                    }
                },
                "AccessPaused": {
                    "entry": ["logAccessPaused"],
                    "on": {
                        "RESUME_ACCESS": {
                            "target": "InAccess",
                            "actions": ["onResumeAccess"]
                        },
                        "ABORT_ACCESS": {
                            "target": "Complete",
                            "actions": ["onAbortAccess"]
                        }
                    }
                },
                "Complete": {
                    "entry": ["logComplete"],
                    "on": {
                        "CARRIER_REMOVED": {
                            "target": "CarrierOut",
                            "actions": ["onCarrierRemoved"]
                        }
                    }
                },
                "CarrierOut": {
                    "entry": ["logCarrierOut"],
                    "type": "final"
                }
            }
        }
        """;

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logNotPresent"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] 📦 Not present");
            },

            ["onCarrierDetected"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] 🔍 Carrier detected at LoadPort");
            },

            ["logWaitingForHost"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] ⏳ Waiting for host authorization ({WaferIds.Count} wafers)");

                ctx.RequestSend("scheduler", "CARRIER_STATUS", new JObject
                {
                    ["carrierId"] = CarrierId,
                    ["state"] = "WaitingForHost",
                    ["waferCount"] = WaferIds.Count
                });
            },

            ["onHostProceed"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] ✅ Host authorized - proceeding to mapping");
            },

            ["onHostCancel"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] ❌ Host cancelled carrier processing");
            },

            ["startMapping"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] 🗺️  Starting slot mapping ({SlotMap.Count} slots)");

                ctx.RequestSend("scheduler", "CARRIER_STATUS", new JObject
                {
                    ["carrierId"] = CarrierId,
                    ["state"] = "Mapping"
                });
            },

            ["onMappingComplete"] = (ctx) =>
            {
                MappingCompleteTime = DateTime.Now;
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] ✅ Slot mapping complete - {SubstrateCount} substrates detected");
            },

            ["onMappingError"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] ❌ Mapping error - retrying");
            },

            ["logMappingVerification"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] 🔍 Verifying slot map...");
            },

            ["onVerifyOk"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] ✅ Slot map verified");
            },

            ["onVerifyFail"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] ❌ Verification failed - remapping");
            },

            ["logReadyToAccess"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] ✅ Ready to access ({SubstrateCount} wafers available)");

                ctx.RequestSend("scheduler", "CARRIER_STATUS", new JObject
                {
                    ["carrierId"] = CarrierId,
                    ["state"] = "ReadyToAccess",
                    ["substrateCount"] = SubstrateCount
                });
            },

            ["onStartAccess"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] 🔧 Starting wafer access");
            },

            ["logInAccess"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] 🔧 Accessing wafers ({WaferIds.Count} total)");

                ctx.RequestSend("scheduler", "CARRIER_STATUS", new JObject
                {
                    ["carrierId"] = CarrierId,
                    ["state"] = "InAccess",
                    ["pendingWafers"] = WaferIds.Count - CompletedWafers.Count,
                    ["completedWafers"] = CompletedWafers.Count
                });
            },

            ["onWaferCompleted"] = (ctx) =>
            {
                // Extract wafer ID from event
                if (_underlyingMachine?.ContextMap?["_event"] is JObject data)
                {
                    int waferId = data["waferId"]?.ToObject<int>() ?? 0;
                    if (waferId > 0 && !CompletedWafers.Contains(waferId))
                    {
                        CompletedWafers.Add(waferId);
                        LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] ✓ Wafer {waferId} completed ({CompletedWafers.Count}/{WaferIds.Count})");

                        // Send status update
                        ctx.RequestSend("scheduler", "CARRIER_STATUS", new JObject
                        {
                            ["carrierId"] = CarrierId,
                            ["state"] = "InAccess",
                            ["completedWafers"] = CompletedWafers.Count,
                            ["pendingWafers"] = WaferIds.Count - CompletedWafers.Count
                        });
                    }
                }
            },

            ["onAccessComplete"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] ✅ All wafers accessed - {CompletedWafers.Count}/{WaferIds.Count} completed");
            },

            ["onAccessError"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] ❌ Access error - pausing");
            },

            ["logAccessPaused"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] ⏸️  Access paused (error recovery mode)");

                ctx.RequestSend("scheduler", "CARRIER_STATUS", new JObject
                {
                    ["carrierId"] = CarrierId,
                    ["state"] = "AccessPaused"
                });
            },

            ["onResumeAccess"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] ▶️  Resuming access");
            },

            ["onAbortAccess"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] ⛔ Access aborted");
            },

            ["logComplete"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] ✅ Processing complete - ready to unload");

                ctx.RequestSend("scheduler", "CARRIER_STATUS", new JObject
                {
                    ["carrierId"] = CarrierId,
                    ["state"] = "Complete",
                    ["completedWafers"] = CompletedWafers.Count
                });
            },

            ["onCarrierRemoved"] = (ctx) =>
            {
                DepartedTime = DateTime.Now;
                var duration = (DepartedTime.Value - ArrivedTime).TotalSeconds;
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] 📤 Carrier removed (Total time: {duration:F1}s)");
            },

            ["logCarrierOut"] = (ctx) =>
            {
                LoggerHelper.Logger.Instance.Log($"[Carrier {CarrierId}] 🚪 Carrier departed from system");

                ctx.RequestSend("scheduler", "CARRIER_STATUS", new JObject
                {
                    ["carrierId"] = CarrierId,
                    ["state"] = "CarrierOut"
                });
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: carrierId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: null,
            services: null,
            enableGuidIsolation: false
        );

        _underlyingMachine = ((PureStateMachineAdapter)_machine).GetUnderlying() as StateMachine;
        _monitor = new StateMachineMonitor(_underlyingMachine!);
        _monitor.StartMonitoring();
    }

    public async Task<string> StartAsync()
    {
        var result = await _machine.StartAsync();

        // NOTE: ExecuteDeferredSends is now automatically handled by StateChanged event
        // Do NOT call it manually here or messages will be sent twice!

        return result;
    }

    // ===== E87 Public API Methods =====

    /// <summary>
    /// E87: Send CARRIER_DETECTED event when carrier arrives
    /// </summary>
    public void SendCarrierDetected()
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "CARRIER_DETECTED", new JObject());
    }

    /// <summary>
    /// E87: Send HOST_PROCEED event to authorize processing
    /// </summary>
    public void SendHostProceed()
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "HOST_PROCEED", new JObject());
    }

    /// <summary>
    /// E87: Send HOST_CANCEL event to cancel processing
    /// </summary>
    public void SendHostCancel()
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "HOST_CANCEL", new JObject());
    }

    /// <summary>
    /// E87: Send MAPPING_COMPLETE event after slot mapping
    /// </summary>
    public void SendMappingComplete()
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "MAPPING_COMPLETE", new JObject());
    }

    /// <summary>
    /// E87: Send MAPPING_ERROR event if mapping fails
    /// </summary>
    public void SendMappingError()
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "MAPPING_ERROR", new JObject());
    }

    /// <summary>
    /// E87: Send VERIFY_OK event after successful verification
    /// </summary>
    public void SendVerifyOk()
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "VERIFY_OK", new JObject());
    }

    /// <summary>
    /// E87: Send VERIFY_FAIL event if verification fails
    /// </summary>
    public void SendVerifyFail()
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "VERIFY_FAIL", new JObject());
    }

    /// <summary>
    /// E87: Send START_ACCESS event to begin wafer access
    /// </summary>
    public void SendStartAccess()
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "START_ACCESS", new JObject());
    }

    /// <summary>
    /// E87: Send WAFER_COMPLETED event when a wafer finishes processing
    /// </summary>
    public void SendWaferCompleted(int waferId)
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "WAFER_COMPLETED", new JObject
        {
            ["waferId"] = waferId
        });
    }

    /// <summary>
    /// E87: Send ACCESS_COMPLETE event when all wafers are processed
    /// </summary>
    public void SendAccessComplete()
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "ACCESS_COMPLETE", new JObject());
    }

    /// <summary>
    /// E87: Send ACCESS_ERROR event if an error occurs
    /// </summary>
    public void SendAccessError()
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "ACCESS_ERROR", new JObject());
    }

    /// <summary>
    /// E87: Send RESUME_ACCESS event to resume after pause
    /// </summary>
    public void SendResumeAccess()
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "RESUME_ACCESS", new JObject());
    }

    /// <summary>
    /// E87: Send ABORT_ACCESS event to abort processing
    /// </summary>
    public void SendAbortAccess()
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "ABORT_ACCESS", new JObject());
    }

    /// <summary>
    /// E87: Send CARRIER_REMOVED event when carrier departs
    /// </summary>
    public void SendCarrierRemoved()
    {
        var context = _orchestrator.GetOrCreateContext(CarrierId);
        context.RequestSend(CarrierId, "CARRIER_REMOVED", new JObject());
    }

    /// <summary>
    /// Check if all wafers are completed
    /// </summary>
    public bool AreAllWafersCompleted()
    {
        return CompletedWafers.Count >= WaferIds.Count;
    }

    /// <summary>
    /// Update slot map after physical mapping
    /// </summary>
    public void UpdateSlotMap(Dictionary<int, SlotState> newSlotMap)
    {
        foreach (var kvp in newSlotMap)
        {
            SlotMap[kvp.Key] = kvp.Value;
        }

        // Count actual substrates
        SubstrateCount = SlotMap.Count(s => s.Value == SlotState.Present);
    }
}

/// <summary>
/// E87 Slot States
/// </summary>
public enum SlotState
{
    Unknown,
    Empty,
    Present,
    DoublePlaced,
    CrossPlaced
}
