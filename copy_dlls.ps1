$source = "C:\Users\digit\.nuget\packages"
$dest = "Test\bin\Debug\net8.0-windows"

$packages = @(
    "microsoft.extensions.configuration.binder\8.0.0\lib\net8.0",
    "microsoft.extensions.dependencyinjection\8.0.0\lib\net8.0",
    "microsoft.extensions.dependencyinjection.abstractions\8.0.0\lib\net8.0",
    "microsoft.extensions.logging\8.0.0\lib\net8.0",
    "microsoft.extensions.logging.abstractions\8.0.0\lib\net8.0",
    "microsoft.extensions.logging.console\8.0.0\lib\net8.0",
    "microsoft.extensions.logging.configuration\8.0.0\lib\net8.0",
    "microsoft.extensions.options\8.0.0\lib\net8.0",
    "microsoft.extensions.options.configurationextensions\8.0.0\lib\net8.0",
    "system.runtime.caching\8.0.0\lib\net8.0",
    "system.text.json\8.0.0\lib\net8.0",
    "system.text.encodings.web\8.0.0\lib\net8.0"
)

foreach ($package in $packages) {
    $fullPath = "$source\$package\*.dll"
    Write-Host "Copying from $fullPath to $dest"
    Copy-Item $fullPath -Destination $dest -Force -ErrorAction SilentlyContinue
}

Write-Host "Done copying DLLs"