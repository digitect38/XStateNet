# State Machine Design for CMP Tool Simulator

## Overview

This document describes the state machine design for each stage/station in the CMP Tool simulator. Each station operates as an independent state machine that coordinates wafer processing.

## Wafer State Machine

### States

```
┌─────────────────────────────────────────────────────────────┐
│                     WAFER LIFECYCLE                          │
└─────────────────────────────────────────────────────────────┘

IDLE (at LoadPort)
    │
    ├─[START]→ TRANSFERRING_TO_BUFFER1
    │              │
    │              ├→ WAITING_IN_BUFFER1
    │              │      │
    │              │      └─[Buffer1 → WTR2]→ TRANSFERRING_TO_POLISHER
    │              │
    │              └─[Direct]→ TRANSFERRING_TO_POLISHER
    │                            │
    │                            ├→ WAITING_FOR_POLISHER
    │                            │      │
    │                            └──────┴→ IN_POLISHER (processing 3000ms)
    │                                       │
    │                                       └→ TRANSFERRING_TO_BUFFER2
    │                                              │
    │                                              ├→ WAITING_IN_BUFFER2
    │                                              │      │
    │                                              └──────┴→ TRANSFERRING_TO_CLEANER
    │                                                         │
    │                                                         ├→ WAITING_FOR_CLEANER
    │                                                         │      │
    │                                                         └──────┴→ IN_CLEANER (processing 2500ms)
    │                                                                    │
    │                                                                    └→ TRANSFERRING_TO_BUFFER3 (return)
    │                                                                           │
    │                                                                           ├→ WAITING_IN_BUFFER3
    │                                                                           │      │
    │                                                                           └──────┴→ RETURNING_TO_LOADPORT
    │                                                                                      │
    └──────────────────────────────────────────────────────────────────────────────────┘
                                                                                      COMPLETED
```

### Events

- **START**: Begin wafer processing journey
- **TRANSFER_COMPLETE**: Wafer arrived at station
- **STATION_AVAILABLE**: Destination station has capacity
- **STATION_BUSY**: Destination station is full
- **PROCESSING_COMPLETE**: Polishing or cleaning finished
- **BUFFER_AVAILABLE**: Buffer has space
- **BUFFER_FULL**: No buffer space available

## Station State Machines

### LoadPort State Machine

```
States:
- READY: Ready to dispatch wafers
- DISPATCHING: Sending wafer to WTR1
- RECEIVING: Receiving wafer back from WTR1
- FULL: All 25 wafers present

Transitions:
- [START_SIMULATION] READY → DISPATCHING
- [WAFER_DISPATCHED] DISPATCHING → READY (if more wafers to send)
- [WAFER_RETURNED] RECEIVING → READY
- [ALL_RETURNED] RECEIVING → FULL
```

### WTR1 State Machine

```
States:
- IDLE: No wafer in transit
- TRANSITING_FORWARD: Moving wafer from LoadPort toward Buffer1
- TRANSITING_RETURN: Moving wafer from Buffer3 toward LoadPort

Capacity: 0 (never stores wafers)

Transitions:
- [PICK_FROM_LOADPORT] IDLE → TRANSITING_FORWARD
- [PLACE_TO_BUFFER1] TRANSITING_FORWARD → IDLE
- [PICK_FROM_BUFFER3] IDLE → TRANSITING_RETURN
- [PLACE_TO_LOADPORT] TRANSITING_RETURN → IDLE
```

### Buffer1 State Machine

```
States:
- EMPTY: No wafer stored
- OCCUPIED: Holding 1 wafer
- DISPATCHING: Sending wafer to WTR2

Capacity: 1

Transitions:
- [WAFER_ARRIVES] EMPTY → OCCUPIED
- [WTR2_READY] OCCUPIED → DISPATCHING
- [WAFER_DISPATCHED] DISPATCHING → EMPTY
```

### WTR2 State Machine

