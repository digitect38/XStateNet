# Enhanced CMP Simulator - User Guide

## Quick Start

### 1. Launch the Application

**Option A: Using dotnet CLI**
```bash
cd C:\Develop25\XStateNet\SemiStandard.WPF.EnhancedCMP
dotnet run
```

**Option B: Using Visual Studio**
1. Open `XStateNet.sln` in Visual Studio
2. Set `SemiStandard.WPF.EnhancedCMP` as startup project
3. Press F5 to run

**Option C: Running the executable**
```bash
cd SemiStandard.WPF.EnhancedCMP
dotnet build
cd bin\Debug\net8.0-windows
SemiStandard.WPF.EnhancedCMP.exe
```

### 2. Understanding the Interface

When the application starts, you'll see:

```
┌─────────────────────────────────────────────────────────────┐
│ Enhanced CMP Simulator                    [▶Start][⏹Stop][➕] │
│ SEMI: E40 Process Jobs • E90 Substrate • E134 Data • E39... │
├─────────────────────────────────────────────────────────────┤
│ Status  │ Master   │ WIP  │ Queue │ Total │ Utilization    │
│ Stopped │ Not      │  0   │   0   │   0   │    0.0%        │
├─────────────────────────────────────────────────────────────┤
│ ┌─────────┐  ┌─────────┐  ┌─────────┐                      │
│ │ TOOL 1  │  │ TOOL 2  │  │ TOOL 3  │                      │
│ │  Idle   │  │  Idle   │  │  Idle   │                      │
│ │         │  │         │  │         │                      │
│ └─────────┘  └─────────┘  └─────────┘                      │
├─────────────────────────────────────────────────────────────┤
│ Event Log                                                   │
│ (empty)                                                     │
└─────────────────────────────────────────────────────────────┘
```

## Step-by-Step Operation

### Step 1: Start the Simulation

1. **Click the "▶ Start" button** (blue button in top-right)

2. **What happens automatically:**
   - EventBusOrchestrator initializes (8 parallel buses)
   - Enhanced Master Scheduler starts with E40/E134/E39 integration
   - 3 CMP Tool Schedulers start with E90/E134/E39 integration
   - Tools register with master scheduler
   - System sends 12 jobs automatically (1 every 1.5 seconds)
   - Status changes to "Running"

3. **You'll see in the Event Log:**
   ```
   [10:30:45] 🔧 Initializing Enhanced CMP System...
   [10:30:45] 📋 Creating Enhanced Master Scheduler...
   [10:30:45]    ✅ E40 Process Job management active
   [10:30:45]    ✅ E134 Data Collection plans configured
   [10:30:45]    ✅ E39 Equipment Metrics defined
   [10:30:46] 🔧 Creating Enhanced CMP Tool Schedulers...
   [10:30:46]    ✅ E90 Substrate Tracking ready
   [10:30:46]    ✅ E134 Tool-level data collection active
   [10:30:46]    ✅ E39 Tool metrics configured
   [10:30:46] 📝 Registering tools with master scheduler...
   [10:30:47] ✅ System Initialized - Ready to Process Wafers
   [10:30:48] 📨 Job sent: JOB_103047 (Priority: High)
   [10:30:49] 📨 Job sent: JOB_103049 (Priority: Normal)
   ```

### Step 2: Monitor the Simulation

**Master Scheduler Status Bar:**
- **Status**: Shows "Running" (green text)
- **Master State**: Shows current state (idle → evaluating → dispatching → waiting)
- **Current WIP**: Number of jobs being processed (max 3)
- **Queue Length**: Jobs waiting to be processed
- **Total Jobs**: Number of completed jobs
- **Utilization**: Percentage of available capacity being used

**Tool Status Cards:**

Each tool shows:
- **Tool ID**: CMP_TOOL_1, CMP_TOOL_2, or CMP_TOOL_3
- **Status Box**: Large colored indicator
  - 🟢 **Green** = Idle (ready for work)
  - 🔵 **Blue** = Processing (actively working on wafer)
  - 🟠 **Orange** = Loading/Unloading
  - 🟣 **Purple** = Maintenance mode
  - 🔴 **Red** = Error state
- **Wafers**: Total wafers processed
- **Slurry**: Slurry level percentage (decreases with use)
- **Pad Wear**: Polishing pad wear percentage (increases with use)
- **Avg Cycle**: Average processing time per wafer

**Real-Time Updates:**
All values update automatically every 500 milliseconds (2 times per second).

### Step 3: Manually Send Jobs

While the system is running, you can manually send additional jobs:

1. **Click "➕ Send Job"** (green button)

2. **What happens:**
   - Creates a new E40 Process Job
   - Registers substrate with E90 Substrate Tracking
   - Job priority alternates (High → Normal → Normal → Normal → High)
   - Job enters master scheduler queue
   - Event log shows: `📨 Job sent: JOB_XXXXXX (Priority: High/Normal)`

3. **Job Processing Flow:**
   ```
   Job Arrival → Queue → Dispatch → Tool Assignment →
   Loading → Processing → Unloading → Complete
   ```

### Step 4: Observe SEMI Standards in Action

**E40 Process Jobs:**
- Each job gets a unique Process Job ID (PJ_XXXXXX)
- State transitions: QUEUED → PROCESSING → COMPLETED
- Visible in event log

**E90 Substrate Tracking:**
- Each wafer tracked through locations:
  - LoadPort → Process Chamber → Unload Port
- Complete genealogy recorded

