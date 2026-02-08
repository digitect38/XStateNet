# Semi Flow Language (SFL) - Complete Guide

## What is SFL? Why Does It Exist?

**Semi Flow Language (SFL)** is a domain-specific language for defining hierarchical scheduling systems in semiconductor manufacturing.

### The Problem

In a semiconductor fab (e.g., CMP polishing line), scheduling 25 wafers through multiple stations with robots requires coordinating:

- **Who** decides which wafer goes where (Master Scheduler)
- **What** each wafer's journey looks like (Wafer Scheduler)
- **How** robots physically move wafers (Robot Scheduler)
- **Where** processing happens (Stations)

Without SFL, engineers must write thousands of lines of JSON state machine definitions by hand. SFL reduces this to ~100-200 lines of readable, declarative code.

### The Solution

SFL provides a **4-layer hierarchical model** that mirrors real fab architecture:

```
L1: MASTER_SCHEDULER  -->  "Distribute 25 wafers across 3 groups"
        |
L2: WAFER_SCHEDULER   -->  "Track my 9 wafers through the pipeline"
        |
L3: ROBOT_SCHEDULER   -->  "Move wafer from polisher to cleaner"
        |
L4: STATION            -->  "Polish this wafer for 180 seconds"
```

- **Commands** flow downward (L1 -> L2 -> L3 -> L4) via Transactions
- **Status** flows upward (L4 -> L1) via Pub/Sub messaging

### Compilation Target

```
  .sfl source file
        |
   [SFL Compiler]
        |
   XState JSON (state machine definitions)
        |
   XStateNet2 Engine (executes the state machines)
        |
   Semiconductor fab operates
```

---

## File Types

| Extension | Purpose               | Example                |
|-----------|-----------------------|------------------------|
| `.sfl`    | Primary source files  | `cmp_line.sfl`         |
| `.sfli`   | Include/header files  | `common_types.sfli`    |
| `.sflc`   | Configuration files   | `fab_settings.sflc`    |

---

## Language Basics

### Program Structure

Every SFL program follows this structure:

```sfl
// 1. Imports (optional)
import semiflow.algorithms.cyclic_zip
import semiflow.semi.e90

// 2. System architecture declaration (optional)
SYSTEM_ARCHITECTURE {
    NAME: "My_FAB_Line"
    VERSION: "1.0.0"
}

// 3. Scheduler definitions (required - at least one)
MASTER_SCHEDULER MSC_001 { ... }
WAFER_SCHEDULER WSC_001 { ... }
ROBOT_SCHEDULER RSC_001 { ... }
STATION STN_001 { ... }

// 4. Supporting definitions (optional)
PIPELINE_SCHEDULING_RULES { ... }
MESSAGE_BROKER STATUS_BROKER_01 { ... }
TRANSACTION_MANAGER TXN_MGR_001 { ... }
```

### Comments

```sfl
// Single-line comment

/*
   Multi-line
   comment
*/
```

### Identifiers

```sfl
// Scheduler IDs - uppercase with underscores
MSC_001          // Master Scheduler
WSC_ZONE_A       // Wafer Scheduler
RSC_EFEM_01      // Robot Scheduler

// Wafer IDs - W + 3 digits
W001, W002, ..., W025

// Station IDs
STN_CMP01        // CMP polishing station
STN_CLN02        // Cleaning station

// Transaction IDs - timestamp-based
TXN_20250101120000_00001_A3F2
```

### Literals

```sfl
// Numbers
wafer_count: 25
max_velocity: 2.0

// Strings
name: "CMP_Polisher"

// Durations - number + unit
timeout: 30s        // seconds
interval: 5m        // minutes
ttl: 1h             // hours
latency: 100ms      // milliseconds

// Frequencies
update_rate: 10Hz
```

### Operators

| Operator | Meaning                        |
|----------|--------------------------------|
| `->`     | Transition / flow direction    |
| `=>`     | Mapping                        |
| `::`     | Scope resolution               |
| `@`      | QoS level (e.g., `@2`)        |
| `#`      | Reference                      |
| `\|>`    | Pipeline forward               |
| `<\|`    | Pipeline backward              |
| `:`      | Property assignment            |

