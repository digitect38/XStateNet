# Install XStateNet InterProcess Message Bus as Windows Service
# Run as Administrator

param(
    [string]$ServiceName = "XStateNetMessageBus",
    [string]$DisplayName = "XStateNet InterProcess Message Bus",
    [string]$Description = "Message bus service for XStateNet InterProcess communication",
    [string]$BinPath = "",
    [string]$StartupType = "Automatic"
)

# Check if running as administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "This script must be run as Administrator"
    exit 1
}

# Default bin path if not provided
if ([string]::IsNullOrEmpty($BinPath)) {
    $BinPath = Join-Path $PSScriptRoot "bin\Release\net9.0\XStateNet.InterProcess.Service.exe"
}

# Check if executable exists
if (-not (Test-Path $BinPath)) {
    Write-Error "Executable not found at: $BinPath"
    Write-Host "Please build the project first: dotnet publish -c Release"
    exit 1
}

Write-Host "Installing XStateNet InterProcess Message Bus Service..." -ForegroundColor Green
Write-Host "Service Name: $ServiceName" -ForegroundColor Cyan
Write-Host "Display Name: $DisplayName" -ForegroundColor Cyan
Write-Host "Executable: $BinPath" -ForegroundColor Cyan
Write-Host ""

# Check if service already exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Service already exists. Stopping and removing..." -ForegroundColor Yellow

    if ($existingService.Status -eq 'Running') {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }

    sc.exe delete $ServiceName
    Start-Sleep -Seconds 2
}

# Create the service
Write-Host "Creating service..." -ForegroundColor Green
New-Service -Name $ServiceName `
    -BinaryPathName $BinPath `
    -DisplayName $DisplayName `
    -Description $Description `
    -StartupType $StartupType

# Start the service
Write-Host "Starting service..." -ForegroundColor Green
Start-Service -Name $ServiceName

# Wait for service to start
Start-Sleep -Seconds 2

# Check service status
$service = Get-Service -Name $ServiceName
if ($service.Status -eq 'Running') {
    Write-Host ""
    Write-Host "✓ Service installed and started successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Service Status: $($service.Status)" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Commands:" -ForegroundColor Yellow
    Write-Host "  Check status:  Get-Service $ServiceName" -ForegroundColor White
    Write-Host "  Stop service:  Stop-Service $ServiceName" -ForegroundColor White
    Write-Host "  Start service: Start-Service $ServiceName" -ForegroundColor White
    Write-Host "  View logs:     Get-EventLog -LogName Application -Source XStateNet.InterProcess -Newest 20" -ForegroundColor White
    Write-Host ""
} else {
    Write-Error "Service failed to start. Status: $($service.Status)"
    Write-Host "Check Event Viewer for errors: Application Log -> Source: XStateNet.InterProcess"
    exit 1
}
