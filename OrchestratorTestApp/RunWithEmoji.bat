@echo off
REM Enable UTF-8 code page for emoji support
chcp 65001 > nul

echo ===============================================
echo Testing emoji display: ✅ ❌ 🚀 📊 🎯 ⚡
echo ===============================================
echo.

REM Run the orchestrator test app
dotnet run --configuration Release -- %*