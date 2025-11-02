# CMPSimXS2 Console Application

## Overview

This console application demonstrates the **CMPSimXS2** wafer fabrication simulation with:
- **Single-Wafer Rules** enforcement (robot and station rules)
- **Train Pattern** wafer journey through all processing stages
- **Two-Carrier Successive Processing** - realistic production scenario with carrier arrival/departure events

## What It Demonstrates

### 1. Single-Wafer Rules Enforcement

#### Robot Rule
**No robot can carry multiple wafers simultaneously.**

- Real `RobotScheduler` enforces this rule
- Robot must return to `idle` state (with no wafer) before picking up another
- Validated by 7 unit tests in `RobotSchedulerSingleWaferRuleTests.cs`

#### Station Rule
**No station can hold multiple wafers simultaneously.**

- Real `WaferJourneyScheduler` enforces this rule
- Each station (Polisher, Cleaner, Buffer) can only hold ONE wafer at a time
- Stations work in PARALLEL, but each is exclusive to one wafer
- Validated by 10 unit tests in `StationSingleWaferRuleTests.cs`

### 2. Train Pattern Wafer Journey

All wafers follow the same sequential 8-step path:

```
Carrier → R1 → Polisher → R2 → Cleaner → R3 → Buffer → R1 → Carrier
```

- **Step 1-2**: Robot 1 transfers wafer from Carrier to Polisher
- **Step 3**: Polisher processes wafer (color changes: ⚫ → 🟡)
- **Step 4**: Robot 2 transfers wafer from Polisher to Cleaner
- **Step 5**: Cleaner processes wafer (color changes: 🟡 → ⚪)
- **Step 6**: Robot 3 transfers wafer from Cleaner to Buffer
- **Step 7**: Buffer stores wafer temporarily
- **Step 8**: Robot 1 returns wafer from Buffer to Carrier

### 3. Two-Carrier Successive Processing

The simulation demonstrates a realistic production scenario with **carrier arrival and departure events**:

```
Event Flow:
1. 🚛 Carrier C1 arrives → Process 5 wafers (IDs: 1-5)
2. ✅ Carrier C1 complete → All wafers processed
3. 🚚 Carrier C1 departs → Wafers removed from system
4. 🚛 Carrier C2 arrives → Process 5 wafers (IDs: 6-10)
5. ✅ Carrier C2 complete → All wafers processed
6. 🚚 Carrier C2 departs → Simulation complete
```

**Key Features:**
- Carriers arrive/depart as discrete events
- Only one carrier is processed at a time
- System waits for current carrier to complete before accepting next
- Total: 10 wafers (2 carriers × 5 wafers each)

### 4. Real Components Used

The console app uses the **same production schedulers** tested by 56 unit tests:

- ✅ **RobotScheduler**: Actual implementation from CMPSimXS2
- ✅ **WaferJourneyScheduler**: Actual implementation from CMPSimXS2
- ✅ **TransferRequest**: Actual model from CMPSimXS2
- ✅ **Wafer**: Actual model with journey tracking
- ✅ **Akka ActorSystem**: Real actor framework
- ✅ **Mock Actors**: Simplified actors for console demonstration

## Features

### Visual Display

The console provides rich emoji-based visualization:

#### Wafer States (5-Stage Color Progression)
- ⚫ = Raw/Unprocessed wafer (InCarrier - BLACK)
- 🔵 = Being polished (Polishing/ToPolisher - BLUE)
- 🟢 = Polished wafer (ToCleaner - GREEN)
- 🟡 = Being cleaned (Cleaning - YELLOW)
- ⚪ = Cleaned wafer (InBuffer/ToCarrier/Completed - WHITE)

#### Journey Icons
- 📦 = InCarrier
- → = ToPolisher / ToCleaner / ToBuffer
- 🔧 = Polishing
- 🧼 = Cleaning
- 💾 = InBuffer
- ← = ToCarrier

#### Station States
- 🟢 = idle (available)
- 🔴 = processing (working on wafer)
- 🟡 = done (ready for pickup)
- 🟠 = occupied (buffer holding wafer)

#### Robot States
- 🟢 = idle (ready for transfer)
- 🔴 = busy (assigned to transfer)
- 🟡 = carrying (holding a wafer)

### Real-Time Monitoring

Each simulation cycle displays:
- **Current Carrier**: Which carrier is being processed (C1 or C2)
- **Wafer Train Status**: All 10 wafers and their current journey stages
- **Station Status**: Which wafer each station is holding (max 1)
- **Robot Status**: Which robot is idle/busy/carrying
- **Queue Status**: Number of pending transfer requests

## Running the Demo

### Quick Start (Default)
```bash
dotnet run --project XStateNet2/CMPSimXS2.Console/CMPSimXS2.Console.csproj
```

### Available Scheduler Options

The console app supports **5 robot scheduler** and **3 journey scheduler** implementations:

#### Robot Scheduler Options
- 🔒 **Lock-based** (default) - Traditional synchronization
- 🎭 **Actor-based** (`--robot-actor`) - Message passing, no locks
- 🔄 **XState-based** (`--robot-xstate`) - Declarative state machines
- ⚡ **Array-optimized** (`--robot-array`) - O(1) byte-indexed lookups, **FASTEST**
- 🤖 **Autonomous** (`--robot-autonomous`) - Self-managing polling loops, **NEW**

#### Journey Scheduler Options
- 🔒 **Lock-based** (default) - Traditional synchronization
- 🎭 **Actor-based** (`--journey-actor`) - Message passing, no locks
- 🔄 **XState-based** (`--journey-xstate`) - Declarative state machines

