# SemiFlow to XState Conversion Example
## CMP 1 Foup, 1 Robot, 1 Platen System

This document shows how a SemiFlow workflow is converted to XState format.

## System Overview

**Equipment Configuration:**
- 1 FOUP (Front Opening Unified Pod) with 25 wafer slots
- 1 Robot with single arm for wafer handling
- 1 Platen for CMP (Chemical Mechanical Planarization) polishing

**Process Flow:**
1. Robot picks unprocessed wafer from FOUP
2. Robot transports wafer to platen
3. Wafer is polished on platen
4. Robot retrieves processed wafer from platen
5. Robot returns wafer to FOUP
6. Repeat for all 25 wafers

## Key Conversion Patterns

### 1. Simple Action Steps

**SemiFlow:**
```json
{
  "id": "initialize",
  "type": "action",
  "action": "initializeSystem"
}
```

**XState:**
```json
{
  "initialize": {
    "entry": ["initializeSystem"],
    "always": [{ "target": "next_state" }]
  }
}
```

### 2. Condition Steps

**SemiFlow:**
```json
{
  "id": "check_wafers_available",
  "type": "condition",
  "expect": "hasUnprocessedWafers",
  "message": "No wafers available"
}
```

**XState:**
```json
{
  "check_wafers_available": {
    "always": [
      {
        "target": "start_cycle",
        "guard": "hasUnprocessedWafers"
      },
      {
        "target": "completed"
      }
    ]
  }
}
```

### 3. Sequence Steps

**SemiFlow:**
```json
{
  "id": "move_robot_to_foup",
  "type": "sequence",
  "steps": [
    {
      "id": "send_move_to_foup",
      "type": "action",
      "action": "sendMoveToFoup"
    },
    {
      "id": "wait_robot_at_foup",
      "type": "wait",
      "duration": 500
    }
  ]
}
```

**XState:**
```json
{
  "move_robot_to_foup": {
    "initial": "sending_command",
    "states": {
      "sending_command": {
        "entry": ["sendMoveToFoup"],
        "after": {
          "500": "arrived"
        }
      },
      "arrived": {
        "always": [{ "target": "..next_state" }]
      }
    }
  }
}
```

### 4. Try-Catch with Retry

**SemiFlow:**
```json
{
  "id": "pick_wafer_from_foup",
  "type": "try",
  "try": [
    {
      "id": "execute_pick",
      "type": "action",
      "action": "pickWaferFromFoup",
      "timeout": 5000
    },
    {
      "id": "verify_pick",
      "type": "condition",
      "expect": "robotHasWafer"
    },
    {
      "id": "update_wafer_picked",
      "type": "action",
      "action": "recordWaferPicked"
    }
  ],
  "catch": [
    {
      "id": "log_pick_error",
      "type": "action",
      "action": "logPickError"
    }
  ],
  "retry": {
    "count": 3,
    "delay": 1000,
    "strategy": "exponential"
  }
}
```

**XState:**
```json
{
  "pick_wafer_from_foup": {
    "initial": "trying",
    "states": {
      "trying": {
        "initial": "executing",
        "states": {
          "executing": {
            "entry": ["pickWaferFromFoup"],
            "on": {
              "PICK_SUCCESS": "verifying",
              "PICK_FAILED": "..handle_error"
            },
            "after": {
              "5000": "..handle_error"
            }
          },
          "verifying": {
            "always": [
              {
                "target": "recording",
                "guard": "robotHasWafer"
              },
              {
                "target": "..handle_error"
              }
            ]
          },
          "recording": {
            "entry": ["recordWaferPicked"],
            "always": [{ "target": "..success" }]
          }
        }
      },
      "handle_error": {
        "entry": ["logPickError"],
        "always": [
          {
            "target": "retrying",
            "guard": "canRetry"
          },
          {
            "target": "..error_fatal"
          }
        ]
      },
      "retrying": {
        "entry": ["incrementRetryCount"],
        "after": {
          "1000": "trying"
        }
      },
      "success": {
        "type": "final"
      }
    },
    "onDone": {
      "target": "next_state"
    }
  }
}
```

### 5. Parallel Steps

**SemiFlow:**
```json
{
  "id": "polish_wafer",
  "type": "parallel",
  "branches": [
    [
      {
        "id": "start_polish",
        "type": "action",
        "action": "startPolishing"
      },
      {
        "id": "wait_polish_complete",
        "type": "wait",
        "duration": 2000
      },
      {
        "id": "stop_polish",
        "type": "action",
        "action": "stopPolishing"
      }
    ],
    [
      {
        "id": "robot_return_to_home",
        "type": "action",
        "action": "sendRobotToHome"
      },
      {
        "id": "wait_robot_home",
        "type": "wait",
        "duration": 500
      }
    ]
  ],
  "wait": "all"
}
```

