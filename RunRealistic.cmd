@echo off
echo Starting Realistic Equipment Simulator...
echo.

cd /d "%~dp0"

dotnet run --project SemiStandard.Testing.Console -- realistic

pause