using System.Text.Json;

namespace CMPSimXS2.EventDriven;

/// <summary>
/// Builds the CMP state machine JSON definition programmatically
/// This matches the JavaScript XState configuration with parallel regions
/// </summary>
/// <remarks>
/// [OBSOLETE] This class is no longer used. The CMP system now loads the state machine
/// definition directly from cmp_machine.json instead of building it programmatically.
///
/// Reasons for the change:
/// - Matches XState v5 specification exactly - no translation layer
/// - Easier to maintain - edit JSON directly without recompiling
/// - Portable - same JSON works across different implementations
/// - Better for SEMI E10 compliance - equipment engineers can edit JSON without knowing C#
///
/// This class is kept for reference/documentation purposes only.
/// </remarks>
[Obsolete("Use cmp_machine.json directly instead. See CMPMachineFactory.Create() for usage.")]
public static class CMPMachineBuilder
{
    public static string BuildMachineJson()
    {
        var machine = new
        {
            id = "cmp",
            type = "parallel",
            context = new Dictionary<string, object>
            {
                // Wafer tracking
                ["carrier_unprocessed"] = 25,
                ["carrier_processed"] = 0,
                ["current_wafer"] = (object?)null,
                ["wafer_history"] = new List<object>(),

                // Resource locking
                ["platen_locked"] = false,
                ["robot_busy"] = false,

                // Configuration
                ["config"] = new
                {
                    polish_time = 2000,
                    move_time = 500,
                    pick_place_time = 300,
                    timeout = 5000
                },

                // Error tracking
                ["error_code"] = (string?)null,
                ["error_message"] = (string?)null,
                ["retry_count"] = 0,
                ["max_retries"] = 3
            },
            states = new Dictionary<string, object>
            {
                // Region 1: SEMI E10 Equipment State
                ["equipment_state"] = new
                {
                    initial = "IDLE",
                    states = new Dictionary<string, object>
                    {
                        ["IDLE"] = new
                        {
                            entry = new[] { "logEquipmentIdle" },
                            on = new Dictionary<string, object>
                            {
                                ["START_PROCESS"] = "EXECUTING"
                            }
                        },
                        ["EXECUTING"] = new
                        {
                            entry = new[] { "logEquipmentExecuting" },
                            on = new Dictionary<string, object>
                            {
                                ["PAUSE_PROCESS"] = "PAUSED",
                                ["PROCESS_ERROR"] = "ALARM",
                                ["ABORT_PROCESS"] = "ABORTED",
                                ["PROCESS_COMPLETE"] = "IDLE"
                            }
                        },
                        ["PAUSED"] = new
                        {
                            entry = new[] { "logEquipmentPaused" },
                            on = new Dictionary<string, object>
                            {
                                ["RESUME_PROCESS"] = "EXECUTING",
                                ["ABORT_PROCESS"] = "ABORTED"
                            }
                        },
                        ["ALARM"] = new
                        {
                            entry = new[] { "logEquipmentAlarm", "notifyAlarm" },
                            on = new Dictionary<string, object>
                            {
                                ["CLEAR_ALARM"] = "IDLE",
                                ["ABORT_PROCESS"] = "ABORTED"
                            }
                        },
                        ["ABORTED"] = new
                        {
                            entry = new[] { "logEquipmentAborted" },
                            on = new Dictionary<string, object>
                            {
                                ["RESET_EQUIPMENT"] = "IDLE"
                            }
                        }
                    }
                },

                // Region 2: Orchestrator (main workflow controller)
                ["orchestrator"] = BuildOrchestratorRegion(),

                // Region 3: Robot (parallel: position + hand)
                ["robot"] = BuildRobotRegion(),

                // Region 4: Carrier
                ["carrier"] = BuildCarrierRegion(),

                // Region 5: Platen
                ["platen"] = BuildPlatenRegion()
            }
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
        };

        return JsonSerializer.Serialize(machine, options);
    }

