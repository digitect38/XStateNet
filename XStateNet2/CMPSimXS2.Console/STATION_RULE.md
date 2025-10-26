# Station Single-Wafer Rule ⚙️

## Critical Rule
**No station can hold multiple wafers simultaneously.**

Each station (Polisher, Cleaner, Buffer) can only process **ONE wafer at a time**.

## Parallel Processing
Stations work **in parallel** with each other, but each individual station is **exclusive**:

```
✅ VALID: Parallel Processing
┌──────────┐     ┌──────────┐     ┌──────────┐
│ Polisher │     │ Cleaner  │     │  Buffer  │
│ Wafer 1  │     │ Wafer 2  │     │ Wafer 3  │
└──────────┘     └──────────┘     └──────────┘
Each station has ONE wafer - working in parallel

❌ INVALID: Multiple Wafers in One Station
┌──────────┐
│ Polisher │
│ Wafer 1  │  ← Cannot have multiple wafers
│ Wafer 2  │  ← in same station!
└──────────┘
```

## Implementation

### WaferJourneyScheduler Enforcement

The `WaferJourneyScheduler` enforces this rule in two places:

#### 1. `StartNextWaferIfPossible()` - Line 130
Checks if Polisher is idle before starting a new wafer:

```csharp
private void StartNextWaferIfPossible()
{
    // Check if we have more wafers to start
    if (_nextWaferToStart > _wafers.Count)
        return;

    // ENFORCE RULE: Polisher must be idle (not processing any wafer)
    var polisher = GetStation("Polisher");
    if (polisher == null || polisher.CurrentState != "idle")
        return; // ← Station busy, cannot start new wafer

    // Safety check: Idle station should not have a wafer
    if (polisher.CurrentWafer.HasValue)
    {
        Logger.Instance.Warning("WaferJourneyScheduler",
            $"Polisher is idle but still has wafer {polisher.CurrentWafer}! Clearing...");
        polisher.CurrentWafer = null;
    }

    // Start next wafer
    var wafer = _wafers.FirstOrDefault(w => w.Id == _nextWaferToStart);
    if (wafer != null && wafer.JourneyStage == "InCarrier")
    {
        RequestTransferToPolisher(wafer);
        _nextWaferToStart++;
    }
}
```

#### 2. `OnTransferCompleted()` - Line 253
Validates station doesn't already have a wafer before loading:

```csharp
private void OnTransferCompleted(int waferId, string arrivedAt, string nextStage)
{
    var wafer = _wafers.FirstOrDefault(w => w.Id == waferId);
    if (wafer == null) return;

    Logger.Instance.Info("WaferJourneyScheduler",
        $"[Wafer {waferId}] Arrived at {arrivedAt}, transitioning to {nextStage}");

    wafer.CurrentStation = arrivedAt;
    wafer.JourneyStage = nextStage;
    _wafersInTransit.Remove(waferId);

    // Load into destination station if needed
    var station = GetStation(arrivedAt);
    if (station?.StateMachine != null &&
        (arrivedAt == "Polisher" || arrivedAt == "Cleaner" || arrivedAt == "Buffer"))
    {
        // ENFORCE RULE: Station must be idle before loading a wafer
        if (station.CurrentWafer.HasValue && station.CurrentWafer.Value != waferId)
        {
            Logger.Instance.Error("WaferJourneyScheduler",
                $"RULE VIOLATION: {arrivedAt} already has wafer {station.CurrentWafer}, cannot load wafer {waferId}!");
            return; // ← Reject loading second wafer
        }

        var eventName = arrivedAt == "Buffer" ? "STORE_WAFER" : "LOAD_WAFER";
        var eventData = new Dictionary<string, object> { ["wafer"] = waferId };
        station.StateMachine.Tell(new SendEvent(eventName, eventData));
    }
}
```

## Test Coverage

**56 unit tests** validate the scheduler logic, including:

### Station Single-Wafer Rule Tests (10 tests)
Location: `XStateNet2.Tests/CMPSimXS2/Schedulers/StationSingleWaferRuleTests.cs`

1. ✅ `Polisher_CanOnlyProcessOneWafer_AtATime`
   - Polisher processing wafer 1 blocks wafer 2 from starting

2. ✅ `Cleaner_CanOnlyProcessOneWafer_AtATime`
   - Cleaner occupied by wafer 1 cannot accept wafer 2

