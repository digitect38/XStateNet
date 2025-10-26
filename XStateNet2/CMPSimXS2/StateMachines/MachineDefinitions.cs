namespace CMPSimXS2.StateMachines;

/// <summary>
/// XState V5 JSON definitions for all CMP components
/// </summary>
public static class MachineDefinitions
{
    public static string GetPolisherMachine() => """
    {
        "id": "polisher",
        "initial": "idle",
        "context": {
            "wafer": null,
            "processTime": 60,
            "currentWafer": null
        },
        "states": {
            "idle": {
                "on": {
                    "LOAD_WAFER": {
                        "target": "processing",
                        "actions": ["storeWafer"]
                    }
                }
            },
            "processing": {
                "entry": ["startProcessing"],
                "after": {
                    "2000": {
                        "target": "done",
                        "actions": ["completeProcessing"]
                    }
                }
            },
            "done": {
                "on": {
                    "UNLOAD_WAFER": {
                        "target": "idle",
                        "actions": ["clearWafer"]
                    }
                }
            }
        }
    }
    """;

    public static string GetCleanerMachine() => """
    {
        "id": "cleaner",
        "initial": "idle",
        "context": {
            "wafer": null,
            "cleanTime": 30,
            "currentWafer": null
        },
        "states": {
            "idle": {
                "on": {
                    "LOAD_WAFER": {
                        "target": "cleaning",
                        "actions": ["storeWafer"]
                    }
                }
            },
            "cleaning": {
                "entry": ["startCleaning"],
                "after": {
                    "1000": {
                        "target": "done",
                        "actions": ["completeCleaning"]
                    }
                }
            },
            "done": {
                "on": {
                    "UNLOAD_WAFER": {
                        "target": "idle",
                        "actions": ["clearWafer"]
                    }
                }
            }
        }
    }
    """;

    public static string GetBufferMachine() => """
    {
        "id": "buffer",
        "initial": "empty",
        "context": {
            "wafer": null,
            "capacity": 1,
            "currentWafer": null
        },
        "states": {
            "empty": {
                "on": {
                    "STORE_WAFER": {
                        "target": "occupied",
                        "actions": ["storeWafer"]
                    }
                }
            },
            "occupied": {
                "on": {
                    "RETRIEVE_WAFER": {
                        "target": "empty",
                        "actions": ["clearWafer"]
                    }
                }
            }
        }
    }
    """;

    public static string GetLoadPortMachine() => """
    {
        "id": "loadport",
        "initial": "empty",
        "context": {
            "currentWafer": null,
            "carrier": null
        },
        "states": {
            "empty": {
                "on": {
                    "LOAD_CARRIER": {
                        "target": "loaded",
                        "actions": ["storeCarrier"]
                    }
                }
            },
            "loaded": {
                "on": {
                    "PICK_WAFER": {
                        "target": "loaded",
                        "actions": ["pickupWafer"]
                    },
                    "UNLOAD_CARRIER": {
                        "target": "empty",
                        "actions": ["clearCarrier"]
                    }
                }
            }
        }
    }
    """;

    public static string GetCarrierMachine() => """
    {
        "id": "carrier",
        "initial": "loaded",
        "context": {
            "wafers": [],
            "capacity": 25,
            "processedCount": 0,
            "currentWafer": null
        },
        "states": {
            "loaded": {
                "on": {
                    "PICK_WAFER": {
                        "target": "loaded",
                        "actions": ["removeWafer"],
                        "cond": "hasWafers"
                    },
                    "PLACE_WAFER": {
                        "target": "loaded",
                        "actions": ["addWafer"]
                    },
                    "ALL_PROCESSED": {
                        "target": "completed",
                        "cond": "allWafersProcessed"
                    }
                }
            },
            "completed": {
                "type": "final"
            }
        }
    }
    """;

    public static string GetRobotMachine() => """
    {
        "id": "robot",
        "initial": "idle",
        "context": {
            "wafer": null,
            "fromStation": null,
            "toStation": null,
            "currentWafer": null
        },
        "states": {
            "idle": {
                "on": {
                    "PICKUP": {
                        "target": "transferring",
                        "actions": ["pickupWafer"]
                    }
                }
            },
            "transferring": {
                "after": {
                    "500": {
                        "target": "idle",
                        "actions": ["placeWafer", "clearWafer"]
                    }
                },
                "on": {
                    "PLACE": {
                        "target": "idle",
                        "actions": ["placeWafer", "clearWafer"]
                    }
                }
            }
        }
    }
    """;

    public static string GetMasterSchedulerMachine() => """
    {
        "id": "masterScheduler",
        "initial": "idle",
        "context": {
            "carrierQueue": [],
            "currentCarrier": null,
            "activeWafers": [],
            "totalProcessed": 0,
            "systemState": "idle"
        },
        "states": {
            "idle": {
                "on": {
                    "START_PROCESSING": {
                        "target": "processing",
                        "actions": ["initializeSystem"]
                    }
                }
            },
            "processing": {
                "on": {
                    "WAFER_READY": {
                        "actions": ["scheduleWafer"]
                    },
                    "WAFER_COMPLETED": {
                        "actions": ["completeWafer"]
                    },
                    "PAUSE": {
                        "target": "paused"
                    },
                    "STOP": {
                        "target": "idle"
                    }
                }
            },
            "paused": {
                "on": {
                    "RESUME": {
                        "target": "processing"
                    },
                    "STOP": {
                        "target": "idle"
                    }
                }
            }
        }
    }
    """;
}
