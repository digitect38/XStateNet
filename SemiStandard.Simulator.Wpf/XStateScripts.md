# XStateNet State Machine Scripts for SEMI Equipment Simulator

## All 11 State Machines with their XState Scripts

### 1. EquipmentController
```json
{
    'id': 'EquipmentController',
    'initial': 'OFFLINE',
    'states': {
        'OFFLINE': {
            'on': {
                'POWER_ON': 'INITIALIZING'
            }
        },
        'INITIALIZING': {
            'on': {
                'INIT_SUCCESS': 'ONLINE',
                'INIT_FAILURE': 'ERROR'
            }
        },
        'ONLINE': {
            'on': {
                'WAFER_IN': 'PROCESSING',
                'ERROR_DETECTED': 'ERROR',
                'SHUTDOWN': 'OFFLINE'
            }
        },
        'PROCESSING': {
            'on': {
                'PROCESS_COMPLETE': 'ONLINE',
                'PROCESS_ERROR': 'ERROR'
            }
        },
        'ERROR': {
            'on': {
                'RESET': 'INITIALIZING',
                'CLEAR_ERROR': 'ONLINE'
            }
        }
    }
}
```

### 2. TransportHandler
```json
{
    'id': 'TransportHandler',
    'initial': 'IDLE',
    'states': {
        'IDLE': {
            'on': {
                'INIT_COMPLETE': 'READY'
            }
        },
        'READY': {
            'on': {
                'WAFER_TRANSFER': 'MOVING',
                'ERROR': 'ERROR'
            }
        },
        'MOVING': {
            'on': {
                'TRANSFER_COMPLETE': 'TRANSFERRING',
                'MOVEMENT_ERROR': 'ERROR'
            }
        },
        'TRANSFERRING': {
            'on': {
                'TRANSFER_DONE': 'READY',
                'TRANSFER_ERROR': 'ERROR'
            }
        },
        'ERROR': {
            'on': {
                'RESET': 'IDLE',
                'RECOVER': 'READY'
            }
        }
    }
}
```

### 3. ProcessManager
```json
{
    'id': 'ProcessManager',
    'initial': 'NOT_READY',
    'states': {
        'NOT_READY': {
            'on': {
                'SYSTEM_READY': 'IDLE'
            }
        },
        'IDLE': {
            'on': {
                'START_PROCESS': 'SETUP',
                'SHUTDOWN': 'NOT_READY'
            }
        },
        'SETUP': {
            'on': {
                'PROCESS_START': 'EXECUTING',
                'SETUP_ERROR': 'IDLE'
            }
        },
        'EXECUTING': {
            'on': {
                'WAFER_OUT': 'COMPLETE',
                'PROCESS_ERROR': 'IDLE'
            }
        },
        'COMPLETE': {
            'on': {
                'RESET': 'IDLE',
                'NEXT_PROCESS': 'SETUP'
            }
        }
    }
}
```

### 4. RecipeExecutor
```json
{
    'id': 'RecipeExecutor',
    'initial': 'WAITING',
    'states': {
        'WAITING': {
            'on': {
                'LOAD_RECIPE': 'LOADING'
            }
        },
        'LOADING': {
            'on': {
                'RECIPE_LOADED': 'VALIDATING',
                'LOAD_ERROR': 'WAITING'
            }
        },
        'VALIDATING': {
            'on': {
                'VALIDATION_OK': 'EXECUTING',
                'VALIDATION_FAIL': 'WAITING'
            }
        },
        'EXECUTING': {
            'on': {
                'RECIPE_DONE': 'COMPLETE',
                'EXECUTION_ERROR': 'WAITING'
            }
        },
        'COMPLETE': {
            'on': {
                'RESET': 'WAITING',
                'LOAD_NEXT': 'LOADING'
            }
        }
    }
}
```

### 5. E30GEM (SEMI E30 Generic Equipment Model)
```json
{
    'id': 'E30GEM',
    'initial': 'disabled',
    'states': {
        'disabled': {
            'on': {
                'ENABLE_COMMAND': 'enabled'
            }
        },
        'enabled': {
            'on': {
                'HOST_SELECT': 'selected',
                'DISABLE_COMMAND': 'disabled'
            }
        },
        'selected': {
            'on': {
                'START_PROCESSING': 'executing',
                'HOST_DESELECT': 'enabled'
            }
        },
        'executing': {
            'on': {
                'PROCESS_COMPLETE': 'selected',
                'ABORT': 'selected',
                'ERROR': 'selected'
            }
        }
    }
}
```

