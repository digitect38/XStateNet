# HSMS Connection Test Fix

## Overview
This document details the resolution of a race condition in the HSMS (High-Speed SECS Message Services) connection integration test where the passive connection would not be fully established before assertions were made, causing test failures.

## Problem Statement

### Test Failure
```
Test: Should_EstablishConnection_BetweenActiveAndPassive
Location: HsmsTransportTests.cs:35
Failure: Assert.True() expected true but got false (line 44)

Failed assertion:
Assert.True(_passiveConnection!.IsConnected);  // ← Returns false
```

### Observed Behavior
Despite logs showing successful connection establishment:
```
[08:56:45.580] [PASSIVE] Accepted connection from 127.0.0.1:61169
[08:56:45.581] [ACTIVE] Connected to 127.0.0.1:61168
```

The test would fail on the assertion that `_passiveConnection.IsConnected` should be true.

## Root Cause Analysis

### Race Condition in Test Setup
The test had a subtle race condition in the `StartPassiveConnectionAsync()` helper method:

```csharp
private async Task StartPassiveConnectionAsync()
{
    _passiveConnection = new HsmsConnection(
        _testEndpoint,
        HsmsConnection.HsmsConnectionMode.Passive,
        _loggerFactory.CreateLogger<HsmsConnection>());

    var connectTask = _passiveConnection.ConnectAsync();  // ← Starts async task

    // Wait for passive connection to start listening
    await DeterministicWait.WaitForConditionAsync(
        condition: () => _passiveConnection.State != HsmsConnectionState.NotConnected,
        getProgress: () => (int)_passiveConnection.State,
        timeoutSeconds: 2);

    // ← RETURNS HERE without awaiting connectTask!
    // The ConnectAsync() task is still running in the background
}
```

### The Problem
1. `ConnectAsync()` is called but NOT awaited
2. Method only waits for state to change from `NotConnected` (which happens when listener starts)
3. **Does not wait** for actual client connection to be accepted and fully established
4. Test proceeds immediately to assertions while connection is still being established

### HsmsConnection.IsConnected Property
```csharp
public bool IsConnected => _tcpClient?.Connected ?? false;
```

This property depends on `TcpClient.Connected`, which requires the TCP connection to be fully bidirectionally established. The passive connection's `AcceptTcpClientAsync()` returns a TcpClient, but there's a brief moment where the TCP connection might not be fully established yet from the passive side's perspective.

### Test Flow Breakdown

**What happened**:
```
1. StartPassiveConnectionAsync() called
   → ConnectAsync() started (async, not awaited)
   → Waits for state != NotConnected (listener started)
   → Returns while ConnectAsync still running

2. ConnectActiveConnectionAsync() called
   → ConnectAsync() awaited
   → Active side connects successfully
   → Returns with IsConnected = true

3. Assertions executed
   → _activeConnection.IsConnected = true ✓
   → _passiveConnection.IsConnected = false ✗ (still establishing!)
```

**What should happen**:
```
1. StartPassiveConnectionAsync() called
   → ConnectAsync() started
   → Waits for listener to start
   → Waits for connection to be fully established ← MISSING!

2. ConnectActiveConnectionAsync() called
   → Active side connects
   → Waits for connection to be fully established

3. Wait for both connections to reach Connected state

4. Assertions executed
   → Both connections fully established ✓
```

## Solution

### Fix Applied
Added a call to the existing `WaitForConnectionsAsync()` helper method in the test before assertions:

**File**: `C:\Develop25\XStateNet\SemiStandard.Integration.Tests\HsmsTransportTests.cs`

**Before (lines 35-50)**:
```csharp
[Fact]
public async Task Should_EstablishConnection_BetweenActiveAndPassive()
{
    // Arrange
    await StartPassiveConnectionAsync();

    // Act
    await ConnectActiveConnectionAsync();

    // Assert
    Assert.True(_passiveConnection!.IsConnected);  // ← RACE CONDITION
    Assert.True(_activeConnection!.IsConnected);
    Assert.Equal(HsmsConnection.HsmsConnectionState.Connected, _passiveConnection.State);
    Assert.Equal(HsmsConnection.HsmsConnectionState.Connected, _activeConnection.State);

    _logger.LogInformation("✓ HSMS connection established successfully");
}
```

**After (lines 35-53)**:
```csharp
[Fact]
public async Task Should_EstablishConnection_BetweenActiveAndPassive()
{
    // Arrange
    await StartPassiveConnectionAsync();

    // Act
    await ConnectActiveConnectionAsync();

    // Wait for both connections to be fully established
    await WaitForConnectionsAsync();  // ← FIX: Added deterministic wait

    // Assert
    Assert.True(_passiveConnection!.IsConnected);
    Assert.True(_activeConnection!.IsConnected);
    Assert.Equal(HsmsConnection.HsmsConnectionState.Connected, _passiveConnection.State);
    Assert.Equal(HsmsConnection.HsmsConnectionState.Connected, _activeConnection.State);

    _logger.LogInformation("✓ HSMS connection established successfully");
}
```

### WaitForConnectionsAsync() Helper
The existing helper method (already used by other tests) provides deterministic waiting:

