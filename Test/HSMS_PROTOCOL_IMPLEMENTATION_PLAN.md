# HSMS Protocol Implementation Plan for FakeHsmsServer

## Overview

Implement full SEMI E37 HSMS protocol support in `FakeHsmsServer` to enable realistic testing of `ImprovedResilientHsmsConnection`.

## Current Status

### What Works ✅
- TCP connection accept/refuse
- Immediate disconnect detection
- Event-driven test synchronization
- Fast tests (8s for 3 tests)

### What's Missing ❌
- **HSMS protocol handshake** (SELECT.REQ → SELECT.RSP)
- **Link testing** (LINKTEST.REQ → LINKTEST.RSP)
- **Disconnect handling** (DESELECT.REQ/RSP)
- **Separation** (SEPARATE.REQ)
- **Message parsing** from NetworkStream

## HSMS Message Format (SEMI E37)

### Message Structure

```
┌─────────────────────────────────────────────────────────┐
│                    Total Message                         │
├──────────────┬──────────────────────────────────────────┤
│ Length (4B)  │              Header + Data               │
├──────────────┼──────────────┬───────────────────────────┤
│              │ Header (10B) │      Data (variable)      │
└──────────────┴──────────────┴───────────────────────────┘

Header breakdown:
Bytes 0-3:   Message Length (4 bytes, big-endian) = Header (10) + Data length
Bytes 4-5:   Session ID (2 bytes, big-endian)
Byte 6:      Stream (1 byte)
Byte 7:      Function (1 byte)
Byte 8:      Message Type (PType/SType)
Byte 9:      Reserved (0x00)
Bytes 10-13: System Bytes (4 bytes, big-endian)
Bytes 14+:   Data (optional)
```

### Message Types

```csharp
public enum HsmsMessageType : byte
{
    DataMessage = 0,    // SECS-II data message
    SelectReq = 1,      // SELECT.REQ - initiate HSMS session
    SelectRsp = 2,      // SELECT.RSP - acknowledge SELECT
    DeselectReq = 3,    // DESELECT.REQ - end HSMS session
    DeselectRsp = 4,    // DESELECT.RSP - acknowledge DESELECT
    LinktestReq = 5,    // LINKTEST.REQ - test connection
    LinktestRsp = 6,    // LINKTEST.RSP - acknowledge LINKTEST
    RejectReq = 7,      // REJECT.REQ - reject message
    SeparateReq = 9     // SEPARATE.REQ - abnormal disconnect
}
```

## Handshake Sequence

### 1. Connection Establishment

```
Client (Active)              Server (Passive)
      │                             │
      ├──── TCP Connect ────────────>
      │                             │
      │<────── TCP Accept ───────────┤
      │                             │
```

### 2. SELECT Handshake

```
Client                       Server
  │                             │
  ├──── SELECT.REQ ────────────>  (SessionId=0xFFFF, SystemBytes=unique)
  │                             │
  │<────── SELECT.RSP ───────────  (Same SystemBytes, SelectStatus=0)
  │                             │
  [State: Selected]           [State: Selected]
```

### 3. Link Testing (Periodic)

```
Client                       Server
  │                             │
  ├──── LINKTEST.REQ ──────────>  (SystemBytes=unique)
  │                             │
  │<────── LINKTEST.RSP ─────────  (Same SystemBytes)
  │                             │
```

### 4. Disconnect Sequence

```
Client                       Server
  │                             │
  ├──── DESELECT.REQ ──────────>  (SystemBytes=unique)
  │                             │
  │<────── DESELECT.RSP ─────────  (Same SystemBytes, DeselectStatus=0)
  │                             │
  ├──── TCP Close ─────────────>
  │                             │
```

## Implementation Plan

### Phase 1: HSMS Message Utilities

Create helper class for HSMS message operations:

