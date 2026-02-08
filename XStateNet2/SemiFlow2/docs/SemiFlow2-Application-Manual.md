# SemiFlow2 Application Manual

## Complete Guide to the Semi Flow Language (SFL)

**Version:** 2.0
**Last Updated:** February 2026

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [Getting Started](#2-getting-started)
3. [Language Fundamentals](#3-language-fundamentals)
4. [Scheduler Hierarchy](#4-scheduler-hierarchy)
5. [Configuration Blocks](#5-configuration-blocks)
6. [Scheduling Rules](#6-scheduling-rules)
7. [Transaction Management](#7-transaction-management)
8. [Pub/Sub Communication](#8-pubsub-communication)
9. [Stations and Equipment](#9-stations-and-equipment)
10. [Complete System Examples](#10-complete-system-examples)
11. [Pipeline Scheduling Patterns](#11-pipeline-scheduling-patterns)
12. [Best Practices](#12-best-practices)
13. [Error Codes Reference](#13-error-codes-reference)
14. [Appendix](#14-appendix)

---

## 1. Introduction

### 1.1 What is SemiFlow2?

SemiFlow2 (Semi Flow Language - SFL) is a **domain-specific language** designed for defining hierarchical scheduling systems in semiconductor manufacturing environments. It provides a declarative way to specify:

- **Hierarchical scheduler structures** (Master → Wafer → Robot → Station)
- **Transaction-based command flows**
- **Pub/Sub messaging patterns**
- **Pipeline scheduling rules**
- **Equipment configurations**

### 1.2 Key Features

| Feature | Description |
|---------|-------------|
| **4-Layer Hierarchy** | Natural expression of MSC→WSC→RSC→Station |
| **Transaction-Based** | All operations tracked via unique transaction IDs |
| **Event-Driven** | Asynchronous messaging with MQTT-style Pub/Sub |
| **Type-Safe** | Strong typing for wafer IDs, stations, commands |
| **SEMI-Compliant** | Aligns with E87, E88, E90, E94 standards |

### 1.3 File Extensions

| Extension | Purpose | Example |
|-----------|---------|---------|
| `.sfl` | Primary source files | `cmp_line.sfl` |
| `.sfli` | Include/header files | `common_types.sfli` |
| `.sflc` | Configuration files | `fab_settings.sflc` |

### 1.4 Use Cases

- CMP (Chemical Mechanical Planarization) line scheduling
- AMHS (Automated Material Handling System) integration
- Wafer tracking and routing
- Robot coordination
- Multi-station pipeline optimization

---

## 2. Getting Started

### 2.1 Your First SFL File

Create a file named `hello_fab.sfl`:

```sfl
// hello_fab.sfl - Your first SemiFlow2 program

// Define a simple master scheduler
MASTER_SCHEDULER MSC_DEMO {
    LAYER: L1

    CONFIG {
        name: "Demo Master Scheduler"
        wafer_count: 5
    }

    SCHEDULE DEMO_RUN {
        APPLY_RULE("WAR_001")

        VERIFY {
            constraint: "all_wafers_assigned"
        }
    }
}
```

### 2.2 Basic Structure

Every SFL program follows this structure:

```sfl
// 1. Imports (optional)
import semiflow.algorithms.cyclic_zip

// 2. System declarations (optional)
SYSTEM_ARCHITECTURE {
    NAME: "My_FAB_Line"
}

// 3. Scheduler definitions (required)
MASTER_SCHEDULER MSC_001 { ... }
WAFER_SCHEDULER WSC_001 { ... }
ROBOT_SCHEDULER RSC_001 { ... }
STATION STN_001 { ... }

// 4. Additional configurations
TRANSACTION_FLOW { ... }
MESSAGE_BROKER { ... }
```

### 2.3 Comments

```sfl
// Single-line comment

/*
   Multi-line
   comment
*/

MASTER_SCHEDULER MSC_001 {
    // Inline comment explaining configuration
    LAYER: L1  // Layer 1 is the top orchestration level
}
```

---

## 3. Language Fundamentals

### 3.1 Identifiers

```sfl
// Scheduler identifiers (uppercase with underscores)
MSC_001      // Master Scheduler
WSC_ZONE_A   // Wafer Scheduler
RSC_EFEM_01  // Robot Scheduler

// Wafer identifiers
W001, W002, ..., W025

// Station identifiers
STN_CMP01    // CMP station
STN_CLN02    // Cleaning station

// Transaction identifiers
TXN_20240101120000_00001_A3F2
```

### 3.2 Literals

```sfl
// Integers
wafer_count: 25
priority: 1

// Floats
max_velocity: 2.0

// Strings
name: "CMP_Polisher"
pattern: "CYCLIC_ZIP"

// Durations
timeout: 30s
interval: 5m
ttl: 1h
process_time: 180s

// Frequencies
update_rate: 10Hz
```

### 3.3 Layers

SFL uses a 4-layer hierarchy:

```sfl
L1  // MASTER_SCHEDULER   - Top orchestration layer
L2  // WAFER_SCHEDULER    - Wafer-level scheduling
L3  // ROBOT_SCHEDULER    - Robot control layer
L4  // STATION            - Physical equipment layer
```

### 3.4 Keywords

| Category | Keywords |
|----------|----------|
| Schedulers | `MASTER_SCHEDULER`, `WAFER_SCHEDULER`, `ROBOT_SCHEDULER`, `STATION` |
| Structure | `LAYER`, `CONFIG`, `SCHEDULE`, `VERIFY` |
| Rules | `APPLY_RULE`, `FORMULA`, `PIPELINE_SCHEDULING_RULES` |
| Messaging | `publish`, `subscribe`, `transaction` |
| Control | `parallel`, `sequential`, `retry`, `timeout`, `await` |

---

## 4. Scheduler Hierarchy

### 4.1 Overview

```
                    ┌─────────────────┐
                    │ MASTER_SCHEDULER│  L1
                    │     (MSC)       │
                    └────────┬────────┘
                             │
           ┌─────────────────┼─────────────────┐
           │                 │                 │
    ┌──────▼──────┐   ┌──────▼──────┐   ┌──────▼──────┐
    │    WSC_001  │   │    WSC_002  │   │    WSC_003  │  L2
    │ (Wafer Sch) │   │ (Wafer Sch) │   │ (Wafer Sch) │
    └──────┬──────┘   └──────┬──────┘   └──────┬──────┘
           │                 │                 │
    ┌──────▼──────┐   ┌──────▼──────┐   ┌──────▼──────┐
    │   RSC_001   │   │   RSC_002   │   │   RSC_003   │  L3
    │ (Robot Sch) │   │ (Robot Sch) │   │ (Robot Sch) │
    └──────┬──────┘   └──────┬──────┘   └──────┬──────┘
           │                 │                 │
    ┌──────▼──────┐   ┌──────▼──────┐   ┌──────▼──────┐
    │   STN_001   │   │   STN_002   │   │   STN_003   │  L4
    │  (Station)  │   │  (Station)  │   │  (Station)  │
    └─────────────┘   └─────────────┘   └─────────────┘
```

### 4.2 MASTER_SCHEDULER (L1)

The top-level orchestrator that coordinates all wafer schedulers.

```sfl
MASTER_SCHEDULER MSC_001 {
    VERSION: "1.0.0"
    NAME: "CMP_Master"
    LAYER: L1

    CONFIG {
        // Scheduling configuration
        scheduling_mode: "HIERARCHICAL_4L"
        optimization_interval: 30s

        // Wafer distribution
        wafer_distribution: "CYCLIC_ZIP"
        total_wafers: 25
        active_wsc_count: 3
    }

    // Define which wafer schedulers this MSC controls
    WAFER_SCHEDULERS {
        WSC_001: { priority: 1, wafers: [1,4,7,10,13,16,19,22,25] }
        WSC_002: { priority: 1, wafers: [2,5,8,11,14,17,20,23] }
        WSC_003: { priority: 1, wafers: [3,6,9,12,15,18,21,24] }
    }

    // Production schedule
    SCHEDULE PRODUCTION_RUN_001 {
        wafer_count: 25
        scheduler_count: 3

        APPLY_RULE("WAR_001")  // Wafer Assignment Rule
        APPLY_RULE("PSR_001")  // Pipeline Slot Rule
        APPLY_RULE("SSR_001")  // Steady State Rule

        VERIFY {
            constraint: "all_wafers_assigned"
            constraint: "no_conflicts"
            constraint: "pipeline_depth <= 3"
        }
    }
}
```

**Key Responsibilities:**
- Distribute wafers across wafer schedulers
- Apply global scheduling rules
- Monitor overall system state
- Handle exceptions and re-scheduling

### 4.3 WAFER_SCHEDULER (L2)

Manages the lifecycle of assigned wafers through the processing pipeline.

```sfl
WAFER_SCHEDULER WSC_001 {
    ID: "wsc_001"
    LAYER: L2
    MASTER: "MSC_001"  // Reports to MSC_001

    CONFIG {
        // Wafer assignment using cyclic zip formula
        assigned_wafers: FORMULA(CYCLIC_ZIP, 0, 3, 25)
        // Parameters: offset=0, stride=3, total=25
        // Results in: [W1, W4, W7, W10, W13, W16, W19, W22, W25]

        max_concurrent: 3
        buffer_size: 5
    }

    // Direct wafer assignment (alternative to FORMULA)
    ASSIGNED_WAFERS {
        pattern: "CYCLIC_ZIP"
        offset: 0
        stride: 3
        wafer_list: [W001, W004, W007, W010, W013, W016, W019, W022, W025]
    }

    // Subscribe to master scheduler commands
    subscribe to "msc/+/command" as msc_commands @2;

    // Publish status updates
    publish status to "wsc/001/status" @1;
}
```

**Key Responsibilities:**
- Track wafer positions and states
- Coordinate with robot schedulers for transfers
- Monitor processing progress
- Report completion status

### 4.4 ROBOT_SCHEDULER (L3)

Controls robot movements and wafer transfers.

```sfl
ROBOT_SCHEDULER RSC_EFEM_001 {
    ID: "rsc_efem_001"
    LAYER: L3

    CONFIG {
        robot_type: "EFEM"         // Equipment Front End Module
        max_velocity: 2.0          // m/s
        acceleration: 1.5          // m/s²
        position_update_rate: 10Hz
        arm_count: 2               // Dual arm robot
    }

    // Controlled robots/transfer units
    CONTROLLED_ROBOTS: ["WTR_001", "WTR_002"]

    // Real-time position updates (QoS 0 for speed)
    publish position to "rsc/efem/position" @0, volatile;

    // Transaction for wafer movement
    transaction MOVE_WAFER {
        parent: TXN_MSC_001
        command: move(W001, STN_CMP01, STN_CLN01)
        timeout: 30s
        retry: exponential_backoff(3)
    }
}
```

**Key Responsibilities:**
- Execute wafer transfer commands
- Coordinate arm movements
- Avoid collisions
- Report position and status

### 4.5 STATION (L4)

Physical equipment that processes wafers.

```sfl
STATION STN_CMP01 {
    ID: "CMP_001"
    TYPE: "CMP_PLATEN"
    LAYER: L4

    CONFIG {
        type: "CMP_POLISHER"
        process_time: 180s
        capacity: 1
        capabilities: ["POLISH", "CONDITION"]
    }

    // State machine for station
    STATE_MACHINE {
        initial: "IDLE"
        states: {
            IDLE: {
                on: {
                    RECEIVE_WAFER: "PROCESSING",
                    MAINTENANCE: "MAINTENANCE"
                }
            }
            PROCESSING: {
                on: {
                    COMPLETE: "IDLE",
                    ERROR: "ALARM"
                }
            }
            MAINTENANCE: {
                on: {
                    COMPLETE: "IDLE"
                }
            }
            ALARM: {
                on: {
                    RESET: "IDLE"
                }
            }
        }
    }

    // Publish state changes with persistence
    publish state to "station/cmp01/state" @2, persistent;
}
```

**Station Types:**

| Type | Description | Typical Process Time |
|------|-------------|---------------------|
| `CMP_PLATEN` | CMP polishing platen | 180s |
| `CMP_CLEANER` | Post-CMP cleaning | 60s |
| `BUFFER` | Temporary wafer storage | Variable |
| `LOADPORT` | FOUP load/unload | 30s |
| `WAFER_TRANSFER_ROBOT` | Transfer robot | Variable |

---

## 5. Configuration Blocks

### 5.1 CONFIG Block Syntax

```sfl
CONFIG {
    property_name: value
    another_property: "string value"
    numeric_value: 123
    duration_value: 30s
    frequency_value: 10Hz
}
```

### 5.2 Common Configuration Properties

**Master Scheduler Config:**
```sfl
CONFIG {
    scheduling_mode: "HIERARCHICAL_4L"    // Scheduling algorithm
    optimization_interval: 30s             // Re-optimization frequency
    wafer_distribution: "CYCLIC_ZIP"      // Distribution pattern
    total_wafers: 25                       // Total wafers to process
    active_wsc_count: 3                    // Active wafer schedulers
    load_balance: true                     // Enable load balancing
}
```

**Wafer Scheduler Config:**
```sfl
CONFIG {
    assigned_wafers: FORMULA(CYCLIC_ZIP, 0, 3, 25)
    max_concurrent: 3                      // Max concurrent wafers
    buffer_size: 5                         // Buffer capacity
    priority: 1                            // Scheduling priority
}
```

**Robot Scheduler Config:**
```sfl
CONFIG {
    robot_type: "EFEM"
    max_velocity: 2.0
    acceleration: 1.5
    position_update_rate: 10Hz
    arm_count: 2
    collision_check: true
}
```

**Station Config:**
```sfl
CONFIG {
    type: "CMP_POLISHER"
    process_time: 180s
    capacity: 1
    capabilities: ["POLISH", "CONDITION", "CLEAN"]
    maintenance_interval: 24h
}
```

---

## 6. Scheduling Rules

### 6.1 Rule Types

| Rule Type | ID Prefix | Purpose |
|-----------|-----------|---------|
| Wafer Assignment Rule | `WAR_xxx` | Distribute wafers to schedulers |
| Pipeline Slot Rule | `PSR_xxx` | Manage pipeline slots |
| Steady State Rule | `SSR_xxx` | Detect and maintain steady state |
| Wafer Transfer Rule | `WTR_xxx` | Control wafer transfers |

### 6.2 Defining Pipeline Rules

```sfl
PIPELINE_SCHEDULING_RULES {
    // Pipeline Slot Assignment
    "PSR_001": {
        name: "Pipeline_Slot_Assignment"
        type: ALLOCATION
        priority: 1
        formula: FORMULA(
            slot_index = wafer_index % pipeline_depth
        )
        constraints: [
            "slot_index < pipeline_depth",
            "no_slot_conflicts"
        ]
    }

    // Processing Time Pattern
    "PSR_002": {
        name: "Processing_Time_Pattern"
        type: SCHEDULING
        priority: 2
        formula: FORMULA(
            next_slot_time = current_time + process_time
        )
        constraints: [
            "slot_time_monotonic"
        ]
    }

    // Wafer Assignment - Cyclic Zip
    "WAR_001": {
        name: "Cyclic_Zip_Distribution"
        type: ALLOCATION
        priority: 1
        formula: FORMULA(CYCLIC_ZIP)
        pattern: {
            algorithm: "round_robin_with_offset"
            load_balance: true
        }
    }

    // Steady State Detection
    "SSR_001": {
        name: "Three_Phase_Steady_State"
        type: VERIFICATION
        priority: 3
        phases: ["RAMP_UP", "STEADY", "RAMP_DOWN"]
        detection: {
            method: "pipeline_full"
            threshold: 3
        }
    }
}
```

### 6.3 Applying Rules

```sfl
SCHEDULE PRODUCTION_RUN {
    // Apply rules in order
    APPLY_RULE("WAR_001")  // First: assign wafers
    APPLY_RULE("PSR_001")  // Second: assign pipeline slots
    APPLY_RULE("PSR_002")  // Third: calculate timing
    APPLY_RULE("SSR_001")  // Fourth: verify steady state
}
```

### 6.4 FORMULA Expressions

```sfl
// Simple formula
slot: FORMULA(wafer_index % 3)

// Formula with named algorithm
wafers: FORMULA(CYCLIC_ZIP, offset, stride, total)

// Complex formula
timing: FORMULA(
    pipeline_depth * ceil(wafer_count / pipeline_depth)
)
```

### 6.5 Verification

```sfl
VERIFY {
    // Boolean constraints
    constraint: "all_wafers_assigned"
    constraint: "no_conflicts"
    constraint: "pipeline_depth <= 3"

    // Numeric expectations
    expected_slots: 27
    actual_wafers: 25
    empty_slots: 2

    // Custom validations
    validate: "no_station_overload"
    validate: "robot_paths_clear"
}
```

---

## 7. Transaction Management

### 7.1 Transaction ID Format

```
TXN_{timestamp}_{sequence}_{checksum}

Example: TXN_20240101120000_00001_A3F2

Where:
- timestamp: YYYYMMDDHHmmss (14 digits)
- sequence:  5-digit sequence number
- checksum:  4-character hex checksum
```

### 7.2 Transaction Schema

```sfl
TRANSACTION_MANAGER TXN_MGR_001 {
    TRANSACTION_SCHEMA {
        transaction_id: {
            format: "TXN_{timestamp}_{sequence}_{checksum}"
            example: "TXN_20240101120000_00001_A3F2"
            unique: true
            ttl: 3600s  // Time to live
        }

        structure: {
            txn_id: STRING
            parent_txn_id: STRING?       // Optional parent
            command_type: ENUM
            chain: ARRAY<LAYER_INFO>
            status: STATUS_OBJECT
        }
    }
}
```

### 7.3 Transaction Definition

```sfl
transaction TXN_MOVE_W001 {
    parent: TXN_MSC_001

    command: move(W001, STN_CMP01, STN_CLN01)

    status: CREATED  // CREATED → QUEUED → EXECUTING → COMPLETED/FAILED

    timeout: 30s

    retry: exponential_backoff(3)  // 3 retries with exponential backoff
}
```

### 7.4 Transaction Flow

```sfl
TRANSACTION_FLOW {
    example: "TXN_20240101120000_00001_A3F2"

    flow: [
        { time: "T0", action: "CREATE",     at: "MSC_001" },
        { time: "T1", action: "FORWARD",    to: "WSC_001" },
        { time: "T2", action: "ENRICH",     at: "WSC_001" },
        { time: "T3", action: "FORWARD",    to: "RSC_001" },
        { time: "T4", action: "DECOMPOSE",  at: "RSC_001" },
        { time: "T5", action: "EXECUTE",    at: "WTR_001" },
        { time: "T6", action: "COMPLETE",   report_to: "MSC_001" }
    ]
}
```

### 7.5 Retry Policies

```sfl
// Fixed interval retry
retry: fixed(3, 5s)  // 3 retries, 5 second interval

// Exponential backoff
retry: exponential_backoff(3)  // 3 retries: 1s, 2s, 4s

// Linear backoff
retry: linear_backoff(5, 2s)  // 5 retries: 2s, 4s, 6s, 8s, 10s

// No retry
retry: none
```

---

## 8. Pub/Sub Communication

### 8.1 Message Broker Definition

```sfl
MESSAGE_BROKER STATUS_BROKER_01 {
    TYPE: "PUB_SUB"

    TOPICS {
        // WTR status topic with wildcard
        WTR_STATUS {
            topic: "wtr/+/status"  // + is single-level wildcard
            publishers: ["WTR_001", "WTR_002"]
            subscribers: [
                { id: "MSC_001", qos: 2 },
                { id: "RSC_001", qos: 1 }
            ]
        }

        // Station state topic
        STATION_STATE {
            topic: "station/#"  // # is multi-level wildcard
            publishers: ["STN_CMP01", "STN_CLN01"]
            subscribers: [
                { id: "MSC_001", qos: 2, persistent: true }
            ]
        }
    }
}
```

### 8.2 Publishing Messages

```sfl
// Basic publish
publish status to "wsc/001/status";

// With QoS level
publish status to "wsc/001/status" @1;

// With QoS and persistence
publish state to "station/cmp01/state" @2, persistent;

// Volatile (non-persistent)
publish position to "rsc/efem/position" @0, volatile;
```

### 8.3 Subscribing to Topics

```sfl
// Basic subscribe
subscribe to "msc/+/command" as msc_commands;

// With QoS
subscribe to "msc/+/command" as msc_commands @2;

// With filter
subscribe to "station/+/state" as station_states
    where state == "ALARM" @2;
```

### 8.4 QoS Levels

| Level | Name | Description |
|-------|------|-------------|
| 0 | At most once | Fire and forget, no guarantee |
| 1 | At least once | Guaranteed delivery, may duplicate |
| 2 | Exactly once | Guaranteed single delivery |

### 8.5 Topic Hierarchy

```sfl
// Standard topic patterns
"msc/+/command"          // Commands from MSC
"wsc/+/status"           // Status from WSC
"rsc/+/position"         // Position updates from RSC
"station/+/state"        // State from stations
"wafer/+/location"       // Wafer tracking
"transaction/+/update"   // Transaction updates

// Wildcards
"+"  // Single level wildcard (matches one topic level)
"#"  // Multi-level wildcard (matches remaining levels)

// Examples
"station/+/state"     // Matches station/cmp01/state, station/cln01/state
"wafer/#"             // Matches wafer/001, wafer/001/location, etc.
```

---

## 9. Stations and Equipment

### 9.1 Station Types

**CMP Polishing Platen:**
```sfl
STATION STN_CMP01 {
    ID: "CMP_001"
    TYPE: "CMP_PLATEN"
    LAYER: L4

    CONFIG {
        process_time: 180s
        capacity: 1
        head_count: 1
        pad_type: "IC1000"
        slurry_type: "COPPER_CMP"
    }

    CAPABILITIES: ["POLISH", "CONDITION"]
}
```

**Cleaning Station:**
```sfl
STATION STN_CLN01 {
    ID: "CLN_001"
    TYPE: "CLEANER"
    LAYER: L4

    CONFIG {
        process_time: 60s
        capacity: 2
        cleaning_method: "BRUSH_SCRUB"
    }

    CAPABILITIES: ["CLEAN", "RINSE", "DRY"]
}
```

**Buffer Station:**
```sfl
STATION STN_BUF01 {
    ID: "BUF_001"
    TYPE: "BUFFER"
    LAYER: L4

    CONFIG {
        capacity: 5
        fifo: true
    }
}
```

**Wafer Transfer Robot:**
```sfl
STATION WTR_001 {
    ID: "WTR_001"
    TYPE: "WAFER_TRANSFER_ROBOT"
    LAYER: L4

    CONFIG {
        arm_count: 2
        max_velocity: 1.5
        reach: 500mm
    }

    STATE_MACHINE {
        initial: "IDLE"
        states: {
            IDLE: {
                on: { RECEIVE_TASK: "EXECUTING" }
            }
            EXECUTING: {
                on: {
                    COMPLETE: "IDLE",
                    ERROR: "ALARM"
                }
            }
            ALARM: {
                on: { RESET: "IDLE" }
            }
        }
    }
}
```

### 9.2 State Machines

Every station can have an embedded state machine:

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
            on: {
                MAINTENANCE_COMPLETE: "IDLE"
            }
        }

        ALARM: {
            entry: notify_operator()
            on: {
                ALARM_CLEAR: "IDLE"
            }
        }
    }
}
```

---

## 10. Complete System Examples

### 10.1 Full CMP Line System

```sfl
// File: cmp_production_line.sfl
// Complete CMP production line with 25 wafers

import semiflow.algorithms.cyclic_zip
import semiflow.semi.e90

//=============================================================================
// SYSTEM ARCHITECTURE
//=============================================================================

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
        COMMAND: {
            type: "TRANSACTION_BASED"
            flow: "MSC→WSC→RSC→Station"
        }
        STATUS: {
            type: "PUBLISH_SUBSCRIBE"
            broker: "STATUS_BROKER_01"
        }
    }
}

//=============================================================================
// MASTER SCHEDULER (L1)
//=============================================================================

MASTER_SCHEDULER MSC_001 {
    VERSION: "1.0.0"
    NAME: "CMP_Master"
    LAYER: L1

    CONFIG {
        scheduling_mode: "HIERARCHICAL_4L"
        optimization_interval: 30s
        wafer_distribution: "CYCLIC_ZIP"
        total_wafers: 25
        active_wsc_count: 3
    }

    // Wafer distribution using Cyclic Zip
    WAFER_SCHEDULERS {
        WSC_001: {
            priority: 1,
            wafers: [1, 4, 7, 10, 13, 16, 19, 22, 25]  // 9 wafers
        }
        WSC_002: {
            priority: 1,
            wafers: [2, 5, 8, 11, 14, 17, 20, 23]      // 8 wafers
        }
        WSC_003: {
            priority: 1,
            wafers: [3, 6, 9, 12, 15, 18, 21, 24]      // 8 wafers
        }
    }

    SCHEDULE PRODUCTION_RUN_001 {
        wafer_count: 25
        scheduler_count: 3
        pipeline_depth: 3

        // Apply scheduling rules
        APPLY_RULE("WAR_001")  // Cyclic Zip Distribution
        APPLY_RULE("PSR_001")  // Pipeline Slot Assignment
        APPLY_RULE("PSR_002")  // Processing Time Pattern
        APPLY_RULE("SSR_001")  // Steady State Detection

        VERIFY {
            constraint: "all_wafers_assigned"
            constraint: "no_conflicts"
            constraint: "pipeline_depth <= 3"
            expected_cycle_time: 180s
        }
    }

    // Subscribe to all WSC status updates
    subscribe to "wsc/+/status" as wsc_updates @2;

    // Publish master commands
    publish commands to "msc/001/command" @2, persistent;
}

//=============================================================================
// WAFER SCHEDULERS (L2)
//=============================================================================

WAFER_SCHEDULER WSC_001 {
    LAYER: L2
    MASTER: "MSC_001"

    CONFIG {
        assigned_wafers: FORMULA(CYCLIC_ZIP, 0, 3, 25)
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

WAFER_SCHEDULER WSC_002 {
    LAYER: L2
    MASTER: "MSC_001"

    CONFIG {
        assigned_wafers: FORMULA(CYCLIC_ZIP, 1, 3, 25)
        max_concurrent: 3
    }

    ASSIGNED_WAFERS {
        pattern: "CYCLIC_ZIP"
        offset: 1
        stride: 3
        wafer_list: [W002, W005, W008, W011, W014, W017, W020, W023]
    }

    subscribe to "msc/+/command" as msc_commands @2;
    publish status to "wsc/002/status" @1;
}

WAFER_SCHEDULER WSC_003 {
    LAYER: L2
    MASTER: "MSC_001"

    CONFIG {
        assigned_wafers: FORMULA(CYCLIC_ZIP, 2, 3, 25)
        max_concurrent: 3
    }

    ASSIGNED_WAFERS {
        pattern: "CYCLIC_ZIP"
        offset: 2
        stride: 3
        wafer_list: [W003, W006, W009, W012, W015, W018, W021, W024]
    }

    subscribe to "msc/+/command" as msc_commands @2;
    publish status to "wsc/003/status" @1;
}

//=============================================================================
// ROBOT SCHEDULERS (L3)
//=============================================================================

ROBOT_SCHEDULER RSC_EFEM_001 {
    LAYER: L3

    CONFIG {
        robot_type: "EFEM"
        max_velocity: 2.0
        position_update_rate: 10Hz
        arm_count: 2
    }

    CONTROLLED_ROBOTS: ["WTR_001"]

    publish position to "rsc/efem/position" @0, volatile;

    transaction MOVE_TO_LOADPORT {
        command: move(wafer_id, current_station, "LOADPORT")
        timeout: 30s
        retry: exponential_backoff(3)
    }
}

ROBOT_SCHEDULER RSC_PROCESS_001 {
    LAYER: L3

    CONFIG {
        robot_type: "PROCESS_ROBOT"
        max_velocity: 1.5
        position_update_rate: 10Hz
    }

    CONTROLLED_ROBOTS: ["WTR_002", "WTR_003"]

    publish position to "rsc/process/position" @0, volatile;

    transaction MOVE_WAFER {
        command: move(wafer_id, source_station, target_station)
        timeout: 45s
        retry: exponential_backoff(3)
    }
}

//=============================================================================
// STATIONS (L4)
//=============================================================================

// Load Port
STATION STN_LP01 {
    TYPE: "LOADPORT"
    LAYER: L4
    CONFIG {
        capacity: 25
        foup_size: 25
    }
}

// CMP Platens
STATION STN_CMP01 {
    TYPE: "CMP_PLATEN"
    LAYER: L4
    CONFIG {
        process_time: 180s
        capacity: 1
    }
    CAPABILITIES: ["POLISH"]
    publish state to "station/cmp01/state" @2, persistent;
}

STATION STN_CMP02 {
    TYPE: "CMP_PLATEN"
    LAYER: L4
    CONFIG {
        process_time: 180s
        capacity: 1
    }
    CAPABILITIES: ["POLISH"]
    publish state to "station/cmp02/state" @2, persistent;
}

// Cleaning Stations
STATION STN_CLN01 {
    TYPE: "CLEANER"
    LAYER: L4
    CONFIG {
        process_time: 60s
        capacity: 2
    }
    CAPABILITIES: ["CLEAN", "RINSE", "DRY"]
    publish state to "station/cln01/state" @2, persistent;
}

// Buffer
STATION STN_BUF01 {
    TYPE: "BUFFER"
    LAYER: L4
    CONFIG {
        capacity: 5
    }
}

// Wafer Transfer Robots
STATION WTR_001 {
    TYPE: "WAFER_TRANSFER_ROBOT"
    LAYER: L4
    STATE_MACHINE {
        initial: "IDLE"
        states: {
            IDLE: { on: { RECEIVE_TASK: "EXECUTING" } }
            EXECUTING: { on: { COMPLETE: "IDLE", ERROR: "ALARM" } }
            ALARM: { on: { RESET: "IDLE" } }
        }
    }
}

STATION WTR_002 {
    TYPE: "WAFER_TRANSFER_ROBOT"
    LAYER: L4
    STATE_MACHINE {
        initial: "IDLE"
        states: {
            IDLE: { on: { RECEIVE_TASK: "EXECUTING" } }
            EXECUTING: { on: { COMPLETE: "IDLE", ERROR: "ALARM" } }
            ALARM: { on: { RESET: "IDLE" } }
        }
    }
}

STATION WTR_003 {
    TYPE: "WAFER_TRANSFER_ROBOT"
    LAYER: L4
    STATE_MACHINE {
        initial: "IDLE"
        states: {
            IDLE: { on: { RECEIVE_TASK: "EXECUTING" } }
            EXECUTING: { on: { COMPLETE: "IDLE", ERROR: "ALARM" } }
            ALARM: { on: { RESET: "IDLE" } }
        }
    }
}

//=============================================================================
// MESSAGE BROKER
//=============================================================================

MESSAGE_BROKER STATUS_BROKER_01 {
    TYPE: "PUB_SUB"

    TOPICS {
        WTR_STATUS {
            topic: "wtr/+/status"
            publishers: ["WTR_001", "WTR_002", "WTR_003"]
            subscribers: [
                { id: "MSC_001", qos: 2 },
                { id: "RSC_EFEM_001", qos: 1 },
                { id: "RSC_PROCESS_001", qos: 1 }
            ]
        }

        STATION_STATE {
            topic: "station/+/state"
            subscribers: [
                { id: "MSC_001", qos: 2, persistent: true }
            ]
        }
    }
}

//=============================================================================
// TRANSACTION MANAGER
//=============================================================================

TRANSACTION_MANAGER TXN_MGR_001 {
    TRANSACTION_SCHEMA {
        transaction_id: {
            format: "TXN_{timestamp}_{sequence}_{checksum}"
            unique: true
            ttl: 3600s
        }
    }
}

//=============================================================================
// SCHEDULING RULES
//=============================================================================

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

    "PSR_002": {
        name: "Processing_Time_Pattern"
        type: SCHEDULING
        priority: 3
        formula: FORMULA(next_time = current_time + process_time)
    }

    "SSR_001": {
        name: "Three_Phase_Steady_State"
        type: VERIFICATION
        priority: 4
        phases: ["RAMP_UP", "STEADY", "RAMP_DOWN"]
        detection: { method: "pipeline_full", threshold: 3 }
    }
}
```

### 10.2 Minimal Example

```sfl
// File: minimal_cmp.sfl
// Minimal CMP system with 3 wafers

MASTER_SCHEDULER MSC_MINI {
    LAYER: L1

    CONFIG {
        total_wafers: 3
        active_wsc_count: 1
    }

    SCHEDULE MINI_RUN {
        APPLY_RULE("WAR_001")
    }
}

WAFER_SCHEDULER WSC_MINI {
    LAYER: L2
    MASTER: "MSC_MINI"

    ASSIGNED_WAFERS {
        wafer_list: [W001, W002, W003]
    }
}

STATION STN_CMP_MINI {
    TYPE: "CMP_PLATEN"
    LAYER: L4

    CONFIG {
        process_time: 60s
    }
}
```

---

## 11. Pipeline Scheduling Patterns

### 11.1 Cyclic Zip Distribution

The Cyclic Zip algorithm distributes wafers evenly across schedulers:

```
Total Wafers: 25
Schedulers: 3 (WSC_001, WSC_002, WSC_003)

Distribution:
WSC_001 (offset=0): W01, W04, W07, W10, W13, W16, W19, W22, W25  (9 wafers)
WSC_002 (offset=1): W02, W05, W08, W11, W14, W17, W20, W23       (8 wafers)
WSC_003 (offset=2): W03, W06, W09, W12, W15, W18, W21, W24       (8 wafers)

Formula: scheduler_index = wafer_index % scheduler_count
```

### 11.2 Three-Phase Pipeline

```
Phase 1: RAMP_UP    - Pipeline is filling
Phase 2: STEADY     - Pipeline is full, maximum throughput
Phase 3: RAMP_DOWN  - Pipeline is draining

Visual Timeline (3-slot pipeline, 25 wafers):

Time  | SLOT_1 (WSC_001)      | SLOT_2 (WSC_002)    | SLOT_3 (WSC_003)
------|----------------------|--------------------|-----------------
T1    | W001 (LP→POL)        | Empty              | Empty           ← RAMP_UP
T2    | W001 (POL)           | Empty              | Empty
T3    | W001 (POL→CLN)       | W002 (LP→POL)      | Empty
T4    | W001 (CLN)           | W002 (POL)         | Empty
T5    | W001 (CLN→BUF)       | W002 (POL→CLN)     | W003 (LP→POL)   ← STEADY begins
T6    | W001 (BUF)           | W002 (CLN)         | W003 (POL)
T7    | W001 (BUF→LP)        | W002 (CLN)         | W003 (POL)
T8    | W004 (LP→POL)        | W002 (CLN→BUF)     | W003 (POL→CLN)  ← STEADY state
...
T71   | W025 (LP→POL)        | W023 (CLN→BUF)     | W024 (POL→CLN)
...
T77   | W025 (CLN→BUF)       | Empty              | Empty           ← RAMP_DOWN
T78   | W025 (BUF)           | Empty              | Empty
T79   | W025 (BUF→LP)        | Empty              | Empty           ← Complete
```

### 11.3 Processing Steps

Each wafer follows this sequence:

```
LP → WTR1 → POL → WTR2 → CLN → WTR3 → BUF → WTR1 → LP

Where:
LP   = Load Port
WTR  = Wafer Transfer Robot
POL  = Polishing Station
CLN  = Cleaning Station
BUF  = Buffer Station
```

---

## 12. Best Practices

### 12.1 Naming Conventions

```sfl
// Scheduler naming: TYPE_ZONE_NUMBER
MSC_001          // Master Scheduler 001
WSC_ZONE_A       // Wafer Scheduler Zone A
RSC_EFEM_01      // Robot Scheduler EFEM 01

// Station naming: STN_TYPE_NUMBER
STN_CMP01        // CMP Station 01
STN_CLN02        // Cleaning Station 02
STN_BUF_A        // Buffer Station A

// Wafer IDs: W + 3-digit number
W001, W002, ..., W025

// Transaction IDs: Include timestamp for uniqueness
TXN_20240101120000_00001_A3F2
```

### 12.2 Layer Hierarchy Rules

```sfl
// DO: Respect layer hierarchy
L1 → L2 → L3 → L4  (Commands flow down)
L4 → L3 → L2 → L1  (Status flows up)

// DON'T: Skip layers
// Bad: L1 directly controlling L4
MASTER_SCHEDULER MSC_001 {
    // Don't directly reference stations
    // control: STN_CMP01  ← Wrong!
}

// DO: Use proper hierarchy
MASTER_SCHEDULER MSC_001 {
    WAFER_SCHEDULERS {
        WSC_001: { ... }  // L2 references
    }
}
```

### 12.3 QoS Selection

```sfl
// QoS 0: Real-time, loss-tolerant data
publish position to "rsc/efem/position" @0, volatile;  // Position updates

// QoS 1: Important but can duplicate
publish status to "wsc/001/status" @1;  // Status updates

// QoS 2: Critical, exactly-once
publish state to "station/cmp01/state" @2, persistent;  // State changes
subscribe to "msc/+/command" as commands @2;  // Commands
```

### 12.4 Transaction Design

```sfl
// DO: Include timeout and retry
transaction MOVE_WAFER {
    command: move(W001, STN_CMP01, STN_CLN01)
    timeout: 30s
    retry: exponential_backoff(3)
}

// DO: Use parent transactions for traceability
transaction MOVE_STEP_1 {
    parent: TXN_PRODUCTION_001
    command: ...
}

// DON'T: Forget error handling
// transaction { command: move(...) }  ← Missing timeout/retry
```

### 12.5 Verification

```sfl
// Always verify critical constraints
SCHEDULE PRODUCTION_RUN {
    APPLY_RULE("WAR_001")
    APPLY_RULE("PSR_001")

    VERIFY {
        // Wafer assignment
        constraint: "all_wafers_assigned"
        constraint: "no_duplicate_assignments"

        // Pipeline
        constraint: "pipeline_depth <= 3"
        constraint: "no_station_conflicts"

        // Resource
        constraint: "robot_capacity_sufficient"
    }
}
```

---

## 13. Error Codes Reference

| Code | Error | Description | Resolution |
|------|-------|-------------|------------|
| SFL001 | Invalid scheduler type | Must be MASTER_SCHEDULER, WAFER_SCHEDULER, ROBOT_SCHEDULER, or STATION | Use valid scheduler type keyword |
| SFL002 | Layer hierarchy violation | L1 cannot directly reference L3/L4 | Respect layer hierarchy |
| SFL003 | Unknown rule identifier | Referenced rule (e.g., "PSR_999") doesn't exist | Define the rule or use existing one |
| SFL004 | Wafer assignment conflict | Wafer assigned to multiple WSCs | Check CYCLIC_ZIP formula parameters |
| SFL005 | Invalid QoS level | QoS must be 0, 1, or 2 | Use valid MQTT QoS level |
| SFL006 | Transaction timeout exceeded | Transaction took longer than allowed | Increase timeout or optimize process |
| SFL007 | Formula syntax error | Invalid FORMULA expression | Check formula syntax |
| SFL008 | Pipeline depth exceeded | Max 3 for standard FABs | Reduce pipeline depth |

### 13.1 Error Examples

```sfl
// SFL001: Invalid scheduler type
SCHEDULER MSC_001 { ... }  // Error! Use MASTER_SCHEDULER

// SFL002: Layer hierarchy violation
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
    direct_control: STN_CMP01  // Error! L1 can't control L4 directly
}

// SFL003: Unknown rule
SCHEDULE RUN_001 {
    APPLY_RULE("PSR_999")  // Error! PSR_999 not defined
}

// SFL004: Wafer conflict
WAFER_SCHEDULERS {
    WSC_001: { wafers: [1, 2, 3] }
    WSC_002: { wafers: [3, 4, 5] }  // Error! W003 assigned twice
}

// SFL005: Invalid QoS
publish status to "topic" @5;  // Error! QoS must be 0, 1, or 2
```

---

## 14. Appendix

### 14.1 EBNF Grammar Summary

```ebnf
(* Top-level *)
sfl_program      ::= import* declaration* scheduler_def+ schedule_def*

(* Scheduler *)
scheduler_def    ::= scheduler_type identifier '{' layer_spec? config_block? scheduler_body '}'
scheduler_type   ::= 'MASTER_SCHEDULER' | 'WAFER_SCHEDULER' | 'ROBOT_SCHEDULER' | 'STATION'

(* Layer *)
layer_spec       ::= 'LAYER' ':' ('L1' | 'L2' | 'L3' | 'L4')

(* Config *)
config_block     ::= 'CONFIG' '{' config_item* '}'
config_item      ::= identifier ':' value

(* Schedule *)
schedule_def     ::= 'SCHEDULE' identifier '{' property* rule_application* verification? '}'
rule_application ::= 'APPLY_RULE' '(' string ')' ';'
verification     ::= 'VERIFY' '{' constraint* '}'

(* Pub/Sub *)
publish_stmt     ::= 'publish' identifier 'to' string qos? persistence? ';'
subscribe_stmt   ::= 'subscribe' 'to' string 'as' identifier qos? filter? ';'
qos              ::= '@' ('0' | '1' | '2')

(* Transaction *)
transaction_def  ::= 'transaction' identifier '{' transaction_body '}'
transaction_body ::= ('parent' ':' txn_id)? 'command' ':' command 'timeout' ':' duration 'retry' ':' retry_policy
```

### 14.2 Type System

```typescript
// Core types
type wafer_id_t = "W" + digit{3}           // W001-W999
type station_id_t = string                  // STN_CMP01
type scheduler_id_t = string                // MSC_001, WSC_001, RSC_001
type txn_id_t = "TXN_" + timestamp + "_" + sequence + "_" + checksum

// Time types
type duration_t = number + ("ms" | "s" | "m" | "h")
type frequency_t = number + "Hz"
type timestamp_t = ISO8601

// Enum types
type layer_t = "L1" | "L2" | "L3" | "L4"
type qos_t = 0 | 1 | 2
type status_t = "CREATED" | "QUEUED" | "EXECUTING" | "COMPLETED" | "FAILED"
```

### 14.3 Compiler Directives

```sfl
#pragma sfl version 2.0           // Language version
#pragma sfl strict                // Enable strict type checking
#pragma sfl optimize pipeline     // Enable pipeline optimization
#pragma sfl fab samsung           // FAB-specific optimizations
#pragma sfl semi e90              // SEMI E90 compliance mode
```

### 14.4 VSCode Extension Setup

Create `.vscode/settings.json`:

```json
{
    "files.associations": {
        "*.sfl": "semiflow",
        "*.sfli": "semiflow",
        "*.sflc": "semiflow"
    }
}
```

### 14.5 SEMI Standards Reference

| Standard | Description | SFL Support |
|----------|-------------|-------------|
| E87 | Carrier Management | Station types, FOUP handling |
| E88 | AMHS (Stocker, Vehicle) | Robot schedulers |
| E90 | Substrate Tracking | Wafer IDs, location tracking |
| E94 | Control Job Management | Schedules, transactions |

---

## Glossary

| Term | Definition |
|------|------------|
| **AMHS** | Automated Material Handling System |
| **CMP** | Chemical Mechanical Planarization |
| **Cyclic Zip** | Wafer distribution algorithm that assigns wafers to schedulers in round-robin fashion |
| **EFEM** | Equipment Front End Module |
| **FOUP** | Front Opening Unified Pod (wafer carrier) |
| **MSC** | Master Scheduler |
| **Pipeline Depth** | Number of concurrent wafers in processing |
| **QoS** | Quality of Service (MQTT message delivery guarantee) |
| **RSC** | Robot Scheduler |
| **SEMI** | Semiconductor Equipment and Materials International |
| **Steady State** | Condition where pipeline is full and operating at maximum throughput |
| **WSC** | Wafer Scheduler |
| **WTR** | Wafer Transfer Robot |

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2024.01 | Initial SFL specification |
| 1.5 | 2024.06 | Added transaction management |
| 2.0 | 2024.11 | Full pub/sub, MQTT QoS, pipeline rules |

---

**Copyright (c) 2024-2026 Semiconductor Manufacturing Consortium**
**Compliant with SEMI E87, E88, E90, E94 standards**