**XState:**
```json
{
  "polish_wafer": {
    "type": "parallel",
    "states": {
      "polishing": {
        "initial": "starting",
        "states": {
          "starting": {
            "entry": ["startPolishing"],
            "after": {
              "2000": "stopping"
            }
          },
          "stopping": {
            "entry": ["stopPolishing"],
            "always": [{ "target": "done" }]
          },
          "done": {
            "type": "final"
          }
        }
      },
      "robot_returning": {
        "initial": "moving",
        "states": {
          "moving": {
            "entry": ["sendRobotToHome"],
            "after": {
              "500": "home"
            }
          },
          "home": {
            "type": "final"
          }
        }
      }
    },
    "onDone": {
      "target": "next_state"
    }
  }
}
```

### 6. Branch/Conditional

**SemiFlow:**
```json
{
  "id": "check_more_wafers",
  "type": "branch",
  "cases": [
    {
      "when": "hasMoreWafers",
      "steps": [
        {
          "id": "loop_continue",
          "type": "action",
          "action": "continueProcessing"
        }
      ]
    }
  ],
  "otherwise": [
    {
      "id": "all_complete",
      "type": "action",
      "action": "allWafersComplete"
    }
  ]
}
```

**XState:**
```json
{
  "check_more_wafers": {
    "initial": "checking",
    "states": {
      "checking": {
        "always": [
          {
            "target": "continue_processing",
            "guard": "hasMoreWafers"
          },
          {
            "target": "all_complete"
          }
        ]
      },
      "continue_processing": {
        "entry": ["continueProcessing"],
        "always": [{ "target": "..start_cycle" }]
      },
      "all_complete": {
        "entry": ["allWafersComplete"],
        "always": [{ "target": "..completed" }]
      }
    }
  }
}
```

### 7. Wait Steps

**SemiFlow:**
```json
{
  "id": "wait_for_platen_ready",
  "type": "wait",
  "until": "platenIsReady",
  "pollInterval": 100,
  "timeout": 10000
}
```

**XState:**
```json
{
  "wait_for_platen_ready": {
    "entry": ["checkPlatenStatus"],
    "always": [
      {
        "target": "next_state",
        "guard": "platenIsReady"
      }
    ],
    "after": {
      "10000": "error_fatal"
    }
  }
}
```

### 8. Emit Event and Collect Metrics

**SemiFlow:**
```json
{
  "id": "emit_cycle_complete",
  "type": "emitEvent",
  "event": "CYCLE_COMPLETE"
},
{
  "id": "collect_metric",
  "type": "collectMetric",
  "metric": "cycle_time",
  "value": "elapsedTime"
}
```

**XState:**
```json
{
  "emit_cycle_complete": {
    "entry": [
      "emitEvent_CYCLE_COMPLETE",
      "collectMetric_cycle_time"
    ],
    "always": [{ "target": "next_state" }]
  }
}
```

## State Machine Visualization

### High-Level Flow

```
initialize
    ↓
check_wafers_available
    ↓
start_cycle
    ↓
move_robot_to_foup → pick_wafer_from_foup (with retry)
    ↓
move_robot_to_platen → wait_for_platen_ready → place_wafer_on_platen (with retry)
    ↓
polish_wafer (parallel: polishing + robot_returning)
    ↓
move_robot_back_to_platen → pick_processed_wafer (with retry)
    ↓
move_robot_back_to_foup → place_processed_wafer (with retry)
    ↓
finalize_wafer_record → emit_cycle_complete
    ↓
check_more_wafers
    ├─ hasMoreWafers: → start_cycle (loop)
    └─ no more: → completed (final)
```

## Context (State Variables)

**SemiFlow Constants:**
```json
{
  "total_wafers": 25,
  "polish_time_ms": 2000,
  "robot_move_time_ms": 500,
  "pick_place_time_ms": 300,
  "timeout_ms": 5000,
  "max_retries": 3
}
```

**SemiFlow Variables:**
```json
{
  "wafers_unprocessed": 25,
  "wafers_processed": 0,
  "current_wafer_id": null,
  "wafer_history": [],
  "retry_count": 0,
  "error_code": null,
  "error_message": null
}
```

**XState Context:** (merged constants + vars + station state)
```json
{
  "total_wafers": 25,
  "polish_time_ms": 2000,
  "wafers_unprocessed": 25,
  "wafers_processed": 0,
  "retry_count": 0,
  "stations": {
    "foup_1": { "state": "idle", "capacity": 25 },
    "robot_1": { "state": "idle", "has_wafer": false, "position": "home" },
    "platen_1": { "state": "idle", "has_wafer": false }
  }
}
```

## Guards (Conditions)

SemiFlow conditions become XState guards:

| SemiFlow Condition | XState Guard |
|-------------------|--------------|
| `hasUnprocessedWafers` | `(context) => context.wafers_unprocessed > 0` |
| `robotHasWafer` | `(context) => context.stations.robot_1.has_wafer === true` |
| `platenIsReady` | `(context) => context.stations.platen_1.state === "idle"` |
| `platenHasWafer` | `(context) => context.stations.platen_1.has_wafer === true` |
| `waferInFoup` | `(context) => !context.stations.robot_1.has_wafer` |
| `canRetry` | `(context) => context.retry_count < context.max_retries` |
| `hasMoreWafers` | `(context) => context.wafers_unprocessed > 0` |

