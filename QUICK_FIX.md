# 🔧 Quick Fix - Test Client Stuck on Connection

## The Problem

Your test client is stuck at:
```
[ping-client] Connecting to pipe: XStateNet.MessageBus...
```

The service shows:
```
info: Client connected. Total connections: 1
info: Health Check - Connections: 1, Machines: 0
```

**This means**: Client connected BUT didn't register (Machines: 0). The client is waiting for a response that never comes.

---

## Solution: Stop, Rebuild, Restart

### Step 1: Stop the Service

In Terminal 1 (where service is running):
```
Press Ctrl+C
```

### Step 2: Rebuild with New Logging

```powershell
cd C:\Develop25\XStateNet\XStateNet.InterProcess.Service
dotnet build -c Release
```

### Step 3: Restart Service

```powershell
cd bin\Release\net9.0
.\XStateNet.InterProcess.Service.exe
```

Now you should see MORE detailed logs like:
```
dbug: Received message: {"Type":0,"Payload":{...}}
dbug: Parsed message type: Register
info: Machine registered: ping-client (PID: 12345)
```

### Step 4: Run Test Client Again

In Terminal 2:
```powershell
cd C:\Develop25\XStateNet\XStateNet.InterProcess.TestClient\bin\Release\net9.0
.\XStateNet.InterProcess.TestClient.exe
```

Select test #1 (Ping-Pong)

---

## What Should Happen

### Terminal 1 (Service) - Expected Logs:

```
dbug: Waiting for client connection...
info: Client connected. Total connections: 1
dbug: Received message: {"Type":0,"Payload":{...}}
dbug: Parsed message type: Register
info: Machine registered: ping-client (PID: 12345)
info: Machine subscribed: ping-client
dbug: Received message: {"Type":0,"Payload":{...}}
dbug: Parsed message type: Register
info: Machine registered: pong-client (PID: 12346)
dbug: Routing event: ping-client -> pong-client: PING
dbug: Routing event: pong-client -> ping-client: PONG
```

### Terminal 2 (Test Client) - Expected Logs:

```
[ping-client] Connecting to pipe: XStateNet.MessageBus...
[ping-client] ✓ Connected to InterProcess Service
[ping-client] Registering machine...
[ping-client] ✓ Machine registered successfully
[ping-client] ✓ Subscribed to events
[pong-client] Connecting to pipe: XStateNet.MessageBus...
[pong-client] ✓ Connected to InterProcess Service
...
[ping-client] Sending PING #1
[pong-client] Received PING, sending PONG back...
[ping-client] Received PONG #1
✓ Test Complete! Received 5/5 PONGs
```

---

## If Still Stuck

### Check 1: Is Service Really Running?

```powershell
Get-Process | Where-Object {$_.ProcessName -like "*InterProcess*"}
```

Should show the service process.

### Check 2: Can You See the Pipe?

```powershell
[System.IO.Directory]::GetFiles("\\.\\pipe\\") | Select-String "XStateNet"
```

Should show: `\\.\pipe\XStateNet.MessageBus`

### Check 3: Kill Everything and Start Fresh

```powershell
# Kill all processes
Get-Process | Where-Object {$_.ProcessName -like "*XStateNet*"} | Stop-Process -Force

# Clean build
cd C:\Develop25\XStateNet
dotnet clean
dotnet build -c Release

# Start service
cd XStateNet.InterProcess.Service\bin\Release\net9.0
.\XStateNet.InterProcess.Service.exe
```

---

## Alternative: Use Simpler Test

If the ping-pong test still doesn't work, try the stress test which is simpler:

```powershell
.\XStateNet.InterProcess.TestClient.exe stress
```

This should work because it's all within a single test client process.

---

## Next Steps After It Works

Once you see:
```
✓ Test Complete! Received 5/5 PONGs
```

You know the service is working correctly! Then you can:

1. Try other tests (Multi-Client, Stress)
2. Install as Windows Service for production
3. Build your own state machines using the InterProcess pattern

---

## Need More Help?

Check the full guides:
- [TESTING_INTERPROCESS_SERVICE.md](./TESTING_INTERPROCESS_SERVICE.md)
- [INTERPROCESS_SERVICE_GUIDE.md](./INTERPROCESS_SERVICE_GUIDE.md)
