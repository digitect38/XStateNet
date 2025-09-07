# Comprehensive fix for remaining NUnit to XUnit/FluentAssertions conversion issues
param(
    [string]$Directory = "C:\Develop25\XStateNet\Test"
)

$files = Get-ChildItem -Path $Directory -Filter "*.cs" -Recurse

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    $originalContent = $content
    
    Write-Host "Processing $($file.Name)..." -ForegroundColor Cyan
    
    # Fix StringAssertions.BeTrue() patterns
    # Pattern: something.Should().Contain("text").BeTrue() -> something.Should().Contain("text")
    $content = $content -replace '\.Should\(\)\.Contain\([^)]+\)\.BeTrue\(\)', '.Should().Contain($1)'
    
    # Fix malformed Contains patterns
    # Pattern: currentState.Contains("text".Should().BeTrue()) -> currentState.Should().Contain("text")
    $content = $content -replace '(\w+)\.Contains\("([^"]+)"\.Should\(\)\.BeTrue\(\)\)', '$1.Should().Contain("$2")'
    
    # Fix numeric comparison patterns
    # Pattern: (x >= y).Should().BeTrue() -> x.Should().BeGreaterThanOrEqualTo(y)
    $content = $content -replace '\(([^)]+)\s*>=\s*([^)]+)\)\.Should\(\)\.BeTrue\(\)', '$1.Should().BeGreaterThanOrEqualTo($2)'
    $content = $content -replace '\(([^)]+)\s*>\s*([^)]+)\)\.Should\(\)\.BeTrue\(\)', '$1.Should().BeGreaterThan($2)'
    $content = $content -replace '\(([^)]+)\s*<=\s*([^)]+)\)\.Should\(\)\.BeTrue\(\)', '$1.Should().BeLessThanOrEqualTo($2)'
    $content = $content -replace '\(([^)]+)\s*<\s*([^)]+)\)\.Should\(\)\.BeTrue\(\)', '$1.Should().BeLessThan($2)'
    $content = $content -replace '\(([^)]+)\s*==\s*([^)]+)\)\.Should\(\)\.BeTrue\(\)', '$1.Should().Be($2)'
    $content = $content -replace '\(([^)]+)\s*!=\s*([^)]+)\)\.Should\(\)\.BeTrue\(\)', '$1.Should().NotBe($2)'
    
    # Fix simple numeric comparisons without parentheses
    $content = $content -replace '(\w+(?:\.\w+)*)\s*>=\s*(\d+)\.Should\(\)\.BeTrue\(\)', '$1.Should().BeGreaterThanOrEqualTo($2)'
    $content = $content -replace '(\w+(?:\.\w+)*)\s*>\s*(\d+)\.Should\(\)\.BeTrue\(\)', '$1.Should().BeGreaterThan($2)'
    $content = $content -replace '(\w+(?:\.\w+)*)\s*<=\s*(\d+)\.Should\(\)\.BeTrue\(\)', '$1.Should().BeLessThanOrEqualTo($2)'
    $content = $content -replace '(\w+(?:\.\w+)*)\s*<\s*(\d+)\.Should\(\)\.BeTrue\(\)', '$1.Should().BeLessThan($2)'
    
    # Fix Assert.That patterns
    # Assert.That(value, Is.EqualTo(expected)) -> value.Should().Be(expected)
    $content = $content -replace 'Assert\.That\(([^,]+),\s*Is\.EqualTo\(([^)]+)\)\)', '$1.Should().Be($2)'
    $content = $content -replace 'Assert\.That\(([^,]+),\s*Is\.Not\.EqualTo\(([^)]+)\)\)', '$1.Should().NotBe($2)'
    $content = $content -replace 'Assert\.That\(([^,]+),\s*Is\.Null\)', '$1.Should().BeNull()'
    $content = $content -replace 'Assert\.That\(([^,]+),\s*Is\.Not\.Null\)', '$1.Should().NotBeNull()'
    $content = $content -replace 'Assert\.That\(([^,]+),\s*Is\.True\)', '$1.Should().BeTrue()'
    $content = $content -replace 'Assert\.That\(([^,]+),\s*Is\.False\)', '$1.Should().BeFalse()'
    $content = $content -replace 'Assert\.That\(([^,]+),\s*Is\.GreaterThan\(([^)]+)\)\)', '$1.Should().BeGreaterThan($2)'
    $content = $content -replace 'Assert\.That\(([^,]+),\s*Is\.LessThan\(([^)]+)\)\)', '$1.Should().BeLessThan($2)'
    $content = $content -replace 'Assert\.That\(([^,]+),\s*Is\.GreaterThanOrEqualTo\(([^)]+)\)\)', '$1.Should().BeGreaterThanOrEqualTo($2)'
    $content = $content -replace 'Assert\.That\(([^,]+),\s*Is\.LessThanOrEqualTo\(([^)]+)\)\)', '$1.Should().BeLessThanOrEqualTo($2)'
    
    # Fix Assert.That with Contains
    $content = $content -replace 'Assert\.That\(([^,]+),\s*Does\.Contain\(([^)]+)\)\)', '$1.Should().Contain($2)'
    $content = $content -replace 'Assert\.That\(([^,]+),\s*Does\.Not\.Contain\(([^)]+)\)\)', '$1.Should().NotContain($2)'
    
    # Fix Assert.That with StartsWith/EndsWith
    $content = $content -replace 'Assert\.That\(([^,]+),\s*Does\.StartWith\(([^)]+)\)\)', '$1.Should().StartWith($2)'
    $content = $content -replace 'Assert\.That\(([^,]+),\s*Does\.EndWith\(([^)]+)\)\)', '$1.Should().EndWith($2)'
    
    # Fix simple Assert.That(condition) -> condition.Should().BeTrue()
    $content = $content -replace 'Assert\.That\(([^,)]+)\)(?!\s*;)', '$1.Should().BeTrue()'
    
    # Fix Assert.Pass - convert to return statement or remove
    $content = $content -replace 'Assert\.Pass\(\);?', '// Test passed - no assertion needed'
    $content = $content -replace 'Assert\.Pass\("([^"]+)"\);?', '// Test passed: $1'
    
    # Fix malformed double Should() patterns
    $content = $content -replace '\.Should\(\)\.Should\(\)', '.Should()'
    
    # Fix patterns where .Should() appears twice
    $content = $content -replace '\.Should\(\)\.BeTrue\(\)\.Should\(\)', '.Should().BeTrue()'
    
    # Fix BooleanAssertions patterns
    $content = $content -replace 'BooleanAssertions\.BeTrue\(\)', '.Should().BeTrue()'
    $content = $content -replace 'BooleanAssertions\.BeFalse\(\)', '.Should().BeFalse()'
    
    # Fix StringAssertions patterns (without BeTrue)
    $content = $content -replace 'StringAssertions\.Contain\(', '.Should().Contain('
    $content = $content -replace 'StringAssertions\.StartWith\(', '.Should().StartWith('
    $content = $content -replace 'StringAssertions\.EndWith\(', '.Should().EndWith('
    
    # Fix ObjectAssertions patterns
    $content = $content -replace 'ObjectAssertions\.Be\(', '.Should().Be('
    $content = $content -replace 'ObjectAssertions\.NotBe\(', '.Should().NotBe('
    
    # Fix NumericAssertions patterns
    $content = $content -replace 'NumericAssertions<int>\.Be\(', '.Should().Be('
    $content = $content -replace 'NumericAssertions<double>\.Be\(', '.Should().Be('
    $content = $content -replace 'NumericAssertions<float>\.Be\(', '.Should().Be('
    $content = $content -replace 'NumericAssertions<long>\.Be\(', '.Should().Be('
    
    # Fix malformed type conversions
    $content = $content -replace '\(FluentAssertions\.Primitives\.StringAssertions\)', ''
    $content = $content -replace '\(FluentAssertions\.Primitives\.BooleanAssertions\)', ''
    $content = $content -replace '\(FluentAssertions\.Numeric\.NumericAssertions<\w+>\)', ''
    
    # Clean up extra parentheses
    $content = $content -replace '\)\)\);', '));'
    $content = $content -replace '\)\)\)\);', ')));'
    
    if ($content -ne $originalContent) {
        Set-Content $file.FullName $content
        Write-Host "  Fixed assertions in $($file.Name)" -ForegroundColor Green
    } else {
        Write-Host "  No changes needed in $($file.Name)" -ForegroundColor Yellow
    }
}

Write-Host "`nAssertion fixes complete!" -ForegroundColor Green