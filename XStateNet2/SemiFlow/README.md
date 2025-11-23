# SemiFlow to XState Converter

A converter that transforms SemiFlow DSL (Domain-Specific Language) workflow definitions into XState-compatible state machine scripts for use with XStateNet2.

## Overview

**SemiFlow** is a multi-lane workflow DSL designed for semiconductor manufacturing, specifically for CMP (Chemical Mechanical Planarization) wafer processing. It provides a high-level declarative syntax for defining complex, resource-constrained workflows with parallel execution lanes.

This converter translates SemiFlow JSON documents into XState machine definitions that can be executed by the XStateNet2 engine.

## Architecture

### Project Structure

```
SemiFlow/
├── SemiFlow.Converter/          # Core converter library
│   ├── Models/                   # SemiFlow data models
│   │   ├── SemiFlowDocument.cs  # Root document model
│   │   └── Steps.cs             # Step type definitions
│   ├── Converters/              # Conversion logic
│   │   └── StepConverter.cs     # Converts SemiFlow steps to XState states
│   ├── Helpers/                 # Utility classes
│   │   └── XStateNodeBuilder.cs # Builder for XState nodes
│   └── SemiFlowToXStateConverter.cs  # Main converter class
├── SemiFlow.CLI/                # Command-line interface
│   └── Program.cs               # CLI entry point
├── SemiFlow_Schema_1_0.json     # JSON Schema for SemiFlow v1.0
└── example_wafer_flow.json      # Example SemiFlow document

```

### Key Components

#### 1. SemiFlow Document Structure

```json
{
  "name": "workflow-name",
  "version": "1.0.0",
  "vars": {},              // Global variables
  "constants": {},         // Immutable constants
  "stations": [],          // Resource catalog
  "lanes": [],             // Parallel workflow lanes
  "events": [],            // Event definitions
  "metrics": [],           // Performance metrics
  "globalHandlers": {}     // Error/timeout handlers
}
```

#### 2. Supported Step Types (19 total)

| Step Type | Description | Maps To |
|-----------|-------------|---------|
| `action` | Execute an action | XState entry action |
| `useStation` | Acquire and use a station resource | Nested state with acquire/use/release |
| `reserve` | Reserve resources | Entry action |
| `release` | Release resources | Entry action |
| `parallel` | Execute branches concurrently | Parallel state |
| `loop` | Iterative execution | Compound state with guards |
| `branch` | Conditional branching (if-then-else) | Guarded transitions |
| `switch` | Value-based case selection | Guarded transitions |
| `wait` | Duration or condition-based delay | After or guarded transition |
| `condition` | Assert expectations | Guarded transition |
| `sequence` | Sequential step execution | Chained states |
| `call` | Invoke another workflow | Invoked service |
| `try` | Exception handling | Compound state with error transitions |
| `emitEvent` | Publish events | Entry action |
| `onEvent` | Event listener | Event transition handler |
| `collectMetric` | Gather metrics | Entry action |
| `race` | First-to-complete wins | Parallel state with onDone |
| `transaction` | ACID-like transactional steps | Compound state with rollback |

#### 3. Conversion Strategy

**Single Lane:**
- Converts directly to a single XState machine
- Workflow steps become states
- Steps are linked sequentially

**Multi-Lane:**
- Creates a parallel XState machine
- Each lane becomes a parallel region
- Lanes execute independently
- Resources are shared across lanes

### Example Conversion

#### Input (SemiFlow):

```json
{
  "name": "SimpleProcessing",
  "version": "1.0.0",
  "lanes": [
    {
      "id": "main",
      "workflow": {
        "id": "process",
        "steps": [
          {
            "id": "start",
            "type": "action",
            "action": "initialize"
          },
          {
            "id": "process",
            "type": "useStation",
            "role": "polisher"
          },
          {
            "id": "complete",
            "type": "action",
            "action": "finalize"
          }
        ]
      }
    }
  ]
}
```

#### Output (XState):

```json
{
  "id": "process",
  "initial": "start",
  "states": {
    "start": {
      "entry": ["initialize"],
      "on": {
        "initialize_DONE": { "target": "process" }
      }
    },
    "process": {
      "initial": "acquiring",
      "states": {
        "acquiring": {
          "entry": ["requestStation_polisher"],
          "on": {
            "STATION_ACQUIRED": { "target": "using" }
          }
        },
        "using": {
          "on": {
            "USAGE_COMPLETE": {
              "target": "..complete",
              "actions": ["releaseStation_polisher"]
            }
          }
        }
      }
    },
    "complete": {
      "entry": ["finalize"],
      "type": "final"
    }
  }
}
```

## Usage

### As a Library

```csharp
using SemiFlow.Converter;

var converter = new SemiFlowToXStateConverter();

// From JSON string
var semiFlowJson = File.ReadAllText("workflow.json");
var xstateMachine = converter.Convert(semiFlowJson);

// From file
converter.ConvertFile("input.json", "output.json");

// Get JSON output
var xstateJson = converter.SerializeToJson(xstateMachine);
```

