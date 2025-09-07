# PowerShell script to convert NUnit tests to XUnit
param(
    [string]$FilePath
)

$content = Get-Content $FilePath -Raw

# Replace using statements
$content = $content -replace 'using NUnit\.Framework;', 'using Xunit;'

# Replace attributes
$content = $content -replace '\[TestFixture\]', ''
$content = $content -replace '\[Test\]', '[Fact]'
$content = $content -replace '\[SetUp\]', ''
$content = $content -replace '\[TearDown\]', ''
$content = $content -replace '\[TestCase\((.*?)\)\]', '[Theory]
    [InlineData($1)]'
$content = $content -replace '\[Theory\]\s*\[Theory\]', '[Theory]'

# Replace Setup method with constructor
if ($content -match 'public void Setup\(\)') {
    $className = if ($content -match 'public class (\w+)') { $Matches[1] } else { "TestClass" }
    $content = $content -replace 'public void Setup\(\)', "public $className()"
}

# Replace TearDown with Dispose pattern
if ($content -match 'public void TearDown\(\)') {
    # Add IDisposable to class
    $content = $content -replace '(public class \w+)', '$1 : IDisposable'
    $content = $content -replace 'public void TearDown\(\)', 'public void Dispose()'
    
    # Add using System if not present
    if ($content -notmatch 'using System;') {
        $content = "using System;`n$content"
    }
}

# Replace common NUnit assertions with FluentAssertions
# Add FluentAssertions using if needed
if ($content -match 'Assert\.' -and $content -notmatch 'using FluentAssertions;') {
    $content = $content -replace '(using Xunit;)', '$1
using FluentAssertions;'
}

# Basic assertion replacements
$content = $content -replace 'Assert\.That\(([^,]+), Is\.EqualTo\(([^)]+)\)\)', '$1.Should().Be($2)'
$content = $content -replace 'Assert\.That\(([^,]+), Is\.Not\.EqualTo\(([^)]+)\)\)', '$1.Should().NotBe($2)'
$content = $content -replace 'Assert\.That\(([^,]+), Is\.Null\)', '$1.Should().BeNull()'
$content = $content -replace 'Assert\.That\(([^,]+), Is\.Not\.Null\)', '$1.Should().NotBeNull()'
$content = $content -replace 'Assert\.That\(([^,]+), Is\.True\)', '$1.Should().BeTrue()'
$content = $content -replace 'Assert\.That\(([^,]+), Is\.False\)', '$1.Should().BeFalse()'
$content = $content -replace 'Assert\.That\(([^,]+), Is\.GreaterThan\(([^)]+)\)\)', '$1.Should().BeGreaterThan($2)'
$content = $content -replace 'Assert\.That\(([^,]+), Is\.LessThan\(([^)]+)\)\)', '$1.Should().BeLessThan($2)'
$content = $content -replace 'Assert\.That\(([^,]+), Does\.Contain\(([^)]+)\)\)', '$1.Should().Contain($2)'
$content = $content -replace 'Assert\.IsTrue\(([^)]+)\)', '$1.Should().BeTrue()'
$content = $content -replace 'Assert\.IsFalse\(([^)]+)\)', '$1.Should().BeFalse()'
$content = $content -replace 'Assert\.IsNull\(([^)]+)\)', '$1.Should().BeNull()'
$content = $content -replace 'Assert\.IsNotNull\(([^)]+)\)', '$1.Should().NotBeNull()'
$content = $content -replace 'Assert\.AreEqual\(([^,]+), ([^)]+)\)', '$2.Should().Be($1)'
$content = $content -replace 'Assert\.AreNotEqual\(([^,]+), ([^)]+)\)', '$2.Should().NotBe($1)'
$content = $content -replace 'Assert\.Throws<([^>]+)>\(([^)]+)\)', 'Assert.Throws<$1>($2)'

Set-Content $FilePath $content
Write-Host "Converted $FilePath"