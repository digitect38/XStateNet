# Enhanced CMP Simulator - WPF Application

## Overview

WPF graphical interface for the Enhanced CMP (Chemical Mechanical Planarization) Simulator with full SEMI standards integration.

## Features

### SEMI Standards Integration
- **E40 Process Job Management** - Formal job lifecycle tracking
- **E90 Substrate Tracking** - Per-wafer genealogy and location history
- **E134 Data Collection Management** - Real-time metrics collection
- **E39 Equipment Metrics** - Performance monitoring

### User Interface
- **Real-Time Visualization** - Live updates every 500ms
- **Multi-Tool Display** - 3 CMP tools with individual status
- **Master Scheduler Status** - WIP, queue length, utilization, throughput
- **Event Log** - Scrolling event history with timestamps
- **Interactive Controls** - Start, Stop, Send Job buttons

### Visual Features
- Dark theme optimized for manufacturing floor displays
- Color-coded tool states:
  - 🟢 Green: Idle
  - 🔵 Blue: Processing
  - 🟠 Orange: Loading/Unloading
  - 🟣 Purple: Maintenance
  - 🔴 Red: Error
- Real-time metrics updates:
  - Wafers processed
  - Slurry level
  - Pad wear percentage
  - Average cycle time

## Running the Application

```bash
cd SemiStandard.WPF.EnhancedCMP
dotnet run
```

Or build and run the executable:

```bash
dotnet build
dotnet run --no-build
```

## Usage

1. **Start Simulation** - Click "▶ Start" to initialize the Enhanced CMP system
   - Creates EventBusOrchestrator
   - Initializes Enhanced Master Scheduler with E40/E134/E39
   - Creates 3 CMP Tool Schedulers with E90/E134/E39
   - Auto-sends 12 jobs with priority scheduling

2. **Monitor Status** - View real-time updates:
   - Master scheduler state and metrics
   - Individual tool status and performance
   - Event log with detailed timestamps

3. **Send Jobs** - Click "➕ Send Job" to manually add jobs
   - Jobs alternate between High and Normal priority
   - Automatic E40 process job creation
   - E90 substrate registration

4. **Stop Simulation** - Click "⏹ Stop" to halt and cleanup

## Architecture

```
SemiStandard.WPF.EnhancedCMP
├── ViewModels/
│   ├── ViewModelBase.cs          # MVVM base class with INotifyPropertyChanged
│   ├── ToolViewModel.cs           # Individual tool state
│   └── MainViewModel.cs           # Main application logic
├── MainWindow.xaml                # UI layout and styling
└── MainWindow.xaml.cs             # Code-behind (minimal)
```

## MVVM Pattern

- **Model**: Enhanced CMP Schedulers (from SemiStandard project)
- **ViewModel**: MainViewModel, ToolViewModel
- **View**: MainWindow.xaml

All UI updates are data-bound using WPF bindings, ensuring clean separation of concerns.

## Technologies

- **.NET 8.0** - Target framework
- **WPF** - Windows Presentation Foundation
- **MVVM** - Model-View-ViewModel pattern
- **XStateNet** - State machine orchestration
- **SEMI Standards** - E40, E90, E134, E39 integration

## Performance

- **Update Frequency**: 500ms (2 Hz)
- **Event Bus**: 8 parallel buses
- **Orchestrator**: EventBusOrchestrator (<0.01ms latency)
- **UI Thread**: Dispatcher-based updates for thread safety

## Future Enhancements

- Charting/graphing for throughput trends
- Historical data collection reports (E134)
- 3D tool visualization
- Multi-fab support with distributed orchestrator
- Export to CSV/Excel
