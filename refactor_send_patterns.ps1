# PowerShell script to refactor Send() + GetActiveStateString() patterns to SendAsyncWithState()

$testFiles = @(
    "Test\UnitTest_DiagramFramework.cs",
    "Test\UnitTest_MultipleTargets.cs",
    "Test\UnitTest_AtmMachine.cs",
    "Test\UnitTest_ErrorHandling_Debug.cs",
    "Test\UnitTest_InternalTransitions.cs",
    "Test\UnitTest_SuperComplex.cs",
    "Test\UnitTest_mutualExclusive.cs",
    "Test\UnitTest_InvokeServices.cs",
    "Test\UnitTest_TrafficLight.cs",
    "SemiStandard.Tests\SemiIntegratedMachineTests.cs"
)

foreach ($file in $testFiles) {
    $filePath = Join-Path $PSScriptRoot $file
    if (Test-Path $filePath) {
        Write-Host "Processing $file..." -ForegroundColor Green

        $content = Get-Content $filePath -Raw
        $originalContent = $content

        # Pattern 1: Single Send() followed by GetActiveStateString()
        $pattern1 = '(\s+)(\w+)\.Send\(([^)]+)\);(\s*)(var\s+)?(\w+)\s*=\s*\2\.GetActiveStateString\(\);'
        $replacement1 = '$1var $6 = await $2.SendAsyncWithState($3);'

        # Pattern 2: Send() with Task.Delay followed by GetActiveStateString()
        $pattern2 = '(\s+)(\w+)\.Send\(([^)]+)\);(\s+)await Task\.Delay\(\d+\);(\s+)(var\s+)?(\w+)\s*=\s*\2\.GetActiveStateString\(\);'
        $replacement2 = '$1var $7 = await $2.SendAsyncWithState($3);'

        # Pattern 3: Multiple Send() calls followed by GetActiveStateString()
        $pattern3 = '(\s+)(\w+)\.Send\(([^)]+)\);(\s+)\2\.Send\(([^)]+)\);(\s+)(var\s+)?(\w+)\s*=\s*\2\.GetActiveStateString\(\);'
        $replacement3 = '$1await $2.SendAsync($3);$4var $8 = await $2.SendAsyncWithState($5);'

        # Apply replacements
        $content = $content -replace $pattern2, $replacement2
        $content = $content -replace $pattern3, $replacement3
        $content = $content -replace $pattern1, $replacement1

        # Fix method signatures - make them async if they contain await
        if ($content -match 'await ') {
            # Pattern for test methods
            $content = $content -replace 'public void (Test\w+)\(\)', 'public async Task $1()'
            $content = $content -replace '\[Fact\](\s+)public void', '[Fact]$1public async Task'
            $content = $content -replace '\[Theory\](\s+)public void', '[Theory]$1public async Task'
        }

        # Only write if content changed
        if ($content -ne $originalContent) {
            Set-Content $filePath $content -NoNewline
            Write-Host "  Updated $file" -ForegroundColor Yellow
        } else {
            Write-Host "  No changes needed for $file" -ForegroundColor Gray
        }
    }
}

Write-Host "`nRefactoring complete!" -ForegroundColor Green
Write-Host "Remember to:" -ForegroundColor Cyan
Write-Host "  1. Add 'using System.Threading.Tasks;' if not present" -ForegroundColor Cyan
Write-Host "  2. Build the solution to check for compilation errors" -ForegroundColor Cyan
Write-Host "  3. Run tests to ensure functionality is preserved" -ForegroundColor Cyan