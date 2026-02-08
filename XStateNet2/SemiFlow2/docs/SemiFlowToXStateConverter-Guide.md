# SemiFlowToXStateConverter - How It Works

**Draft: 2025.11.23**

---

## What It Does

Takes a **SemiFlow JSON document** as input and produces an **XState state machine definition** as output. It does NOT read `.sfl` files — it only accepts pre-structured JSON.

**Source:** `SemiFlow/SemiFlow/SemiFlow.Converter/SemiFlowToXStateConverter.cs`

---

## Input: SemiFlowDocument (JSON)

```
SemiFlowDocument
├── name, version
├── vars, constants              (global variables)
├── stations[]                   (station definitions)
│   └── id, role, kind, capacity, state, capabilities
├── resourceGroups[]             (shared resource pools)
├── events[]                     (event definitions)
├── metrics[]                    (metric definitions)
├── lanes[]                      (processing lanes - the core)
│   ├── id, priority, maxConcurrentWafers
│   ├── vars, stationPools
│   ├── eventHandlers[]
│   └── workflow
│       ├── id, vars, preconditions, postconditions
│       └── steps[]              (the actual workflow steps)
└── globalHandlers               (onError, onTimeout, onEmergencyStop)
```

---

## The 17 Step Types

Each step in the workflow has a `type` that determines how it converts to XState:

| Step Type        | What It Does                              | XState Output                        |
|------------------|-------------------------------------------|--------------------------------------|
| `action`         | Execute an action                         | State with entry action + completion event |
| `useStation`     | Acquire → use → release a station         | Nested states: acquiring/waiting/using |
| `reserve`        | Reserve resources                         | State with entry action, auto-transition |
| `release`        | Release resources                         | State with entry action, auto-transition |
| `parallel`       | Run branches concurrently                 | `type: "parallel"` with branch regions |
| `loop`           | Repeat steps while condition is true      | Nested states with loop_check guard |
| `branch`         | Conditional branching (if/else if/else)   | Always transitions with guards |
| `switch`         | Switch/case on a value                    | Always transitions with value guards |
| `wait`           | Wait for duration or condition            | `after` (duration) or `always` with guard |
| `condition`      | Assert a condition                        | `always` transition with guard |
| `sequence`       | Group steps sequentially                  | Nested compound state |
| `call`           | Invoke an external service                | `invoke` with onDone transition |
| `try`            | Try/catch/finally error handling          | Nested: try → catch → finally states |
| `emitEvent`      | Emit an event                             | Entry action, auto-transition |
| `onEvent`        | Wait for and handle an event              | waiting → handling nested states |
| `collectMetric`  | Record a metric                           | Entry action, auto-transition |
| `race`           | Run branches, first to finish wins        | Parallel with race semantics |
| `transaction`    | Atomic operation with rollback            | body → commit/rollback nested states |

---

## Conversion Flow

```
SemiFlow JSON string
       |
  JsonSerializer.Deserialize<SemiFlowDocument>()
       |
  BuildContext()          index stations, resources, events, metrics
       |
  Single lane? ──yes──> ConvertSingleLane()
       |                     |
      no                StepConverter.ConvertStepSequence()
       |                     |
  ConvertMultiLane()    Each step → XStateNode
       |                     |
  Creates parallel      Add "completed" final state
  regions per lane           |
       |                XStateMachineScript
       |________________________|
                |
        SerializeToJson()
                |
        XState JSON output
```

---

## Key Design Decisions

1. **Single lane** → direct state machine with sequential states
2. **Multi-lane** → `type: "parallel"` machine with one region per lane
3. **Sync actions** wait for `{action}_DONE` event before transitioning
4. **Async actions** transition immediately via `always`
5. **Station usage** creates nested acquire/wait/use sub-states
6. **Transactions** have body/commit/rollback structure with ERROR handling

---

## Source Files

| File | Lines | Purpose |
|------|-------|---------|
| `SemiFlowToXStateConverter.cs` | 391 | Main converter: single/multi lane, context building |
| `Converters/StepConverter.cs` | 851 | 17 step type conversions to XState nodes |
| `Models/SemiFlowDocument.cs` | 202 | Input model: Document, Station, Lane, Workflow |
| `Models/Steps.cs` | 212 | Step model with all type-specific properties |

All files located under: `SemiFlow/SemiFlow/SemiFlow.Converter/`

---

## Model Details

### Station

```
Station
├── id           (string)     "CMP_001"
├── role         (string)     "polisher"
├── kind         (string)     dedicated | shared | swappable
├── capacity     (int)        1
├── state        (string)     idle | busy | maintenance | error
├── capabilities (string[])   ["POLISH", "CONDITION"]
├── healthCheck
│   ├── interval (int)
│   └── action   (string)
└── meta         (dict)
```

### Lane

```
Lane
├── id                   (string)
├── priority             (int)
├── maxConcurrentWafers  (int)
├── vars                 (dict)
├── stationPools         (dict)
├── eventHandlers[]
│   ├── event   (string)
│   ├── filter  (string)
│   └── steps[] (Step[])
└── workflow
    ├── id              (string)
    ├── vars            (dict)
    ├── preconditions   (string[])
    ├── postconditions  (string[])
    └── steps[]         (Step[])
```

