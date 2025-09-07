# Fix parentheses issues in test files
$filePath = "C:\Develop25\XStateNet\Test\UnitTest_SemiIntegrated.cs"
$content = Get-Content $filePath -Raw

# Fix triple closing parentheses
$content = $content -replace '\)\)\);', '));'

# Fix double parentheses in Should().Contain patterns
$content = $content -replace '\.Should\(\)\.Contain\("([^"]+)"\)\)', '.Should().Contain("$1")'

Set-Content $filePath $content
Write-Host "Fixed parentheses issues"