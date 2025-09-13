@echo off
echo Starting WPF Simulator with Main Window...
echo.

cd /d "%~dp0"

REM Launch the main XState simulator directly
dotnet run --project SemiStandard.Simulator.Wpf -- xstate

pause