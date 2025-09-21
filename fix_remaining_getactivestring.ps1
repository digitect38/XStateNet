# PowerShell script to fix remaining GetActiveStateString() patterns

$filePath = "SemiStandard.Tests\SemiIntegratedMachineTests.cs"
$fullPath = Join-Path $PSScriptRoot $filePath

if (Test-Path $fullPath) {
    Write-Host "Processing remaining GetActiveStateString patterns..." -ForegroundColor Green

    $content = Get-Content $fullPath -Raw
    $originalContent = $content

    # Pattern 1: _stateMachine!.Send("EVENT"); _stateMachine.GetActiveStateString().Should()...
    $pattern1 = '(\s+)(_stateMachine!\.Send\([^)]+\);)\s+(_stateMachine\.GetActiveStateString\(\)\.Should\(\)[^;]+;)'
    $replacement1 = '$1var currentState = await $2$1currentState.Should().Contain($3'

    # Pattern 2: _stateMachine!.Send("EVENT"); _stateMachine!.Send("EVENT2"); _stateMachine.GetActiveStateString().Should()...
    $pattern2 = '(\s+)(_stateMachine!\.Send\([^)]+\);)\s+(_stateMachine!\.Send\([^)]+\);)\s+(_stateMachine\.GetActiveStateString\(\)\.Should\(\)[^;]+;)'
    $replacement2 = '$1await $2$1var currentState = await $3$1currentState.Should().Contain($4'

    # Pattern 3: Standalone _stateMachine.GetActiveStateString().Should()...
    $pattern3 = '(\s+)(_stateMachine\.GetActiveStateString\(\)\.Should\(\)[^;]+;)'
    $replacement3 = '$1var currentState = _stateMachine.GetActiveStateString();$1currentState.Should()' + $content.Substring($content.IndexOf('.Should()') + 9)

    # Apply specific transformations for the patterns found

    # Fix line 859: var currentState = _stateMachine!.GetActiveStateString();
    $content = $content -replace '(\s+)(var currentState = _stateMachine!\.GetActiveStateString\(\);)', '$1var currentState = _stateMachine!.GetActiveStateString();'

    # Fix patterns like: _stateMachine!.Send("EVENT"); _stateMachine.GetActiveStateString().Should().Contain...
    $content = $content -replace '(\s+)(_stateMachine!\.Send\([^)]+\));\s+(_stateMachine\.GetActiveStateString\(\)\.Should\(\)\.Contain\([^)]+\));', '$1var currentState = await $2AsyncWithState($3;$1currentState.Should().Contain($4);'

    # More specific pattern matching for the actual content
    $lines = $content -split "`r?`n"
    $newLines = @()
    $i = 0

    while ($i -lt $lines.Length) {
        $line = $lines[$i]

        # Check if this line contains Send followed by GetActiveStateString on next line
        if ($line -match '^\s+_stateMachine!?\.Send\([^)]+\);$' -and $i + 1 -lt $lines.Length) {
            $nextLine = $lines[$i + 1]
            if ($nextLine -match '^\s+_stateMachine\.GetActiveStateString\(\)\.Should\(\)\.Contain\([^)]+\);$') {
                # Transform Send + GetActiveStateString to SendAsyncWithState
                $indentation = [regex]::Match($line, '^\s+').Value
                $sendCall = [regex]::Match($line, '_stateMachine!?\.Send\(([^)]+)\)').Groups[1].Value
                $shouldCall = [regex]::Match($nextLine, '\.Should\(\)\.Contain\([^)]+\)').Value

                $newLines += "$indentation" + "var currentState = await _stateMachine!.SendAsyncWithState($sendCall);"
                $newLines += "$indentation" + "currentState$shouldCall;"
                $i += 2
                continue
            }
        }

        # Check for standalone GetActiveStateString calls
        if ($line -match '^\s+_stateMachine\.GetActiveStateString\(\)\.Should\(\)\.Contain\([^)]+\);$') {
            $indentation = [regex]::Match($line, '^\s+').Value
            $shouldCall = [regex]::Match($line, '\.Should\(\)\.Contain\([^)]+\)').Value

            $newLines += "$indentation" + "var currentState = _stateMachine.GetActiveStateString();"
            $newLines += "$indentation" + "currentState$shouldCall;"
            $i++
            continue
        }

        $newLines += $line
        $i++
    }

    $content = $newLines -join "`r`n"

    # Fix method signatures - make them async if they contain await
    if ($content -match 'await ') {
        # Pattern for test methods
        $content = $content -replace 'public void (Test\w+)\(\)', 'public async Task $1()'
        $content = $content -replace '\[Fact\](\s+)public void', '[Fact]$1public async Task'
        $content = $content -replace '\[Theory\](\s+)public void', '[Theory]$1public async Task'
    }

    # Only write if content changed
    if ($content -ne $originalContent) {
        Set-Content $fullPath $content -NoNewline
        Write-Host "  Updated $filePath" -ForegroundColor Yellow
    } else {
        Write-Host "  No changes needed for $filePath" -ForegroundColor Gray
    }
}

Write-Host "`nRefactoring complete!" -ForegroundColor Green