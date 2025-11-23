$files = @(
    "MasterSchedulerActor.cs",
    "RobotSchedulersActor.cs",
    "WaferSchedulerActor.cs",
    "SystemCoordinator.cs"
)

foreach ($file in $files) {
    $content = Get-Content $file -Raw
    $content = $content -replace 'Console\.WriteLine\(', 'TableLogger.Log('
    Set-Content -Path $file -Value $content -NoNewline
}