## Actions

SemiFlow actions become XState actions:

```typescript
const actions = {
  initializeSystem: assign((context) => ({
    ...context,
    wafers_unprocessed: context.total_wafers,
    wafers_processed: 0,
    retry_count: 0
  })),

  createWaferRecord: assign((context) => ({
    ...context,
    current_wafer_id: `wafer_${Date.now()}`,
    cycle_start_time: Date.now()
  })),

  pickWaferFromFoup: assign((context) => ({
    ...context,
    stations: {
      ...context.stations,
      robot_1: {
        ...context.stations.robot_1,
        has_wafer: true
      }
    }
  })),

  decrementUnprocessedCount: assign((context) => ({
    ...context,
    wafers_unprocessed: context.wafers_unprocessed - 1
  })),

  incrementProcessedCount: assign((context) => ({
    ...context,
    wafers_processed: context.wafers_processed + 1
  })),

  incrementRetryCount: assign((context) => ({
    ...context,
    retry_count: context.retry_count + 1
  })),

  resetRetryCount: assign((context) => ({
    ...context,
    retry_count: 0
  }))
};
```

## Events

SemiFlow events become XState events:

| SemiFlow Event | XState Event | Trigger |
|---------------|--------------|---------|
| `WAFER_PICKED` | `PICK_SUCCESS` | Robot successfully picks wafer |
| `WAFER_PLACED` | `PLACE_SUCCESS` | Robot successfully places wafer |
| `ROBOT_AT_FOUP` | Implicit (after delay) | Robot arrives at FOUP |
| `ROBOT_AT_PLATEN` | Implicit (after delay) | Robot arrives at platen |
| `POLISH_COMPLETE` | Implicit (after delay) | Polishing completes |
| `PLATEN_READY` | Check via guard | Platen becomes available |
| `CYCLE_COMPLETE` | Action side-effect | One wafer cycle completes |
| `ALL_WAFERS_COMPLETE` | Action side-effect | All wafers processed |
| `ERROR_OCCURRED` | `PICK_FAILED`, `PLACE_FAILED` | Operation fails |
| `EMERGENCY_STOP` | `EMERGENCY_STOP` | Emergency stop triggered |

## Error Handling

**SemiFlow Global Handlers:**
```json
{
  "globalHandlers": {
    "onError": [
      { "type": "action", "action": "logErrorToSystem" },
      { "type": "emitEvent", "event": "ERROR_OCCURRED" }
    ]
  }
}
```

**XState Global Handler:**
```json
{
  "on": {
    "EMERGENCY_STOP": {
      "target": ".error_fatal",
      "actions": ["stopAllMotion", "releaseAllResources"]
    }
  }
}
```

## Metrics Collection

**SemiFlow:**
```json
{
  "metrics": [
    {
      "name": "cycle_time",
      "type": "timer",
      "unit": "ms",
      "aggregation": "avg"
    },
    {
      "name": "wafers_processed_count",
      "type": "counter",
      "unit": "count",
      "aggregation": "sum"
    }
  ]
}
```

**XState Implementation:**
```typescript
const actions = {
  collectMetric_cycle_time: (context) => {
    const elapsed = Date.now() - context.cycle_start_time;
    metricsCollector.recordTimer('cycle_time', elapsed);
  },

  collectMetric_wafers_processed_count: () => {
    metricsCollector.incrementCounter('wafers_processed_count');
  }
};
```

## Key Differences

### 1. Structure
- **SemiFlow**: Flat list of steps with sequential linking
- **XState**: Hierarchical nested states with explicit transitions

### 2. Control Flow
- **SemiFlow**: Implicit sequencing (steps auto-link to next)
- **XState**: Explicit transitions (always/on/after)

### 3. Error Handling
- **SemiFlow**: Try-catch blocks with retry policies
- **XState**: Error states with conditional transitions

### 4. Parallelism
- **SemiFlow**: Parallel branches with wait strategies
- **XState**: Parallel regions with onDone

### 5. Resource Management
- **SemiFlow**: First-class stations and resource pools
- **XState**: Modeled in context and actions

## Benefits of Each Format

### SemiFlow Benefits
- ✅ Domain-specific for manufacturing
- ✅ Built-in resource management
- ✅ Declarative retry policies
- ✅ Explicit metrics and events
- ✅ Higher-level abstractions
- ✅ Easier for process engineers

### XState Benefits
- ✅ Industry-standard format
- ✅ Excellent tooling and visualization
- ✅ Battle-tested execution engine
- ✅ TypeScript support
- ✅ Developer-friendly
- ✅ Large ecosystem

## Conclusion

The SemiFlow to XState conversion demonstrates how domain-specific workflow DSLs can be translated to standard state machine formats. SemiFlow provides manufacturing-specific abstractions (stations, resource pools, retry policies) while XState provides a robust execution model with excellent tooling.

The converter bridges these worlds, allowing process engineers to design in SemiFlow and execute on XStateNet2.