---

## The 4-Layer Scheduler Hierarchy

### Visual Overview

```
                    +-------------------+
                    | MASTER_SCHEDULER  |  L1 - Orchestration
                    |     (MSC)         |
                    +--------+----------+
                             |
           +-----------------+-----------------+
           |                 |                 |
    +------v------+   +------v------+   +------v------+
    |   WSC_001   |   |   WSC_002   |   |   WSC_003   |  L2 - Wafer Management
    | (9 wafers)  |   | (8 wafers)  |   | (8 wafers)  |
    +------+------+   +------+------+   +------+------+
           |                 |                 |
    +------v------+   +------v------+   +------v------+
    |   RSC_001   |   |   RSC_002   |   |   RSC_003   |  L3 - Robot Control
    +------+------+   +------+------+   +------+------+
           |                 |                 |
    +------v------+   +------v------+   +------v------+
    |   STN_001   |   |   STN_002   |   |   STN_003   |  L4 - Equipment
    +-------------+   +-------------+   +-------------+
```

### Layer 1: MASTER_SCHEDULER (MSC)

The top-level orchestrator. Distributes wafers, applies global rules, monitors the system.

```sfl
MASTER_SCHEDULER MSC_001 {
    LAYER: L1

    CONFIG {
        scheduling_mode: "HIERARCHICAL_4L"
        optimization_interval: 30s
        wafer_distribution: "CYCLIC_ZIP"
        total_wafers: 25
        active_wsc_count: 3
    }

    // Assign wafers to wafer schedulers
    WAFER_SCHEDULERS {
        WSC_001: { priority: 1, wafers: [1,4,7,10,13,16,19,22,25] }  // 9 wafers
        WSC_002: { priority: 1, wafers: [2,5,8,11,14,17,20,23] }     // 8 wafers
        WSC_003: { priority: 1, wafers: [3,6,9,12,15,18,21,24] }     // 8 wafers
    }

    // Define a production schedule
    SCHEDULE PRODUCTION_RUN_001 {
        wafer_count: 25
        scheduler_count: 3

        APPLY_RULE("WAR_001")  // Wafer Assignment Rule (Cyclic Zip)
        APPLY_RULE("PSR_001")  // Pipeline Slot Rule
        APPLY_RULE("SSR_001")  // Steady State Rule

        VERIFY {
            constraint: "all_wafers_assigned"
            constraint: "no_conflicts"
            constraint: "pipeline_depth <= 3"
        }
    }

    // Pub/Sub
    subscribe to "wsc/+/status" as wsc_updates @2;
    publish commands to "msc/001/command" @2, persistent;
}
```

### Layer 2: WAFER_SCHEDULER (WSC)

Manages the lifecycle of its assigned wafers through the processing pipeline.

```sfl
WAFER_SCHEDULER WSC_001 {
    LAYER: L2
    MASTER: "MSC_001"

    CONFIG {
        assigned_wafers: FORMULA(CYCLIC_ZIP, 0, 3, 25)
        // offset=0, stride=3, total=25
        // Result: [W1, W4, W7, W10, W13, W16, W19, W22, W25]
        max_concurrent: 3
        buffer_size: 5
    }

    ASSIGNED_WAFERS {
        pattern: "CYCLIC_ZIP"
        offset: 0
        stride: 3
        wafer_list: [W001, W004, W007, W010, W013, W016, W019, W022, W025]
    }

    subscribe to "msc/+/command" as msc_commands @2;
    publish status to "wsc/001/status" @1;
}
```

### Layer 3: ROBOT_SCHEDULER (RSC)

Controls robot movements and wafer transfers between stations.