```
States:
- IDLE: No wafer in transit
- TRANSITING_TO_POLISHER: Moving wafer from Buffer1 to Polisher
- TRANSITING_TO_BUFFER2: Moving wafer from Polisher to Buffer2
- TRANSITING_TO_BUFFER3: Moving wafer from Cleaner to Buffer3

Capacity: 0 (never stores wafers)

Transitions:
- [PICK_FROM_BUFFER1] IDLE → TRANSITING_TO_POLISHER
- [PLACE_TO_POLISHER] TRANSITING_TO_POLISHER → IDLE
- [PICK_FROM_POLISHER] IDLE → TRANSITING_TO_BUFFER2
- [PLACE_TO_BUFFER2] TRANSITING_TO_BUFFER2 → IDLE
- [PICK_FROM_CLEANER] IDLE → TRANSITING_TO_BUFFER3
- [PLACE_TO_BUFFER3] TRANSITING_TO_BUFFER3 → IDLE
```

### Polisher State Machine

```
States:
- IDLE: Ready to accept wafer
- RECEIVING: Wafer being placed
- PROCESSING: Polishing in progress (3000ms)
- DISPATCHING: Sending wafer to WTR2

Capacity: 1

Transitions:
- [WAFER_ARRIVES] IDLE → RECEIVING
- [WAFER_PLACED] RECEIVING → PROCESSING
- [PROCESSING_COMPLETE] PROCESSING → DISPATCHING
- [WAFER_DISPATCHED] DISPATCHING → IDLE

Guards:
- canAcceptWafer: Returns true only if IDLE
```

### Buffer2 State Machine

```
States:
- EMPTY: No wafer stored
- OCCUPIED: Holding 1 wafer
- DISPATCHING: Sending wafer to Cleaner

Capacity: 1

Transitions:
- [WAFER_ARRIVES] EMPTY → OCCUPIED
- [CLEANER_READY] OCCUPIED → DISPATCHING
- [WAFER_DISPATCHED] DISPATCHING → EMPTY
```

### Cleaner State Machine

```
States:
- IDLE: Ready to accept wafer
- RECEIVING: Wafer being placed
- PROCESSING: Cleaning in progress (2500ms)
- DISPATCHING: Sending wafer to WTR2

Capacity: 1

Transitions:
- [WAFER_ARRIVES] IDLE → RECEIVING
- [WAFER_PLACED] RECEIVING → PROCESSING
- [PROCESSING_COMPLETE] PROCESSING → DISPATCHING
- [WAFER_DISPATCHED] DISPATCHING → IDLE

Guards:
- canAcceptWafer: Returns true only if IDLE
```

### Buffer3 State Machine

```
States:
- EMPTY: No wafer stored
- OCCUPIED: Holding 1 wafer
- DISPATCHING: Sending wafer to WTR1

Capacity: 1

Transitions:
- [WAFER_ARRIVES] EMPTY → OCCUPIED
- [WTR1_READY] OCCUPIED → DISPATCHING
- [WAFER_DISPATCHED] DISPATCHING → EMPTY
```

## XState JSON Format Example

### Polisher State Machine (XState Compatible)

```json
{
  "id": "polisher",
  "initial": "idle",
  "context": {
    "currentWaferId": null,
    "processStartTime": null
  },
  "states": {
    "idle": {
      "on": {
        "WAFER_ARRIVES": {
          "target": "receiving",
          "actions": ["acceptWafer"]
        }
      }
    },
    "receiving": {
      "on": {
        "WAFER_PLACED": {
          "target": "processing",
          "actions": ["startProcessing"]
        }
      }
    },
    "processing": {
      "after": {
        "3000": {
          "target": "dispatching",
          "actions": ["completeProcessing"]
        }
      }
    },
    "dispatching": {
      "on": {
        "WAFER_DISPATCHED": {
          "target": "idle",
          "actions": ["clearWafer"]
        }
      }
    }
  }
}
```

### Cleaner State Machine (XState Compatible)

```json
{
  "id": "cleaner",
  "initial": "idle",
  "context": {
    "currentWaferId": null,
    "processStartTime": null
  },
  "states": {
    "idle": {
      "on": {
        "WAFER_ARRIVES": {
          "target": "receiving",
          "actions": ["acceptWafer"]
        }
      }
    },
    "receiving": {
      "on": {
        "WAFER_PLACED": {
          "target": "processing",
          "actions": ["startProcessing"]
        }
      }
    },
    "processing": {
      "after": {
        "2500": {
          "target": "dispatching",
          "actions": ["completeProcessing"]
        }
      }
    },
    "dispatching": {
      "on": {
        "WAFER_DISPATCHED": {
          "target": "idle",
          "actions": ["clearWafer"]
        }
      }
    }
  }
}
```

