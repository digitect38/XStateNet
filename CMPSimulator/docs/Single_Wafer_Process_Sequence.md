# Single Wafer Process - Sequence Diagram

## Overview
This document shows the complete lifecycle of a single wafer (e.g., Wafer #1) through the CMP (Chemical Mechanical Planarization) system, from carrier arrival to completion.

## Sequence Diagram

```mermaid
sequenceDiagram
    participant User
    participant MainWindow
    participant Controller
    participant Scheduler
    participant LoadPort
    participant Carrier
    participant Wafer1 as Wafer #1
    participant R1 as Robot R1
    participant Polisher
    participant R2 as Robot R2
    participant Cleaner
    participant R3 as Robot R3
    participant Buffer

    %% Initialization Phase
    User->>MainWindow: Click Start Button
    MainWindow->>Controller: StartSimulation()

    %% Start all state machines
    Controller->>Scheduler: StartAsync()
    Controller->>LoadPort: StartAsync()
    Controller->>Carrier: StartAsync()
    Controller->>R1: StartAsync()
    Controller->>Polisher: StartAsync()
    Controller->>R2: StartAsync()
    Controller->>Cleaner: StartAsync()
    Controller->>R3: StartAsync()
    Controller->>Buffer: StartAsync()

    %% Initialize wafer E90 state
    Controller->>Wafer1: AcquireAsync()
    Note over Wafer1: E90 State: InCarrier

    %% E87 Carrier Workflow
    Controller->>Carrier: SendCarrierDetected()
    Note over Carrier: E87: NotPresent → WaitingForHost

    Controller->>Carrier: SendHostProceed()
    Note over Carrier: E87: WaitingForHost → Mapping

    Controller->>Carrier: SendMappingComplete()
    Note over Carrier: E87: Mapping → MappingVerification → ReadyToAccess

    Controller->>Carrier: SendStartAccess()
    Note over Carrier: E87: ReadyToAccess → InAccess

    Controller->>LoadPort: SendCarrierArrive(CARRIER_001)
    Note over LoadPort: E84: empty → carrierArrived

    Controller->>LoadPort: SendDock()
    Note over LoadPort: E84: carrierArrived → docked

    Controller->>LoadPort: SendStartProcessing()
    Note over LoadPort: E84: docked → processing

    %% Processing begins - Wafer #1 journey
    Note over Scheduler,Buffer: Wafer #1 Processing Journey

    %% P3: LoadPort → Polisher (via R1)
    Scheduler->>LoadPort: Check for pending wafers
    LoadPort-->>Scheduler: Wafer #1 available
    Scheduler->>Polisher: Check if idle
    Polisher-->>Scheduler: Idle (empty)
    Scheduler->>R1: TRANSFER(from=LoadPort, to=Polisher, wafer=1)

    Note over R1: idle → pickingUp
    R1->>LoadPort: Pick up Wafer #1
    Wafer1->>Wafer1: SelectForProcessAsync()
    Note over Wafer1: E90: InCarrier → NeedsProcessing

    Note over R1: pickingUp → holding
    Note over R1: holding → placingDown
    R1->>Polisher: Place Wafer #1
    Wafer1->>Wafer1: PlacedInProcessModuleAsync()
    Note over Wafer1: E90: NeedsProcessing → ReadyToProcess

    Wafer1->>Wafer1: StartProcessAsync()
    Note over Wafer1: E90: ReadyToProcess → InProcess.Polishing

    Note over R1: placingDown → returning → idle

    %% Polisher processes Wafer #1
    Polisher->>Polisher: Start processing (3000ms)
    Note over Polisher: empty → processing
    Note over Polisher,Wafer1: Polishing Sub-States:
    Note over Wafer1: Loading (100ms)
    Note over Wafer1: Chucking (200ms)
    Note over Wafer1: Polishing (2400ms)
    Note over Wafer1: Dechucking (200ms)
    Note over Wafer1: Unloading (100ms)

    Polisher->>Scheduler: POLISHER_DONE (wafer=1)
    Note over Polisher: processing → done

    %% P2: Polisher → Cleaner (via R2)
    Scheduler->>Cleaner: Check if idle
    Cleaner-->>Scheduler: Idle (empty)
    Scheduler->>R2: TRANSFER(from=Polisher, to=Cleaner, wafer=1)

    Note over R2: idle → pickingUp → holding
    R2->>Polisher: Pick up Wafer #1
    Wafer1->>Wafer1: CompletePolishingAsync()
    Note over Wafer1: E90: InProcess.Polishing → InProcess.Cleaning
    Note over Polisher: done → empty

    Note over R2: holding → placingDown
    R2->>Cleaner: Place Wafer #1
    Note over R2: placingDown → returning → idle

    %% Cleaner processes Wafer #1
    Cleaner->>Cleaner: Start processing (3000ms)
    Note over Cleaner: empty → processing
    Note over Wafer1: E90: Still in InProcess.Cleaning

    Cleaner->>Scheduler: CLEANER_DONE (wafer=1)
    Note over Cleaner: processing → done

    %% P1: Cleaner → Buffer (via R3)
    Scheduler->>Buffer: Check if idle
    Buffer-->>Scheduler: Idle (empty)
    Scheduler->>R3: TRANSFER(from=Cleaner, to=Buffer, wafer=1)

    Note over R3: idle → pickingUp → holding
    R3->>Cleaner: Pick up Wafer #1
    Wafer1->>Wafer1: CompleteCleaningAsync()
    Note over Wafer1: E90: InProcess.Cleaning → Processed
    Note over Cleaner: done → empty

    Note over R3: holding → placingDown
    R3->>Buffer: Place Wafer #1
    Note over Buffer: empty → occupied
    Note over R3: placingDown → returning → idle

    %% P4: Buffer → LoadPort (via R1)
    Scheduler->>LoadPort: Check if ready
    LoadPort-->>Scheduler: Ready
    Scheduler->>R1: TRANSFER(from=Buffer, to=LoadPort, wafer=1)

    Note over R1: idle → pickingUp → holding
    R1->>Buffer: Pick up Wafer #1
    Note over Buffer: occupied → empty

    Note over R1: holding → placingDown
    R1->>LoadPort: Place Wafer #1
    Wafer1->>Wafer1: PlacedInCarrierAsync()
    Note over Wafer1: E90: Processed → Complete
    Note over Wafer1: IsCompleted = true
    Note over R1: placingDown → returning → idle

    %% Notify completion
    Scheduler->>Scheduler: Add to Completed list
    Scheduler->>Carrier: WAFER_COMPLETED (wafer=1)

    Note over Wafer1: ✅ Wafer #1 Complete!
    Note over LoadPort: Wafer #1 back in carrier
```

## Key States and Transitions

### E90 Substrate (Wafer) States
1. **InCarrier** - Wafer is in the FOUP carrier
2. **NeedsProcessing** - Wafer selected for processing, picked up by R1
3. **ReadyToProcess** - Wafer placed in Polisher, ready to start
4. **InProcess.Polishing** - Wafer being polished (with sub-states)
   - Loading → Chucking → Polishing → Dechucking → Unloading
5. **InProcess.Cleaning** - Wafer being cleaned
6. **Processed** - Wafer processing complete
7. **Complete** - Wafer returned to carrier

### E87 Carrier States
1. **NotPresent** - No carrier at load port
2. **WaitingForHost** - Carrier detected, waiting for host approval
3. **Mapping** - Mapping slot contents
4. **MappingVerification** - Verifying slot map
5. **ReadyToAccess** - Ready for wafer access
6. **InAccess** - Wafers being accessed/processed
7. **Complete** - All wafers processed
8. **CarrierOut** - Carrier removed

### E84 LoadPort States
1. **empty** - No carrier present
2. **carrierArrived** - Carrier arrived at load port
3. **docked** - Carrier docked and secured
4. **processing** - Wafers being processed
5. **unloading** - Carrier being unloaded

### Robot States (R1, R2, R3)
1. **idle** - Robot waiting for command
2. **pickingUp** - Robot picking up wafer
3. **holding** - Robot holding wafer
4. **placingDown** - Robot placing wafer
5. **returning** - Robot returning to idle position

### Station States (Polisher, Cleaner, Buffer)
1. **empty** - No wafer in station
2. **processing** - Processing wafer
3. **done** - Processing complete
4. **occupied** - Station holding wafer (Buffer only)

## Priority Order (Forward Priority Scheduler)

The scheduler processes transfers in this priority order:

1. **P1 (Highest):** Cleaner → Buffer (via R3)
2. **P2:** Polisher → Cleaner (via R2)
3. **P3:** LoadPort → Polisher (via R1)
4. **P4 (Lowest):** Buffer → LoadPort (via R1)

## Timing (Default Configuration)

- **Transfer Time (R1, R2, R3):** 300ms
- **Polishing Time:** 3000ms
  - Loading: 100ms
  - Chucking: 200ms
  - Polishing: 2400ms
  - Dechucking: 200ms
  - Unloading: 100ms
- **Cleaning Time:** 3000ms
- **Total Time per Wafer:** ~7200ms (ideal pipeline)

## Event-Driven Architecture

The system uses **EventBusOrchestrator** (Pub/Sub pattern) for all inter-machine communication:

- Stations report completion → Scheduler
- Scheduler commands robots → Robots
- State machines publish state changes → Controller → UI
- No polling - all updates are event-driven
- Deferred sends ensure proper event ordering

## File References

- **Controller:** `CMPSimulator/Controllers/OrchestratedForwardPriorityController.cs`
- **Scheduler:** `CMPSimulator/StateMachines/DeclarativeSchedulerMachine.cs`
- **Carrier:** `CMPSimulator/StateMachines/CarrierMachine.cs`
- **Wafer:** `CMPSimulator/StateMachines/WaferMachine.cs`
- **LoadPort:** `CMPSimulator/StateMachines/LoadPortMachine.cs`
- **Robots:** `CMPSimulator/StateMachines/RobotMachine.cs`
- **Polisher:** `CMPSimulator/StateMachines/PolisherMachine.cs`
- **Cleaner:** `CMPSimulator/StateMachines/CleanerMachine.cs`
- **Buffer:** `CMPSimulator/StateMachines/BufferMachine.cs`