    private static object BuildOrchestratorRegion()
    {
        return new
        {
            initial = "idle",
            states = new Dictionary<string, object>
            {
                ["idle"] = new
                {
                    entry = new[] { "logOrchIdle", "checkEquipmentState" },
                    always = new[]
                    {
                        new
                        {
                            target = "start_cycle",
                            guard = "canStartCycle"
                        }
                    },
                    on = new Dictionary<string, object>
                    {
                        ["MANUAL_START"] = "start_cycle"
                    }
                },
                ["start_cycle"] = new
                {
                    entry = new[] { "createWaferRecord", "lockRobot", "sendStartProcess", "sendMoveToCarrier" },
                    on = new Dictionary<string, object>
                    {
                        ["ROBOT_AT_CARRIER"] = "picking_unprocessed",
                        ["TIMEOUT"] = "error_timeout"
                    },
                    after = new Dictionary<string, string>
                    {
                        ["5000"] = "error_timeout"
                    }
                },
                ["picking_unprocessed"] = new
                {
                    entry = new[] { "sendPickWafer" },
                    on = new Dictionary<string, object>
                    {
                        ["WAFER_PICKED"] = new
                        {
                            target = "moving_to_platen",
                            actions = new[] { "decrementUnprocessed", "updateWaferPickTime" }
                        },
                        ["PICK_FAILED"] = "error_handling",
                        ["TIMEOUT"] = "error_timeout"
                    },
                    after = new Dictionary<string, string>
                    {
                        ["5000"] = "error_timeout"
                    }
                },
                ["moving_to_platen"] = new
                {
                    entry = new[] { "sendMoveToPlaten" },
                    on = new Dictionary<string, object>
                    {
                        ["ROBOT_AT_PLATEN"] = "waiting_for_platen",
                        ["TIMEOUT"] = "error_timeout"
                    },
                    after = new Dictionary<string, string>
                    {
                        ["5000"] = "error_timeout"
                    }
                },
                ["waiting_for_platen"] = new
                {
                    entry = new[] { "checkPlatenStatus" },
                    on = new Dictionary<string, object>
                    {
                        ["PLATEN_READY"] = "placing_wafer",
                        ["PLATEN_BUSY"] = "waiting_for_platen",
                        ["TIMEOUT"] = "error_timeout"
                    },
                    after = new Dictionary<string, string>
                    {
                        ["10000"] = "error_timeout"
                    }
                },
                ["placing_wafer"] = new
                {
                    entry = new[] { "sendPlaceWafer", "lockPlaten" },
                    on = new Dictionary<string, object>
                    {
                        ["WAFER_PLACED"] = new
                        {
                            target = "processing",
                            actions = new[] { "updateWaferLoadTime" }
                        },
                        ["PLACE_FAILED"] = "error_handling",
                        ["TIMEOUT"] = "error_timeout"
                    },
                    after = new Dictionary<string, string>
                    {
                        ["5000"] = "error_timeout"
                    }
                },
                ["processing"] = new
                {
                    entry = new[] { "sendStartPolish" },
                    on = new Dictionary<string, object>
                    {
                        ["POLISH_COMPLETED"] = new
                        {
                            target = "picking_processed",
                            actions = new[] { "updateWaferProcessTime" }
                        },
                        ["PROCESS_ERROR"] = "error_handling",
                        ["TIMEOUT"] = "error_timeout"
                    }
                },
                ["picking_processed"] = new
                {
                    entry = new[] { "sendPickWafer", "unlockPlaten" },
                    on = new Dictionary<string, object>
                    {
                        ["WAFER_PICKED"] = new
                        {
                            target = "returning_to_carrier",
                            actions = new[] { "updateWaferUnloadTime" }
                        },
                        ["PICK_FAILED"] = "error_handling",
                        ["TIMEOUT"] = "error_timeout"
                    },
                    after = new Dictionary<string, string>
                    {
                        ["5000"] = "error_timeout"
                    }
                },
                ["returning_to_carrier"] = new
                {
                    entry = new[] { "sendMoveToCarrier" },
                    on = new Dictionary<string, object>
                    {
                        ["ROBOT_AT_CARRIER"] = "placing_processed",
                        ["TIMEOUT"] = "error_timeout"
                    },
                    after = new Dictionary<string, string>
                    {
                        ["5000"] = "error_timeout"
                    }
                },
                ["placing_processed"] = new
                {
                    entry = new[] { "sendPlaceWafer" },
                    on = new Dictionary<string, object>
                    {
                        ["WAFER_PLACED"] = new
                        {
                            target = "cycle_complete",
                            actions = new[] { "incrementProcessed", "finalizeWaferRecord" }
                        },
                        ["PLACE_FAILED"] = "error_handling",
                        ["TIMEOUT"] = "error_timeout"
                    },
                    after = new Dictionary<string, string>
                    {
                        ["5000"] = "error_timeout"
                    }
                },
                ["cycle_complete"] = new
                {
                    entry = new[] { "logCycleComplete", "unlockRobot", "sendMoveToHome" },
                    on = new Dictionary<string, object>
                    {
                        ["ROBOT_AT_HOME"] = new object[]
                        {
                            new
                            {
                                target = "completed",
                                guard = "allWafersProcessed"
                            },
                            new
                            {
                                target = "idle",
                                actions = new[] { "resetRetryCount" }
                            }
                        },
                        ["TIMEOUT"] = "error_timeout"
                    },
                    after = new Dictionary<string, string>
                    {
                        ["5000"] = "error_timeout"
                    }
                },
                ["completed"] = new
                {
                    type = "final",
                    entry = new[] { "logAllCompleted", "sendProcessComplete", "generateReport" }
                },
                ["error_handling"] = new
                {
                    entry = new[] { "logError", "sendProcessError" },
                    always = new object[]
                    {
                        new
                        {
                            target = "retry",
                            guard = "canRetry"
                        },
                        new
                        {
                            target = "error_fatal"
                        }
                    }
                },
                ["retry"] = new
                {
                    entry = new[] { "incrementRetryCount", "logRetry" },
                    after = new Dictionary<string, string>
                    {
                        ["1000"] = "idle"
                    }
                },
                ["error_timeout"] = new
                {
                    entry = new[] { "setTimeoutError", "sendProcessError" },
                    always = new object[]
                    {
                        new
                        {
                            target = "retry",
                            guard = "canRetry"
                        },
                        new
                        {
                            target = "error_fatal"
                        }
                    }
                },
                ["error_fatal"] = new
                {
                    entry = new[] { "logFatalError", "unlockAllResources", "sendAbortProcess" },
                    on = new Dictionary<string, object>
                    {
                        ["RESET"] = new
                        {
                            target = "idle",
                            actions = new[] { "resetSystem" }
                        }
                    }
                }
            }
        };
    }

