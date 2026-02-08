# Semi Flow Language (SFL) Grammar Specification
## Language Designer's Reference Manual v2.2

### 1. Language Overview

#### 1.1 Purpose
The **Semi Flow Language (SFL)** is a domain-specific language for defining hierarchical scheduling systems in semiconductor manufacturing environments, supporting wafer fabrication automation and AMHS (Automated Material Handling System) integration.

#### 1.2 File Extension
- **Primary**: `*.sfl`
- **Include files**: `*.sfli`
- **Configuration**: `*.sflc`

#### 1.3 Design Principles
- **Hierarchical Composition**: Natural expression of MSC→WSC→RSC→Station hierarchy
- **Transaction-Based**: All operations tracked via transaction IDs
- **Event-Driven**: Asynchronous messaging with Pub/Sub patterns
- **Type-Safe**: Strong typing for wafer IDs, station names, and commands
- **SEMI-Compliant**: Aligns with E87, E88, E90, E94 standards

### 2. Lexical Structure

#### 2.1 Character Set
```ebnf
letter     ::= [a-zA-Z]
digit      ::= [0-9]
underscore ::= '_'
hyphen     ::= '-'
dot        ::= '.'
slash      ::= '/'
```

#### 2.2 Tokens
```ebnf
(* Identifiers *)
identifier ::= letter (letter | digit | underscore)*
               | [A-Z] (letter | digit | underscore)*  (* For schedulers *)
wafer_id   ::= 'W' digit+ ('_' digit+)?
station_id ::= letter+ digit+ ('_' identifier)?
txn_id     ::= 'TXN' '_' digit+ ('_' [A-Z0-9]+)*

(* Keywords - Semi Flow Specific *)
keyword ::= 'MASTER_SCHEDULER' | 'WAFER_SCHEDULER' | 'ROBOT_SCHEDULER'
          | 'STATION' | 'SCHEDULE' | 'PIPELINE_SCHEDULING_RULES'
          | 'APPLY_RULE' | 'VERIFY' | 'FORMULA' | 'CONFIG'
          | 'LAYER' | 'L1' | 'L2' | 'L3' | 'L4'
          | 'FLOW' | 'CROSSOVER' | 'MUTEX' | 'CONSTRAINTS'
          | 'transaction' | 'subscribe' | 'publish' | 'await'
          | 'parallel' | 'sequential' | 'retry' | 'timeout'

(* Operators *)
operator ::= '->' | '=>' | '::' | '@' | '#' | '|>' | '<|' | ':' 

(* Literals *)
integer    ::= digit+
float      ::= digit+ '.' digit+
string     ::= '"' [^"]* '"'
duration   ::= digit+ ('ms' | 's' | 'm' | 'h')
frequency  ::= digit+ 'Hz'
```

### 3. Grammar Specification (EBNF)

#### 3.1 Top-Level Structure
```ebnf
sfl_program ::= import* declaration* topology_block* scheduler_def+ schedule_def*

topology_block ::= flow_block | crossover_block | mutex_block | constraints_block

import ::= 'import' module_path ('as' identifier)?
         | 'from' module_path 'import' import_list

module_path ::= identifier ('.' identifier)*
import_list ::= identifier (',' identifier)* | '*'

declaration ::= constant_def | type_def | rule_def | protocol_def
```

#### 3.2 Scheduler Definitions (Semi Flow Core)
```ebnf
scheduler_def ::= scheduler_type identifier '{' 
                    layer_spec?
                    config_block?
                    scheduler_body 
                  '}'

scheduler_type ::= 'MASTER_SCHEDULER' | 'WAFER_SCHEDULER' 
                 | 'ROBOT_SCHEDULER' | 'STATION'

layer_spec ::= 'LAYER' ':' layer_identifier

layer_identifier ::= 'L1' | 'L2' | 'L3' | 'L4' | integer

config_block ::= 'CONFIG' '{' config_items '}'

config_items ::= (config_item ';')*

config_item ::= identifier ':' (literal | expression)

scheduler_body ::= (property | method | event_handler | rule_application)*
```

#### 3.3 Schedule Definitions (Semi Flow Specific)
```ebnf
schedule_def ::= 'SCHEDULE' identifier '{' 
                   schedule_properties
                   rule_applications
                   verification? 
                 '}'

schedule_properties ::= property_def*

property_def ::= identifier ':' value_expression

rule_applications ::= ('APPLY_RULE' '(' string_literal ')' ';')*

verification ::= 'VERIFY' '{' verification_rules '}'
```

