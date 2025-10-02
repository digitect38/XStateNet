using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using XStateNet.Orchestration;
using XStateNet.Semi.Standards;
using static SemiStandard.Tests.StateMachineTestHelpers;

namespace SemiStandard.Tests;

public class E157ModuleProcessTrackingMachineTests : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E157ModuleProcessTrackingMachine _tracking;

    public E157ModuleProcessTrackingMachineTests()
    {
        var config = new OrchestratorConfig { EnableLogging = true, PoolSize = 4, EnableMetrics = false };
        _orchestrator = new EventBusOrchestrator(config);
        _tracking = new E157ModuleProcessTrackingMachine("EQ001", _orchestrator);
    }

    public void Dispose() => _orchestrator?.Dispose();

    [Fact]
    public async Task E157_Should_Register_Module()
    {
        var module = await _tracking.RegisterModuleAsync("MODULE001");

        Assert.NotNull(module);
        Assert.Equal("MODULE001", module.ModuleId);
        Assert.Contains("Idle", module.GetCurrentState());
    }

    [Fact]
    public async Task E157_Should_Process_Material_With_PreProcess()
    {
        var module = await _tracking.RegisterModuleAsync("MODULE001");

        // Material arrives
        var result = await module.MaterialArriveAsync("WAFER001", "STEP_LITHO");
        AssertState(result, "MaterialArrived");
        Assert.Equal("WAFER001", module.CurrentMaterialId);

        // Pre-process
        result = await module.StartPreProcessAsync();
        AssertState(result, "PreProcessing");

        result = await module.PreProcessCompleteAsync();
        AssertState(result, "Processing");

        // Main process
        result = await module.ProcessCompleteAsync();
        AssertState(result, "PostProcessing");

        // Post-process
        result = await module.PostProcessCompleteAsync();
        AssertState(result, "MaterialComplete");
    }

    [Fact]
    public async Task E157_Should_Skip_PreProcess()
    {
        var module = await _tracking.RegisterModuleAsync("MODULE001");

        await module.MaterialArriveAsync("WAFER001", "STEP_ETCH");

        var result = await module.SkipPreProcessAsync();
        AssertState(result, "Processing");
    }

    [Fact]
    public async Task E157_Should_Skip_PostProcess()
    {
        var module = await _tracking.RegisterModuleAsync("MODULE001");

        await module.MaterialArriveAsync("WAFER001", "STEP_CMP");
        await module.SkipPreProcessAsync();

        var result = await module.SkipPostProcessAsync();
        AssertState(result, "MaterialComplete");
    }

    [Fact]
    public async Task E157_Should_Handle_PreProcess_Error()
    {
        var module = await _tracking.RegisterModuleAsync("MODULE001");

        await module.MaterialArriveAsync("WAFER001", "STEP_LITHO");
        await module.StartPreProcessAsync();

        var result = await module.ReportErrorAsync("PRE_PROCESS_ERROR");
        AssertState(result, "Error");

        Assert.Equal(1, module.ErrorCount);
    }

    [Fact]
    public async Task E157_Should_Handle_Process_Error()
    {
        var module = await _tracking.RegisterModuleAsync("MODULE001");

        await module.MaterialArriveAsync("WAFER001", "STEP_ETCH");
        await module.SkipPreProcessAsync();

        var result = await module.ReportErrorAsync("PROCESS_ERROR");
        AssertState(result, "Error");
    }

    [Fact]
    public async Task E157_Should_Clear_Error()
    {
        var module = await _tracking.RegisterModuleAsync("MODULE001");

        await module.MaterialArriveAsync("WAFER001", "STEP_CMP");
        await module.SkipPreProcessAsync();
        await module.ReportErrorAsync("PROCESS_ERROR");

        var result = await module.ClearErrorAsync();
        AssertState(result, "Idle");
    }

    [Fact]
    public async Task E157_Should_Abort_Processing()
    {
        var module = await _tracking.RegisterModuleAsync("MODULE001");

        await module.MaterialArriveAsync("WAFER001", "STEP_LITHO");
        await module.StartPreProcessAsync();

        var result = await module.AbortAsync();
        AssertState(result, "Idle");

        Assert.Null(module.CurrentMaterialId);
    }

    [Fact]
    public async Task E157_Should_Remove_Material()
    {
        var module = await _tracking.RegisterModuleAsync("MODULE001");

        await module.MaterialArriveAsync("WAFER001", "STEP_ETCH");
        await module.SkipPreProcessAsync();
        await module.SkipPostProcessAsync();

        var result = await module.MaterialRemoveAsync();
        AssertState(result, "Idle");

        Assert.Null(module.CurrentMaterialId);
    }

    [Fact]
    public async Task E157_Should_Process_Next_Material()
    {
        var module = await _tracking.RegisterModuleAsync("MODULE001");

        // First material
        await module.MaterialArriveAsync("WAFER001", "STEP_CMP");
        await module.SkipPreProcessAsync();
        await module.SkipPostProcessAsync();

        // Next material
        var result = await module.NextMaterialAsync("WAFER002", "STEP_CMP");
        AssertState(result, "MaterialArrived");

        Assert.Equal("WAFER002", module.CurrentMaterialId);
    }

    [Fact]
    public async Task E157_Should_Track_Step_Count()
    {
        var module = await _tracking.RegisterModuleAsync("MODULE001");

        await module.MaterialArriveAsync("WAFER001", "STEP_LITHO");
        await module.StartPreProcessAsync();
        await module.PreProcessCompleteAsync();
        await module.ProcessCompleteAsync();

        Assert.True(module.StepCount >= 2); // PreProcess + MainProcess
    }

    [Fact]
    public async Task E157_Should_Generate_Process_Report()
    {
        var module = await _tracking.RegisterModuleAsync("MODULE001");

        await module.MaterialArriveAsync("WAFER001", "STEP_ETCH");
        await module.SkipPreProcessAsync();
        await module.SkipPostProcessAsync();

        var report = module.GenerateProcessReport("WAFER001");

        Assert.NotNull(report);
        Assert.Equal("MODULE001", report.ModuleId);
        Assert.Equal("WAFER001", report.MaterialId);
        Assert.True(report.Steps.Count > 0);
    }

    [Fact]
    public async Task E157_Should_Get_Material_History()
    {
        var module = await _tracking.RegisterModuleAsync("MODULE001");

        var result = await module.MaterialArriveAsync("WAFER001", "STEP_CMP");
        AssertState(result, "MaterialArrived");

        result = await module.SkipPreProcessAsync();
        AssertState(result, "Processing");

        result = await module.SkipPostProcessAsync();
        AssertState(result, "MaterialComplete");

        // Small delay to ensure entry actions have completed and history is populated
        await Task.Delay(50);

        var history = module.GetMaterialHistory("WAFER001").ToList();

        Assert.NotEmpty(history);
        Assert.All(history, step => Assert.Equal("WAFER001", step.MaterialId));
    }

    [Fact]
    public async Task E157_Should_Get_All_Modules()
    {
        await _tracking.RegisterModuleAsync("MODULE001");
        await _tracking.RegisterModuleAsync("MODULE002");
        await _tracking.RegisterModuleAsync("MODULE003");

        var modules = _tracking.GetAllModules().ToList();
        Assert.Equal(3, modules.Count);
    }

    [Fact]
    public async Task E157_Should_Get_Active_Modules()
    {
        var module1 = await _tracking.RegisterModuleAsync("MODULE001");
        var module2 = await _tracking.RegisterModuleAsync("MODULE002");

        await module1.MaterialArriveAsync("WAFER001", "STEP_LITHO");

        var activeModules = _tracking.GetActiveModules().ToList();
        Assert.Single(activeModules);
        Assert.Equal("MODULE001", activeModules[0].ModuleId);
    }

    [Fact]
    public async Task E157_Should_Have_Correct_MachineId()
    {
        Assert.Equal("E157_MODULE_TRACKING_EQ001", _tracking.MachineId);
    }
}
