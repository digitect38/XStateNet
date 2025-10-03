# ⚠️ IMPORTANT: Start Service First!

## The test client is waiting because the service isn't running.

### Quick Fix (2 Steps)

#### Step 1: Open Terminal 1 - Start the Service

```powershell
cd C:\Develop25\XStateNet\XStateNet.InterProcess.Service

# Build first if not already done
dotnet build -c Release

# Run the service
cd bin\Release\net9.0
.\XStateNet.InterProcess.Service.exe
```

**Wait for this message:**
```
info: InterProcess Message Bus Service started successfully
```

#### Step 2: Keep Terminal 1 Running, Open Terminal 2 - Run Tests

```powershell
cd C:\Develop25\XStateNet\XStateNet.InterProcess.TestClient
cd bin\Release\net9.0
.\XStateNet.InterProcess.TestClient.exe
```

---

## Detailed Instructions

### Terminal 1: Service (Must Start First!)

```powershell
cd C:\Develop25\XStateNet\XStateNet.InterProcess.Service\bin\Release\net9.0
.\XStateNet.InterProcess.Service.exe
```

**Expected Output:**
```
info: XStateNet.InterProcess.Service.InterProcessMessageBusWorker[0]
      InterProcess Message Bus Service starting...
info: XStateNet.InterProcess.Service.NamedPipeMessageBus[0]
      Starting Named Pipe Message Bus on pipe: XStateNet.MessageBus
info: XStateNet.InterProcess.Service.NamedPipeMessageBus[0]
      Waiting for client connection...
info: XStateNet.InterProcess.Service.InterProcessMessageBusWorker[0]
      InterProcess Message Bus Service started successfully
```

✅ **Leave this terminal running!**

### Terminal 2: Test Client (Start After Service)

```powershell
cd C:\Develop25\XStateNet\XStateNet.InterProcess.TestClient\bin\Release\net9.0
.\XStateNet.InterProcess.TestClient.exe
```

**Expected Output:**
```
╔═══════════════════════════════════════════════════════╗
║  XStateNet InterProcess Service - Test Client        ║
╚═══════════════════════════════════════════════════════╝

Select Test:
  1. Ping-Pong Test
  ...
```

---

## Using Batch Files (Easier)

### Terminal 1: Service
```powershell
cd C:\Develop25\XStateNet\XStateNet.InterProcess.Service
.\run-console.bat
```

### Terminal 2: Test Client
```powershell
cd C:\Develop25\XStateNet\XStateNet.InterProcess.TestClient
.\run-tests.bat
```

---

## Troubleshooting

### Service Won't Start

**Check if already running:**
```powershell
Get-Process | Where-Object {$_.ProcessName -like "*InterProcess*"}
```

**Check if port/pipe is in use:**
```powershell
# Kill any existing processes
Get-Process | Where-Object {$_.ProcessName -like "*XStateNet*"} | Stop-Process -Force
```

**Rebuild if necessary:**
```powershell
cd C:\Develop25\XStateNet
dotnet clean
dotnet build -c Release
```

---

## Visual Architecture

```
┌─────────────────────────────────────────┐
│  Terminal 1: SERVICE (Start First!)    │
│  .\XStateNet.InterProcess.Service.exe   │
│                                         │
│  Creates: Named Pipe                    │
│  "XStateNet.MessageBus"                 │
│                                         │
│  Status: MUST BE RUNNING ✓              │
└─────────────────┬───────────────────────┘
                  │
                  │ Clients connect here
                  │
┌─────────────────▼───────────────────────┐
│  Terminal 2: TEST CLIENT                │
│  .\XStateNet.InterProcess.TestClient    │
│                                         │
│  Connects to: Named Pipe                │
│  "XStateNet.MessageBus"                 │
│                                         │
│  Status: Can start after service ✓      │
└─────────────────────────────────────────┘
```

---

## Quick Check Script

Create this PowerShell script to check if service is running:

```powershell
# check-service.ps1
$pipeName = "XStateNet.MessageBus"
$pipes = [System.IO.Directory]::GetFiles("\\.\\pipe\\")

if ($pipes -match $pipeName) {
    Write-Host "✓ Service is running! Pipe exists: $pipeName" -ForegroundColor Green
} else {
    Write-Host "✗ Service is NOT running! Pipe not found: $pipeName" -ForegroundColor Red
    Write-Host ""
    Write-Host "Start the service first:" -ForegroundColor Yellow
    Write-Host "  cd XStateNet.InterProcess.Service\bin\Release\net9.0"
    Write-Host "  .\XStateNet.InterProcess.Service.exe"
}
```

Run with:
```powershell
.\check-service.ps1
```
