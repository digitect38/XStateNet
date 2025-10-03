# Uninstall XStateNet InterProcess Message Bus Windows Service
# Run as Administrator

param(
    [string]$ServiceName = "XStateNetMessageBus"
)

# Check if running as administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "This script must be run as Administrator"
    exit 1
}

Write-Host "Uninstalling XStateNet InterProcess Message Bus Service..." -ForegroundColor Yellow
Write-Host "Service Name: $ServiceName" -ForegroundColor Cyan
Write-Host ""

# Check if service exists
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "Service '$ServiceName' not found." -ForegroundColor Yellow
    exit 0
}

# Stop the service if running
if ($service.Status -eq 'Running') {
    Write-Host "Stopping service..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force
    Start-Sleep -Seconds 2
}

# Delete the service
Write-Host "Removing service..." -ForegroundColor Yellow
sc.exe delete $ServiceName

Start-Sleep -Seconds 2

# Verify removal
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    Write-Error "Failed to remove service"
    exit 1
} else {
    Write-Host ""
    Write-Host "✓ Service uninstalled successfully!" -ForegroundColor Green
    Write-Host ""
}
