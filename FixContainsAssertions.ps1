# Fix Contains assertions that were incorrectly converted
$filePath = "C:\Develop25\XStateNet\Test\UnitTest_SemiIntegrated.cs"
$content = Get-Content $filePath -Raw

# Fix patterns like: currentState.Contains("text".Should().BeTrue())
# Should be: currentState.Should().Contain("text")
$content = $content -replace 'currentState\.Contains\("([^"]+)"\.Should\(\)\.BeTrue\(\)\)', 'currentState.Should().Contain("$1")'

# Fix patterns like: visitedStates.Contains("text".Should().BeTrue())
$content = $content -replace 'visitedStates\.Contains\("([^"]+)"\.Should\(\)\.BeTrue\(\)\)', 'visitedStates.Should().Contain("$1")'

# Fix any other .Contains("xxx".Should().BeTrue()) patterns
$content = $content -replace '(\w+)\.Contains\("([^"]+)"\.Should\(\)\.BeTrue\(\)\)', '$1.Should().Contain("$2")'

Set-Content $filePath $content
Write-Host "Fixed Contains assertions"