# Let me check if the events are being fired
$env:DOTNET_LOG_LEVEL = "debug"
dotnet test Test/XStateNet.Tests.csproj --no-build --filter "FullyQualifiedName=TimelineWPF.Tests.RealTimeIntegrationTests.RealTimeAdapter_CapturesActions" --logger:"console;verbosity=diagnostic"