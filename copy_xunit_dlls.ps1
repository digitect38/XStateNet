$dest = "Test\bin\Debug\net8.0-windows"
$xunitPackages = @(
    "xunit.abstractions\2.0.3\lib\netstandard2.0\xunit.abstractions.dll",
    "xunit.extensibility.core\2.9.3\lib\netstandard1.1\xunit.core.dll",
    "xunit.extensibility.execution\2.9.3\lib\netstandard1.1\xunit.execution.dotnet.dll",
    "xunit.assert\2.9.3\lib\netstandard1.1\xunit.assert.dll"
)

foreach ($package in $xunitPackages) {
    $source = "C:\Users\digit\.nuget\packages\$package"
    if (Test-Path $source) {
        Copy-Item $source -Destination $dest -Force
        Write-Host "Copied: $package"
    } else {
        Write-Host "Not found: $source"
    }
}

Get-ChildItem "$dest\xunit*.dll" | Select-Object Name