```sfl
ROBOT_SCHEDULER RSC_EFEM_001 {
    LAYER: L3

    CONFIG {
        robot_type: "EFEM"
        max_velocity: 2.0
        position_update_rate: 10Hz
        arm_count: 2
    }

    CONTROLLED_ROBOTS: ["WTR_001"]

    // Real-time position (QoS 0 = fire-and-forget, for speed)
    publish position to "rsc/efem/position" @0, volatile;

    // Wafer movement command
    transaction MOVE_WAFER {
        parent: TXN_MSC_001
        command: move(W001, STN_CMP01, STN_CLN01)
        timeout: 30s
        retry: exponential_backoff(3)
    }
}
```

### Layer 4: STATION (STN)

Physical equipment that processes wafers. Each station can have an embedded state machine.

```sfl
STATION STN_CMP01 {
    TYPE: "CMP_PLATEN"
    LAYER: L4

    CONFIG {
        process_time: 180s
        capacity: 1
    }

    CAPABILITIES: ["POLISH", "CONDITION"]

    STATE_MACHINE {
        initial: "IDLE"
        states: {
            IDLE:       { on: { RECEIVE_WAFER: "PROCESSING", MAINTENANCE: "MAINTENANCE" } }
            PROCESSING: { on: { COMPLETE: "IDLE", ERROR: "ALARM" } }
            MAINTENANCE:{ on: { COMPLETE: "IDLE" } }
            ALARM:      { on: { RESET: "IDLE" } }
        }
    }

    publish state to "station/cmp01/state" @2, persistent;
}
```

**Common Station Types:**

| Type                   | Description              | Typical Process Time |
|------------------------|--------------------------|----------------------|
| `CMP_PLATEN`           | CMP polishing platen     | 180s                 |
| `CLEANER`              | Post-CMP cleaning        | 60s                  |
| `BUFFER`               | Temporary wafer storage  | Variable             |
| `LOADPORT`             | FOUP load/unload         | 30s                  |
| `WAFER_TRANSFER_ROBOT`  | Transfer robot           | Variable             |

---

## Scheduling Rules

SFL has 3 categories of scheduling rules, each with a unique ID prefix:

| Category                 | Prefix  | Purpose                               |
|--------------------------|---------|---------------------------------------|
| Wafer Assignment Rule    | `WAR_`  | Distribute wafers to schedulers       |
| Pipeline Slot Rule       | `PSR_`  | Manage pipeline slot timing           |
| Steady State Rule        | `SSR_`  | Detect and maintain steady state      |

### Defining Rules

```sfl
PIPELINE_SCHEDULING_RULES {
    "WAR_001": {
        name: "Cyclic_Zip_Distribution"
        type: ALLOCATION
        priority: 1
        formula: FORMULA(CYCLIC_ZIP)
        constraints: ["all_wafers_assigned", "balanced_load"]
    }

    "PSR_001": {
        name: "Pipeline_Slot_Assignment"
        type: ALLOCATION
        priority: 2
        formula: FORMULA(slot = wafer_index % pipeline_depth)
        constraints: ["slot < pipeline_depth"]
    }

    "SSR_001": {
        name: "Three_Phase_Steady_State"
        type: VERIFICATION
        priority: 3
        phases: ["RAMP_UP", "STEADY", "RAMP_DOWN"]
        detection: { method: "pipeline_full", threshold: 3 }
    }
}
```

### Applying Rules

```sfl
SCHEDULE PRODUCTION_RUN {
    APPLY_RULE("WAR_001")  // First: assign wafers
    APPLY_RULE("PSR_001")  // Second: assign pipeline slots
    APPLY_RULE("SSR_001")  // Third: verify steady state

    VERIFY {
        constraint: "all_wafers_assigned"
        constraint: "no_conflicts"
    }
}
```

### The Cyclic Zip Algorithm (WAR_001)

Distributes 25 wafers evenly across 3 schedulers using round-robin:

```
Formula: scheduler_index = wafer_index % scheduler_count

WSC_001 (offset=0): W01, W04, W07, W10, W13, W16, W19, W22, W25  (9 wafers)
WSC_002 (offset=1): W02, W05, W08, W11, W14, W17, W20, W23       (8 wafers)
WSC_003 (offset=2): W03, W06, W09, W12, W15, W18, W21, W24       (8 wafers)
```