### Example Commands

```bash
# Default (Lock + Lock)
dotnet run

# Maximum performance (Array + XState)
dotnet run --robot-array --journey-xstate

# Autonomous robots (NEW! Self-managing)
dotnet run --robot-autonomous --journey-xstate

# High concurrency (Actor + Actor)
dotnet run --robot-actor --journey-actor

# Run benchmark
dotnet run --benchmark
```

### Expected Output
The simulation will run for up to 100 cycles or until all wafers from both carriers complete their journey:
1. Carrier C1 arrives with 5 wafers
2. Wafers 1-5 process through pipeline
3. Carrier C1 completes and departs
4. Carrier C2 arrives with 5 wafers
5. Wafers 6-10 process through pipeline
6. Carrier C2 completes and departs
7. Simulation finishes

### Detailed Logs
The autonomous scheduler writes detailed logs to:
```
XStateNet2/CMPSimXS2.Console/bin/Debug/net8.0/recent processing history.log
```

## Test Coverage

The schedulers demonstrated in this console app are validated by **56 unit tests** (100% passing):

### Robot Single-Wafer Rule Tests (7 tests)
Location: `XStateNet2.Tests/CMPSimXS2/Schedulers/RobotSchedulerSingleWaferRuleTests.cs`

1. ✅ RobotMustBeIdle_BeforePickingUpWafer
2. ✅ RobotCannotBeIdle_WhileHoldingWafer
3. ✅ RobotMustPlaceWafer_BeforePickingAnother
4. ✅ IdleRobot_ShouldHaveNoWafer
5. ✅ BusyRobot_CannotAcceptNewTransfer
6. ✅ CarryingRobot_CannotAcceptNewTransfer
7. ✅ MultipleRobots_CanWorkInParallel_EachWithOneWafer

### Station Single-Wafer Rule Tests (10 tests)
Location: `XStateNet2.Tests/CMPSimXS2/Schedulers/StationSingleWaferRuleTests.cs`

1. ✅ Polisher_CanOnlyProcessOneWafer_AtATime
2. ✅ Cleaner_CanOnlyProcessOneWafer_AtATime
3. ✅ Buffer_CanOnlyStoreOneWafer_AtATime
4. ✅ Station_MustBeIdle_BeforeAcceptingNewWafer
5. ✅ MultipleStations_CanWorkInParallel_EachWithOneWafer
6. ✅ Station_CanAcceptNewWafer_AfterCurrentCompletes
7. ✅ IdleStation_ShouldNotHaveWafer
8. ✅ ProcessingStation_MustHaveWafer
9. ✅ Station_CannotAccept_IfAlreadyHasWafer
10. ✅ WaferMustWait_UntilStationAvailable

### Other Tests (39 tests)
- RobotScheduler tests: 13 tests
- WaferJourneyScheduler tests: 11 tests
- TransferRequest tests: 9 tests
- Integration tests: 6 tests

## Documentation

See related documentation:
- **ROBOT_RULE.md**: Detailed explanation of robot single-wafer rule
- **STATION_RULE.md**: Detailed explanation of station single-wafer rule

## Development Strategy

This console application follows the **Test-Driven Development (TDD)** strategy:

```
Tests (✅ 56 passing) → Console App (demonstrated) → WPF App (next phase)
```

1. **Tests First**: Comprehensive unit tests validate scheduler logic
2. **Console Demo**: Real schedulers demonstrated in console environment
3. **WPF Application**: Next step - full visual simulation with state machines

## Architecture Insights

This demonstration revealed an important architectural consideration:

- The `RobotScheduler` and `WaferJourneyScheduler` provide the core scheduling logic
- Transfer completion callbacks need an orchestration layer to wire them together
- The WPF application will integrate this using XStateNet2 state machines
- Mock actors in this console demo simulate the async message flow

## Key Achievements

✅ **Rules Enforced**: Both single-wafer rules working correctly
✅ **Train Pattern**: All wafers follow sequential fixed path
✅ **Parallel Processing**: Stations work simultaneously with exclusive wafer ownership
✅ **Two-Carrier Support**: Realistic carrier arrival/departure events
✅ **Event-Driven**: Discrete carrier lifecycle events (arrival, completion, departure)
✅ **Real Schedulers**: Production code used, not simplified demo versions
✅ **5x3 Scheduler Matrix**: 15 combinations (5 robot × 3 journey schedulers)
  - ⚡ **Array-optimized**: O(1) lookups with byte indices (FASTEST)
  - 🤖 **Autonomous**: Self-managing robots with polling loops (NEW)
  - 🎭 **Actor-based**: High concurrency without locks
  - 🔄 **XState-based**: Declarative state machines
  - 🔒 **Lock-based**: Traditional synchronization
✅ **Comprehensive Tests**: 56 tests validating all logic (100% passing)
✅ **Beautiful Visualization**: Rich 5-stage color progression (⚫ → 🔵 → 🟢 → 🟡 → ⚪)
✅ **UTF-8 Support**: Proper emoji rendering in Windows console
✅ **Thread-Safe**: All implementations use appropriate concurrency models

## Next Steps

The next phase is the **WPF Application** which will:
- Use real XStateNet2 state machines (not mock actors)
- Provide full visual animation of wafer movements
- Complete the async orchestration with proper callbacks
- Enable interactive simulation control

---

🤖 **Generated as part of CMPSimXS2 development**
📝 **Test-driven development: Tests → Console → WPF**
✅ **56 unit tests passing - Rules validated and enforced**
