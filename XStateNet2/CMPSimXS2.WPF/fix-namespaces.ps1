# Fix all namespace references in C# files
$sourceFiles = Get-ChildItem -Path "." -Filter "*.cs" -Recurse | Where-Object { $_.FullName -notmatch "\\obj\\" -and $_.FullName -notmatch "\\bin\\" }

foreach ($file in $sourceFiles) {
    $content = Get-Content $file.FullName -Raw

    # Replace namespace declarations
    $content = $content -replace "namespace CMPSimulator", "namespace CMPSimXS2.WPF"

    # Replace using statements
    $content = $content -replace "using CMPSimulator\.Controllers", "using CMPSimXS2.WPF.Controllers"
    $content = $content -replace "using CMPSimulator\.Controls", "using CMPSimXS2.WPF.Controls"
    $content = $content -replace "using CMPSimulator\.Models", "using CMPSimXS2.WPF.Models"
    $content = $content -replace "using CMPSimulator\.Helpers", "using CMPSimXS2.WPF.Helpers"
    $content = $content -replace "using CMPSimulator\.StateMachines", "using CMPSimXS2.WPF.StateMachines"
    $content = $content -replace "using CMPSimulator;", "using CMPSimXS2.WPF;"

    Set-Content -Path $file.FullName -Value $content -NoNewline
    Write-Host "Updated: $($file.Name)"
}

Write-Host "Namespace update complete!"