### FORMULA Expressions

```sfl
// Named algorithm
wafers: FORMULA(CYCLIC_ZIP, offset, stride, total)

// Arithmetic
slot: FORMULA(wafer_index % 3)

// Complex
timing: FORMULA(pipeline_depth * ceil(wafer_count / pipeline_depth))
```

### Three-Phase Pipeline (SSR_001)

Each production run goes through 3 phases:

```
Phase 1: RAMP_UP    - Pipeline is filling, not all slots occupied
Phase 2: STEADY     - Pipeline is full, maximum throughput
Phase 3: RAMP_DOWN  - Pipeline is draining, last wafers finishing

Timeline (3-slot pipeline, 25 wafers):

Time | SLOT_1 (WSC_001) | SLOT_2 (WSC_002) | SLOT_3 (WSC_003) | Phase
-----|-------------------|-------------------|-------------------|----------
T1   | W001 (LP->POL)   | Empty             | Empty             | RAMP_UP
T3   | W001 (POL->CLN)  | W002 (LP->POL)    | Empty             | RAMP_UP
T5   | W001 (CLN->BUF)  | W002 (POL->CLN)   | W003 (LP->POL)   | STEADY
T8   | W004 (LP->POL)   | W002 (CLN->BUF)   | W003 (POL->CLN)  | STEADY
...  | ...               | ...               | ...               | STEADY
T77  | W025 (CLN->BUF)  | Empty             | Empty             | RAMP_DOWN
```

---

## Transaction Management

Transactions represent **commands flowing downward** through the hierarchy. Every wafer movement is a tracked transaction.

### Transaction ID Format

```
TXN_{timestamp}_{sequence}_{checksum}

Example: TXN_20250101120000_00001_A3F2

- timestamp: YYYYMMDDHHmmss (14 digits)
- sequence:  5-digit sequence number
- checksum:  4-character hex checksum
```

### Defining Transactions

```sfl
transaction MOVE_WAFER {
    parent: TXN_MSC_001                          // Parent transaction (traceability)
    command: move(W001, STN_CMP01, STN_CLN01)    // What to do
    timeout: 30s                                  // Max duration
    retry: exponential_backoff(3)                 // Retry policy
}
```

### Transaction Lifecycle

```
Status: CREATED -> QUEUED -> EXECUTING -> COMPLETED
                                       -> FAILED
```

### Transaction Flow Through Layers

```
T0: CREATE     at MSC_001  (L1 creates the command)
T1: FORWARD    to WSC_001  (L1 sends to L2)
T2: ENRICH     at WSC_001  (L2 adds wafer context)
T3: FORWARD    to RSC_001  (L2 sends to L3)
T4: DECOMPOSE  at RSC_001  (L3 breaks into robot moves)
T5: EXECUTE    at WTR_001  (L4 physically moves wafer)
T6: COMPLETE   report_to MSC_001  (Status flows back up)
```

### Retry Policies

```sfl
retry: fixed(3, 5s)              // 3 retries, 5 second intervals
retry: exponential_backoff(3)    // 3 retries: 1s, 2s, 4s
retry: linear_backoff(5, 2s)     // 5 retries: 2s, 4s, 6s, 8s, 10s
retry: none                      // No retry
```

---

## Pub/Sub Communication

Status flows **upward** through the hierarchy using MQTT-style publish/subscribe messaging.

### Publishing

```sfl
// Basic
publish status to "wsc/001/status";

// With QoS level
publish status to "wsc/001/status" @1;

// With QoS and persistence
publish state to "station/cmp01/state" @2, persistent;

// Volatile (non-persistent, for real-time data)
publish position to "rsc/efem/position" @0, volatile;
```

### Subscribing

```sfl
// Basic
subscribe to "msc/+/command" as msc_commands;

// With QoS
subscribe to "msc/+/command" as msc_commands @2;

// With filter
subscribe to "station/+/state" as station_states
    where state == "ALARM" @2;
```

### QoS Levels