### As a CLI Tool

```bash
# Basic usage
dotnet run --project SemiFlow.CLI -- input.json output.json

# Using default output name (output_xstate.json)
dotnet run --project SemiFlow.CLI -- input.json
```

## SemiFlow Features

### Resource Management

```json
{
  "stations": [
    {
      "id": "polisher_1",
      "role": "polisher",
      "kind": "dedicated",    // dedicated | shared | swappable
      "capacity": 1,
      "state": "idle"
    }
  ],
  "globalStationPools": {
    "polisher": ["polisher_1", "polisher_2"]
  }
}
```

### Retry Policies

```json
{
  "id": "risky_operation",
  "type": "action",
  "action": "performOperation",
  "retry": {
    "count": 3,
    "delay": 1000,
    "strategy": "exponential",  // fixed | exponential | linear
    "maxDelay": 10000,
    "jitter": true
  }
}
```

### Parallel Execution

```json
{
  "id": "parallel_work",
  "type": "parallel",
  "branches": [
    [/* branch 1 steps */],
    [/* branch 2 steps */]
  ],
  "wait": "all"  // all | any | race | none
}
```

### Conditional Logic

```json
{
  "id": "quality_check",
  "type": "branch",
  "cases": [
    {
      "when": "qualityGood",
      "steps": [/* good path */]
    },
    {
      "when": "qualityBad",
      "steps": [/* reject path */]
    }
  ],
  "otherwise": [/* default path */]
}
```

### Event Handling

```json
{
  "events": [
    {
      "name": "WAFER_READY",
      "type": "wafer",
      "description": "Wafer ready for processing"
    }
  ],
  "lanes": [
    {
      "eventHandlers": [
        {
          "event": "WAFER_READY",
          "filter": "waferType === 'premium'",
          "steps": [/* handler steps */]
        }
      ]
    }
  ]
}
```

### Metrics Collection

```json
{
  "metrics": [
    {
      "name": "cycle_time",
      "type": "timer",        // counter | gauge | histogram | timer
      "unit": "ms",
      "aggregation": "avg"    // sum | avg | min | max | p50 | p95 | p99
    }
  ],
  "lanes": [
    {
      "workflow": {
        "steps": [
          {
            "id": "track_time",
            "type": "collectMetric",
            "metric": "cycle_time",
            "value": "elapsedTime"
          }
        ]
      }
    }
  ]
}
```

## Implementation Status

### ✅ Completed
- SemiFlow data models (Document, Lane, Workflow, all 19 step types)
- JSON schema for SemiFlow v1.0
- Core converter architecture
- Step-to-state conversion logic for all step types
- Multi-lane to parallel state conversion
- Example SemiFlow documents
- CLI application

### 🚧 In Progress
- Fixing readonly dictionary assignments in XStateNode builder
- Unit tests for conversion logic
- Integration tests with XStateNet2 engine

### 📋 TODO
- Action/guard/service implementations for converted machines
- Resource pool management runtime
- Metrics collection infrastructure
- Event bus integration
- Error handling and validation improvements
- Performance optimization for large workflows
- Documentation improvements

## Known Issues

1. **Readonly Dictionary Assignments**: The XStateNode class uses `IReadOnlyDictionary` properties, but the converter needs to build these incrementally. The `XStateNodeBuilder` helper class provides a solution, but the StepConverter needs to be refactored to use it.

2. **Type Conversions**: Some properties expect `List<object>` (for actions that can be strings or complex objects) but the converter provides `List<string>`. Need to cast appropriately.

3. **After Property**: Expects `IReadOnlyDictionary<int, List<XStateTransition>>` but code tries to assign `Dictionary<string, object>`. Need to parse delays as integers.

## Contributing

When fixing compilation errors:

1. Use `XStateNodeBuilder` instead of directly constructing `XStateNode`
2. Cast string lists to `List<object>` for Entry/Exit properties
3. Ensure After dictionary uses `int` keys, not strings
4. Create mutable dictionaries first, then assign to readonly properties

Example:

```csharp
// ❌ Wrong
var state = new XStateNode
{
    States = new Dictionary<string, XStateNode>()
};
state.States["child"] = childState;  // Error: readonly property

// ✅ Correct
var states = new Dictionary<string, XStateNode>();
states["child"] = childState;
var state = new XStateNode
{
    States = states  // Assign complete dictionary
};

// ✅ Even Better
var state = new XStateNodeBuilder()
    .WithState("child", childState)
    .Build();
```

## License

Part of the XStateNet2 project.

## References

- [SemiFlow Schema](SemiFlow_Schema_1_0.json)
- [XState Documentation](https://xstate.js.org/)
- [XStateNet2 Core](../XStateNet2.Core/)