```csharp
private async Task WaitForConnectionsAsync()
{
    // Wait for both connections to be fully established
    var connected = await DeterministicWait.WaitForConditionAsync(
        condition: () => _activeConnection?.IsConnected == true &&
                        _passiveConnection?.IsConnected == true &&
                        _activeConnection.State == HsmsConnection.HsmsConnectionState.Connected &&
                        _passiveConnection.State == HsmsConnection.HsmsConnectionState.Connected,
        getProgress: () =>
        {
            int progress = 0;
            if (_activeConnection?.IsConnected == true) progress++;
            if (_passiveConnection?.IsConnected == true) progress++;
            if (_activeConnection?.State == HsmsConnection.HsmsConnectionState.Connected) progress++;
            if (_passiveConnection?.State == HsmsConnection.HsmsConnectionState.Connected) progress++;
            return progress;
        },
        timeoutSeconds: 5,
        pollIntervalMs: 10);

    if (!connected)
        throw new TimeoutException("Connections did not fully establish within timeout");
}
```

### Why This Works
1. **Checks both IsConnected and State**: Ensures both the TcpClient connection AND the state machine state are correct
2. **Deterministic waiting**: Uses polling with progress tracking instead of arbitrary delays
3. **Timeout protection**: Throws clear exception if connections don't establish within 5 seconds
4. **Consistent pattern**: Same approach used by other tests like `Should_HandleLinktestMessage`

## Testing Results

### Before Fix
```
Test: Should_EstablishConnection_BetweenActiveAndPassive
Result: FAILED
Reason: Assert.True(_passiveConnection!.IsConnected) failed
```

### After Fix
All 6 HSMS transport tests pass:
```
✓ Should_EstablishConnection_BetweenActiveAndPassive [6 ms]
✓ Should_HandleConnectionDisconnection [4 ms]
✓ Should_HandleLinktestMessage [64 ms]
✓ Should_HandleMultipleSimultaneousMessages [11 ms]
✓ Should_SendControlMessage_SelectReqRsp [5 ms]
✓ Should_SendDataMessage_WithCorrectEncoding [4 ms]

Total: 6 tests passed
Total time: 0.89 seconds
```

## Technical Details

### HSMS Connection Modes
- **Active Mode**: Initiates TCP connection (typically Host/Computer)
- **Passive Mode**: Accepts TCP connection (typically Equipment)

### Connection State Machine
```
NotConnected → Connecting → Connected → Selected
                    ↓
                  Error
```

### TcpClient.Connected Property
From .NET documentation:
> Gets a value that indicates whether a Socket is connected to a remote host as of the last Send or Receive operation.

**Important**: This property can lag slightly behind the actual connection establishment, especially on the passive (accepting) side. This is why deterministic waiting is necessary in tests.

### DeterministicWait Pattern
The codebase uses `DeterministicWait.WaitForConditionAsync()` instead of arbitrary `Task.Delay()` calls:

**Benefits**:
- **Faster tests**: Returns immediately when condition is met
- **More reliable**: No arbitrary timeout values to tune
- **Better diagnostics**: Progress tracking shows what's waiting
- **Consistent**: Same pattern used throughout test suite

## Related Files

### Modified
- `C:\Develop25\XStateNet\SemiStandard.Integration.Tests\HsmsTransportTests.cs:44`
  - Added `await WaitForConnectionsAsync()` call

### Referenced
- `C:\Develop25\XStateNet\SemiStandard\Transport\HsmsConnection.cs`
  - HSMS connection implementation
  - Line 35: `IsConnected` property definition
  - Lines 77-108: `ConnectAsync()` method
  - Lines 127-175: `ConnectPassiveAsync()` method

- `C:\Develop25\XStateNet\XStateNet5Impl\Helpers\DeterministicWait.cs` (referenced)
  - Deterministic waiting helper utility

## Pattern Consistency

This fix aligns the `Should_EstablishConnection_BetweenActiveAndPassive` test with other tests in the suite that already used `WaitForConnectionsAsync()`:

### Tests Already Using WaitForConnectionsAsync
- `Should_HandleLinktestMessage` (line 153)
  - Comment: "Ensure both connections are fully established before sending"

### Tests NOT Using It (Now Fixed)
- ~~`Should_EstablishConnection_BetweenActiveAndPassive`~~ ✓ Fixed

## Lessons Learned

1. **Async task completion ≠ synchronous completion**: Starting an async task doesn't mean it's done when you return
2. **Test what you're testing**: A connection establishment test should wait for connections to establish
3. **Race conditions in integration tests**: Network operations are inherently asynchronous and need proper synchronization
4. **Reuse existing patterns**: The `WaitForConnectionsAsync()` helper already existed and solved this exact problem
5. **Deterministic testing**: Never rely on timing assumptions; always wait for explicit conditions

## Future Improvements

1. **Refactor StartPassiveConnectionAsync**: Consider making it await the full connection establishment internally
2. **Add connection establishment events**: HsmsConnection could fire events when fully connected
3. **Connection readiness abstraction**: Create a `WaitUntilReady()` method on HsmsConnection itself
4. **Test categorization**: Mark integration tests that require network connections for parallel test execution control

## Related Standards

- **SEMI E37**: HSMS (High-Speed SECS Message Services) Generic Services
- **TCP/IP**: Transmission Control Protocol for reliable network connections
- **xUnit**: .NET testing framework used for these integration tests

---

*Document created: 2025-10-18*
*Last updated: 2025-10-18*
*Author: Claude Code*