| Level | Name           | Meaning                            | Use Case              |
|-------|----------------|------------------------------------|-----------------------|
| `@0`  | At most once   | Fire and forget, may be lost       | Position updates      |
| `@1`  | At least once  | Guaranteed delivery, may duplicate | Status updates        |
| `@2`  | Exactly once   | Guaranteed single delivery         | Commands, state changes|

### Topic Patterns

```sfl
"msc/+/command"         // Commands from any MSC
"wsc/+/status"          // Status from any WSC
"rsc/+/position"        // Position from any RSC
"station/+/state"       // State from any station
"wafer/+/location"      // Wafer tracking
"transaction/+/update"  // Transaction updates

// Wildcards
"+"  // Single level  - matches one topic level
"#"  // Multi level   - matches all remaining levels

// Examples
"station/+/state"    // Matches station/cmp01/state, station/cln01/state
"wafer/#"            // Matches wafer/001, wafer/001/location, etc.
```

### Message Broker Definition

```sfl
MESSAGE_BROKER STATUS_BROKER_01 {
    TYPE: "PUB_SUB"

    TOPICS {
        WTR_STATUS {
            topic: "wtr/+/status"
            publishers: ["WTR_001", "WTR_002"]
            subscribers: [
                { id: "MSC_001", qos: 2 },
                { id: "RSC_001", qos: 1 }
            ]
        }

        STATION_STATE {
            topic: "station/#"
            subscribers: [
                { id: "MSC_001", qos: 2, persistent: true }
            ]
        }
    }
}
```

---

## Embedded State Machines

Stations can have embedded state machines that define their behavior:

```sfl
STATE_MACHINE {
    initial: "IDLE"

    states: {
        IDLE: {
            entry: log("Station entering IDLE")
            on: {
                RECEIVE_WAFER: "LOADING"
                MAINTENANCE_REQUEST: "MAINTENANCE"
            }
        }

        LOADING: {
            on: {
                LOAD_COMPLETE: "PROCESSING"
                LOAD_FAIL: "ALARM"
            }
        }

        PROCESSING: {
            entry: start_timer(process_time)
            on: {
                PROCESS_COMPLETE: "UNLOADING"
                PROCESS_ABORT: "ALARM"
            }
        }

        UNLOADING: {
            on: {
                UNLOAD_COMPLETE: "IDLE"
                UNLOAD_FAIL: "ALARM"
            }
        }

        MAINTENANCE: {
            on: { MAINTENANCE_COMPLETE: "IDLE" }
        }

        ALARM: {
            entry: notify_operator()
            on: { ALARM_CLEAR: "IDLE" }
        }
    }
}
```

---

## Type System

### Core Types

| Type              | Format                                  | Example                              |
|-------------------|-----------------------------------------|--------------------------------------|
| `wafer_id_t`      | `W` + 3 digits                          | `W001`, `W025`                       |
| `lot_id_t`        | `LOT_` + date + sequence                | `LOT_20250101_001`                   |
| `recipe_id_t`     | `RCP_` + process + sequence             | `RCP_CMP_001`                        |
| `station_id_t`    | Free-form string                        | `STN_CMP01`, `STN_CLN02`            |
| `scheduler_id_t`  | Type prefix + sequence                  | `MSC_001`, `WSC_001`, `RSC_001`     |
| `txn_id_t`        | `TXN_` + timestamp + seq + checksum     | `TXN_20250101120000_00001_A3F2`     |
| `duration_t`      | Number + unit                           | `30s`, `5m`, `1h`, `100ms`          |
| `frequency_t`     | Number + `Hz`                           | `10Hz`                               |
| `layer_t`         | `L1` through `L4`                       | `L1`, `L2`, `L3`, `L4`             |
| `qos_t`           | `0`, `1`, or `2`                        | `@0`, `@1`, `@2`                    |
| `status_t`        | Enum                                    | `CREATED`, `QUEUED`, `EXECUTING`, `COMPLETED`, `FAILED` |

### Rule Types

