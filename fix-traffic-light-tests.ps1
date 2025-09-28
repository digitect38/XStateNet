$file = "C:\Develop25\XStateNet\Test\UnitTest_TrafficLight.cs"
$content = Get-Content $file -Raw

# Replace pattern: CreateFromScript(json, guidIsolate: false, _actions1, _guards)
# To: CreateFromScript(json, true, false, _actions1, _guards)
$content = $content -replace 'CreateFromScript\(json,\s*guidIsolate:\s*false,\s*(_actions\d+),\s*(_guards)\)', 'CreateFromScript(json, true, false, $1, $2)'

Set-Content -Path $file -Value $content -NoNewline
Write-Host "Fixed UnitTest_TrafficLight.cs"