    private static object BuildRobotRegion()
    {
        return new
        {
            type = "parallel",
            states = new Dictionary<string, object>
            {
                // Sub-region: position
                ["position"] = new
                {
                    initial = "at_home",
                    states = new Dictionary<string, object>
                    {
                        ["at_home"] = new
                        {
                            entry = new[] { "logAtHome", "notifyAtHome" },
                            on = new Dictionary<string, object>
                            {
                                ["CMD_MOVE_TO_CARRIER"] = new
                                {
                                    target = "moving_to_carrier",
                                    guard = "robotNotBusy"
                                },
                                ["CMD_MOVE_TO_PLATEN"] = new
                                {
                                    target = "moving_to_platen",
                                    guard = "robotNotBusy"
                                }
                            }
                        },
                        ["moving_to_carrier"] = new
                        {
                            entry = new[] { "logMovingToCarrier" },
                            after = new Dictionary<string, object>
                            {
                                ["500"] = new
                                {
                                    target = "at_carrier"
                                }
                            }
                        },
                        ["at_carrier"] = new
                        {
                            entry = new[] { "logAtCarrier", "notifyAtCarrier" },
                            on = new Dictionary<string, object>
                            {
                                ["CMD_MOVE_TO_PLATEN"] = new
                                {
                                    target = "moving_to_platen",
                                    guard = "robotNotBusy"
                                },
                                ["CMD_MOVE_TO_HOME"] = new
                                {
                                    target = "moving_to_home",
                                    guard = "robotNotBusy"
                                }
                            }
                        },
                        ["moving_to_platen"] = new
                        {
                            entry = new[] { "logMovingToPlaten" },
                            after = new Dictionary<string, object>
                            {
                                ["500"] = new
                                {
                                    target = "at_platen"
                                }
                            }
                        },
                        ["at_platen"] = new
                        {
                            entry = new[] { "logAtPlaten", "notifyAtPlaten" },
                            on = new Dictionary<string, object>
                            {
                                ["CMD_MOVE_TO_CARRIER"] = new
                                {
                                    target = "moving_to_carrier",
                                    guard = "robotNotBusy"
                                },
                                ["CMD_MOVE_TO_HOME"] = new
                                {
                                    target = "moving_to_home",
                                    guard = "robotNotBusy"
                                }
                            }
                        },
                        ["moving_to_home"] = new
                        {
                            entry = new[] { "logMovingToHome" },
                            after = new Dictionary<string, object>
                            {
                                ["500"] = new
                                {
                                    target = "at_home"
                                }
                            }
                        }
                    }
                },
                // Sub-region: hand
                ["hand"] = new
                {
                    initial = "empty",
                    states = new Dictionary<string, object>
                    {
                        ["empty"] = new
                        {
                            entry = new[] { "logHandEmpty" },
                            on = new Dictionary<string, object>
                            {
                                ["CMD_PICK_WAFER"] = new
                                {
                                    target = "picking",
                                    guard = "robotNotBusy"
                                }
                            }
                        },
                        ["picking"] = new
                        {
                            entry = new[] { "logPicking" },
                            after = new Dictionary<string, object>
                            {
                                ["300"] = new object[]
                                {
                                    new
                                    {
                                        target = "has_wafer",
                                        guard = "pickSuccessful",
                                        actions = new[] { "notifyWaferPicked" }
                                    },
                                    new
                                    {
                                        target = "empty",
                                        actions = new[] { "notifyPickFailed" }
                                    }
                                }
                            }
                        },
                        ["has_wafer"] = new
                        {
                            entry = new[] { "logHasWafer" },
                            on = new Dictionary<string, object>
                            {
                                ["CMD_PLACE_WAFER"] = new
                                {
                                    target = "placing",
                                    guard = "robotNotBusy"
                                }
                            }
                        },
                        ["placing"] = new
                        {
                            entry = new[] { "logPlacing" },
                            after = new Dictionary<string, object>
                            {
                                ["300"] = new object[]
                                {
                                    new
                                    {
                                        target = "empty",
                                        guard = "placeSuccessful",
                                        actions = new[] { "notifyWaferPlaced" }
                                    },
                                    new
                                    {
                                        target = "has_wafer",
                                        actions = new[] { "notifyPlaceFailed" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    private static object BuildCarrierRegion()
    {
        return new
        {
            initial = "active",
            states = new Dictionary<string, object>
            {
                ["active"] = new
                {
                    entry = new[] { "logCarrierActive" }
                    // Note: Carrier stays active - completion is tracked by orchestrator
                },
                ["completed"] = new
                {
                    type = "final",
                    entry = new[] { "logCarrierCompleted" }
                }
            }
        };
    }

    private static object BuildPlatenRegion()
    {
        return new
        {
            initial = "empty",
            states = new Dictionary<string, object>
            {
                ["empty"] = new
                {
                    entry = new[] { "logPlatenEmpty", "notifyPlatenReady" },
                    on = new Dictionary<string, object>
                    {
                        ["CMD_START_POLISH"] = new
                        {
                            target = "polishing",
                            guard = "platenNotLocked"
                        }
                    }
                },
                ["polishing"] = new
                {
                    entry = new[] { "logPolishingStarted" },
                    after = new Dictionary<string, object>
                    {
                        ["2000"] = new object[]
                        {
                            new
                            {
                                target = "completed",
                                guard = "polishSuccessful",
                                actions = new[] { "notifyPolishCompleted" }
                            },
                            new
                            {
                                target = "error",
                                actions = new[] { "notifyProcessError" }
                            }
                        }
                    }
                },
                ["completed"] = new
                {
                    entry = new[] { "logPlatenCompleted" },
                    on = new Dictionary<string, object>
                    {
                        ["CMD_PICK_WAFER"] = "empty"
                    }
                },
                ["error"] = new
                {
                    entry = new[] { "logPlatenError" },
                    on = new Dictionary<string, object>
                    {
                        ["CLEAR_ERROR"] = "empty"
                    }
                }
            }
        };
    }
}
