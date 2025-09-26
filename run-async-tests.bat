@echo off
echo ========================================
echo Running Improved Async Pattern Tests
echo ========================================
echo.

echo Building test project...
cd Test
dotnet build --nologo -v q

if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    exit /b 1
)

echo.
echo ========================================
echo Running Simple Async Pattern Tests
echo ========================================
dotnet test --filter "SimpleAsyncPatternTests" --no-build -v n

echo.
echo ========================================
echo Running Basic Async Pattern Tests
echo ========================================
dotnet test --filter "AsyncPatternTests" --no-build -v n

echo.
echo ========================================
echo Tests Complete!
echo ========================================
echo.
echo To run all async-related tests, use:
echo   dotnet test --filter "Async" --no-build
echo.
pause