#### 3.4 Pipeline Scheduling Rules (SFL Unique)
```ebnf
pipeline_rules ::= 'PIPELINE_SCHEDULING_RULES' '{' 
                     rule_definition+ 
                   '}'

rule_definition ::= rule_id ':' '{' 
                      'name' ':' string_literal ','
                      'type' ':' rule_type ','
                      'priority' ':' integer ','
                      'formula' ':' formula_expression ','
                      'constraints' ':' constraint_list
                    '}'

rule_id ::= '"' ('PSR' | 'WAR' | 'SSR' | 'WTR') '_' digit+ '"'

rule_type ::= 'ALLOCATION' | 'SCHEDULING' | 'OPTIMIZATION' | 'VERIFICATION'

formula_expression ::= 'FORMULA' '(' formula_params ')'
```

#### 3.5 Transaction Management
```ebnf
transaction_def ::= 'transaction' txn_id? '{' 
                      'parent' ':' txn_id? ','
                      'command' ':' command_spec ','
                      'status' ':' status_spec ','
                      'timeout' ':' duration ','
                      'retry' ':' retry_policy
                    '}'

txn_id ::= 'TXN' '_' timestamp '_' sequence '_' checksum

timestamp ::= digit{14}  (* YYYYMMDDHHmmss *)
sequence ::= digit{5}
checksum ::= [A-F0-9]{4}

command_spec ::= command_type '(' parameter_list ')'

status_spec ::= 'CREATED' | 'QUEUED' | 'EXECUTING' | 'COMPLETED' | 'FAILED'
```

#### 3.7 Pipeline Topology Blocks
```ebnf
(* Pipeline topology blocks define the physical flow path, cross-lane behavior,
   mutual exclusion constraints, and scheduling constraints for the production line.
   All four blocks are optional and compile to the "meta" object in XState JSON output. *)

flow_block ::= 'FLOW' identifier '{' flow_sequence '}'

flow_sequence ::= identifier ('->' identifier)+
(* Minimum 3 elements. Convention: start with SRC, end with DST.
   Elements represent stations and robots along the pipeline path.
   Example: SRC -> R1 -> POL -> R2 -> CLN -> R3 -> RNS -> R4 -> BUF -> R5 -> DST *)

crossover_block ::= 'CROSSOVER' '{' crossover_entry+ '}'

crossover_entry ::= identifier ':' ('enabled' | 'disabled')
(* Defines whether a process station allows cross-lane wafer movement.
   Station names should reference stations in the FLOW sequence.
   Robots are excluded — only process stations are configured. *)

mutex_block ::= 'MUTEX' '{' mutex_group+ '}'

mutex_group ::= 'group' ':' mutex_pattern (',' mutex_pattern)*

mutex_pattern ::= lane_spec '.' resource_spec

lane_spec ::= 'L' digit+ | 'L*' | '*'
resource_spec ::= identifier | identifier '*' | '*'
(* Patterns define mutual exclusion groups for robot resources.
   L* and * are wildcards matching all lanes.
   Examples: L1.R1, L*.R1 (all lanes share R1), *.* (global mutex) *)

constraints_block ::= 'CONSTRAINTS' '{' config_items '}'
(* Key-value properties defining scheduling constraints.
   Common properties:
     scheduling_mode: "TAKT_TIME"  — scheduling strategy: "TAKT_TIME" | "EVENT_DRIVEN" | "DEADLINE_DRIVEN"
     takt_interval: 1s             — tick interval for TAKT_TIME mode (optional)
     deadline_policy: "EDF"        — deadline strategy: "EDF" | "LLF" (only with DEADLINE_DRIVEN)
     no_wait: [R1, R2]             — robots that cannot hold wafers (immediate handoff)
     max_wip: 10                   — maximum work-in-progress wafer count
     priority: "FIFO"              — scheduling priority strategy

   Scheduling modes:
     TAKT_TIME (default)  — fixed-interval tick, countdown by 1, backward flow priority
     EVENT_DRIVEN         — jump to next event (min remainingTime), skip idle ticks
     DEADLINE_DRIVEN      — fixed tick like takt, but closest-deadline-first priority
*)
```

##### 3.7.1 Compilation to XState JSON

Pipeline topology blocks compile to the top-level `meta` object in the XState JSON output:

```json
{
  "id": "sfl_system",
  "type": "parallel",
  "meta": {
    "flowName": "PRODUCTION_LINE",
    "flow": ["SRC", "R1", "POL", "R2", "CLN", "R3", "DST"],
    "crossover": { "POL": false, "CLN": true },
    "mutex": [
      { "patterns": ["L*.R1"], "isGlobal": true },
      { "patterns": ["L1.R2", "L1.R3"], "isGlobal": false }
    ],
    "constraints": { "no_wait": ["R1", "R2"], "max_wip": 10, "priority": "FIFO" }
  },
  "states": { ... }
}
```

##### 3.7.2 Semantic Validations

| Code   | Rule                                                              | Severity |
|--------|-------------------------------------------------------------------|----------|
| SFL009 | Flow must have >= 3 elements; should start with SRC, end with DST | Error/Warning |
| SFL010 | Crossover stations should exist in the flow sequence              | Warning  |
| SFL011 | Mutex patterns must match `(L<n>|L*|*).(R<n>|R*|*)`              | Error    |
| SFL012 | `max_wip` must be positive; `no_wait` entries should be in flow   | Error/Warning |
| SFL013 | `scheduling_mode` must be valid; `deadline_policy` only with DEADLINE_DRIVEN | Error/Warning |

#### 3.6 Pub/Sub Communication (Semi Flow Messaging)
```ebnf
publish_def ::= 'publish' message_spec 'to' topic_spec qos_spec? ';'

subscribe_def ::= 'subscribe' 'to' topic_spec 'as' identifier 
                  filter_spec? qos_spec? ';'

topic_spec ::= string_literal  (* e.g., "wafer/+/status" *)

message_spec ::= '{' message_fields '}'

qos_spec ::= '@' qos_level (',' reliability)?

qos_level ::= '0' | '1' | '2'  (* MQTT QoS levels *)

filter_spec ::= 'where' condition_expression
```

### 4. Type System

#### 4.1 Semi Flow Native Types
```typescript
// Semiconductor-specific types
wafer_id_t      // W001, W002, ..., W025
lot_id_t        // LOT_20240101_001
recipe_id_t     // RCP_CMP_001
station_id_t    // STN_CMP01, STN_CLN02
scheduler_id_t  // MSC_001, WSC_001, RSC_001

// Transaction types
txn_id_t        // TXN_20240101120000_00001_A3F2
command_id_t    // CMD_MOVE_WAFER_001

// Time types
timestamp_t     // 2024-01-01T12:00:00Z
duration_t      // 30s, 5m, 1h
frequency_t     // 10Hz, 1Hz
```

#### 4.2 Rule Types (Semi Flow Specific)
```typescript
// Pipeline Scheduling Rules
type PSR = {
  id: string,           // "PSR_001"
  name: string,         // "Pipeline_Slot_Assignment"
  priority: integer,
  formula: Formula,
  constraints: Constraint[]
}

// Wafer Assignment Rules
type WAR = {
  id: string,           // "WAR_001"
  name: string,         // "Cyclic_Zip_Distribution"
  pattern: Pattern,
  load_balance: boolean
}

// Steady State Rules
type SSR = {
  id: string,           // "SSR_001"
  name: string,         // "Three_Phase_Steady_State"
  phases: Phase[],
  detection: DetectionMethod
}
```

### 5. Semantic Rules

#### 5.1 Cyclic Zip Distribution
```sfl
// Semi Flow cyclic zip implementation
FORMULA(CYCLIC_ZIP) {
  input: wafer_count, scheduler_count
  algorithm: {
    for i in 0..wafer_count-1:
      scheduler_index = i % scheduler_count
      assign(wafer[i], scheduler[scheduler_index])
  }
  output: assignment_matrix
}
```

#### 5.2 Layer Hierarchy
```sfl
// Layer definitions in Semi Flow
L1: MASTER_SCHEDULER    // Top orchestration layer
L2: WAFER_SCHEDULER      // Wafer-level scheduling
L3: ROBOT_SCHEDULER      // Robot control layer
L4: STATION              // Physical equipment layer
```

#### 5.3 Message Topics (MQTT-style)
```sfl
// Topic hierarchy in Semi Flow
"msc/+/command"          // Commands from MSC
"wsc/+/status"           // Status from WSC
"rsc/+/position"         // Position updates from RSC
"station/+/state"        // State from stations
"wafer/+/location"       // Wafer tracking
"transaction/+/update"   // Transaction updates
```

### 6. Standard Library