| Type  | Purpose                  | Example  |
|-------|--------------------------|----------|
| `PSR` | Pipeline Slot Rules      | `PSR_001`|
| `WAR` | Wafer Assignment Rules   | `WAR_001`|
| `SSR` | Steady State Rules       | `SSR_001`|
| `WTR` | Wafer Transfer Rules     | `WTR_001`|

---

## Imports and Standard Library

### Available Modules

```sfl
// Scheduling algorithms
import semiflow.algorithms.cyclic_zip
import semiflow.algorithms.round_robin
import semiflow.algorithms.load_balanced

// SEMI standard compliance
import semiflow.semi.e87    // Carrier Management
import semiflow.semi.e88    // AMHS
import semiflow.semi.e90    // Substrate Tracking
import semiflow.semi.e94    // Control Job Management

// Communication
import semiflow.comm.mqtt          // MQTT messaging
import semiflow.comm.transaction   // Transaction management
```

### Import Syntax

```sfl
// Simple import
import semiflow.algorithms.cyclic_zip

// Aliased import
import semiflow.algorithms.cyclic_zip as cz

// Selective import
from semiflow.algorithms import cyclic_zip, round_robin

// Wildcard import
from semiflow.algorithms import *
```

---

## Compiler Directives

```sfl
#pragma sfl version 2.0           // Language version
#pragma sfl strict                // Enable strict type checking
#pragma sfl optimize pipeline     // Enable pipeline optimization
#pragma sfl fab samsung           // FAB-specific optimizations
#pragma sfl semi e90              // SEMI E90 compliance mode
```

---

## Wafer Processing Flow

Each wafer follows this physical path through the system:

```
LP -> WTR1 -> POL -> WTR2 -> CLN -> WTR3 -> BUF -> WTR1 -> LP

Where:
  LP   = Load Port (FOUP)
  WTR  = Wafer Transfer Robot
  POL  = Polishing Station (CMP)
  CLN  = Cleaning Station
  BUF  = Buffer Station
```

---

## Complete Example: CMP Production Line