### Step (universal model with type-specific properties)

```
Step (common)
├── id              (string)     unique step identifier
├── type            (string)     one of the 17 step types
├── description     (string?)
├── enabled         (bool)       default true
├── retry           (RetryPolicy?)
├── timeout         (int?)       milliseconds
├── onTimeout       (Step[]?)
├── tags            (string[]?)

Step (action)
├── action          (string)     action name to execute
├── args            (dict?)      action arguments
├── assignResult    (string?)    variable to store result
├── async           (bool?)      fire-and-forget mode

Step (useStation)
├── role            (string)     station role to acquire
├── capability      (string?)    required capability
├── preferred       (string[]?)  preferred station IDs
├── fallback        (string[]?)  fallback station IDs
├── waitForAvailable (bool?)     wait or fail immediately
├── maxWaitTime     (int?)

Step (reserve/release)
├── resources       (string[])   resource IDs

Step (parallel/race)
├── branches        (Step[][])   concurrent branches
├── maxConcurrency  (int?)
├── cancelOthers    (bool?)      race: cancel losers
├── assignWinner    (string?)    race: store winner

Step (loop)
├── condition       (string?)    continue condition (guard)
├── count           (int?)       fixed iteration count
├── items           (string?)    iterate over collection
├── itemVar         (string?)    current item variable
├── indexVar        (string?)    current index variable
├── maxIterations   (int?)       safety limit
├── steps           (Step[])     loop body

Step (branch)
├── cases           (BranchCase[])
│   ├── when        (string)     guard condition
│   └── steps       (Step[])
├── otherwise       (Step[]?)    else branch

Step (switch)
├── value           (string)     expression to switch on
├── cases           (dict)       { "value": Step[] }
├── default         (Step[]?)

Step (wait)
├── duration        (int?)       wait milliseconds
├── until           (string?)    wait for condition

Step (condition)
├── expect          (string)     guard to assert

Step (call)
├── target          (string)     service to invoke

Step (try)
├── try             (Step[])     try block
├── catch           (Step[]?)    catch block
├── finally         (Step[]?)    finally block
├── catchOn         (string[]?)  error types to catch

Step (emitEvent/onEvent)
├── event           (string)     event name
├── payload         (dict?)      event data
├── filter          (string?)    event filter
├── once            (bool?)      handle only once

Step (collectMetric)
├── metric          (string)     metric name

Step (transaction)
├── steps           (Step[])     transaction body
├── rollback        (Step[]?)    rollback on error
├── isolationLevel  (string?)
```

### RetryPolicy

```
RetryPolicy
├── count      (int)        number of retries
├── delay      (int)        delay in ms
├── strategy   (string)     fixed | exponential | linear
├── maxDelay   (int?)       cap on delay
├── jitter     (bool)       add randomness
├── retryOn    (string[]?)  error types to retry on
```

---

## Conversion Examples

### action step

**Input (JSON):**
```json
{ "id": "polish", "type": "action", "action": "startPolishing", "timeout": 30000 }
```

**Output (XState):**
```json
"polish": {
    "entry": ["startPolishing"],
    "after": { "30000": [{ "target": "nextState" }] },
    "on": { "startPolishing_DONE": [{ "target": "nextState" }] }
}
```

### useStation step

**Input:** useStation with role "polisher"

**Output (XState):**
```json
"acquirePolisher": {
    "initial": "acquiring",
    "entry": ["acquireStation_polisher"],
    "states": {
        "acquiring": {
            "entry": ["requestStation_polisher"],
            "on": {
                "STATION_ACQUIRED": [{ "target": "using" }],
                "STATION_UNAVAILABLE": [{ "target": "waiting" }]
            }
        },
        "waiting": {
            "after": { "1000": [{ "target": "acquiring" }] }
        },
        "using": {
            "on": {
                "USAGE_COMPLETE": [{
                    "target": "..nextState",
                    "actions": ["releaseStation_polisher"]
                }]
            }
        }
    }
}
```

### transaction step

**Input:** Transaction with steps + rollback

**Output (XState):**
```json
"myTransaction": {
    "entry": ["beginTransaction"],
    "initial": "body",
    "states": {
        "body": {
            "initial": "step1",
            "states": { "..." },
            "on": { "ERROR": [{ "target": "rollback" }] }
        },
        "commit": {
            "entry": ["commitTransaction"],
            "always": [{ "target": "..nextState" }]
        },
        "rollback": {
            "entry": ["rollbackTransaction"],
            "states": { "..." }
        }
    }
}
```

### parallel step

**Input:** Parallel with 2 branches

**Output (XState):**
```json
"parallelStep": {
    "type": "parallel",
    "states": {
        "branch_0": {
            "initial": "step1",
            "states": { "...": "...", "final": { "type": "final" } }
        },
        "branch_1": {
            "initial": "step2",
            "states": { "...": "...", "final": { "type": "final" } }
        }
    },
    "onDone": { "target": "nextState" }
}
```

---

## Relationship to the SFL Compiler

This converter is the **second half** of the pipeline. The SFL compiler needs to produce `SemiFlowDocument` JSON that this converter already knows how to process:

```
.sfl file → [SFL Compiler] → SemiFlowDocument JSON → [This Converter] → XState JSON
                                                             |
                                                      (already exists)
```
