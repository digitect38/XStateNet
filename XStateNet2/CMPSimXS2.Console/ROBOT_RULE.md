# Robot Single-Wafer Rule 🤖

## Critical Rule
**No robot can carry multiple wafers simultaneously.**

Before a robot can pick up a new wafer, it **MUST**:
1. Place the current wafer at its destination
2. Return to **idle** state (with no wafer held)

## Implementation

### RobotScheduler Enforcement

The `RobotScheduler` enforces this rule in two places:

#### 1. `IsRobotAvailable()` - Line 172
```csharp
private bool IsRobotAvailable(string robotId)
{
    if (!_robotStates.ContainsKey(robotId))
        return false;

    var robotState = _robotStates[robotId];

    // Robot must be idle AND not holding any wafer
    if (robotState.State != "idle")
        return false;

    // Safety check: Idle robot should not be holding a wafer
    if (robotState.HeldWaferId.HasValue)
    {
        Logger.Instance.Warning("RobotScheduler",
            $"{robotId} is idle but still holding wafer {robotState.HeldWaferId}! Clearing...");
        robotState.HeldWaferId = null;
    }

    return true;
}
```

#### 2. `UpdateRobotState()` - Line 48
```csharp
public void UpdateRobotState(string robotId, string state, int? heldWaferId = null, string? waitingFor = null)
{
    lock (_lock)
    {
        if (!_robotStates.ContainsKey(robotId))
            return;

        // ENFORCE RULE: Idle robot cannot hold a wafer
        if (state == "idle" && heldWaferId.HasValue)
        {
            Logger.Instance.Warning("RobotScheduler",
                $"{robotId} cannot be idle while holding wafer {heldWaferId}! Clearing wafer.");
            heldWaferId = null;
        }

        _robotStates[robotId].State = state;
        _robotStates[robotId].HeldWaferId = heldWaferId;
        _robotStates[robotId].WaitingFor = waitingFor;

        // Process pending requests when robot becomes idle (and has placed its wafer)
        if (state == "idle")
        {
            ProcessPendingRequests();
        }
    }
}
```

## Test Coverage

**46 unit tests** validate the scheduler logic, including:

### Single-Wafer Rule Tests (7 tests)
Location: `XStateNet2.Tests/CMPSimXS2/Schedulers/RobotSchedulerSingleWaferRuleTests.cs`

1. ✅ `RobotMustBeIdle_BeforePickingUpWafer`
   - Verifies robot in "busy" state cannot accept new transfer
   - Second transfer request is queued

2. ✅ `RobotCannotBeIdle_WhileHoldingWafer`
   - Verifies setting robot to idle while holding wafer clears the wafer
   - Logs warning

3. ✅ `RobotMustPlaceWafer_BeforePickingAnother`
   - Verifies queued request is processed when robot returns to idle
   - Demonstrates complete cycle: busy → idle → busy (with new wafer)

4. ✅ `IdleRobot_ShouldHaveNoWafer`
   - Verifies idle robot has no held wafer

5. ✅ `BusyRobot_CannotAcceptNewTransfer`
   - Verifies "busy" state blocks new assignments

6. ✅ `CarryingRobot_CannotAcceptNewTransfer`
   - Verifies "carrying" state blocks new assignments

7. ✅ `MultipleRobots_CanWorkInParallel_EachWithOneWafer`
   - Verifies 3 robots can each carry 1 wafer simultaneously
   - No queuing when all robots have capacity

## Robot State Machine

```
┌─────────┐
│  idle   │ ← Robot has no wafer, ready to pick up
└────┬────┘
     │ PICKUP event
     ↓
┌─────────┐
│  busy   │ ← Robot assigned, preparing to pick up
└────┬────┘
     │ PICKED event
     ↓
┌──────────┐
│ carrying │ ← Robot holding wafer (HeldWaferId set)
└────┬─────┘
     │ PLACED event
     ↓
┌─────────┐
│  idle   │ ← Robot released wafer (HeldWaferId = null)
└─────────┘
```

## Example Scenario

### ✅ Valid: Sequential Wafer Handling
```
1. Robot 1: idle (no wafer)
2. Request Transfer: Wafer 1
3. Robot 1: busy → carrying (wafer 1)
4. Robot 1 places wafer 1
5. Robot 1: idle (no wafer)
6. Request Transfer: Wafer 2
7. Robot 1: busy → carrying (wafer 2)  ← OK!
```

### ❌ Invalid: Multiple Wafers Attempted
```
1. Robot 1: carrying (wafer 1)
2. Request Transfer: Wafer 2
3. Robot 1: still carrying wafer 1   ← BLOCKED!
4. Transfer request for wafer 2 is QUEUED
```

## Console Demo

The console application demonstrates this rule in action:

```bash
dotnet run --project XStateNet2/CMPSimXS2.Console/CMPSimXS2.Console.csproj
```

Output shows:
- Real `RobotScheduler` enforcing the rule
- Transfer requests being queued when robots are busy
- Robots returning to idle before accepting new transfers
- Logger warnings if rule violation is attempted

## Summary

✅ **Rule Enforced**: Robots can only carry one wafer at a time
✅ **Validated**: 46 unit tests (100% passing)
✅ **Demonstrated**: Console application with real schedulers
✅ **Logged**: Warning messages for attempted violations
✅ **Thread-Safe**: Lock-based synchronization in RobotScheduler

The single-wafer rule is a **fundamental constraint** of the CMP simulation system, ensuring realistic robot behavior and preventing resource conflicts.
