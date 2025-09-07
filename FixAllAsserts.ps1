# Comprehensive NUnit to XUnit/FluentAssertions conversion
param(
    [string]$Directory = "C:\Develop25\XStateNet\Test"
)

$files = Get-ChildItem -Path $Directory -Filter "*.cs" -Recurse

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    $modified = $false
    
    # Simple Assert.IsTrue/IsFalse
    if ($content -match 'Assert\.(IsTrue|IsFalse)') {
        $content = $content -replace 'Assert\.IsTrue\(([^)]+)\)', '$1.Should().BeTrue()'
        $content = $content -replace 'Assert\.IsFalse\(([^)]+)\)', '$1.Should().BeFalse()'
        $modified = $true
    }
    
    # Assert.AreEqual
    if ($content -match 'Assert\.AreEqual') {
        $content = $content -replace 'Assert\.AreEqual\(([^,]+),\s*([^)]+)\)', '$2.Should().Be($1)'
        $modified = $true
    }
    
    # Assert.AreNotEqual
    if ($content -match 'Assert\.AreNotEqual') {
        $content = $content -replace 'Assert\.AreNotEqual\(([^,]+),\s*([^)]+)\)', '$2.Should().NotBe($1)'
        $modified = $true
    }
    
    # Assert.IsNull/IsNotNull
    if ($content -match 'Assert\.(IsNull|IsNotNull)') {
        $content = $content -replace 'Assert\.IsNull\(([^)]+)\)', '$1.Should().BeNull()'
        $content = $content -replace 'Assert\.IsNotNull\(([^)]+)\)', '$1.Should().NotBeNull()'
        $modified = $true
    }
    
    # Assert.Contains
    if ($content -match 'Assert\.Contains') {
        $content = $content -replace 'Assert\.Contains\(([^,]+),\s*([^)]+)\)', '$2.Should().Contain($1)'
        $modified = $true
    }
    
    # Assert.DoesNotThrow
    if ($content -match 'Assert\.DoesNotThrow') {
        $content = $content -replace 'Assert\.DoesNotThrow\(\(\)\s*=>\s*([^)]+)\)', 'var action = () => $1; action.Should().NotThrow()'
        $modified = $true
    }
    
    # Assert.Throws
    if ($content -match 'Assert\.Throws<([^>]+)>') {
        # Keep Assert.Throws as XUnit supports it
        $modified = $true
    }
    
    # CollectionAssert
    if ($content -match 'CollectionAssert\.') {
        $content = $content -replace 'CollectionAssert\.AreEqual\(([^,]+),\s*([^)]+)\)', '$2.Should().BeEquivalentTo($1)'
        $content = $content -replace 'CollectionAssert\.Contains\(([^,]+),\s*([^)]+)\)', '$1.Should().Contain($2)'
        $modified = $true
    }
    
    if ($modified) {
        Set-Content $file.FullName $content
        Write-Host "Fixed assertions in $($file.Name)"
    }
}

Write-Host "Comprehensive assertion conversion complete!"