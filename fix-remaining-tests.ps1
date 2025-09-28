# Fix remaining compilation errors

# Fix UnitTest_AtmMachine.cs
$file = "C:\Develop25\XStateNet\Test\UnitTest_AtmMachine.cs"
$content = Get-Content $file -Raw
$content = $content -replace 'CreateFromScript\(jsonScript,\s*guidIsolate:\s*false\)', 'CreateFromScript(jsonScript, true, false)'
Set-Content -Path $file -Value $content -NoNewline
Write-Host "Fixed: UnitTest_AtmMachine.cs"

# Fix SendAndForgetTests.cs
$file = "C:\Develop25\XStateNet\Test\SendAndForgetTests.cs"
$content = Get-Content $file -Raw
# These need to be changed to call the overload with StateMachine parameter
$content = $content -replace 'StateMachineFactory\.CreateFromScript\(new StateMachine\(\),\s*json,\s*actionMap\)', 'StateMachineFactory.CreateFromScript(new StateMachine(), json, null, false, actionMap)'
$content = $content -replace 'StateMachineFactory\.CreateFromScript\(new StateMachine\(\),\s*jsonScript,\s*actionCallbacks\)', 'StateMachineFactory.CreateFromScript(new StateMachine(), jsonScript, null, false, actionCallbacks)'
$content = $content -replace 'StateMachineFactory\.CreateFromScript\(new StateMachine\(\),\s*simpleScript,\s*actionCallbacks\)', 'StateMachineFactory.CreateFromScript(new StateMachine(), simpleScript, null, false, actionCallbacks)'
Set-Content -Path $file -Value $content -NoNewline
Write-Host "Fixed: SendAndForgetTests.cs"

# Fix PingPongTests.cs - remaining named parameter issues
$file = "C:\Develop25\XStateNet\Test\PingPongTests.cs"
$content = Get-Content $file -Raw
$content = $content -replace 'CreateFromScript\(complexJson,\s*guidIsolate:\s*true,\s*complexActions,\s*complexGuards\)', 'CreateFromScript(complexJson, true, true, complexActions, complexGuards)'
$content = $content -replace 'CreateFromScript\(jsonScript,\s*guidIsolate:\s*true,\s*actionMap,\s*guardMap\)', 'CreateFromScript(jsonScript, true, true, actionMap, guardMap)'
Set-Content -Path $file -Value $content -NoNewline
Write-Host "Fixed: PingPongTests.cs"

# Fix UnitTest_TrafficLight.cs
$file = "C:\Develop25\XStateNet\Test\UnitTest_TrafficLight.cs"
$content = Get-Content $file -Raw
$content = $content -replace 'StateMachineFactory\.CreateFromScript\(trafficLightJson,\s*guidIsolate:\s*true,\s*actionMap,\s*guardMap\)', 'StateMachineFactory.CreateFromScript(trafficLightJson, true, true, actionMap, guardMap)'
Set-Content -Path $file -Value $content -NoNewline
Write-Host "Fixed: UnitTest_TrafficLight.cs"

# Fix InterMachine tests
$files = @(
    "C:\Develop25\XStateNet\Test\InterMachine\InterMachinePingPongTests.cs",
    "C:\Develop25\XStateNet\Test\InterMachine\InterMachineShowcaseTests.cs"
)

foreach ($file in $files) {
    if (Test-Path $file) {
        $content = Get-Content $file -Raw
        # Fix patterns like CreateFromScript(new StateMachine(), jsonScript, actionCallbacks)
        $content = $content -replace 'StateMachineFactory\.CreateFromScript\(new StateMachine\(\),\s*(\w+),\s*(\w+)\)', 'StateMachineFactory.CreateFromScript(new StateMachine(), $1, null, false, $2)'
        Set-Content -Path $file -Value $content -NoNewline
        Write-Host "Fixed: $(Split-Path $file -Leaf)"
    }
}

# Fix PubSubTimelineIntegrationTests.cs and RealTimeMonitoringTests.cs
$files = @(
    "C:\Develop25\XStateNet\Test\PubSubTimelineIntegrationTests.cs",
    "C:\Develop25\XStateNet\Test\RealTimeMonitoringTests.cs"
)

foreach ($file in $files) {
    if (Test-Path $file) {
        $content = Get-Content $file -Raw
        $content = $content -replace 'CreateFromScript\(json,\s*guidIsolate:\s*true,\s*actionMap\)', 'CreateFromScript(json, true, true, actionMap)'
        $content = $content -replace 'CreateFromScript\(json,\s*guidIsolate:\s*true,\s*actionMap,\s*guardMap\)', 'CreateFromScript(json, true, true, actionMap, guardMap)'
        Set-Content -Path $file -Value $content -NoNewline
        Write-Host "Fixed: $(Split-Path $file -Leaf)"
    }
}

# Fix CommunicatingMachinesTests.cs
$file = "C:\Develop25\XStateNet\Test\CommunicatingMachinesTests.cs"
if (Test-Path $file) {
    $content = Get-Content $file -Raw
    $content = $content -replace 'CreateFromScript\(pingScript,\s*guidIsolate:\s*true,\s*pingActions\)', 'CreateFromScript(pingScript, true, true, pingActions)'
    $content = $content -replace 'CreateFromScript\(pongScript,\s*guidIsolate:\s*true,\s*pongActions\)', 'CreateFromScript(pongScript, true, true, pongActions)'
    Set-Content -Path $file -Value $content -NoNewline
    Write-Host "Fixed: CommunicatingMachinesTests.cs"
}

Write-Host "All fixes completed."