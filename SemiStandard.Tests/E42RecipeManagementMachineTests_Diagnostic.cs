using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using XStateNet.Orchestration;
using XStateNet.Semi.Standards;
using static SemiStandard.Tests.StateMachineTestHelpers;

namespace SemiStandard.Tests;

public class E42RecipeManagementMachineTests_Diagnostic : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E42RecipeManagementMachine _recipeMgmt;
    private readonly ITestOutputHelper _output;

    public E42RecipeManagementMachineTests_Diagnostic(ITestOutputHelper output)
    {
        _output = output;
        var config = new OrchestratorConfig { EnableLogging = true, PoolSize = 4, EnableMetrics = false };
        _orchestrator = new EventBusOrchestrator(config);
        _recipeMgmt = new E42RecipeManagementMachine("EQ001", _orchestrator);
    }

    public void Dispose() => _orchestrator?.Dispose();

    [Fact]
    public async Task Diagnostic_Single_Recipe_Should_Work()
    {
        _output.WriteLine("=== Creating RECIPE001 ===");
        var recipe1 = await _recipeMgmt.CreateRecipeAsync("RECIPE001", "1.0");
        await AssertStateAsync(recipe1, "NoRecipe");

        _output.WriteLine($"RECIPE001 initial state: {recipe1.GetCurrentState()}");
        _output.WriteLine($"RECIPE001 MachineId: {recipe1.MachineId}");

        _output.WriteLine("\n=== Calling DownloadAsync on RECIPE001 ===");
        var result = await recipe1.DownloadAsync();
        _output.WriteLine($"DownloadAsync success: {result.Success}");
        _output.WriteLine($"DownloadAsync new state: {result.NewState}");

        AssertState(result, "Downloading");
        _output.WriteLine($"RECIPE001 state after download: {recipe1.GetCurrentState()}");
    }

    [Fact]
    public async Task Diagnostic_Two_Recipes_Sequential()
    {
        _output.WriteLine("=== Creating RECIPE001 ===");
        var recipe1 = await _recipeMgmt.CreateRecipeAsync("RECIPE001", "1.0");
        await AssertStateAsync(recipe1, "NoRecipe");
        _output.WriteLine($"RECIPE001 state: {recipe1.GetCurrentState()}");
        _output.WriteLine($"RECIPE001 MachineId: {recipe1.MachineId}");

        _output.WriteLine("\n=== Creating RECIPE002 ===");
        var recipe2 = await _recipeMgmt.CreateRecipeAsync("RECIPE002", "1.0");
        await AssertStateAsync(recipe2, "NoRecipe");
        _output.WriteLine($"RECIPE002 state: {recipe2.GetCurrentState()}");
        _output.WriteLine($"RECIPE002 MachineId: {recipe2.MachineId}");

        _output.WriteLine("\n=== Calling DownloadAsync on RECIPE001 ONLY ===");
        var result1 = await recipe1.DownloadAsync();
        _output.WriteLine($"DownloadAsync success: {result1.Success}");
        _output.WriteLine($"DownloadAsync new state: {result1.NewState}");

        AssertState(result1, "Downloading");
        _output.WriteLine($"\nRECIPE001 state: {recipe1.GetCurrentState()}");
        _output.WriteLine($"RECIPE002 state: {recipe2.GetCurrentState()}");

        _output.WriteLine("\n=== Expected: RECIPE001 = Downloading, RECIPE002 = NoRecipe ===");
        Assert.Contains("Downloading", recipe1.GetCurrentState());
        Assert.Contains("NoRecipe", recipe2.GetCurrentState());
    }

    [Fact]
    public async Task Diagnostic_Check_Machine_IDs()
    {
        var recipe1 = await _recipeMgmt.CreateRecipeAsync("RECIPE001", "1.0");
        var recipe2 = await _recipeMgmt.CreateRecipeAsync("RECIPE002", "1.0");
        await Task.Delay(200);

        _output.WriteLine($"RECIPE001 MachineId: {recipe1.MachineId}");
        _output.WriteLine($"RECIPE002 MachineId: {recipe2.MachineId}");
        _output.WriteLine($"RECIPE001 RecipeId: {recipe1.RecipeId}");
        _output.WriteLine($"RECIPE002 RecipeId: {recipe2.RecipeId}");

        Assert.NotEqual(recipe1.MachineId, recipe2.MachineId);
        Assert.StartsWith("E42_RECIPE_RECIPE001_", recipe1.MachineId);
        Assert.StartsWith("E42_RECIPE_RECIPE002_", recipe2.MachineId);
    }
}
