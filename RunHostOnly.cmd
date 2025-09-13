@echo off
echo ===========================================
echo     Starting SEMI Host Only (Client)
echo ===========================================
echo.
echo This will connect to equipment at 127.0.0.1:5000
echo Make sure the equipment simulator is running first!
echo.

cd SemiStandard.Testing.Console
dotnet run --project SemiStandard.Testing.Console.csproj HostOnlyProgram

pause