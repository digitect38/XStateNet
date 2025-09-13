@echo off
echo Starting WPF Photolithography Equipment Simulator...
echo.

cd /d "%~dp0"

dotnet run --project SemiStandard.Simulator.Wpf -- realistic

pause