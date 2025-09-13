@echo off
echo ===========================================
echo     SEMI Host Simulator
echo ===========================================
echo.
echo Starting host connection to equipment at 127.0.0.1:5000
echo.

cd SemiStandard.Testing.Console
dotnet run --project SemiStandard.Testing.Console.csproj

pause