@echo off
echo.
echo ===============================================
echo        SIMPLE HOST CLIENT
echo    (Connects to Equipment at port 5000)
echo ===============================================
echo.
echo IMPORTANT: Make sure equipment is running first!
echo You can start equipment by running the WPF Simulator
echo and clicking "Start Simulator"
echo.
echo Starting host connection in 3 seconds...
timeout /t 3

cd SemiStandard.Testing.Console
echo.
echo Running host connection test...
dotnet run --no-build

echo.
echo Host test completed.
pause