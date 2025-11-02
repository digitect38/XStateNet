# Fix all namespace references in XAML files
$xamlFiles = Get-ChildItem -Path "." -Filter "*.xaml" -Recurse | Where-Object { $_.FullName -notmatch "\\obj\\" -and $_.FullName -notmatch "\\bin\\" }

foreach ($file in $xamlFiles) {
    $content = Get-Content $file.FullName -Raw

    # Replace xmlns declarations
    $content = $content -replace 'clr-namespace:CMPSimulator\.Controls', 'clr-namespace:CMPSimXS2.WPF.Controls'
    $content = $content -replace 'clr-namespace:CMPSimulator\.Models', 'clr-namespace:CMPSimXS2.WPF.Models'
    $content = $content -replace 'clr-namespace:CMPSimulator\.Helpers', 'clr-namespace:CMPSimXS2.WPF.Helpers'
    $content = $content -replace 'clr-namespace:CMPSimulator', 'clr-namespace:CMPSimXS2.WPF'

    # Replace x:Class declarations
    $content = $content -replace 'x:Class="CMPSimulator\.', 'x:Class="CMPSimXS2.WPF.'

    Set-Content -Path $file.FullName -Value $content -NoNewline
    Write-Host "Updated: $($file.Name)"
}

Write-Host "XAML namespace update complete!"
