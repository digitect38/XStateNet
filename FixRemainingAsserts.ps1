# PowerShell script to fix remaining NUnit Assert.That statements
param(
    [string]$Directory = "C:\Develop25\XStateNet\Test"
)

$files = Get-ChildItem -Path $Directory -Filter "*.cs" -Recurse

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    $modified = $false
    
    # Fix Assert.That with Does.Contain
    if ($content -match 'Assert\.That\([^,]+,\s*Does\.Contain\(') {
        $content = $content -replace 'Assert\.That\(([^,]+),\s*Does\.Contain\("([^"]+)"\)\)', '$1.Should().Contain("$2")'
        $modified = $true
    }
    
    # Fix Assert.That with Is.EqualTo
    if ($content -match 'Assert\.That\([^,]+,\s*Is\.EqualTo\(') {
        $content = $content -replace 'Assert\.That\(([^,]+),\s*Is\.EqualTo\(([^)]+)\)\)', '$1.Should().Be($2)'
        $modified = $true
    }
    
    # Fix Assert.That with Is.True
    if ($content -match 'Assert\.That\([^,]+,\s*Is\.True\)') {
        $content = $content -replace 'Assert\.That\(([^,]+),\s*Is\.True\)', '$1.Should().BeTrue()'
        $modified = $true
    }
    
    # Fix Assert.That with Is.False
    if ($content -match 'Assert\.That\([^,]+,\s*Is\.False\)') {
        $content = $content -replace 'Assert\.That\(([^,]+),\s*Is\.False\)', '$1.Should().BeFalse()'
        $modified = $true
    }
    
    # Fix Assert.That with Does.Not.Contain
    if ($content -match 'Assert\.That\([^,]+,\s*Does\.Not\.Contain\(') {
        $content = $content -replace 'Assert\.That\(([^,]+),\s*Does\.Not\.Contain\("([^"]+)"\)\)', '$1.Should().NotContain("$2")'
        $modified = $true
    }
    
    if ($modified) {
        Set-Content $file.FullName $content
        Write-Host "Fixed assertions in $($file.Name)"
    }
}

Write-Host "Assert fixes complete!"