#### 6.1 Semi Flow Core Modules
```sfl
// Core scheduling algorithms
import semiflow.algorithms.cyclic_zip
import semiflow.algorithms.round_robin
import semiflow.algorithms.load_balanced

// SEMI standard compliance
import semiflow.semi.e87  // Carrier Management
import semiflow.semi.e88  // AMHS
import semiflow.semi.e90  // Substrate Tracking
import semiflow.semi.e94  // Control Job Management

// Communication
import semiflow.comm.mqtt  // MQTT messaging
import semiflow.comm.transaction  // Transaction management
```

#### 6.2 Built-in Rules
```sfl
PIPELINE_SCHEDULING_RULES {
  // Pipeline slot assignment
  "PSR_001": { name: "Pipeline_Slot_Assignment", ... }
  "PSR_002": { name: "Processing_Time_Pattern", ... }
  "PSR_003": { name: "WTR_Assignment_Matrix", ... }
  
  // Wafer assignment rules
  "WAR_001": { name: "Cyclic_Zip_Distribution", ... }
  "WAR_002": { name: "WSC_Pipeline_Slot_Control", ... }
  
  // Steady state rules
  "SSR_001": { name: "Three_Phase_Steady_State", ... }
  "SSR_002": { name: "Pipeline_State_Detection", ... }
}
```

### 7. Example Programs

#### 7.1 Complete Semi Flow System
```sfl
// File: cmp_line_system.sfl
// Semi Flow Language Example - CMP Production Line

import semiflow.algorithms.cyclic_zip
import semiflow.semi.e90

// Pipeline topology
FLOW PRODUCTION_LINE {
  SRC -> R1 -> POL -> R2 -> CLN -> R3 -> RNS -> R4 -> BUF -> R5 -> DST
}

CROSSOVER {
  POL: disabled
  CLN: enabled
  RNS: enabled
  BUF: enabled
}

MUTEX {
  group: L*.R1          // All lanes share R1
  group: L1.R2, L1.R3   // Lane 1 robots are mutually exclusive
}

CONSTRAINTS {
  scheduling_mode: "TAKT_TIME"
  no_wait: [R1, R2]
  max_wip: 10
  priority: "FIFO"
}

MASTER_SCHEDULER MSC_001 {
  LAYER: L1
  
  CONFIG {
    wafer_distribution: "CYCLIC_ZIP"
    total_wafers: 25
    active_wsc_count: 3
    optimization_interval: 30s
  }
  
  // Cyclic zip distribution for 25 wafers
  SCHEDULE PRODUCTION_RUN_001 {
    wafer_count: 25
    scheduler_count: 3
    
    // Apply Semi Flow rules
    APPLY_RULE("WAR_001")  // Cyclic Zip
    APPLY_RULE("PSR_001")  // Pipeline Slots
    APPLY_RULE("SSR_001")  // Steady State
    
    // Verification
    VERIFY {
      constraint: "all_wafers_assigned"
      constraint: "no_conflicts"
      constraint: "pipeline_depth <= 3"
    }
  }
}

WAFER_SCHEDULER WSC_001 {
  LAYER: L2
  
  CONFIG {
    assigned_wafers: FORMULA(CYCLIC_ZIP, 0, 3, 25)
    // Results in: [W1, W4, W7, W10, W13, W16, W19, W22, W25]
    max_concurrent: 3
  }
  
  // Subscribe to commands
  subscribe to "msc/+/command" as msc_commands @2;
  
  // Publish status
  publish status to "wsc/001/status" @1;
}

ROBOT_SCHEDULER RSC_EFEM_001 {
  LAYER: L3
  
  CONFIG {
    robot_type: "EFEM"
    max_velocity: 2.0  // m/s
    position_update_rate: 10Hz
  }
  
  // Real-time position updates
  publish position to "rsc/efem/position" @0, volatile;
  
  // Transaction handling
  transaction MOVE_WAFER {
    parent: TXN_MSC_001
    command: move(W001, STN_CMP01, STN_CLN01)
    timeout: 30s
    retry: exponential_backoff(3)
  }
}

STATION STN_CMP01 {
  LAYER: L4
  
  CONFIG {
    type: "CMP_POLISHER"
    process_time: 180s
    capacity: 1
  }
  
  // Direct status to MSC
  publish state to "station/cmp01/state" @2, persistent;
}
```

