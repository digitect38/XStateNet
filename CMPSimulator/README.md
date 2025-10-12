# CMP Tool Simulator

A WPF-based visual simulator for Chemical Mechanical Polishing (CMP) tool operations, demonstrating wafer flow through processing stations with XStateNet orchestration.

## Overview

This simulator models a CMP tool with the following characteristics:

### Wafer Journey
```
LoadPort → WTR1 → Polisher → WTR2 → Cleaner → WTR2 → WTR1 → LoadPort
```

### Components

1. **LoadPort**: Holds 25 wafers waiting for processing
2. **WTR1 (Wafer Transfer Robot 1)**: Transfers wafers between LoadPort and Polisher
3. **Polisher**: Processes one wafer at a time (polishing operation)
4. **WTR2 (Wafer Transfer Robot 2)**: Transfers wafers between Polisher and Cleaner
5. **Cleaner**: Processes one wafer at a time (cleaning operation)

### Features

- **25 Unique Wafers**: Each wafer has a unique ID (1-25) and distinct color for visual identification
- **True Pipeline Parallelism**:
  - **Polisher and Cleaner work simultaneously on different wafers**
  - As soon as a wafer leaves the Polisher, the next wafer can enter
  - While wafer N is being cleaned, wafer N+1 can be polished in parallel
  - Maximizes throughput by keeping both processing stations busy
- **Visual Representation**:
  - Wafers: Colored circles with ID numbers
  - Stations: Rectangles showing station status
  - Smooth animations for wafer movement (800ms transitions)
- **Real-time Statistics**:
  - Runtime, completion count, throughput metrics
  - Polisher and Cleaner processing counters
  - Updates every 5 seconds
- **Enhanced Logging**:
  - Tracks wafer movement with station occupancy info
  - Shows when wafers are waiting for occupied stations
  - Displays processing start/completion events
- **Interactive Controls**: Start, Pause, and Reset simulation

## Architecture

### State Machine Design

Each station is modeled with XStateNet state machines:

- **LoadPort**: Manages wafer loading/unloading with capacity tracking
- **Polisher**: Idle → Processing → Completed states
- **Cleaner**: Idle → Processing → Completed states
- **WTR (Robots)**: Idle → Picking → Moving → Placing → Completed states

### Orchestration & Pipeline Parallelism

The `CMPToolController` implements **true pipeline parallelism**:

#### Key Design Decisions

1. **Independent Wafer Tasks**: Each wafer runs as an independent `async Task`, allowing multiple wafers to be at different pipeline stages simultaneously

2. **Station Capacity Management**:
   - `StationPosition.CanAcceptWafer()` ensures capacity constraints (Polisher: 1, Cleaner: 1, LoadPort: 25)
   - `MoveWafer()` waits asynchronously until destination station is available

3. **Staggered Launch**: Wafers are launched every 3.5 seconds to prevent initial congestion and allow the pipeline to fill gradually

4. **Parallel Processing Example**:
   ```
   Time T:   Wafer 1 in Cleaner  | Wafer 2 in Polisher  | Wafer 3 in WTR1
   Time T+1: Wafer 1 returning   | Wafer 2 in Polisher  | Wafer 3 in Polisher
   ```
   This demonstrates how Polisher and Cleaner can work on different wafers concurrently

5. **Thread-Safe Counters**: Uses `Interlocked.Increment` for `_polisherThroughput` and `_cleanerThroughput` to safely track processing in parallel tasks

## Running the Simulator

### Build
```bash
cd C:\Develop25\XStateNet\CMPSimulator
dotnet build
```

### Run
```bash
dotnet run
```

Or run from Visual Studio by setting CMPSimulator as startup project.

## Usage

1. **Start**: Click "▶ Start" to begin simulation
   - Wafers launch every 3.5 seconds
   - Watch the pipeline fill up gradually
   - **Observe parallel processing**: When Wafer N leaves Polisher for Cleaner, Wafer N+1 immediately enters Polisher
   - Statistics update every 5 seconds in the top-right corner

2. **Pause**: Click "⏸ Pause" to stop wafer processing
   - Current wafers remain in their positions
   - Can resume by clicking Start again

3. **Reset**: Click "↻ Reset" to return all wafers to LoadPort
   - Resets all counters and station states
   - Clears the log

## Observing Pipeline Parallelism

To see the parallel processing in action, watch the logs:

```
[HH:mm:ss] Wafer 1 polishing started
[HH:mm:ss] Wafer 1 polishing completed ✓
[HH:mm:ss] Wafer 1: Polisher → WTR2 [Wafer 1]
[HH:mm:ss] Wafer 2: WTR1 → Polisher [Wafer 2]    ← New wafer enters Polisher
[HH:mm:ss] Wafer 2 polishing started              ← While Wafer 1 is moving to Cleaner
[HH:mm:ss] Wafer 1: WTR2 → Cleaner [Wafer 1]
[HH:mm:ss] Wafer 1 cleaning started               ← Both stations working!
```

**Key Observation**: After Wafer 1 completes polishing and moves to the Cleaner, Wafer 2 immediately starts polishing. This demonstrates true pipeline parallelism where both Polisher and Cleaner are actively processing different wafers.

## Visual Guide

### Station Colors

- **LoadPort**: White background
- **Polisher**: Light yellow background (indicates processing station)
- **Cleaner**: Light cyan background (indicates processing station)
- **WTR1/WTR2**: White background (robots)

### Wafer Colors

Each of the 25 wafers has a unique color generated using HSV color space to ensure visual distinction.

## Implementation Details

### Key Components

- **Models/Wafer.cs**: Wafer data model with position and color properties
- **Models/StationPosition.cs**: Station layout and capacity management
- **Controllers/CMPToolController.cs**: Main simulation controller
- **StateMachines/CMPStationMachines.cs**: XStateNet state machine definitions
- **MainWindow.xaml**: WPF UI layout
- **MainWindow.xaml.cs**: UI interaction and animation logic

### Technologies

- **.NET 8.0** with WPF for UI
- **XStateNet** for state machine orchestration
- **MVVM Pattern** for data binding
- **WPF Animations** for smooth wafer movement

## Extending the Simulator

To add more features:

1. **Add new stations**: Define new station positions in `InitializeStations()`
2. **Modify processing times**: Adjust delays in `ProcessNextWafer()`
3. **Add state machines**: Create new machine definitions in `CMPStationMachines.cs`
4. **Enhance visualization**: Modify XAML styles and add new visual elements

## Notes

- Processing times are shortened for demonstration purposes (3s polishing, 2.5s cleaning)
- Real CMP tools would have longer processing times (30s+)
- The simulator demonstrates pipeline parallelism - multiple wafers can be in different stages simultaneously
- Maximum throughput is limited by the slowest station (Polisher in this case)
