# Manually fix remaining Assert statements

$files = @(
    "test\UnitTest_SuperComplex.cs",
    "test\UnitTest_ErrorHandling_Debug.cs", 
    "test\UnitTest_MultipleTargets.cs"
)

foreach ($file in $files) {
    $content = Get-Content $file -Raw
    
    # Fix Assert.AreEqual
    $content = $content -replace 'Assert\.AreEqual\("([^"]+)",\s*([^)]+)\)', 'Assert.That($2, Is.EqualTo("$1"))'
    $content = $content -replace 'Assert\.AreEqual\(([^,]+),\s*([^)]+)\)', 'Assert.That($2, Is.EqualTo($1))'
    
    # Fix Assert.IsTrue  
    $content = $content -replace 'Assert\.IsTrue\(([^)]+)\.Contains\("([^"]+)"\)\)', 'Assert.That($1, Does.Contain("$2"))'
    $content = $content -replace 'Assert\.IsTrue\(([^)]+)\)', 'Assert.That($1, Is.True)'
    
    # Fix Assert.IsFalse
    $content = $content -replace 'Assert\.IsFalse\(([^)]+)\)', 'Assert.That($1, Is.False)'
    
    Set-Content $file $content -NoNewline
}

Write-Host "Fixed manually specified files"