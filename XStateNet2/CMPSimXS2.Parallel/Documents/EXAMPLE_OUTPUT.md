# CMPSimXS2.Parallel Example Output

This document shows example output from the CMP wafer processing simulation, demonstrating the complete initialization sequence with SYSTEM_READY broadcast and subsequent wafer processing.

## Complete System Startup Sequence

### Initial Output with Column Headers

```
Step      COORD                              R1_FWD                             POLISHER                           R2                                 CLEANER                            R3                                 BUFFER                             R1_RET
----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
Step 1    [ ROBOTS -> COORD ] R-1:READY,R-2:READY,R-3:READY
Step 2    [ EQUIPMENT -> COORD ] PLATEN:READY,CLEANER:READY,BUFFER:READY
Step 3    [ WSCH-001 -> COORD ] READY
Step 4    [ COORD -> ALL ] ALL SYSTEMS READY
Step 5                                       [ WSCH-001 -> R-1 ] REQUEST_ROBOT_p1
Step 6    [ R-1 -> COORD ] REQUEST_PERMISSION
Step 7    [ COORD -> R-1 ] PERMIT
Step 8                                       [ R-1 -> WSCH-001 ] R1AVAILABLE_PICK_P1
Step 9                                       [ R-1 -> WSCH-001 ] pick from carrier
Step 10                                      [ R-1 -> WSCH-001 ] move to platen
Step 11                                                                         [ PLATEN -> WSCH-001 ] REQUEST_POLISH
Step 12                                                                         [ PLATEN -> WSCH-001 ] POLISHING
```

## Detailed Step-by-Step Explanation

### Initialization Phase (Steps 1-4)

#### Step 1: Robot Schedulers Report Ready
```
Step 1    [ ROBOTS -> COORD ] R-1:READY,R-2:READY,R-3:READY
```
- **Who**: RobotSchedulersActor → SystemCoordinator
- **What**: Reports all three robots (R-1, R-2, R-3) are initialized and ready
- **When**: On RobotSchedulersActor construction
- **Code**: `TableLogger.LogEvent("INIT_STATUS", "ROBOTS", "R-1:READY,R-2:READY,R-3:READY", "SYSTEM")`

#### Step 2: Equipment Schedulers Report Ready
```
Step 2    [ EQUIPMENT -> COORD ] PLATEN:READY,CLEANER:READY,BUFFER:READY
```
- **Who**: RobotSchedulersActor (equipment manager) → SystemCoordinator
- **What**: Reports all three equipment stations (PLATEN, CLEANER, BUFFER) are initialized and ready
- **When**: On RobotSchedulersActor construction (after robots)
- **Code**: `TableLogger.LogEvent("INIT_STATUS", "EQUIPMENT", "PLATEN:READY,CLEANER:READY,BUFFER:READY", "SYSTEM")`

#### Step 3: First Wafer Scheduler Reports Ready
```
Step 3    [ WSCH-001 -> COORD ] READY
```
- **Who**: WaferSchedulerActor (W-001) → SystemCoordinator
- **What**: First wafer scheduler reports it is initialized and ready to start processing
- **When**: On WaferSchedulerActor construction
- **Code**: `TableLogger.LogEvent("INIT_STATUS", waferSchId, "READY", _waferId)`

#### Step 4: Coordinator Broadcasts System Ready ⭐
```
Step 4    [ COORD -> ALL ] ALL SYSTEMS READY
```
- **Who**: SystemCoordinator → ALL actors (broadcast)
- **What**: **Coordinator confirms that all subsystems are ready** - everyone now knows everyone is ready
- **When**: After first wafer scheduler reports ready (trigger: `_waferCounter == 1`)
- **Why**: This is the **mutual knowledge** step - ensures all actors know the system is ready before processing begins
- **Code**: `TableLogger.LogEvent("SYSTEM_READY", "COORD", "ALL SYSTEMS READY", "SYSTEM")`