```csharp
public static class HsmsMessageHelper
{
    // Read HSMS message from NetworkStream
    public static async Task<HsmsMessage?> ReadMessageAsync(
        NetworkStream stream,
        CancellationToken cancellationToken);

    // Write HSMS message to NetworkStream
    public static async Task WriteMessageAsync(
        NetworkStream stream,
        HsmsMessage message,
        CancellationToken cancellationToken);

    // Create SELECT.RSP from SELECT.REQ
    public static HsmsMessage CreateSelectResponse(HsmsMessage request);

    // Create LINKTEST.RSP from LINKTEST.REQ
    public static HsmsMessage CreateLinktestResponse(HsmsMessage request);

    // Create DESELECT.RSP from DESELECT.REQ
    public static HsmsMessage CreateDeselectResponse(HsmsMessage request);
}
```

### Phase 2: Update FakeHsmsServer

Modify `HandleClientAsync` to implement full protocol:

```csharp
private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
{
    try
    {
        var stream = client.GetStream();

        switch (_behavior)
        {
            case FakeServerBehavior.AcceptAndRespond:
                await HandleFullProtocolAsync(stream, cancellationToken);
                break;

            case FakeServerBehavior.AcceptAndCloseImmediately:
                _output.WriteLine($"[FakeServer] Closing connection immediately");
                client.Close();
                break;

            case FakeServerBehavior.DelayThenAccept:
                await Task.Delay(1000, cancellationToken);
                await HandleFullProtocolAsync(stream, cancellationToken);
                break;

            case FakeServerBehavior.RefuseConnection:
                client.Close();
                break;
        }
    }
    catch (OperationCanceledException)
    {
        // Expected
    }
    finally
    {
        client.Dispose();
    }
}

private async Task HandleFullProtocolAsync(NetworkStream stream, CancellationToken cancellationToken)
{
    // 1. Wait for SELECT.REQ
    var selectReq = await HsmsMessageHelper.ReadMessageAsync(stream, cancellationToken);
    if (selectReq?.MessageType != HsmsMessageType.SelectReq)
    {
        _output.WriteLine($"[FakeServer] Expected SELECT.REQ, got {selectReq?.MessageType}");
        return;
    }

    _output.WriteLine($"[FakeServer] Received SELECT.REQ (SystemBytes: 0x{selectReq.SystemBytes:X8})");

    // 2. Send SELECT.RSP
    var selectRsp = HsmsMessageHelper.CreateSelectResponse(selectReq);
    await HsmsMessageHelper.WriteMessageAsync(stream, selectRsp, cancellationToken);
    _output.WriteLine($"[FakeServer] Sent SELECT.RSP");

    // 3. Enter message loop (handle LINKTEST, DESELECT, etc.)
    await MessageLoopAsync(stream, cancellationToken);
}

private async Task MessageLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        var message = await HsmsMessageHelper.ReadMessageAsync(stream, cancellationToken);
        if (message == null)
            break; // Connection closed

        switch (message.MessageType)
        {
            case HsmsMessageType.LinktestReq:
                var linktestRsp = HsmsMessageHelper.CreateLinktestResponse(message);
                await HsmsMessageHelper.WriteMessageAsync(stream, linktestRsp, cancellationToken);
                _output.WriteLine($"[FakeServer] LINKTEST.REQ/RSP exchange");
                break;

            case HsmsMessageType.DeselectReq:
                var deselectRsp = HsmsMessageHelper.CreateDeselectResponse(message);
                await HsmsMessageHelper.WriteMessageAsync(stream, deselectRsp, cancellationToken);
                _output.WriteLine($"[FakeServer] DESELECT.REQ/RSP exchange");
                return; // Exit loop after deselect

            case HsmsMessageType.SeparateReq:
                _output.WriteLine($"[FakeServer] Received SEPARATE.REQ");
                return; // Abnormal disconnect

            default:
                _output.WriteLine($"[FakeServer] Unexpected message type: {message.MessageType}");
                break;
        }
    }
}
```

### Phase 3: Tests to Unskip

Once protocol is implemented, unskip these tests:

1. **Connection_WithFakeServer_ConnectsSuccessfully**
   - Should now complete SELECT handshake
   - Expected: `Connected` state reached