3. ✅ `Buffer_CanOnlyStoreOneWafer_AtATime`
   - Buffer holding wafer 1 cannot store wafer 2

4. ✅ `Station_MustBeIdle_BeforeAcceptingNewWafer`
   - Station must complete current wafer before accepting new one

5. ✅ `MultipleStations_CanWorkInParallel_EachWithOneWafer`
   - Polisher, Cleaner, Buffer each process one wafer simultaneously
   - Demonstrates parallel processing capability

6. ✅ `Station_CanAcceptNewWafer_AfterCurrentCompletes`
   - After wafer 1 completes, station can accept wafer 2

7. ✅ `IdleStation_ShouldNotHaveWafer`
   - Idle stations must have CurrentWafer = null

8. ✅ `ProcessingStation_MustHaveWafer`
   - Processing stations must have valid CurrentWafer ID

9. ✅ `Station_CannotAccept_IfAlreadyHasWafer`
   - Station with wafer 1 cannot accept wafer 2

10. ✅ `WaferMustWait_UntilStationAvailable`
    - Wafers queue when station is occupied
    - Proceed when station becomes idle

## Station State Machine

```
┌─────────┐
│  idle   │ ← Station ready, no wafer (CurrentWafer = null)
└────┬────┘
     │ LOAD_WAFER / STORE_WAFER
     ↓
┌────────────┐
│ processing │ ← Station working on wafer (CurrentWafer = ID)
│ occupied   │    (processing for Polisher/Cleaner, occupied for Buffer)
└─────┬──────┘
      │ COMPLETE / UNLOAD_WAFER / RETRIEVE_WAFER
      ↓
┌─────────┐
│  done   │ ← Processing complete, wafer ready for pickup
└────┬────┘
     │ UNLOAD_WAFER
     ↓
┌─────────┐
│  idle   │ ← Station empty again, ready for next wafer
└─────────┘
```

## Example Scenarios

### ✅ Valid: Sequential Processing
```
Time 1: Polisher idle → Load wafer 1
Time 2: Polisher processing wafer 1
Time 3: Polisher done → Unload wafer 1
Time 4: Polisher idle → Load wafer 2  ← OK!
```

### ✅ Valid: Parallel Processing (Different Stations)
```
Time 1:
  Polisher: processing wafer 1  ← OK
  Cleaner:  processing wafer 2  ← OK
  Buffer:   occupied with wafer 3  ← OK

All stations working in parallel, each with ONE wafer
```

### ❌ Invalid: Multiple Wafers in Same Station
```
Time 1: Polisher processing wafer 1
Time 2: Try to load wafer 2 into Polisher  ← BLOCKED!
Result: Wafer 2 must wait until Polisher is idle
```

## Relationship with Robot Rule

Both rules work together:

| Resource | Rule | Parallelism |
|----------|------|-------------|
| **Robot** | Can carry only 1 wafer | 3 robots work in parallel |
| **Station** | Can hold only 1 wafer | 3 stations work in parallel |

**Example of both rules:**
```
Wafer 1: Polisher (processing) ← Station occupied
Wafer 2: Robot 1 (carrying) ← Robot occupied
Wafer 3: Cleaner (processing) ← Different station, OK

Wafer 4: Waiting ← Polisher busy with wafer 1
```

## Console Demo

The console application demonstrates this rule in action:

```bash
dotnet run --project XStateNet2/CMPSimXS2.Console/CMPSimXS2.Console.csproj
```

Output shows:
- Real `WaferJourneyScheduler` enforcing the rule
- Stations only accepting one wafer at a time
- Stations working in parallel (Polisher + Cleaner + Buffer)
- Logger warnings if rule violation is attempted

## Summary

✅ **Rule Enforced**: Stations can only hold one wafer at a time
✅ **Parallel Work**: Multiple stations can work simultaneously
✅ **Validated**: 56 unit tests (100% passing)
✅ **Logged**: Error messages for attempted violations
✅ **Thread-Safe**: Lock-based synchronization in WaferJourneyScheduler

The single-wafer-per-station rule ensures:
- **Resource integrity**: No station overload
- **Realistic simulation**: Matches physical equipment constraints
- **Predictable scheduling**: Clear station availability
- **Error detection**: Violations caught and logged

This rule, combined with the robot single-wafer rule, creates a robust and realistic CMP simulation system.
