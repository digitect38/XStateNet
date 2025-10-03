# Minimal Named Pipe Test
# This tests if Named Pipes work at all on your system

$pipeName = "XStateNet.MessageBus"

Write-Host "Testing Named Pipe Connection..." -ForegroundColor Cyan
Write-Host ""

# Check if pipe exists
$pipes = [System.IO.Directory]::GetFiles("\\.\\pipe\\")
$pipeExists = $pipes -match $pipeName

if ($pipeExists) {
    Write-Host "✓ Pipe exists: $pipeName" -ForegroundColor Green
} else {
    Write-Host "✗ Pipe NOT found: $pipeName" -ForegroundColor Red
    Write-Host "Make sure the service is running first!" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "Attempting to connect to pipe..." -ForegroundColor Yellow

try {
    $pipeClient = New-Object System.IO.Pipes.NamedPipeClientStream(".", $pipeName, [System.IO.Pipes.PipeDirection]::InOut)

    Write-Host "Created pipe client, connecting..." -ForegroundColor Gray

    # Try to connect with 5 second timeout
    $pipeClient.Connect(5000)

    Write-Host "✓ SUCCESS: Connected to pipe!" -ForegroundColor Green
    Write-Host ""
    Write-Host "The pipe is working correctly." -ForegroundColor Green
    Write-Host "The issue must be in the C# client code." -ForegroundColor Yellow

    $pipeClient.Close()
    $pipeClient.Dispose()

} catch [System.TimeoutException] {
    Write-Host "✗ TIMEOUT: Could not connect within 5 seconds" -ForegroundColor Red
    Write-Host ""
    Write-Host "Possible causes:" -ForegroundColor Yellow
    Write-Host "  1. Service not accepting connections" -ForegroundColor White
    Write-Host "  2. Another client already connected and blocking" -ForegroundColor White
    Write-Host "  3. Named Pipe configuration issue" -ForegroundColor White

} catch {
    Write-Host "✗ ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Full error:" -ForegroundColor Yellow
    Write-Host $_ -ForegroundColor Gray
}

Write-Host ""
