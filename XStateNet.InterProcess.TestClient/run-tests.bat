@echo off
REM Run InterProcess Test Client

echo ╔═══════════════════════════════════════════════════════╗
echo ║  XStateNet InterProcess Service - Test Client        ║
echo ╚═══════════════════════════════════════════════════════╝
echo.

cd /d "%~dp0"

if not exist "bin\Release\net9.0\XStateNet.InterProcess.TestClient.exe" (
    echo ERROR: Test client executable not found!
    echo Please build the project first: dotnet build -c Release
    echo.
    pause
    exit /b 1
)

cd bin\Release\net9.0
XStateNet.InterProcess.TestClient.exe %*

pause
