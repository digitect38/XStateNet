# How to Fix Test Explorer Not Showing Tests

## Problem
Tests run fine from command line but don't appear in Visual Studio Test Explorer.

## Solutions (Try in Order):

### 1. ✅ Clean and Rebuild Solution
```powershell
# In Visual Studio or command line:
dotnet clean
dotnet build
```

### 2. ✅ Clear Test Explorer Cache
In Visual Studio:
- **Test** → **Test Explorer**
- Click the **Settings** gear icon ⚙️
- Select **Clear Test Results**
- Close and reopen Test Explorer

### 3. ✅ Reset Visual Studio Test Window
- Close Test Explorer window
- **Test** → **Windows** → **Test Explorer** (reopen it)
- Click **Run All Tests** button (even if no tests show)

### 4. ✅ Check Test Discovery Settings
In Visual Studio:
- **Tools** → **Options** → **Test**
- Under **General**, ensure:
  - ✓ "Discover tests in real time from C# and Visual Basic .NET source files" is checked
  - ✓ "Additionally discover tests from built assemblies after builds" is checked

### 5. ✅ Delete Temporary Files
Close Visual Studio and delete:
```powershell
# Delete these folders:
rm -r .vs
rm -r Test\bin
rm -r Test\obj
rm -r TestResults

# Then rebuild:
dotnet restore
dotnet build
```

### 6. ✅ Verify Test Adapter is Loading
Check Output window in Visual Studio:
- **View** → **Output**
- In "Show output from:" dropdown, select **Tests**
- Look for errors loading xunit.runner.visualstudio

### 7. ✅ Force Test Discovery
```xml
<!-- Add to Test\XStateNet.Tests.csproj if needed: -->
<PropertyGroup>
  <GenerateProgramFile>false</GenerateProgramFile>
  <IsTestProject>true</IsTestProject>
</PropertyGroup>
```

### 8. ✅ Check Platform/Architecture Match
Ensure your test project and Visual Studio are using the same platform:
- Right-click Test project → Properties
- Check Platform target (x64, x86, or Any CPU)
- In Test Explorer, check the platform selector matches

### 9. ✅ Run Tests from Developer Command Prompt
If Test Explorer still doesn't work, use:
```powershell
# Visual Studio Developer Command Prompt:
vstest.console.exe Test\bin\Debug\net8.0-windows\XStateNet.Tests.dll

# Or use dotnet CLI:
dotnet test --logger:"trx;LogFileName=test_results.trx"
```

### 10. ✅ Alternative: Use .NET CLI with Watch
```powershell
# Auto-run tests on file changes:
cd Test
dotnet watch test --filter "SimpleAsyncPatternTests"
```

## Quick Test Commands That Work

### Run Specific Test Class:
```bash
dotnet test --filter "ClassName=SimpleAsyncPatternTests"
```

### Run Tests with Detailed Output:
```bash
dotnet test --filter "SimpleAsync" -v detailed --logger "console;verbosity=detailed"
```

### List All Tests:
```bash
dotnet test --list-tests
```

## Visual Studio Specific Issues

### If using Visual Studio 2022:
1. Ensure you have the latest update (Help → Check for Updates)
2. Ensure ".NET desktop development" workload is installed
3. Try: **Extensions** → **Manage Extensions** → Search for "xUnit" → Install/Update

### If using Visual Studio Code:
Install the C# Dev Kit extension which includes test explorer support.

## Working Test Pattern
Your tests ARE working correctly from command line:
```
✅ SimpleAsyncPatternTests: 5/5 passed
✅ AsyncPatternTests: 5/5 passed
```

The issue is only with Visual Studio Test Explorer discovery, not with the tests themselves.