#### 7.2 Pipeline Scheduling Example
```sfl
// File: pipeline_schedule.sfl

SCHEDULE THREE_PHASE_PIPELINE {
  pipeline_depth: 3
  wafer_count: 25
  
  // Apply all pipeline rules
  APPLY_RULE("PSR_001")  // Slot assignment
  APPLY_RULE("PSR_002")  // Time patterns
  APPLY_RULE("PSR_003")  // WTR matrix
  
  // Formula for slot calculation
  slot_assignment: FORMULA(
    pipeline_depth * ceil(wafer_count / pipeline_depth)
  )
  
  VERIFY {
    expected_slots: 27  // 3 * 9
    actual_wafers: 25
    empty_slots: 2
  }
}
```

### 8. VSCode Extension Support

#### 8.1 File Association
```json
{
  "files.associations": {
    "*.sfl": "semiflow",
    "*.sfli": "semiflow",
    "*.sflc": "semiflow"
  }
}
```

#### 8.2 Syntax Highlighting Scopes
```yaml
# TextMate scopes for Semi Flow
- keyword.control.sfl: MASTER_SCHEDULER, WAFER_SCHEDULER, etc.
- entity.name.type.sfl: MSC_*, WSC_*, RSC_*
- constant.numeric.sfl: L1, L2, L3, L4
- string.quoted.sfl: "WAR_001", "PSR_002", etc.
- variable.other.sfl: W001-W025 (wafer IDs)
- entity.name.function.sfl: APPLY_RULE, VERIFY, FORMULA
- comment.line.sfl: // comments
- comment.block.sfl: /* block comments */
```

### 9. Grammar Checker Rules

#### 9.1 Validation Rules
```typescript
// Semi Flow validation rules
interface SFLValidation {
  // Structural validation
  checkLayerHierarchy(): boolean  // L1 > L2 > L3 > L4
  checkSchedulerNaming(): boolean  // MSC_*, WSC_*, RSC_*
  
  // Semantic validation
  checkWaferAssignment(): boolean  // All wafers assigned
  checkRuleExistence(): boolean    // Applied rules exist
  checkTransactionChain(): boolean // Parent-child valid
  
  // Performance validation
  checkUpdateFrequency(): boolean  // 10Hz max for position
  checkQoSLevels(): boolean        // 0, 1, 2 only
}
```

### 10. Compiler Directives

#### 10.1 Semi Flow Specific Pragmas
```sfl
#pragma sfl version 2.0
#pragma sfl strict              // Strict type checking
#pragma sfl optimize pipeline   // Pipeline optimization
#pragma sfl fab samsung         // FAB-specific optimizations
#pragma sfl semi e90            // SEMI standard compliance
```

### 11. Error Messages

#### 11.1 Semi Flow Error Codes
```
SFL001: Invalid scheduler type (must be MASTER_SCHEDULER, WAFER_SCHEDULER, etc.)
SFL002: Layer hierarchy violation (L1 cannot reference L3 directly)
SFL003: Unknown rule identifier (e.g., "PSR_999" doesn't exist)
SFL004: Wafer assignment conflict (wafer assigned to multiple WSCs)
SFL005: Invalid QoS level (must be 0, 1, or 2)
SFL006: Transaction timeout (exceeds maximum duration)
SFL007: Formula syntax error (invalid FORMULA expression)
SFL008: Pipeline depth exceeded (max 3 for standard FABs)
SFL009: Invalid flow sequence (must have >= 3 elements, should start with SRC, end with DST)
SFL010: Crossover station not found in flow sequence
SFL011: Invalid mutex pattern (must match (L<n>|L*|*).(R<n>|R*|*))
SFL012: Invalid constraint value (max_wip must be positive, no_wait entries should be in flow)
SFL013: Invalid scheduling constraint (scheduling_mode must be TAKT_TIME|EVENT_DRIVEN|DEADLINE_DRIVEN, deadline_policy must be EDF|LLF)
```

### 12. Language Evolution

#### Version History
- **v2.2** (2026.02.08): Added multi-mode scheduling (TAKT_TIME, EVENT_DRIVEN, DEADLINE_DRIVEN), SFL013
- **v2.1** (2026.02.08): Added Pipeline Topology Blocks (FLOW, CROSSOVER, MUTEX, CONSTRAINTS), SFL009-SFL012
- **Draft** (2025.11.23): Initial SFL draft

---

## License & Standards

Semi Flow Language Specification - Draft 2025.11.23
Compliant with SEMI E87, E88, E90, E94 standards

This specification is the official grammar reference for the Semi Flow Language (*.sfl) used in semiconductor manufacturing automation systems.