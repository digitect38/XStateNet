$dest = "Test\bin\Debug\net8.0-windows"

# Find and copy testhost files
$testhostPath = "C:\Program Files\dotnet\sdk\*\TestHost"
$latestTestHost = Get-ChildItem $testhostPath | Sort-Object Name -Descending | Select-Object -First 1

if ($latestTestHost) {
    Write-Host "Found TestHost at: $($latestTestHost.FullName)"

    # Copy testhost.exe and testhost.dll
    $testHostExe = Join-Path $latestTestHost.FullName "testhost.exe"
    $testHostDll = Join-Path $latestTestHost.FullName "testhost.dll"

    if (Test-Path $testHostExe) {
        Copy-Item $testHostExe -Destination $dest -Force
        Write-Host "Copied testhost.exe"
    }

    if (Test-Path $testHostDll) {
        Copy-Item $testHostDll -Destination $dest -Force
        Write-Host "Copied testhost.dll"
    }

    # Copy Microsoft.TestPlatform DLLs
    $testPlatformDlls = Get-ChildItem (Join-Path $latestTestHost.FullName "Microsoft.TestPlatform*.dll")
    foreach ($dll in $testPlatformDlls) {
        Copy-Item $dll.FullName -Destination $dest -Force
        Write-Host "Copied: $($dll.Name)"
    }
} else {
    Write-Host "TestHost not found in SDK"
}

# List what we have
Write-Host "`nFiles in test output directory:"
Get-ChildItem "$dest\*.exe" | Select-Object Name
Get-ChildItem "$dest\Microsoft.TestPlatform*.dll" | Select-Object Name