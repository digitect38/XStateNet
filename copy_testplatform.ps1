$sdkPath = "C:\Program Files\dotnet\sdk\9.0.300"
$dest = "Test\bin\Debug\net8.0-windows"

# Copy testhost files
Copy-Item (Join-Path $sdkPath "testhost.dll") -Destination $dest -Force
Copy-Item (Join-Path $sdkPath "testhost.exe") -Destination $dest -Force
Write-Host "Copied testhost files"

# Copy Microsoft.TestPlatform DLLs
$dlls = Get-ChildItem (Join-Path $sdkPath "Microsoft.TestPlatform*.dll")
foreach ($dll in $dlls) {
    Copy-Item $dll.FullName -Destination $dest -Force
}
Write-Host "Copied Microsoft.TestPlatform DLLs"

# Check what we have
Get-ChildItem "$dest\testhost.*" | Select-Object Name
Get-ChildItem "$dest\Microsoft.TestPlatform*.dll" | Select-Object Name