# SimpleTaskLoopCMPXS2

XStateNet2 version of the Simple Task Loop CMP Scheduler.

## Architecture

This is a conversion of the `SimpleTaskLoopCMP(NonXS)` project to use XStateNet2 state machines.

### Key Components

1. **Station Actors** - Each station (Carrier, Buffer, Polisher, Cleaner) is an Akka.NET actor
2. **Robot Actors** - Each robot (R1, R2, R3) uses XStateNet2 state machines to manage its states
3. **XState Scheduler** - Orchestrates the robot task loops using actor-based communication

### State Machines

- **Robot States**: Empty, HasNPW (Has Non-Processed Wafer), HasPW (Has Processed Wafer), Moving, Picking, Placing
- **Buffer Station States**: Empty, HasWafer
- **Process Station States**: Empty, Idle, Processing, AlmostDone, Done

### Design Notes

The implementation uses:
- **XStateNet2** for robot state management (behavior modeling)
- **Akka.NET actors** for asynchronous communication between components
- **Task loops** for continuous robot coordination (similar to the original non-XS version)

### Running

```bash
dotnet run [waferCount]
```

Example:
```bash
dotnet run 25  # Process 25 wafers (default)
dotnet run 5   # Process 5 wafers
```

### Comparison with Non-XS Version

| Feature | Non-XS | XS2 |
|---------|--------|-----|
| Threading | Direct Task.Run loops | Akka.NET actor model |
| State Management | Lock-free atomic operations | XStateNet2 state machines |
| Communication | Direct method calls | Actor message passing |
| Concurrency | Manual Task coordination | Actor supervision |

## Status

🚧 **Development/Learning Implementation** - Demonstrates XStateNet2 integration patterns with a CMP simulation scheduler.

### Working Components

- ✅ All actors and state machines initialize correctly
- ✅ XStateNet2 state machines for robots and stations are defined and running
- ✅ Actor-based architecture with message passing
- ✅ Processing stations simulate wafer processing with progress tracking (0% → 80% → 100%)
- ✅ Robots can move between stations
- ✅ Basic pick/place operations at station level

### Known Limitations

This implementation serves as a **reference architecture** and **learning tool** rather than a complete working scheduler:

- Wafer flow coordination needs refinement (wafers picked but not fully flowing through pipeline)
- Robot state synchronization between scheduler and robot actors needs improvement
- The scheduler uses local wafer tracking which doesn't fully sync with robot actor states

### Architecture Notes

- Uses Akka.NET `Ask` pattern for queries (causes harmless "dead letter" logs for responses)
- Demonstrates XStateNet2 state machine integration with Akka.NET actors
- Shows how to model manufacturing equipment behavior with declarative state definitions
- Illustrates the complexity of coordinating distributed actor states in a scheduler

### Value as Reference Implementation

This project successfully demonstrates:
1. How to structure an XStateNet2-based manufacturing simulation
2. Actor-based communication patterns
3. State machine definitions for robots and equipment
4. The architectural patterns needed for such systems

For a production-ready scheduler, consider the patterns in `CMPSimXS2.Console` which has mature coordination logic.
