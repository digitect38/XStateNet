# PowerShell script to fix class structure issues
param(
    [string]$Directory = "C:\Develop25\XStateNet\Test"
)

$problemFiles = @(
    "UnitTest_DiagramFramework.cs",
    "UnitTest_ComplexState.cs",
    "UnitTest_InCondition.cs",
    "UnitTest_LeaderFolower.cs",
    "unitTest_PingPongInAMachine.cs",
    "UnitTest_MultipleTargets.cs",
    "UnitTest_SuperComplex.cs",
    "UnitTest_TrafficLight.cs",
    "UnitTest_VideoPlayer.cs"
)

foreach ($fileName in $problemFiles) {
    $filePath = Join-Path $Directory $fileName
    if (Test-Path $filePath) {
        $content = Get-Content $filePath -Raw
        
        # Remove the problematic Dispose placement
        # First remove any Dispose within the class that's misplaced
        $content = $content -replace '}\s*\n\s*public void Dispose\(\)\s*{\s*//\s*Cleanup if needed\s*}\s*}', '}'
        
        # Find the last namespace closing brace and add Dispose properly
        $lastIndex = $content.LastIndexOf('}')
        if ($lastIndex -gt 0) {
            # Find class closing brace (second to last })
            $classEnd = $content.LastIndexOf('}', $lastIndex - 1)
            
            # Check if Dispose already exists properly
            if ($content -notmatch 'public void Dispose\(\)\s*\{[^}]*\}\s*\}[\s]*\}[\s]*$') {
                # Insert Dispose method before class closing brace
                $disposeMethod = "`n`n    public void Dispose()`n    {`n        // Cleanup if needed`n    }"
                $content = $content.Insert($classEnd, $disposeMethod)
            }
        }
        
        Set-Content $filePath $content
        Write-Host "Fixed $fileName"
    }
}

Write-Host "Structure fixes complete!"