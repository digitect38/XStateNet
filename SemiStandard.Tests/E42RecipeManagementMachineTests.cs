using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using XStateNet.Orchestration;
using XStateNet.Semi.Standards;
using static SemiStandard.Tests.StateMachineTestHelpers;

namespace SemiStandard.Tests;

public class E42RecipeManagementMachineTests : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E42RecipeManagementMachine _recipeMgmt;

    public E42RecipeManagementMachineTests()
    {
        var config = new OrchestratorConfig { EnableLogging = true, PoolSize = 4, EnableMetrics = false };
        _orchestrator = new EventBusOrchestrator(config);
        _recipeMgmt = new E42RecipeManagementMachine("EQ001", _orchestrator);
    }

    public void Dispose() => _orchestrator?.Dispose();

    [Fact]
    public async Task E42_Should_Create_Recipe()
    {
        var recipe = await _recipeMgmt.CreateRecipeAsync("RECIPE001", "1.0");
        await AssertStateAsync(recipe, "NoRecipe");
        Assert.NotNull(recipe);
        Assert.Equal("RECIPE001", recipe.RecipeId);
    }

    [Fact]
    public async Task E42_Should_Complete_Full_Recipe_Lifecycle()
    {
        var recipe = await _recipeMgmt.CreateRecipeAsync("RECIPE001", "1.0");
        await AssertStateAsync(recipe, "NoRecipe");

        // Download - assert directly on EventResult.NewState (no extra state polling!)
        var result = await recipe.DownloadAsync();
        AssertState(result, "Downloading");

        result = await recipe.DownloadSuccessAsync(new Dictionary<string, object> { ["param1"] = "value1" });
        AssertState(result, "Downloaded");
        Assert.NotNull(recipe.DownloadTime);

        // Verify
        result = await recipe.VerifyAsync();
        AssertState(result, "Verifying");

        result = await recipe.VerifySuccessAsync();
        AssertState(result, "Verified");
        Assert.True(recipe.IsVerified);

        // Select
        result = await recipe.SelectAsync();
        AssertState(result, "Selected");
        Assert.NotNull(recipe.SelectTime);

        // Process
        result = await recipe.StartProcessingAsync();
        AssertState(result, "Processing");

        result = await recipe.CompleteProcessingAsync();
        AssertState(result, "Selected");
    }

    [Fact]
    public async Task E42_Should_Handle_Download_Failure()
    {
        var recipe = await _recipeMgmt.CreateRecipeAsync("RECIPE001", "1.0");
        await AssertStateAsync(recipe, "NoRecipe");

        var result = await recipe.DownloadAsync();
        AssertState(result, "Downloading");

        result = await recipe.DownloadFailedAsync();
        AssertState(result, "NoRecipe");
    }

    [Fact]
    public async Task E42_Should_Handle_Verification_Failure()
    {
        var recipe = await _recipeMgmt.CreateRecipeAsync("RECIPE001", "1.0");
        await AssertStateAsync(recipe, "NoRecipe");

        var result = await recipe.DownloadAsync();
        AssertState(result, "Downloading");

        result = await recipe.DownloadSuccessAsync(new Dictionary<string, object>());
        AssertState(result, "Downloaded");

        result = await recipe.VerifyAsync();
        AssertState(result, "Verifying");

        result = await recipe.VerifyFailedAsync();
        AssertState(result, "Downloaded");
    }

    [Fact]
    public async Task E42_Should_Deselect_Recipe()
    {
        var recipe = await _recipeMgmt.CreateRecipeAsync("RECIPE001", "1.0");
        await AssertStateAsync(recipe, "NoRecipe");

        var result = await recipe.DownloadAsync();
        AssertState(result, "Downloading");

        result = await recipe.DownloadSuccessAsync(new Dictionary<string, object>());
        AssertState(result, "Downloaded");

        result = await recipe.VerifyAsync();
        AssertState(result, "Verifying");

        result = await recipe.VerifySuccessAsync();
        AssertState(result, "Verified");

        result = await recipe.SelectAsync();
        AssertState(result, "Selected");

        result = await recipe.DeselectAsync();
        AssertState(result, "Verified");
    }

    [Fact]
    public async Task E42_Should_Get_Selected_Recipe()
    {
        var recipe1 = await _recipeMgmt.CreateRecipeAsync("RECIPE001", "1.0");
        var recipe2 = await _recipeMgmt.CreateRecipeAsync("RECIPE002", "1.0");

        // Select recipe1 through full workflow
        var result = await recipe1.DownloadAsync();
        AssertState(result, "Downloading");

        result = await recipe1.DownloadSuccessAsync(new Dictionary<string, object>());
        AssertState(result, "Downloaded");

        result = await recipe1.VerifyAsync();
        AssertState(result, "Verifying");

        result = await recipe1.VerifySuccessAsync();
        AssertState(result, "Verified");

        result = await recipe1.SelectAsync();
        AssertState(result, "Selected");

        var selected = _recipeMgmt.GetSelectedRecipe();
        Assert.NotNull(selected);
        Assert.Equal("RECIPE001", selected.RecipeId);
    }

    [Fact]
    public async Task E42_Should_Delete_Recipe()
    {
        var recipe = await _recipeMgmt.CreateRecipeAsync("RECIPE001", "1.0");
        await AssertStateAsync(recipe, "NoRecipe");

        var result = await _recipeMgmt.DeleteRecipeAsync("RECIPE001");
        Assert.True(result);

        var deleted = _recipeMgmt.GetRecipe("RECIPE001");
        Assert.Null(deleted);
    }

    [Fact]
    public async Task E42_Should_Get_All_Recipes()
    {
        await _recipeMgmt.CreateRecipeAsync("RECIPE001", "1.0");
        await _recipeMgmt.CreateRecipeAsync("RECIPE002", "1.0");
        await _recipeMgmt.CreateRecipeAsync("RECIPE003", "1.0");

        var recipes = _recipeMgmt.GetAllRecipes().ToList();
        Assert.Equal(3, recipes.Count);
    }

    [Fact]
    public async Task E42_Should_Have_Correct_MachineId()
    {
        Assert.Equal("E42_RECIPE_MGMT_EQ001", _recipeMgmt.MachineId);
    }
}