```sfl
// File: cmp_production_line.sfl
// A complete CMP line processing 25 wafers

import semiflow.algorithms.cyclic_zip
import semiflow.semi.e90

// System declaration
SYSTEM_ARCHITECTURE {
    NAME: "FAB_CMP_LINE_01"
    VERSION: "2.0.0"

    LAYERS: {
        L1: "MASTER_SCHEDULER"
        L2: "WAFER_SCHEDULER"
        L3: "ROBOT_SCHEDULER"
        L4: "STATION"
    }

    COMMUNICATION: {
        COMMAND: { type: "TRANSACTION_BASED", flow: "MSC->WSC->RSC->Station" }
        STATUS:  { type: "PUBLISH_SUBSCRIBE", broker: "STATUS_BROKER_01" }
    }
}

// L1 - Master Scheduler
MASTER_SCHEDULER MSC_001 {
    LAYER: L1

    CONFIG {
        wafer_distribution: "CYCLIC_ZIP"
        total_wafers: 25
        active_wsc_count: 3
    }

    WAFER_SCHEDULERS {
        WSC_001: { priority: 1, wafers: [1,4,7,10,13,16,19,22,25] }
        WSC_002: { priority: 1, wafers: [2,5,8,11,14,17,20,23] }
        WSC_003: { priority: 1, wafers: [3,6,9,12,15,18,21,24] }
    }

    SCHEDULE PRODUCTION_RUN_001 {
        APPLY_RULE("WAR_001")
        APPLY_RULE("PSR_001")
        APPLY_RULE("SSR_001")

        VERIFY {
            constraint: "all_wafers_assigned"
            constraint: "no_conflicts"
            constraint: "pipeline_depth <= 3"
        }
    }
}

// L2 - Wafer Schedulers
WAFER_SCHEDULER WSC_001 {
    LAYER: L2
    MASTER: "MSC_001"
    CONFIG { assigned_wafers: FORMULA(CYCLIC_ZIP, 0, 3, 25) }
    subscribe to "msc/+/command" as msc_commands @2;
    publish status to "wsc/001/status" @1;
}

WAFER_SCHEDULER WSC_002 {
    LAYER: L2
    MASTER: "MSC_001"
    CONFIG { assigned_wafers: FORMULA(CYCLIC_ZIP, 1, 3, 25) }
    subscribe to "msc/+/command" as msc_commands @2;
    publish status to "wsc/002/status" @1;
}

WAFER_SCHEDULER WSC_003 {
    LAYER: L2
    MASTER: "MSC_001"
    CONFIG { assigned_wafers: FORMULA(CYCLIC_ZIP, 2, 3, 25) }
    subscribe to "msc/+/command" as msc_commands @2;
    publish status to "wsc/003/status" @1;
}

// L3 - Robot Schedulers
ROBOT_SCHEDULER RSC_EFEM_001 {
    LAYER: L3
    CONFIG { robot_type: "EFEM", max_velocity: 2.0, arm_count: 2 }
    CONTROLLED_ROBOTS: ["WTR_001"]
    publish position to "rsc/efem/position" @0, volatile;
}

ROBOT_SCHEDULER RSC_PROCESS_001 {
    LAYER: L3
    CONFIG { robot_type: "PROCESS_ROBOT", max_velocity: 1.5 }
    CONTROLLED_ROBOTS: ["WTR_002", "WTR_003"]
    publish position to "rsc/process/position" @0, volatile;
}

// L4 - Stations
STATION STN_LP01   { TYPE: "LOADPORT",  LAYER: L4, CONFIG { capacity: 25 } }
STATION STN_CMP01  { TYPE: "CMP_PLATEN", LAYER: L4, CONFIG { process_time: 180s, capacity: 1 } }
STATION STN_CMP02  { TYPE: "CMP_PLATEN", LAYER: L4, CONFIG { process_time: 180s, capacity: 1 } }
STATION STN_CLN01  { TYPE: "CLEANER",   LAYER: L4, CONFIG { process_time: 60s, capacity: 2 } }
STATION STN_BUF01  { TYPE: "BUFFER",    LAYER: L4, CONFIG { capacity: 5 } }

// L4 - Wafer Transfer Robots
STATION WTR_001 {
    TYPE: "WAFER_TRANSFER_ROBOT"
    LAYER: L4
    STATE_MACHINE {
        initial: "IDLE"
        states: {
            IDLE:      { on: { RECEIVE_TASK: "EXECUTING" } }
            EXECUTING: { on: { COMPLETE: "IDLE", ERROR: "ALARM" } }
            ALARM:     { on: { RESET: "IDLE" } }
        }
    }
}

// Scheduling Rules
PIPELINE_SCHEDULING_RULES {
    "WAR_001": {
        name: "Cyclic_Zip_Distribution"
        type: ALLOCATION
        priority: 1
        formula: FORMULA(CYCLIC_ZIP)
    }
    "PSR_001": {
        name: "Pipeline_Slot_Assignment"
        type: ALLOCATION
        priority: 2
        formula: FORMULA(slot = wafer_index % pipeline_depth)
    }
    "SSR_001": {
        name: "Three_Phase_Steady_State"
        type: VERIFICATION
        priority: 3
        phases: ["RAMP_UP", "STEADY", "RAMP_DOWN"]
    }
}
```

---

## Minimal Example

```sfl
// File: minimal.sfl
// Simplest possible SFL program

MASTER_SCHEDULER MSC_MINI {
    LAYER: L1
    CONFIG { total_wafers: 3 }

    SCHEDULE MINI_RUN {
        APPLY_RULE("WAR_001")
    }
}

WAFER_SCHEDULER WSC_MINI {
    LAYER: L2
    MASTER: "MSC_MINI"
    ASSIGNED_WAFERS { wafer_list: [W001, W002, W003] }
}

STATION STN_CMP_MINI {
    TYPE: "CMP_PLATEN"
    LAYER: L4
    CONFIG { process_time: 60s }
}
```

---

## Best Practices

### Layer Hierarchy Rules