**Key Insight**: Steps 1-4 form the initialization handshake protocol. No wafer processing begins until Step 4 completes.

---

### Processing Phase (Steps 5+)

After SYSTEM_READY broadcast, wafer processing begins following the standard protocol.

#### Step 5: Wafer Requests Robot1 (Priority p1)
```
Step 5                                       [ WSCH-001 -> R-1 ] REQUEST_ROBOT_p1
```
- **Column**: R1_FWD (Robot1 forward path)
- **Who**: WSCH-001 → R-1
- **What**: Wafer scheduler requests Robot1 with priority p1 (pick from carrier)
- **Transition**: `created` → `readyToStart` → `waitingForR1Pickup` (after START_PROCESSING)

#### Step 6: Robot1 Requests Permission from Coordinator
```
Step 6    [ R-1 -> COORD ] REQUEST_PERMISSION
```
- **Column**: COORD (coordination communication)
- **Who**: R-1 → COORD
- **What**: Robot1 asks coordinator for permission to execute task
- **Protocol**: Layer 3 → Layer 1 permission request

#### Step 7: Coordinator Grants Permission
```
Step 7    [ COORD -> R-1 ] PERMIT
```
- **Column**: COORD
- **Who**: COORD → R-1
- **What**: Coordinator grants permission (resource R-1 is available)
- **Resource Check**: One-to-One Rule verified - R-1 not owned by another wafer

#### Step 8: Robot1 Notifies Wafer Scheduler
```
Step 8                                       [ R-1 -> WSCH-001 ] R1AVAILABLE_PICK_P1
```
- **Column**: R1_FWD
- **Who**: R-1 → WSCH-001
- **What**: Robot1 confirms it's available and will execute pick with priority p1
- **Guard Condition**: `HasRobot1Permission` flag set (0x000200)

#### Step 9: Robot1 Picks Wafer from Carrier
```
Step 9                                       [ R-1 -> WSCH-001 ] pick from carrier
```
- **Column**: R1_FWD
- **Who**: R-1 → WSCH-001
- **What**: Robot1 performs physical pickup action
- **Guard Condition**: `CanPickFromCarrier` verified
- **State Update**: `WaferOnRobot` flag set (0x008000)

#### Step 10: Robot1 Moves to Platen
```
Step 10                                      [ R-1 -> WSCH-001 ] move to platen
```
- **Column**: R1_FWD
- **Who**: R-1 → WSCH-001
- **What**: Robot1 moves wafer to polisher platen
- **Guard Condition**: `CanMoveToPlaten` = `WaferOnRobot | HasRobot1Permission` (0x008200)
- **Next**: Will request PLATEN_LOCATION permission

#### Step 11: Wafer Requests Polishing
```
Step 11                                                                         [ PLATEN -> WSCH-001 ] REQUEST_POLISH
```
- **Column**: POLISHER
- **Who**: WSCH-001 → PLATEN
- **What**: After placing wafer on platen, requests polishing operation
- **State**: `WaferAtPlaten` flag set (0x010000)

#### Step 12: Polishing Starts
```
Step 12                                                                         [ PLATEN -> WSCH-001 ] POLISHING
```
- **Column**: POLISHER
- **Who**: PLATEN → WSCH-001
- **What**: Polisher confirms operation started
- **Duration**: Controlled by POLISH_DURATION timeout in state machine
- **Next**: Will eventually send POLISH_COMPLETE

---

## Example: WAIT Mechanism in Action

This example shows what happens when a resource collision occurs:

```
Step      COORD                              R1_FWD                             POLISHER                           R2                                 CLEANER                            R3                                 BUFFER                             R1_RET
----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
Step 1    [ ROBOTS -> COORD ] R-1:READY,R-2:READY,R-3:READY
Step 2    [ EQUIPMENT -> COORD ] PLATEN:READY,CLEANER:READY,BUFFER:READY
Step 3    [ WSCH-001 -> COORD ] READY
Step 4    [ COORD -> ALL ] ALL SYSTEMS READY
Step 5                                       [ WSCH-001 -> R-1 ] REQUEST_ROBOT_p1
Step 6    [ R-1 -> COORD ] REQUEST_PERMISSION
Step 7    [ COORD -> R-1 ] PERMIT
Step 8                                       [ WSCH-001 -> R-2 ] REQUEST_ROBOT_p2
Step 9    [ R-2 -> COORD ] REQUEST_PERMISSION
Step 10   [ COORD -> R-2 ] PERMIT
Step 11   [ WSCH-002 -> COORD ] READY
Step 12                                      [ WSCH-002 -> R-1 ] REQUEST_ROBOT_p1
Step 13   [ R-1 -> COORD ] REQUEST_PERMISSION
Step 14   [ COORD -> R-1 ] WAIT (owned by W-001)
Step 15                                      [ R-1 -> WSCH-002 ] WAIT (retry in 50ms)
...
(50ms delay - automatic retry scheduled)
...
Step 16   [ R-1 -> COORD ] REQUEST_PERMISSION
Step 17   [ COORD -> R-1 ] PERMIT
Step 18                                      [ R-1 -> WSCH-002 ] R1AVAILABLE_PICK_P1
```

**Explanation**:
- **Steps 5-7**: W-001 gets R-1 successfully
- **Steps 8-10**: W-001 also gets R-2 successfully
- **Steps 12-13**: W-002 tries to get R-1 (collision!)
- **Step 14**: Coordinator detects collision, sends WAIT (not DENY)
- **Step 15**: R-1 notifies WSCH-002 to wait, schedules 50ms retry
- **Step 16**: After 50ms, automatic retry (no manual intervention needed)
- **Step 17**: Resource now available, permission granted
- **Step 18**: WSCH-002 proceeds normally

**Key Features**:
- Non-blocking WAIT instead of hard DENY
- Automatic retry after agreed 50ms delay
- Full visibility of wait reason in COORD column
- Wafer scheduler gets wait notification

---

## Example: FIFO Location Queue

This example shows multiple wafers competing for the same location:

```
Step      COORD                              R1_FWD                             POLISHER                           R2                                 CLEANER                            R3                                 BUFFER                             R1_RET
----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
Step 30                                      [ R-1 -> WSCH-001 ] move to platen
Step 31   [ R-1 -> COORD ] REQUEST_PERMISSION (PLATEN_LOCATION)
Step 32   [ COORD -> R-1 ] PERMIT
Step 33                                                                         [ R-1 -> WSCH-001 ] place on platen
Step 34                                      [ R-1 -> WSCH-002 ] move to platen
Step 35   [ R-1 -> COORD ] REQUEST_PERMISSION (PLATEN_LOCATION)
Step 36   [ COORD -> R-1 ] WAIT (queued position 1, owned by W-001)
Step 37                                      [ R-1 -> WSCH-003 ] move to platen
Step 38   [ R-1 -> COORD ] REQUEST_PERMISSION (PLATEN_LOCATION)
Step 39   [ COORD -> R-1 ] WAIT (queued position 2, owned by W-001)
...
(W-001 finishes polishing)
...
Step 45   [ WSCH-001 -> COORD ] FREE_PLATEN_LOCATION
Step 46   [ COORD -> R-1 ] PERMIT (auto-granted to W-002, first in queue)
Step 47                                                                         [ R-1 -> WSCH-002 ] place on platen
```

**Explanation**:
- **Steps 30-33**: W-001 takes PLATEN_LOCATION
- **Steps 34-36**: W-002 tries, gets queued (position 1)
- **Steps 37-39**: W-003 tries, gets queued (position 2)
- **Step 45**: W-001 releases location
- **Step 46**: Coordinator auto-grants to W-002 (FIFO order)
- **Step 47**: W-002 proceeds immediately

