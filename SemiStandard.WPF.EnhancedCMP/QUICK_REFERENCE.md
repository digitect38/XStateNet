# Enhanced CMP Simulator - Quick Reference

## 🚀 Launch

```bash
cd SemiStandard.WPF.EnhancedCMP
dotnet run
```

## 🎮 Controls

| Button | Action | Result |
|--------|--------|--------|
| **▶ Start** | Initialize system | Starts orchestrator, creates 12 jobs automatically |
| **⏹ Stop** | Halt simulation | Stops all processing, cleans up resources |
| **➕ Send Job** | Add manual job | Creates E40 job, registers substrate |

## 📊 Status Indicators

### Master Scheduler
- **Status**: Running / Stopped
- **Master State**: XState current state
- **WIP**: Jobs being processed (0-3)
- **Queue**: Waiting jobs
- **Total**: Completed jobs
- **Utilization**: Capacity usage %

### Tool States (Color-Coded)

| Color | State | Meaning |
|-------|-------|---------|
| 🟢 Green | Idle | Ready for work |
| 🔵 Blue | Processing | Working on wafer |
| 🟠 Orange | Loading/Unloading | Transfer in progress |
| 🟣 Purple | Maintenance | PM cycle |
| 🔴 Red | Error | Fault condition |

## 📈 Metrics Explained

### Tool Metrics
- **Wafers**: Total processed count
- **Slurry**: Consumable level (100% → 0%)
- **Pad Wear**: Wear percentage (0% → 100%)
- **Avg Cycle**: Processing time per wafer

### Performance Indicators
- **Good Utilization**: 70-85%
- **Typical Throughput**: 10-20 wafers/hour
- **Target Cycle Time**: 2-5 seconds

## 🔄 Job Flow

```
Send Job → Queue → Dispatch → Load → Process → Unload → Complete
```

## 🎯 SEMI Standards

| Standard | What It Does |
|----------|--------------|
| **E40** | Process job lifecycle tracking |
| **E90** | Per-wafer location history |
| **E134** | Real-time data collection (6 plans) |
| **E39** | Equipment performance metrics |

## ⏱️ Update Frequency

- **UI Refresh**: Every 500ms (2 Hz)
- **Auto Jobs**: 12 jobs @ 1.5s interval
- **Event Log**: Real-time as events occur

## 🔧 Typical Workflow

1. **Click Start** - System initializes
2. **Wait 2 seconds** - Jobs begin processing
3. **Watch tools** - Monitor state transitions
4. **Check metrics** - Observe utilization
5. **Send more jobs** - Click ➕ if needed
6. **Review log** - Check event history
7. **Click Stop** - Clean shutdown

## 📝 Event Log Icons

- 🔧 System operations
- 📋 Scheduler events
- 📨 Job arrivals
- 💎 Processing
- ✅ Success
- ❌ Errors
- ⚠️ Warnings

## 🎨 UI Layout

```
┌──────────────────────────────────────────┐
│ Header: Title + Buttons                  │
├──────────────────────────────────────────┤
│ Master Status: 6 metrics in grid         │
├──────────────────────────────────────────┤
│ ┌──────┐  ┌──────┐  ┌──────┐            │
│ │Tool 1│  │Tool 2│  │Tool 3│            │
│ │      │  │      │  │      │            │
│ └──────┘  └──────┘  └──────┘            │
├──────────────────────────────────────────┤
│ Event Log: Scrolling list                │
└──────────────────────────────────────────┘
```

## 💡 Tips

- **Max WIP = 3**: System won't exceed 3 concurrent jobs
- **Priority Jobs**: Every 4th job is High priority
- **Auto-Refill**: Slurry refills automatically when low
- **PM Trigger**: Tools go to maintenance at 50 wafers
- **Event Log**: Keeps last 100 events

## 🐛 Quick Troubleshooting

| Problem | Solution |
|---------|----------|
| Nothing happening | Click Start button |
| No jobs processing | Click Send Job |
| UI frozen | Stop and restart |
| Build errors | Run `dotnet restore` |

## 📦 What's Happening Behind the Scenes

When you click **Start**:
1. Creates EventBusOrchestrator (8 buses)
2. Starts Enhanced Master Scheduler
3. Starts 3 Enhanced Tool Schedulers
4. Registers tools with master
5. Auto-sends 12 jobs
6. Starts 500ms update timer

When you click **Send Job**:
1. Creates E40 Process Job
2. Registers E90 Substrate
3. Queues in master scheduler
4. Waits for available tool
5. Dispatches when ready

When processing:
1. Tool transitions to Loading
2. E90 updates location
3. Tool transitions to Processing
4. E134 collects data
5. E39 updates metrics
6. Tool transitions to Unloading
7. Job completes
8. UI updates (next 500ms tick)

## 🎓 Learning Resources

- **Full Guide**: See `USER_GUIDE.md`
- **Architecture**: See `README.md`
- **SEMI Standards**: See `CMP_SIMULATOR_ENHANCED_ARCHITECTURE.md`
- **Code Examples**: See `SemiStandard/Schedulers/`

## 🚦 Performance Indicators

| Metric | Good | Warning | Critical |
|--------|------|---------|----------|
| Utilization | 70-85% | >90% | >95% |
| Queue | 0-5 | 5-10 | >10 |
| Slurry | >50% | 20-50% | <20% |
| Pad Wear | <80% | 80-95% | >95% |

---

**For detailed instructions, see `USER_GUIDE.md`**