```
DO:   L1 -> L2 -> L3 -> L4  (commands flow down one level at a time)
DO:   L4 -> L3 -> L2 -> L1  (status flows up via Pub/Sub)

DON'T: L1 directly controlling L4 (skipping layers)
```

### QoS Selection Guide

| Data Type          | Recommended QoS | Why                           |
|--------------------|------------------|-------------------------------|
| Position updates   | `@0`             | High frequency, loss-tolerant |
| Status updates     | `@1`             | Important but duplicates OK   |
| Commands           | `@2`             | Must arrive exactly once      |
| State changes      | `@2, persistent` | Critical, must not be lost    |

### Transaction Design

- Always include `timeout` and `retry`
- Use `parent` for transaction traceability
- Use exponential backoff for retries

### Naming Conventions

| Entity    | Pattern                | Examples                     |
|-----------|------------------------|------------------------------|
| MSC       | `MSC_` + number        | `MSC_001`                    |
| WSC       | `WSC_` + zone/number   | `WSC_001`, `WSC_ZONE_A`     |
| RSC       | `RSC_` + type + number | `RSC_EFEM_001`               |
| Station   | `STN_` + type + number | `STN_CMP01`, `STN_CLN02`    |
| Wafer     | `W` + 3 digits         | `W001` through `W025`        |
| Robot     | `WTR_` + number        | `WTR_001`                    |

---

## Error Codes

| Code    | Error                        | Description                                         |
|---------|------------------------------|-----------------------------------------------------|
| SFL001  | Invalid scheduler type       | Must be MASTER_SCHEDULER, WAFER_SCHEDULER, ROBOT_SCHEDULER, or STATION |
| SFL002  | Layer hierarchy violation    | L1 cannot directly reference L3/L4                  |
| SFL003  | Unknown rule identifier      | Referenced rule (e.g., "PSR_999") doesn't exist     |
| SFL004  | Wafer assignment conflict    | Same wafer assigned to multiple WSCs                |
| SFL005  | Invalid QoS level            | QoS must be 0, 1, or 2                             |
| SFL006  | Transaction timeout exceeded | Transaction took longer than allowed                |
| SFL007  | Formula syntax error         | Invalid FORMULA expression                          |
| SFL008  | Pipeline depth exceeded      | Max 3 for standard FABs                             |

---

## SEMI Standards Compliance

SFL aligns with international semiconductor equipment standards:

| Standard | Name                     | SFL Coverage                              |
|----------|--------------------------|-------------------------------------------|
| E87      | Carrier Management       | FOUP handling, load port stations         |
| E88      | AMHS                     | Robot schedulers, wafer transfer          |
| E90      | Substrate Tracking       | Wafer IDs, location tracking via Pub/Sub  |
| E94      | Control Job Management   | Schedules, transactions, production runs  |

---

## Glossary

| Term             | Definition                                                              |
|------------------|-------------------------------------------------------------------------|
| **AMHS**         | Automated Material Handling System                                      |
| **CMP**          | Chemical Mechanical Planarization (polishing process)                   |
| **Cyclic Zip**   | Wafer distribution algorithm - round-robin assignment across schedulers |
| **EFEM**         | Equipment Front End Module (robot interface to tools)                   |
| **FOUP**         | Front Opening Unified Pod (25-wafer carrier)                            |
| **MSC**          | Master Scheduler                                                        |
| **Pipeline Depth** | Number of concurrent wafers in processing                             |
| **QoS**          | Quality of Service (MQTT message delivery guarantee)                    |
| **RSC**          | Robot Scheduler                                                         |
| **SEMI**         | Semiconductor Equipment and Materials International                     |
| **Steady State** | Pipeline is full, operating at maximum throughput                       |
| **WSC**          | Wafer Scheduler                                                         |
| **WTR**          | Wafer Transfer Robot                                                    |

---

## Version History

| Version | Date       | Changes                |
|---------|------------|------------------------|
| Draft   | 2025.11.23 | Initial SFL draft      |

---

**Semi Flow Language Specification**
**Draft: 2025.11.23**
**Compliant with SEMI E87, E88, E90, E94 standards**