**E134 Data Collection:**
- 6 active data collection plans:
  1. JOB_ARRIVAL - Tracks queue metrics
  2. JOB_DISPATCH - Records WIP changes
  3. JOB_COMPLETION - Logs throughput
  4. TOOL_STATE - Monitors tool state changes
  5. CONSUMABLES - Tracks slurry/pad wear
  6. WAFER_COMPLETION - Records cycle times

**E39 Equipment Metrics:**
- Real-time performance tracking
- State transitions logged
- Metrics: Utilization, Throughput, Cycle Time

### Step 5: Understanding Tool Behavior

**Tool States and Transitions:**

```
Idle → Loading → Processing → Unloading → Idle
  ↓                                          ↑
  └─→ Maintenance (every 50 wafers) ────────┘
  └─→ Error (on failure) ────────────────────┘
```

**Consumable Usage:**
- **Slurry**: Decreases 1-3% per wafer
- **Pad Wear**: Increases 0.1-0.6% per wafer
- **Auto-Refill**: Slurry automatically refills when low
- **Maintenance**: Tool goes to PM when pad wear reaches threshold

**Tool Assignment:**
- Master scheduler uses load balancing
- Prefers tools with:
  - Lower wafer count
  - Better consumable levels
  - Matching recipe capability

### Step 6: Reading the Event Log

Event log shows chronological activity:

```
[HH:MM:SS] 📨 Job sent: JOB_103500 (Priority: High)
[HH:MM:SS] 🔧 Registered tool: CMP_TOOL_1
[HH:MM:SS] ✅ E90 Substrate tracking started for W1035
[HH:MM:SS] 💎 CMP_TOOL_1 CMP processing - Job: PJ_103501
[HH:MM:SS] ✅ Wafer complete - Total: 5/50
```

**Icon Guide:**
- 🔧 System operations
- 📋 Scheduler events
- 📨 Job arrivals
- 💎 Processing activities
- ✅ Completions
- ❌ Errors
- ⚠️ Warnings

### Step 7: Stop the Simulation

1. **Click "⏹ Stop"** (red button)

2. **What happens:**
   - Update timer stops
   - Orchestrator disposes gracefully
   - All state machines stop
   - Memory is freed
   - Status changes to "Stopped"

3. **Event log shows:**
   ```
   [10:35:00] ⏹️ Simulation stopped
   ```

## Advanced Usage

### Monitoring Performance Metrics

**Utilization:**
- Formula: (Current WIP / Max WIP) × 100%
- Target: 70-85% for optimal throughput
- <50%: Underutilized
- >90%: Bottleneck risk

**Throughput:**
- Measured in wafers per hour (WPH)
- Calculated from total jobs / elapsed time
- Typical range: 10-20 WPH for 3 tools

**Queue Management:**
- Queue builds when: Demand > Tool capacity
- Queue drains when: Tools become available
- High-priority jobs dispatch first

### Understanding Tool Metrics

**Slurry Level:**
- Starts at 100%
- Decreases with each wafer
- Auto-refills when low
- Critical threshold: <20%

**Pad Wear:**
- Starts at 0%
- Increases with each wafer
- Triggers PM at 100%
- Resets to 0% after maintenance

**Average Cycle Time:**
- Running average of last 100 wafers
- Typical: 2-5 seconds per wafer
- Increases if consumables are low

### Troubleshooting

**Problem: No jobs processing**
- Check: Is simulation started?
- Check: Are tools in Idle state (green)?
- Solution: Click "➕ Send Job" manually

**Problem: Tools stuck in one state**
- Cause: State machine may be waiting for event
- Solution: Stop and restart simulation

**Problem: UI not updating**
- Cause: Update timer may have failed
- Solution: Stop and restart simulation

**Problem: Build errors**
- Check: .NET 8.0 SDK installed
- Check: All dependencies restored
- Solution: `dotnet restore` then `dotnet build`

## Keyboard Shortcuts

Currently, the application uses mouse/touch only. Future versions may include:
- `Ctrl+S` - Start
- `Ctrl+T` - Stop
- `Ctrl+J` - Send Job
- `F5` - Refresh

## Performance Tips

1. **Optimal Job Rate**: 1 job per 1-2 seconds
2. **Max WIP**: Keep at 3 for balanced throughput
3. **Monitor Queue**: Queue >10 indicates bottleneck
4. **Tool Count**: 3 tools handle ~15-20 WPH

## Understanding the Data Flow

```
User → [Send Job] → E40 Process Job Created
                          ↓
                   Master Scheduler Queue
                          ↓
                   Tool Selection (Load Balancing)
                          ↓
                   E90 Substrate Registration
                          ↓
                   Tool Processing (E134 Data Collection)
                          ↓
                   E39 Metrics Update
                          ↓
                   Job Complete → UI Update (500ms)
```

## Next Steps

- **Experiment**: Try sending jobs at different rates
- **Observe**: Watch how tools balance load
- **Analyze**: Study utilization vs throughput relationship
- **Extend**: Modify code to add custom metrics
- **Integrate**: Connect to real SECS/GEM equipment

## Support

For issues or questions:
- Check console output for detailed logs
- Review event log for error messages
- Examine `XStateNet.Orchestration` logs
- See main README.md for architecture details

## Summary

The WPF Enhanced CMP Simulator provides a production-grade visualization of semiconductor manufacturing automation with full SEMI standards compliance. It demonstrates:

✅ Real-time state machine orchestration
✅ Multi-tool coordination
✅ SEMI E40/E90/E134/E39 integration
✅ Professional UI/UX design
✅ Thread-safe async operations

Enjoy exploring the capabilities of XStateNet! 🚀