**FIFO Queue Guarantees**:
- Locations maintain strict order: PLATEN_LOCATION, CLEANER_LOCATION, BUFFER_LOCATION
- First requested = first granted (fairness)
- Auto-grant on release (no manual retry needed)
- Queue position visible in WAIT messages

---

## Example: Complete Single Wafer Lifecycle

This shows a complete wafer journey from creation to completion:

```
Step      COORD                              R1_FWD                             POLISHER                           R2                                 CLEANER                            R3                                 BUFFER                             R1_RET
----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
Step 1    [ ROBOTS -> COORD ] R-1:READY,R-2:READY,R-3:READY
Step 2    [ EQUIPMENT -> COORD ] PLATEN:READY,CLEANER:READY,BUFFER:READY
Step 3    [ WSCH-001 -> COORD ] READY
Step 4    [ COORD -> ALL ] ALL SYSTEMS READY

--- Stage 1: Carrier → Platen (R-1 forward) ---
Step 5                                       [ WSCH-001 -> R-1 ] REQUEST_ROBOT_p1
Step 6    [ R-1 -> COORD ] REQUEST_PERMISSION
Step 7    [ COORD -> R-1 ] PERMIT
Step 8                                       [ R-1 -> WSCH-001 ] pick from carrier
Step 9                                       [ R-1 -> WSCH-001 ] move to platen
Step 10                                                                         [ R-1 -> WSCH-001 ] place on platen

--- Stage 2: Polishing ---
Step 11                                                                         [ WSCH-001 -> PLATEN ] REQUEST_POLISH
Step 12                                                                         [ PLATEN -> WSCH-001 ] POLISHING
Step 13                                                                         [ PLATEN -> WSCH-001 ] POLISH_COMPLETE

--- Stage 3: Platen → Cleaner (R-2) ---
Step 14                                                                                                            [ WSCH-001 -> R-2 ] REQUEST_ROBOT_p2
Step 15   [ R-2 -> COORD ] REQUEST_PERMISSION
Step 16   [ COORD -> R-2 ] PERMIT
Step 17                                                                                                            [ R-2 -> WSCH-001 ] pick from platen
Step 18                                                                                                            [ R-2 -> WSCH-001 ] move to cleaner
Step 19                                                                                                                                               [ R-2 -> WSCH-001 ] place on cleaner

--- Stage 4: Cleaning ---
Step 20                                                                                                                                               [ WSCH-001 -> CLEANER ] REQUEST_CLEAN
Step 21                                                                                                                                               [ CLEANER -> WSCH-001 ] CLEANING
Step 22                                                                                                                                               [ CLEANER -> WSCH-001 ] CLEAN_COMPLETE

--- Stage 5: Cleaner → Buffer (R-3) ---
Step 23                                                                                                                                                                              [ WSCH-001 -> R-3 ] REQUEST_ROBOT_p3
Step 24   [ R-3 -> COORD ] REQUEST_PERMISSION
Step 25   [ COORD -> R-3 ] PERMIT
Step 26                                                                                                                                                                              [ R-3 -> WSCH-001 ] pick from cleaner
Step 27                                                                                                                                                                              [ R-3 -> WSCH-001 ] move to buffer
Step 28                                                                                                                                                                                                             [ R-3 -> WSCH-001 ] place on buffer

--- Stage 6: Buffering ---
Step 29                                                                                                                                                                                                             [ WSCH-001 -> BUFFER ] REQUEST_BUFFER
Step 30                                                                                                                                                                                                             [ BUFFER -> WSCH-001 ] BUFFERING
Step 31                                                                                                                                                                                                             [ BUFFER -> WSCH-001 ] BUFFER_COMPLETE

--- Stage 7: Buffer → Carrier (R-1 return) ---
Step 32                                                                                                                                                                                                                                            [ WSCH-001 -> R-1 ] REQUEST_ROBOT_p4
Step 33   [ R-1 -> COORD ] REQUEST_PERMISSION
Step 34   [ COORD -> R-1 ] PERMIT
Step 35                                                                                                                                                                                                                                            [ R-1 -> WSCH-001 ] pick from buffer
Step 36                                                                                                                                                                                                                                            [ R-1 -> WSCH-001 ] move to carrier
Step 37                                                                                                                                                                                                                                            [ R-1 -> WSCH-001 ] place on carrier

(W-001 completed - actor terminated)
```

