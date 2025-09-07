# PowerShell script to fix remaining XUnit conversion issues
param(
    [string]$Directory = "C:\Develop25\XStateNet\Test"
)

# Get all test files
$files = Get-ChildItem -Path $Directory -Filter "*.cs" -Recurse

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    $modified = $false
    
    # Remove any remaining NUnit attributes
    if ($content -match '\[SetUp\]') {
        $content = $content -replace '\[SetUp\]', ''
        $modified = $true
    }
    
    if ($content -match '\[TearDown\]') {
        $content = $content -replace '\[TearDown\]', ''
        $modified = $true
    }
    
    # Fix Setup methods - convert to constructor if needed
    if ($content -match 'public void Setup\(\)') {
        # Extract class name
        if ($content -match 'public class (\w+)') {
            $className = $Matches[1]
            $content = $content -replace 'public void Setup\(\)', "public $className()"
            $modified = $true
        }
    }
    
    # Fix TearDown methods - convert to Dispose
    if ($content -match 'public void TearDown\(\)') {
        $content = $content -replace 'public void TearDown\(\)', 'public void Dispose()'
        $modified = $true
    }
    
    # Add Dispose method to classes implementing IDisposable but missing the method
    if ($content -match ': IDisposable' -and $content -notmatch 'public void Dispose\(\)') {
        # Find the last closing brace of the class
        $lastBrace = $content.LastIndexOf('}')
        if ($lastBrace -gt 0) {
            # Find the second to last brace (end of class body)
            $classEndBrace = $content.LastIndexOf('}', $lastBrace - 1)
            if ($classEndBrace -gt 0) {
                $disposeMethod = "`n`n        public void Dispose()`n        {`n            // Cleanup if needed`n        }"
                $content = $content.Insert($classEndBrace, $disposeMethod)
                $modified = $true
            }
        }
    }
    
    if ($modified) {
        Set-Content $file.FullName $content
        Write-Host "Fixed $($file.Name)"
    }
}

Write-Host "Conversion fixes complete!"