## Orchestration Events

The EventBusOrchestrator coordinates these state machines through events:

### Event Flow Example

```
1. LoadPort sends: { type: "WAFER_DISPATCHED", waferId: 1, destination: "Buffer1" }
2. WTR1 receives and transits (600ms)
3. WTR1 sends: { type: "WAFER_ARRIVED", waferId: 1, location: "Buffer1" }
4. Buffer1 transitions: EMPTY → OCCUPIED
5. Buffer1 checks: Polisher.state === "idle"
6. Buffer1 sends: { type: "WAFER_READY", waferId: 1, from: "Buffer1", to: "Polisher" }
7. WTR2 picks up and transits
8. Polisher receives: { type: "WAFER_ARRIVES", waferId: 1 }
9. Polisher transitions: IDLE → RECEIVING → PROCESSING
10. After 3000ms: Polisher → DISPATCHING
11. Polisher sends: { type: "PROCESSING_COMPLETE", waferId: 1 }
...and so on
```

## Benefits of State Machine Approach

1. **Clear State Tracking**: Each station's state is explicitly defined
2. **Event-Driven**: Coordination through events, not direct method calls
3. **Testability**: Each state machine can be tested independently
4. **Visualization**: States can be visualized in UI
5. **Debugging**: Easy to see which state each station is in
6. **Extensibility**: Easy to add new states or transitions

## Implementation Strategy

### Current Implementation (Simplified)
- Uses direct async/await with SimpleTransfer
- No explicit state machines
- Good for demonstration and testing

### Future Implementation (Full State Machines)
- Create XState JSON for each station
- Use EventBusOrchestrator for coordination
- Each station runs as independent state machine
- Events trigger state transitions
- More realistic simulation of real hardware

## Example: Converting Current Code to State Machines

### Current Code
```csharp
await SimpleTransfer(wafer, "LoadPort", "WTR1");
await SimpleTransfer(wafer, "WTR1", "Buffer1");
```

### With State Machines
```csharp
// LoadPort state machine sends event
orchestrator.SendEvent("loadport", new Event {
    Type = "DISPATCH_WAFER",
    Payload = new { waferId = 1, destination = "Buffer1" }
});

// WTR1 state machine handles the event automatically
// WTR1 transitions: IDLE → TRANSITING_FORWARD
// After 600ms, WTR1 sends: WAFER_ARRIVED to Buffer1
// Buffer1 transitions: EMPTY → OCCUPIED
```

## Timing and Delays

```
LoadPort → WTR1:           600ms (transit)
WTR1 → Buffer1:            200ms (placement)
Buffer1 → WTR2:            600ms (transit)
WTR2 → Polisher:           600ms (transit)
Polisher (processing):    3000ms
Polisher → WTR2:           600ms (transit)
WTR2 → Buffer2:            200ms (placement)
Buffer2 → Cleaner:         direct transfer
Cleaner (processing):     2500ms
Cleaner → WTR2:            600ms (transit)
WTR2 → Buffer3:            200ms (placement)
Buffer3 → WTR1:            600ms (transit)
WTR1 → LoadPort:           600ms (transit)
```

## State Machine Guards

### Polisher.canAcceptWafer()
```csharp
bool CanAcceptWafer() {
    return currentState == "idle" && waferCount < maxCapacity;
}
```

### Buffer1.canDispatch()
```csharp
bool CanDispatch() {
    return currentState == "occupied" &&
           wtr2.IsIdle() &&
           polisher.CanAcceptWafer();
}
```

## Parallel Processing Coordination

The state machines enable **true pipeline parallelism**:

```
Time 0s:   Wafer 1 → Polisher (IDLE → PROCESSING)
Time 3s:   Wafer 1 → Polisher (PROCESSING → DISPATCHING)
           Wafer 2 → Polisher (IDLE → PROCESSING)  ← Parallel!
Time 3.5s: Wafer 1 → Cleaner (IDLE → PROCESSING)
Time 6s:   Wafer 2 → Polisher (PROCESSING → DISPATCHING)
           Wafer 3 → Polisher (IDLE → PROCESSING)
Time 6.5s: Wafer 1 → Cleaner (PROCESSING → DISPATCHING) ← Still parallel!
```

Each state machine operates independently, coordinated only through events.
