@echo off
REM Run InterProcess Service in Console Mode (for testing)

echo ╔═══════════════════════════════════════════════════════╗
echo ║  XStateNet InterProcess Service - Console Mode       ║
echo ╚═══════════════════════════════════════════════════════╝
echo.

cd /d "%~dp0"

if not exist "bin\Release\net9.0\XStateNet.InterProcess.Service.exe" (
    echo ERROR: Service executable not found!
    echo Please build the project first: dotnet build -c Release
    echo.
    pause
    exit /b 1
)

echo Starting service in console mode...
echo Press Ctrl+C to stop
echo.

cd bin\Release\net9.0
XStateNet.InterProcess.Service.exe

pause
