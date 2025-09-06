# PowerShell script to update Assert statements to new NUnit style

$testFiles = Get-ChildItem -Path "test" -Filter "*.cs" -File

foreach ($file in $testFiles) {
    $content = Get-Content $file.FullName -Raw
    
    # Update Assert.AreEqual(expected, actual) to Assert.That(actual, Is.EqualTo(expected))
    $content = $content -replace 'Assert\.AreEqual\(([^,]+),\s*([^)]+)\)', 'Assert.That($2, Is.EqualTo($1))'
    
    # Update Assert.IsTrue(condition) to Assert.That(condition, Is.True)
    $content = $content -replace 'Assert\.IsTrue\(([^)]+)\)(?!;)', 'Assert.That($1, Is.True)'
    
    # Update Assert.IsFalse(condition) to Assert.That(condition, Is.False)
    $content = $content -replace 'Assert\.IsFalse\(([^)]+)\)', 'Assert.That($1, Is.False)'
    
    # Update Assert.IsNull(value) to Assert.That(value, Is.Null)
    $content = $content -replace 'Assert\.IsNull\(([^)]+)\)', 'Assert.That($1, Is.Null)'
    
    # Update Assert.IsNotNull(value) to Assert.That(value, Is.Not.Null)
    $content = $content -replace 'Assert\.IsNotNull\(([^)]+)\)', 'Assert.That($1, Is.Not.Null)'
    
    # Update Assert.IsEmpty(collection) to Assert.That(collection, Is.Empty)
    $content = $content -replace 'Assert\.IsEmpty\(([^)]+)\)', 'Assert.That($1, Is.Empty)'
    
    # Save the updated content
    Set-Content -Path $file.FullName -Value $content -NoNewline
    
    Write-Host "Updated: $($file.Name)"
}

Write-Host "All test files have been updated!"