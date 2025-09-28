$testDir = "C:\Develop25\XStateNet\Test"

# Get all test files with compilation errors
$files = Get-ChildItem -Path $testDir -Filter "*.cs" -Recurse

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    $modified = $false

    # Pattern 1: CreateFromScript with guidIsolate named parameter
    # Fix: CreateFromScript(json, guidIsolate: true, actionMap) -> CreateFromScript(json, true, true, actionMap)
    if ($content -match 'CreateFromScript\([^,]+,\s*guidIsolate:\s*true,\s*(\w+)\)') {
        $content = $content -replace 'CreateFromScript\(([^,]+),\s*guidIsolate:\s*true,\s*(\w+)\)', 'CreateFromScript($1, true, true, $2)'
        $modified = $true
    }

    # Pattern 2: CreateFromScript with guidIsolate and guardMap
    # Fix: CreateFromScript(json, guidIsolate: true, actionMap, guardMap) -> CreateFromScript(json, true, true, actionMap, guardMap)
    if ($content -match 'CreateFromScript\([^,]+,\s*guidIsolate:\s*true,\s*(\w+),\s*(\w+)\)') {
        $content = $content -replace 'CreateFromScript\(([^,]+),\s*guidIsolate:\s*true,\s*(\w+),\s*(\w+)\)', 'CreateFromScript($1, true, true, $2, $3)'
        $modified = $true
    }

    # Pattern 3: CreateFromScript with existing machine and wrong params
    # This needs the new signature: CreateFromScript(sm, json, customMachineId, threadSafe, actionMap, guardMap...)
    if ($content -match 'StateMachineFactory\.CreateFromScript\((\w+),\s*(\w+),\s*(\w+),\s*(\w+)\)' -and $content -notmatch 'customMachineId:') {
        # For cases like: CreateFromScript(sm, json, actionMap, guardMap)
        # Change to: CreateFromScript(sm, json, null, false, actionMap, guardMap)
        $content = $content -replace 'StateMachineFactory\.CreateFromScript\((\w+),\s*(\w+),\s*(\w+),\s*(\w+)\)', 'StateMachineFactory.CreateFromScript($1, $2, null, false, $3, $4)'
        $modified = $true
    }

    # Pattern 4: Fix UnitTest_SuperComplex specific issue
    if ($file.Name -eq "UnitTest_SuperComplex.cs") {
        # The issue is it's calling with wrong order: CreateFromScript(json, true, actionMap, guardMap)
        # Should be: CreateFromScript(json, true, true, actionMap, guardMap)
        $content = $content -replace 'StateMachineFactory\.CreateFromScript\(script,\s*true,\s*actions,\s*guards\)', 'StateMachineFactory.CreateFromScript(script, true, true, actions, guards)'
        $modified = $true
    }

    if ($modified) {
        Set-Content -Path $file.FullName -Value $content -NoNewline
        Write-Host "Fixed: $($file.Name)"
    }
}

Write-Host "Compilation fixes completed."