**Complete Journey Summary**:
1. **Initialization** (Steps 1-4): System ready handshake
2. **R1 → Platen** (Steps 5-10): Robot1 picks from carrier and places on polisher
3. **Polishing** (Steps 11-13): Wafer polished at platen station
4. **R2 → Cleaner** (Steps 14-19): Robot2 moves wafer to cleaner
5. **Cleaning** (Steps 20-22): Wafer cleaned at cleaner station
6. **R3 → Buffer** (Steps 23-28): Robot3 moves wafer to buffer
7. **Buffering** (Steps 29-31): Wafer buffered at buffer station
8. **R1 Return** (Steps 32-37): Robot1 returns wafer to carrier

**Total Steps**: 37 (including 4 initialization steps)
**Physical Stations**: Carrier → Platen → Cleaner → Buffer → Carrier
**Robots Used**: R-1 (twice), R-2, R-3
**Processing Stations**: PLATEN, CLEANER, BUFFER

---

## Configuration and Testing

### Running the Simulation

```bash
cd CMPSimXS2.Parallel
dotnet run
```

### Expected Console Output

The simulation will print:
1. **Column headers** (cyan color, one-time)
2. **Separator line** (dashes)
3. **Initialization sequence** (Steps 1-4)
4. **Wafer processing steps** (Steps 5+, blue color)
5. **Completion summary** when all wafers done

### Unit Testing

```bash
cd CMPSimXS2.Tests
dotnet test

# Specific test suites
dotnet test --filter "FullyQualifiedName~SystemReadyTests"
dotnet test --filter "FullyQualifiedName~GuardConditionsTests"
dotnet test --filter "FullyQualifiedName~WaitMechanismTests"
```

---

## Key Takeaways

### Initialization Protocol ⭐
1. **Step 1**: ROBOTS report ready
2. **Step 2**: EQUIPMENT reports ready
3. **Step 3**: WSCH-001 reports ready
4. **Step 4**: **COORD broadcasts SYSTEM_READY** ← Everyone knows everyone is ready!
5. **Step 5+**: Processing begins

### Column Layout (8 Columns)
- **COORD**: Coordination messages (permissions, broadcasts)
- **R1_FWD**: Robot1 forward (carrier → platen)
- **POLISHER**: Platen station (polishing)
- **R2**: Robot2 (platen → cleaner)
- **CLEANER**: Cleaner station (cleaning)
- **R3**: Robot3 (cleaner → buffer)
- **BUFFER**: Buffer station (buffering)
- **R1_RET**: Robot1 return (buffer → carrier)

### Guard Conditions (Bitmasking)
- 22 individual flags (0x000001 - 0x200000)
- 7 complex combinations (e.g., CanMoveToPlaten = 0x008200)
- Efficient multi-condition checking with HasAll()
- Hex string format for debugging

### WAIT Mechanism
- WAIT instead of DENY (cooperative waiting)
- Agreed 50ms retry delay (constant)
- Automatic retry scheduling
- Full visibility in COORD column

### Resource Management
- One-to-One Rule (collision prevention)
- FIFO queues for locations (fairness)
- Auto-grant on release (efficiency)
- Re-entrant requests allowed (same wafer)

---

**Document Version**: 1.0
**Last Updated**: 2025-11-16
**Related**: ARCHITECTURE.md, GuardConditionsTests.cs, WaitMechanismTests.cs, SystemReadyTests.cs
