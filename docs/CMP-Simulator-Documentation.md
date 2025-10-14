# CMP Tool Simulator Documentation

## Overview

The CMP (Chemical Mechanical Planarization) Tool Simulator is a comprehensive semiconductor manufacturing simulator built with XStateNet. It demonstrates event-driven state machine orchestration, SEMI standards integration (E87/E90), and declarative scheduling for wafer processing automation.

**Version:** 1.4.0
**Latest Tag:** v1.4.0-ui-blue-outline
**Platform:** WPF (.NET 8.0)

---

## Table of Contents

1. [Architecture](#architecture)
2. [Features](#features)
3. [Getting Started](#getting-started)
4. [User Interface](#user-interface)
5. [State Machines](#state-machines)
6. [Scheduling System](#scheduling-system)
7. [SEMI Standards](#semi-standards)
8. [Configuration](#configuration)
9. [Troubleshooting](#troubleshooting)
10. [Development Guide](#development-guide)

---

## Architecture

### System Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    CMP Tool Simulator                        │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  ┌────────────────────────────────────────────────────────┐ │
│  │                    WPF UI Layer                        │ │
│  │  - MainWindow (Control Panel, Visualization)          │ │
│  │  - StateTreeControl (Real-time state hierarchy)       │ │
│  │  - CMPSystemControl (Station canvas with zoom/pan)    │ │
│  │  - Property Editor (Settings and geometry)            │ │
│  └────────────────────────────────────────────────────────┘ │
│                            │                                 │
│                            ▼                                 │
│  ┌────────────────────────────────────────────────────────┐ │
│  │              Controller Layer                          │ │
│  │  - OrchestratedForwardPriorityController              │ │
│  │  - Settings Manager (Persistence)                     │ │
│  │  - Station Editor (Drag & drop geometry)              │ │
│  └────────────────────────────────────────────────────────┘ │
│                            │                                 │
│                            ▼                                 │
│  ┌────────────────────────────────────────────────────────┐ │
│  │          State Machine Orchestration Layer             │ │
│  │                                                         │ │
│  │  ┌──────────────────────────────────────────────────┐ │ │
│  │  │    DeclarativeSchedulerMachine (JSON DSL)        │ │ │
│  │  │    - Priority-based rule execution               │ │ │
│  │  │    - Event-driven decision making                │ │ │
│  │  └──────────────────────────────────────────────────┘ │ │
│  │                            │                            │ │
│  │  ┌──────────────────────────────────────────────────┐ │ │
│  │  │         EventBusOrchestrator                     │ │ │
│  │  │         - Pub/Sub event distribution             │ │ │
│  │  │         - State machine coordination             │ │ │
│  │  │         - Deferred send execution                │ │ │
│  │  └──────────────────────────────────────────────────┘ │ │
│  │                            │                            │ │
│  │  ┌──────────────────────────────────────────────────┐ │ │
│  │  │           Station State Machines                 │ │ │
│  │  │  - PolisherMachine (5 sub-states)                │ │ │
│  │  │  - CleanerMachine                                │ │ │
│  │  │  - BufferMachine                                 │ │ │
│  │  │  - LoadPortMachine (E87)                         │ │ │
│  │  └──────────────────────────────────────────────────┘ │ │
│  │                                                         │ │
│  │  ┌──────────────────────────────────────────────────┐ │ │
│  │  │            Robot State Machines                  │ │ │
│  │  │  - R1Machine (LoadPort ↔ Polisher)              │ │ │
│  │  │  - R2Machine (Polisher ↔ Cleaner)               │ │ │
│  │  │  - R3Machine (Cleaner ↔ Buffer)                 │ │ │
│  │  └──────────────────────────────────────────────────┘ │ │
│  │                                                         │ │
│  │  ┌──────────────────────────────────────────────────┐ │ │
│  │  │       E90 Wafer Tracking Machines                │ │ │
│  │  │  - WaferMachine (per wafer)                      │ │ │
│  │  │  - Hierarchical states (InProcess → Polishing)   │ │ │
│  │  │  - 3-level state nesting support                 │ │ │
│  │  └──────────────────────────────────────────────────┘ │ │
│  │                                                         │ │
│  │  ┌──────────────────────────────────────────────────┐ │ │
│  │  │         Carrier State Machines                   │ │ │
│  │  │  - CarrierMachine (E87 lifecycle)                │ │ │
│  │  │  - CarrierManager (E87/E90 integration)          │ │ │
│  │  └──────────────────────────────────────────────────┘ │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                               │
└─────────────────────────────────────────────────────────────┘
```

### Design Patterns

1. **Event-Driven Architecture**: All components communicate via Pub/Sub events
2. **State Machine Pattern**: Every entity (station, robot, wafer) has its own state machine
3. **Hierarchical State Machines**: Support for nested states (3 levels deep)
4. **Declarative Configuration**: JSON-based scheduling rules
5. **MVVM Pattern**: WPF data binding for reactive UI updates
6. **Factory Pattern**: State machine creation via ExtendedPureStateMachineFactory

---

## Features

### Core Features

✅ **XState-based State Machines**
- Pure state machines with guards and services
- Hierarchical state nesting (3 levels)
- Event-driven transitions
- Invoked services for async operations

✅ **Declarative Scheduling DSL**
- JSON-based rule definition
- Priority-based execution (P1 > P2 > P3 > P4)
- Dynamic rule modification
- No code recompilation required

✅ **SEMI Standards Integration**
- E87 Carrier Management
- E90 Substrate Tracking
- Industry-standard state lifecycles

✅ **Real-time Visualization**
- Interactive station canvas (drag & drop)
- Live wafer position tracking
- State tree hierarchy view
- Animated wafer transfers

✅ **Advanced UI Features**
- Zoom and pan (Ctrl+Scroll, Ctrl+Drag)
- Grid display modes (dots, lines, both)
- Property editor with live updates
- Settings persistence (JSON)
- Real-time statistics dashboard

### v1.4.0 Features

🆕 **Blue Outline State Tree**
- Replaced yellow fill with blue outline (2px border)
- Improved color contrast for accessibility
- Cornflower blue and light sky blue badges
- No yellow with white font anywhere in UI

### v1.3.0 Features

🆕 **Scheduler DSL and Engine**
- DeclarativeSchedulerMachine with JSON rules
- SchedulingRuleEngine with expression evaluation
- CMP_Scheduling_Rules.json example
- Migration guide from hardcoded scheduling

### Historical Features

📦 **v1.2.0: E87/E90 Integration**
- LoadPortMachine, CarrierMachine
- WaferMachine with hierarchical states
- CarrierManager service layer

📦 **v1.1.0: Pub/Sub Architecture**
- EventBusOrchestrator
- Eliminated UI polling
- Direct state change notifications

---

## Getting Started

### Prerequisites

- **.NET 8.0 SDK** or higher
- **Visual Studio 2022** or JetBrains Rider
- **Windows 10/11** (WPF requirement)

### Installation

1. **Clone the repository:**
```bash
git clone https://github.com/digitect38/XStateNet.git
cd XStateNet
```

2. **Restore dependencies:**
```bash
dotnet restore
```

3. **Build the solution:**
```bash
dotnet build
```

4. **Run the simulator:**
```bash
cd CMPSimulator
dotnet run
```

Or open `XStateNet.sln` in Visual Studio and press F5.

### First Run

1. **Start the simulation:**
   - Click the **▶ Start** button in the control panel

2. **Observe the process:**
   - Wafers move from LoadPort → Polisher → Cleaner → Buffer → LoadPort
   - State tree shows real-time state changes
   - Statistics update every 100ms

3. **Interact with the UI:**
   - **Zoom**: Ctrl+Scroll wheel
   - **Pan**: Ctrl+Drag with mouse
   - **Select Station**: Click on any station to view properties
   - **Edit Mode**: Enable checkbox to drag stations

4. **Stop and Reset:**
   - Click **⏸ Pause** to stop
   - Click **↻ Reset** to restart simulation

---

## User Interface

### Main Window Layout

```
┌────────────────────────────────────────────────────────────┐
│  Control Panel (Top Bar)                                   │
│  ▶ Start | ⏭ Step | ⏸ Pause | ↻ Reset | ✏ Edit Mode      │
│  Statistics: Elapsed, Completed, Efficiency, Throughput    │
├────────────────────────────────────────────────────────────┤
│                     │                                       │
│  Simulation Canvas  │  Right Panel (Tabs)                 │
│  (Zoom/Pan enabled) │                                      │
│                     │  1. Property Tab                     │
│  ┌─LoadPort─┐      │     - Selected object info           │
│  │ 🟢🟢🟢🟢🟢 │      │     - Global timing settings         │
│  │ 🟢🟢🟢🟢🟢 │      │     - Real-time statistics           │
│  │ 🟢🟢🟢🟢🟢 │      │                                      │
│  │ 🟢🟢🟢🟢🟢 │      │  2. State Tree Tab                   │
│  │ 🟢🟢🟢🟢🟢 │      │     - Hierarchical state view        │
│  └───────────┘      │     - Active state highlighting      │
│       ↓             │     - Blue outline for in-process    │
│   ┌──R1──┐          │                                      │
│   │  🟢  │          │  3. Log Tab                          │
│   └──────┘          │     - Event log                      │
│       ↓             │     - State transitions              │
│  ┌─Polisher─┐       │     - Debug messages                 │
│  │    🟢    │       │                                      │
│  │  3.2s    │       │                                      │
│  └──────────┘       │                                      │
│       ↓             │                                      │
│   (and so on...)    │                                      │
│                     │                                       │
└────────────────────────────────────────────────────────────┘
```

### Control Panel

#### Buttons

- **▶ Start**: Begin simulation
- **⏭ Step**: Execute one step (SYNC mode only)
- **⏸ Pause**: Pause simulation
- **↻ Reset**: Reset to initial state
- **✏ Edit Mode**: Enable station drag & drop

#### Execution Modes

- **Async**: Continuous automatic execution (default)
- **Sync**: Manual step-by-step execution (debugging)

### Simulation Canvas

#### Features

- **Stations**: LoadPort, R1, Polisher, R2, Cleaner, R3, Buffer
- **Wafers**: Colored circles with IDs (1-10)
- **Grid**: Configurable dots/lines (8px spacing)
- **Zoom**: Ctrl+Scroll (0.1x - 10x)
- **Pan**: Ctrl+Drag to move viewport

#### Station States

- **Gray**: Idle/Empty
- **Yellow**: Loading/Processing
- **Green**: Ready/Done
- **Red**: Error

### Property Panel

#### Selected Object Info

Shows details about the selected station:
- Type (Station, Robot, LoadPort, etc.)
- Current state
- Geometry (X, Y, Width, Height)
- Wafer count (for LoadPort)

#### Global Timing Settings

Configure operation durations (milliseconds):
- R1 Transfer: 1000ms (default)
- Polisher: 5000ms (default)
- R2 Transfer: 1000ms
- Cleaner: 3000ms (default)
- R3 Transfer: 1000ms
- Buffer Hold: 0ms
- R1 Return: 1000ms

**Note:** Settings can only be changed when simulation is stopped.

#### Real-time Statistics

- **Elapsed Time**: Total simulation time (seconds)
- **Completed**: Number of wafers processed
- **Pending**: Remaining wafers
- **Throughput**: Wafers per second
- **Theoretical Min**: Optimal completion time
- **Efficiency**: Actual vs. theoretical performance

### State Tree Tab

Hierarchical view of all state machines:

```
📦 CARRIER_001
  └─ State: atLoadPort

💿 WAFER_W1
  └─ InProcess (blue outline when active)
      └─ Polishing
          └─ Loading (green when active)

⚙️ polisher
  └─ processing
      └─ Loading
      └─ Chucking
      └─ Polishing
      └─ Dechucking
      └─ Unloading
```

**Features:**
- Active states highlighted in **green**
- In-process wafers have **blue outline** (2px border)
- State badges color-coded by type
- Expandable/collapsible tree

### Log Tab

Real-time event log:
```
[2025-10-15 02:00:00] ✓ All state machines started
[2025-10-15 02:00:01] [Wafer 1] E90 State → InCarrier
[2025-10-15 02:00:02] [R1] PICK from LoadPort (wafer 1)
[2025-10-15 02:00:03] [polisher] State: processing (wafer 1)
[2025-10-15 02:00:04] [polisher] Sub-state: Loading (wafer 1)
```

**Features:**
- Copy/paste support (Ctrl+C)
- Select all (Ctrl+A)
- Auto-scroll to latest
- Searchable text

---

## State Machines

### Station State Machines

#### PolisherMachine

**File:** `CMPSimulator/StateMachines/PolisherMachine.cs`

**States:**
```
empty → processing → done → idle → empty
         ↓
    5 sub-states:
    Loading → Chucking → Polishing → Dechucking → Unloading
```

**Sub-state Timing:**
Each sub-state gets 1/5 of total polishing time.

**Events:**
- `PLACE`: Receive wafer, enter processing
- `PICK`: Release wafer, enter idle
- Auto-transition after processing complete

**Integration:**
- Triggers WaferMachine sub-state transitions
- Reports status to scheduler via `STATION_STATUS`

#### CleanerMachine

**File:** `CMPSimulator/StateMachines/CleanerMachine.cs`

**States:**
```
empty → processing → done → idle → empty
```

**Events:**
- `PLACE`: Start cleaning
- `PICK`: Release cleaned wafer

#### BufferMachine

**File:** `CMPSimulator/StateMachines/BufferMachine.cs`

**States:**
```
empty → holding → idle → empty
```

**Features:**
- Temporary storage for completed wafers
- Configurable hold time

### Robot State Machines

#### RobotMachine

**File:** `CMPSimulator/StateMachines/RobotMachine.cs`

**States:**
```
idle → picking → transferring → placing → idle
```

**Events:**
- `PICK`: Start pick operation
- `PLACE`: Start place operation

**Properties:**
- `HeldWafer`: Currently held wafer ID (null if empty)
- `CurrentState`: State machine state
- `TransferTime`: Duration of transfer (configurable)

### E87 LoadPort Machine

#### LoadPortMachine

**File:** `CMPSimulator/StateMachines/LoadPortMachine.cs`

**States (SEMI E87):**
```
empty → carrierArrived → docked → processing →
unloading → empty
```

**Events:**
- `CARRIER_ARRIVE`: Carrier docked
- `DOCK`: E84 handshake complete
- `START_PROCESSING`: Begin wafer processing
- `COMPLETE`: All wafers processed
- `UNDOCK`: Release carrier

### E90 Wafer Tracking

#### WaferMachine

**File:** `CMPSimulator/StateMachines/WaferMachine.cs`

**States (SEMI E90):**
```
WaitingForHost → InCarrier → NeedsProcessing →
ReadyToProcess → InProcess → Processed → Complete
                      ↓
                 InProcess:
                   Polishing (5 sub-states)
                   Cleaning
```

**Hierarchical Structure:**
- **Level 1**: E90 lifecycle states
- **Level 2**: Processing phases (Polishing, Cleaning)
- **Level 3**: Polishing sub-steps (Loading, Chucking, etc.)

**Events:**
- `ACQUIRE`: Wafer placed in carrier
- `SELECT_FOR_PROCESS`: Chosen for processing
- `PLACED_IN_PROCESS_MODULE`: Loaded to station
- `START_PROCESS`: Begin processing
- `POLISHING_COMPLETE`: Polishing done
- `CLEANING_COMPLETE`: Cleaning done
- `PLACED_IN_CARRIER`: Returned to carrier

**Timing Tracking:**
- `AcquiredTime`: When wafer entered system
- `ProcessStartTime`: When processing began
- `ProcessEndTime`: When processing completed
- `ProcessingTime`: Total processing duration

---

## Scheduling System

### Declarative Scheduler

See [Scheduler DSL Documentation](./Scheduler-DSL-Documentation.md) for complete details.

**Quick Overview:**

1. **Rules File**: `CMPSimulator/SchedulingRules/CMP_Scheduling_Rules.json`
2. **Engine**: `SchedulingRuleEngine` evaluates conditions
3. **Machine**: `DeclarativeSchedulerMachine` executes actions

**Forward Priority Rules:**
- **P1**: Cleaner → Buffer (highest priority)
- **P2**: Polisher → Cleaner
- **P3**: LoadPort → Polisher
- **P4**: Buffer → LoadPort (lowest priority)

**Example Rule:**
```json
{
  "id": "P1_CleanerToBuffer",
  "priority": 1,
  "conditions": [
    "cleaner.state == 'done'",
    "r3.state == 'idle'",
    "buffer.state == 'empty'"
  ],
  "actions": [
    {
      "type": "PICK_PLACE",
      "robot": "R3",
      "from": "cleaner",
      "to": "buffer"
    }
  ]
}
```

---

## SEMI Standards

### E87 Carrier Management

**Implemented in:**
- `LoadPortMachine.cs`
- `CarrierMachine.cs`
- `CarrierManager.cs`

**Key Concepts:**
- **LoadPort**: Physical docking station with E84 handshake
- **Carrier**: FOUP container holding wafers
- **States**: NotPresent → Mapping → ReadyToAccess → InAccess → Complete

### E90 Substrate Tracking

**Implemented in:**
- `WaferMachine.cs`

**Key Concepts:**
- **Substrate**: Individual wafer with unique ID
- **Lifecycle**: WaitingForHost → InCarrier → ... → Complete
- **Hierarchical States**: Support for nested processing states

**State Mapping:**
| E90 State | Description | UI Indicator |
|-----------|-------------|--------------|
| WaitingForHost | Not yet acquired | Gray |
| InCarrier | In FOUP | Light blue |
| NeedsProcessing | Selected for processing | Orange |
| InProcess | Currently processing | Green (hierarchical) |
| Processed | Processing complete | Dark green |
| Complete | Returned to carrier | Final state |

---

## Configuration

### Settings File

**Location:** `CMPSimulator/appsettings.json`

**Structure:**
```json
{
  "SimulatorSettings": {
    "R1TransferTime": 1000,
    "PolisherTime": 5000,
    "R2TransferTime": 1000,
    "CleanerTime": 3000,
    "R3TransferTime": 1000,
    "BufferHoldTime": 0,
    "LoadPortReturnTime": 1000,
    "InitialWaferCount": 10,
    "StationGeometry": {
      "LoadPort": { "Left": 46, "Top": 256, "Width": 108, "Height": 108 },
      "R1": { "Left": 250, "Top": 270, "Width": 80, "Height": 80 },
      "Polisher": { "Left": 420, "Top": 250, "Width": 120, "Height": 120 },
      "R2": { "Left": 590, "Top": 270, "Width": 80, "Height": 80 },
      "Cleaner": { "Left": 760, "Top": 250, "Width": 120, "Height": 120 },
      "R3": { "Left": 590, "Top": 460, "Width": 80, "Height": 80 },
      "Buffer": { "Left": 440, "Top": 460, "Width": 80, "Height": 80 }
    }
  }
}
```

### Runtime Settings

Settings can be modified in the Property Editor panel:
1. Stop the simulation (↻ Reset)
2. Modify timing values
3. Click **Apply Settings**
4. Restart simulation (▶ Start)

**Note:** Geometry changes require Edit Mode (✏ checkbox).

---

## Troubleshooting

### Common Issues

#### 1. Wafers Not Moving

**Symptoms:** Wafers stay at LoadPort after starting

**Causes:**
- Scheduler not receiving events
- Station state machines not started
- Robot in wrong state

**Solution:**
```csharp
// Check log for state machine startup messages
✓ All state machines started (including E90 substrate tracking)
✓ DeclarativeScheduler started and ready to receive events
```

If missing, check `OrchestratedForwardPriorityController.StartSimulation()`.

#### 2. State Tree Not Updating

**Symptoms:** State tree shows outdated states

**Causes:**
- Event subscription not working
- UI dispatcher not invoked
- StateChanged event not fired

**Solution:**
Check `MainWindow.xaml.cs` event handlers:
```csharp
private void Controller_StationStatusChanged(object? sender, EventArgs e)
{
    // Update state tree
    UpdateStateTree();
}
```

#### 3. Simulation Hangs

**Symptoms:** Simulation stops responding mid-run

**Causes:**
- Deadlock in state machine
- Infinite loop in rule evaluation
- UI thread blocked

**Solution:**
1. Check for circular rule dependencies
2. Enable logging in DeclarativeSchedulerMachine
3. Use SYNC mode to step through execution

#### 4. Settings Not Persisting

**Symptoms:** Settings reset after restart

**Cause:** appsettings.json not being saved

**Solution:**
```csharp
// In SettingsManager.cs
public static void SaveSettings(SimulatorSettings settings)
{
    var json = JsonConvert.SerializeObject(new { SimulatorSettings = settings }, Formatting.Indented);
    File.WriteAllText("appsettings.json", json);
}
```

Ensure write permissions in application directory.

#### 5. Wafer Colors Hard to Read

**Symptoms:** Yellow wafer text unreadable

**Solution:**
Already fixed in v1.4.0! Yellow hues (45-75°) now use darker values (0.45) for better contrast.

If issue persists, adjust in `OrchestratedForwardPriorityController.cs`:
```csharp
if (hue >= 45 && hue <= 75)
{
    value = 0.40; // Even darker
}
```

---

## Development Guide

### Adding a New Station

1. **Create State Machine:**
```csharp
public class MyStationMachine
{
    public MyStationMachine(
        string stationName,
        EventBusOrchestrator orchestrator,
        int processingTime,
        Action<string> logger)
    {
        // Define XState JSON
        var definition = $$"""
        {
            "id": "{{stationName}}",
            "initial": "empty",
            "states": {
                "empty": { "on": { "PLACE": "processing" } },
                "processing": {
                    "invoke": {
                        "src": "processWafer",
                        "onDone": "done"
                    }
                },
                "done": { "on": { "PICK": "empty" } }
            }
        }
        """;

        // Create machine
        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: stationName,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            services: services
        );
    }
}
```

2. **Add to Controller:**
```csharp
// In OrchestratedForwardPriorityController.cs
private MyStationMachine? _myStation;

private void InitializeStateMachines()
{
    _myStation = new MyStationMachine("MyStation", _orchestrator, 2000, Log);
}
```

3. **Create UI Control:**
```csharp
// In MainWindow.xaml.cs
var myStationControl = new ProcessStationControl
{
    StationName = "MyStation",
    StatusText = "Idle",
    BackgroundColor = Brushes.LightBlue,
    Width = 120,
    Height = 120
};
CMPSystem.AddStation(myStationControl, 300, 400);
```

### Adding a Scheduling Rule

1. **Open** `CMP_Scheduling_Rules.json`

2. **Add Rule:**
```json
{
  "id": "P5_MyCustomRule",
  "priority": 5,
  "description": "My custom scheduling logic",
  "conditions": [
    "mystation.state == 'done'",
    "r1.state == 'idle'"
  ],
  "actions": [
    {
      "type": "PICK_PLACE",
      "robot": "R1",
      "from": "mystation",
      "to": "buffer"
    }
  ]
}
```

3. **Restart Simulator** (rules loaded at startup)

### Extending WaferMachine States

To add custom E90 states:

```csharp
// In WaferMachine.cs definition
"states": {
    "WaitingForHost": { "on": { "ACQUIRE": "InCarrier" } },
    "InCarrier": { "on": { "SELECT_FOR_PROCESS": "NeedsProcessing" } },
    "NeedsProcessing": {
        "on": {
            "PLACED_IN_ALIGNER": "Aligning",
            "PLACED_IN_INSPECTION": "Inspecting"  // NEW
        }
    },
    "Inspecting": {  // NEW STATE
        "entry": "notifyStateChange",
        "on": {
            "INSPECTION_PASS": "ReadyToProcess",
            "INSPECTION_FAIL": "Rejected"
        }
    }
}
```

Add corresponding API methods:
```csharp
public async Task<EventResult> PlacedInInspectionAsync()
{
    return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PLACED_IN_INSPECTION", null);
}
```

---

## Performance Optimization

### Tips for Large-Scale Simulations

1. **Limit Wafer Count:**
   - Recommended: 10-25 wafers
   - Maximum tested: 50 wafers

2. **Reduce Logging:**
```csharp
// Disable verbose logging in production
if (DEBUG_MODE)
{
    Log($"[Wafer {waferId}] State changed to {newState}");
}
```

3. **Optimize UI Updates:**
```csharp
// Batch updates using DispatcherTimer
private void UpdateProgress(object? state)
{
    Application.Current?.Dispatcher.BeginInvoke(() =>
    {
        // Update all statistics at once
    }, DispatcherPriority.Background);
}
```

4. **Use Background Priority for Non-Critical Updates:**
```csharp
Dispatcher.BeginInvoke(action, DispatcherPriority.Background);
```

---

## References

### Internal Documentation

- [Scheduler DSL Documentation](./Scheduler-DSL-Documentation.md)
- [XStateNet Core Documentation](./XStateNet-Documentation.md) *(Coming soon)*
- [EventBusOrchestrator Guide](./EventBusOrchestrator-Guide.md) *(Coming soon)*

### External Resources

- [XState Documentation](https://xstate.js.org/docs/)
- [SEMI Standards](https://www.semi.org/)
- [WPF Documentation](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/)

---

## Changelog

### v1.4.0 (2025-10-15)
- Blue outline state tree (replaced yellow fill)
- Improved color contrast for accessibility
- Darker yellow hues for better text readability

### v1.3.0 (2025-10-15)
- Declarative Scheduling DSL and Engine
- JSON-based rule configuration
- SchedulingRuleEngine implementation

### v1.2.0 (2025-10-14)
- E87 Carrier Management integration
- E90 Substrate Tracking with hierarchical states
- CarrierManager service layer

### v1.1.0 (2025-10-13)
- EventBusOrchestrator Pub/Sub pattern
- Eliminated UI polling
- Real-time state change notifications

---

## License

Copyright (c) 2025 XStateNet Project
Licensed under MIT License

---

## Support

For questions and support:
- **GitHub Issues:** https://github.com/digitect38/XStateNet/issues
- **Documentation:** https://docs.xstatenet.dev
- **Email:** support@xstatenet.dev

---

**Last Updated:** October 15, 2025
**Version:** 1.4.0
**Simulator Build:** CMPSimulator.exe (WPF .NET 8.0)