2. **Connection_CancellationDuringConnect_CancelsCleanly**
   - Test cancellation during SELECT handshake
   - Expected: Cancellation honored, no hanging

3. **Connection_CircuitBreaker_OpensAfterFailures**
   - Test circuit breaker with real connection failures
   - Expected: Circuit opens after threshold failures

4. **Connection_MultipleConnectAttempts_ReturnsConsistentResult**
   - Test concurrent connection attempts
   - Expected: All return same result (true after SELECT)

## Implementation Steps

### Step 1: Create HsmsMessageHelper class

File: `Test/Infrastructure/HsmsMessageHelper.cs`

- [x] ReadMessageAsync (read length + header + data)
- [x] WriteMessageAsync (encode and write)
- [x] CreateSelectResponse
- [x] CreateLinktestResponse
- [x] CreateDeselectResponse

### Step 2: Update FakeHsmsServer

File: `Test/ResilientHsmsConnectionWithFakeServerTests.cs`

- [x] Add HandleFullProtocolAsync method
- [x] Add MessageLoopAsync method
- [x] Update HandleClientAsync to use protocol handler
- [x] Add protocol logging

### Step 3: Test with Real Connection

- [x] Run Connection_ServerRefusesConnection_FailsGracefully (should still pass)
- [x] Run Connection_ServerClosesImmediately_DetectsDisconnection (should still pass)
- [x] Unskip Connection_WithFakeServer_ConnectsSuccessfully
- [x] Verify SELECT handshake completes

### Step 4: Unskip Remaining Tests

- [x] Connection_CancellationDuringConnect_CancelsCleanly
- [x] Connection_CircuitBreaker_OpensAfterFailures
- [x] Connection_MultipleConnectAttempts_ReturnsConsistentResult

### Step 5: Performance Verification

- [x] Measure test duration (target: < 10 seconds total)
- [x] Verify event-driven synchronization still works
- [x] Ensure no timing dependencies

## Expected Test Results After Implementation

| Test | Before | After |
|------|--------|-------|
| Connection_ServerRefusesConnection_FailsGracefully | ✅ Pass (686ms) | ✅ Pass (should remain same) |
| Connection_ServerClosesImmediately_DetectsDisconnection | ✅ Pass | ✅ Pass (should remain same) |
| Connection_DisposeWhileConnecting_CompletesQuickly | ✅ Pass | ✅ Pass (should remain same) |
| Connection_WithFakeServer_ConnectsSuccessfully | ⏭️ Skip | ✅ **Pass (new)** |
| Connection_CancellationDuringConnect_CancelsCleanly | ⏭️ Skip | ✅ **Pass (new)** |
| Connection_CircuitBreaker_OpensAfterFailures | ⏭️ Skip | ✅ **Pass (new)** |
| Connection_MultipleConnectAttempts_ReturnsConsistentResult | ⏭️ Skip | ✅ **Pass (new)** |

**Total:** 3 passing → **7 passing** (100% coverage)

## Benefits of Full Protocol Implementation

1. ✅ **Realistic testing** - Tests actual HSMS handshake
2. ✅ **Bug discovery** - Found CircuitBreakerThreshold bug
3. ✅ **Production confidence** - Validates real network behavior
4. ✅ **No mocks** - Real TCP/IP stack, real protocol
5. ✅ **Fast enough** - Optimized timeouts keep tests < 10s
6. ✅ **Event-driven** - No arbitrary delays
7. ✅ **Reusable** - Fake server can run on remote node

## Future Enhancements

1. **Standalone Fake Server Service**
   - Run as Windows Service or Linux daemon
   - Accept connections from remote tests
   - Distributed testing capability

2. **Protocol Fuzzing**
   - Invalid message sequences
   - Malformed messages
   - Timeout scenarios

3. **SECS-II Data Messages**
   - S1F1/F2 (Equipment status)
   - S2F13/F14 (Equipment constants)
   - Full SECS-II message support

---

*Created: 2025-10-04*
*Status: Ready for implementation*
*Estimated effort: 2-3 hours*
