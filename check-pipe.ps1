# Quick script to check if the pipe exists
$pipeName = "XStateNet.MessageBus"

Write-Host "Checking for pipe: $pipeName" -ForegroundColor Cyan
Write-Host ""

try {
    $pipes = [System.IO.Directory]::GetFiles("\\.\\pipe\\")
    Write-Host "Available pipes:"
    $pipes | ForEach-Object {
        $name = $_ -replace '.*\\', ''
        if ($name -like "*XState*") {
            Write-Host "  ✓ $name" -ForegroundColor Green
        } else {
            Write-Host "  - $name" -ForegroundColor Gray
        }
    }

    Write-Host ""

    if ($pipes -match $pipeName) {
        Write-Host "✓ SUCCESS: Pipe '$pipeName' exists!" -ForegroundColor Green
        Write-Host "The service is running correctly." -ForegroundColor Green
    } else {
        Write-Host "✗ ERROR: Pipe '$pipeName' NOT found!" -ForegroundColor Red
        Write-Host ""
        Write-Host "This means the service is NOT running or failed to create the pipe." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Start the service:" -ForegroundColor Yellow
        Write-Host "  cd XStateNet.InterProcess.Service\bin\Release\net9.0" -ForegroundColor White
        Write-Host "  .\XStateNet.InterProcess.Service.exe" -ForegroundColor White
    }
} catch {
    Write-Host "Error checking pipes: $_" -ForegroundColor Red
}
