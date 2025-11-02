namespace CMPSimXS2.WPF.StateMachines;

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
            "processTime": 5000,
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
                    "5000": {
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
            "cleanTime": 3000,
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
                    "3000": {
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
                        "target": "carrying",
                        "actions": ["pickupWafer"]
                    }
                }
            },
            "carrying": {
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
}
