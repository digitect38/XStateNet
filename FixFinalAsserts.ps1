# Fix final remaining Assert patterns
param(
    [string]$Directory = "C:\Develop25\XStateNet\Test"
)

$files = @(
    "C:\Develop25\XStateNet\Test\UnitTest_Ping_and_Pong_Machines.cs",
    "C:\Develop25\XStateNet\Test\UnitTest_TrafficLight.cs"
)

foreach ($file in $files) {
    if (Test-Path $file) {
        $content = Get-Content $file -Raw
        $originalContent = $content
        
        Write-Host "Processing $([System.IO.Path]::GetFileName($file))..." -ForegroundColor Cyan
        
        # Fix simple Assert.That with equality comparisons
        # Pattern: Assert.That(6 == _transitionLog.Count) -> _transitionLog.Count.Should().Be(6)
        $content = $content -replace 'Assert\.That\((\d+)\s*==\s*([^)]+)\)', '$2.Should().Be($1)'
        
        # Pattern: Assert.That("text" == variable) -> variable.Should().Be("text")
        $content = $content -replace 'Assert\.That\("([^"]+)"\s*==\s*([^)]+)\)', '$2.Should().Be("$1")'
        
        # Fix Assert.That with Does.Contain
        # Pattern: Assert.That(currentState, Does.Contain("yellow"), "message") -> currentState.Should().Contain("yellow", "message")
        $content = $content -replace 'Assert\.That\(([^,]+),\s*Does\.Contain\("([^"]+)"\),\s*"([^"]+)"\)', '$1.Should().Contain("$2", "$3")'
        $content = $content -replace 'Assert\.That\(([^,]+),\s*Does\.Contain\("([^"]+)"\)\)', '$1.Should().Contain("$2")'
        
        if ($content -ne $originalContent) {
            Set-Content $file $content
            Write-Host "  Fixed assertions in $([System.IO.Path]::GetFileName($file))" -ForegroundColor Green
        } else {
            Write-Host "  No changes needed in $([System.IO.Path]::GetFileName($file))" -ForegroundColor Yellow
        }
    }
}

Write-Host "`nFinal assertion fixes complete!" -ForegroundColor Green