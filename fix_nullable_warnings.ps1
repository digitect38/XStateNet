# Fix common nullable warnings in test files

$testFiles = Get-ChildItem -Path "test\*.cs" -Recurse

foreach ($file in $testFiles) {
    $content = Get-Content $file.FullName -Raw
    $modified = $false
    
    # Fix _stateMachine. patterns to add null checks
    if ($content -match '_stateMachine\.') {
        $content = $content -replace '(\s+)(_stateMachine)\.Send\(', '$1$2!.Send('
        $content = $content -replace '(\s+)(_stateMachine)\.GetActiveStateString\(', '$1$2!.GetActiveStateString('
        $content = $content -replace '(\s+)(_stateMachine)\.ContextMap\[', '$1$2!.ContextMap!['
        $content = $content -replace '(\s+)(_stateMachine)\.Start\(', '$1$2!.Start('
        $content = $content -replace '(\s+)(_stateMachine)\.Stop\(', '$1$2!.Stop('
        $modified = $true
    }
    
    # Fix double !! patterns
    $content = $content -replace '!!', '!'
    
    if ($modified) {
        Set-Content $file.FullName $content -NoNewline
        Write-Host "Fixed: $($file.Name)"
    }
}

Write-Host "Completed fixing nullable warnings in test files"