### 6. E87Carrier (SEMI E87 Carrier Management)
```json
{
    'id': 'E87Carrier',
    'initial': 'NotPresent',
    'states': {
        'NotPresent': {
            'on': {
                'CARRIER_DETECTED': 'Present'
            }
        },
        'Present': {
            'on': {
                'CARRIER_MAPPED': 'Mapped',
                'CARRIER_REMOVED': 'NotPresent'
            }
        },
        'Mapped': {
            'on': {
                'START_CARRIER': 'Processing',
                'CARRIER_UNMAPPED': 'Present'
            }
        },
        'Processing': {
            'on': {
                'CARRIER_COMPLETE': 'Mapped',
                'CARRIER_ERROR': 'Mapped'
            }
        }
    }
}
```

### 7. E94ControlJob (SEMI E94 Control Job Management)
```json
{
    'id': 'E94ControlJob',
    'initial': 'NoJob',
    'states': {
        'NoJob': {
            'on': {
                'CREATE_JOB': 'Created'
            }
        },
        'Created': {
            'on': {
                'CJ_START': 'Running',
                'CJ_CANCEL': 'NoJob'
            }
        },
        'Running': {
            'on': {
                'CJ_COMPLETE': 'Complete',
                'CJ_ABORT': 'Created',
                'CJ_ERROR': 'Created'
            }
        },
        'Complete': {
            'on': {
                'NEW_JOB': 'Created',
                'CLEAR_JOB': 'NoJob'
            }
        }
    }
}
```

### 8. E37HSMSSession (SEMI E37 HSMS Communication)
```json
{
    'id': 'E37HSMSSession',
    'initial': 'NotConnected',
    'states': {
        'NotConnected': {
            'on': {
                'CONNECT': 'Connected'
            }
        },
        'Connected': {
            'on': {
                'SELECT': 'Selected',
                'DISCONNECT': 'NotConnected'
            }
        },
        'Selected': {
            'on': {
                'ACTIVATE': 'Active',
                'DESELECT': 'Connected'
            }
        },
        'Active': {
            'on': {
                'DEACTIVATE': 'Selected',
                'COMM_ERROR': 'Connected'
            }
        }
    }
}
```

### 9. ProcessControl (Additional Process Control)
```json
{
    'id': 'ProcessControl',
    'initial': 'IDLE',
    'states': {
        'IDLE': {
            'on': {
                'START_REQUEST': 'STARTING'
            }
        },
        'STARTING': {
            'on': {
                'PROCESS_READY': 'PROCESSING',
                'START_FAILED': 'IDLE'
            }
        },
        'PROCESSING': {
            'on': {
                'STOP_REQUEST': 'STOPPING',
                'PROCESS_COMPLETE': 'IDLE'
            }
        },
        'STOPPING': {
            'on': {
                'STOPPED': 'IDLE',
                'STOP_ERROR': 'PROCESSING'
            }
        }
    }
}
```

### 10. MaterialHandling
```json
{
    'id': 'MaterialHandling',
    'initial': 'NO_MATERIAL',
    'states': {
        'NO_MATERIAL': {
            'on': {
                'LOAD_START': 'LOADING'
            }
        },
        'LOADING': {
            'on': {
                'LOAD_COMPLETE': 'LOADED',
                'LOAD_ERROR': 'NO_MATERIAL'
            }
        },
        'LOADED': {
            'on': {
                'UNLOAD_START': 'UNLOADING',
                'MATERIAL_ERROR': 'NO_MATERIAL'
            }
        },
        'UNLOADING': {
            'on': {
                'UNLOAD_COMPLETE': 'NO_MATERIAL',
                'UNLOAD_ERROR': 'LOADED'
            }
        }
    }
}
```

### 11. AlarmManager
```json
{
    'id': 'AlarmManager',
    'initial': 'NO_ALARM',
    'states': {
        'NO_ALARM': {
            'on': {
                'WARNING_DETECTED': 'WARNING',
                'ERROR_DETECTED': 'ERROR',
                'CRITICAL_DETECTED': 'CRITICAL'
            }
        },
        'WARNING': {
            'on': {
                'ALARM_CLEARED': 'NO_ALARM',
                'ERROR_DETECTED': 'ERROR',
                'CRITICAL_DETECTED': 'CRITICAL'
            }
        },
        'ERROR': {
            'on': {
                'ERROR_RESOLVED': 'WARNING',
                'ALARM_CLEARED': 'NO_ALARM',
                'CRITICAL_DETECTED': 'CRITICAL'
            }
        },
        'CRITICAL': {
            'on': {
                'CRITICAL_RESOLVED': 'ERROR',
                'EMERGENCY_CLEAR': 'NO_ALARM'
            }
        }
    }
}
```

## State Machine Integration

These state machines are created using XStateNet's `StateMachine.CreateFromScript()` method and properly track state transitions in real-time. Each machine:

1. Has a unique ID
2. Defines an initial state
3. Specifies all possible states and their transitions
4. Uses event-driven transitions (e.g., 'POWER_ON', 'INIT_SUCCESS')
5. Supports OnTransition callbacks for real-time monitoring

The state machines are integrated into the WPF simulator to provide real-time visualization in the UML timing diagram with swimming lanes.