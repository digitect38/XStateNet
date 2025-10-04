using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Standards;

/// <summary>
/// E42 Recipe Management Machine - SEMI E42 Standard
/// Manages recipe download, verification, selection, and execution lifecycle
/// Refactored to use ExtendedPureStateMachineFactory with EventBusOrchestrator
/// </summary>
public class E42RecipeManagementMachine
{
    private readonly string _equipmentId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly ConcurrentDictionary<string, RecipeMachine> _recipes = new();

    public string MachineId => $"E42_RECIPE_MGMT_{_equipmentId}";

    public E42RecipeManagementMachine(string equipmentId, EventBusOrchestrator orchestrator)
    {
        _equipmentId = equipmentId;
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Create and register a recipe
    /// </summary>
    public async Task<RecipeMachine> CreateRecipeAsync(string recipeId, string recipeVersion)
    {
        if (_recipes.ContainsKey(recipeId))
        {
            return _recipes[recipeId];
        }

        var recipe = new RecipeMachine(recipeId, recipeVersion, _equipmentId, _orchestrator);
        _recipes[recipeId] = recipe;

        await recipe.StartAsync();

        return recipe;
    }

    /// <summary>
    /// Get recipe
    /// </summary>
    public RecipeMachine? GetRecipe(string recipeId)
    {
        return _recipes.TryGetValue(recipeId, out var recipe) ? recipe : null;
    }

    /// <summary>
    /// Get all recipes
    /// </summary>
    public IEnumerable<RecipeMachine> GetAllRecipes()
    {
        return _recipes.Values;
    }

    /// <summary>
    /// Get selected recipe (if any)
    /// </summary>
    public RecipeMachine? GetSelectedRecipe()
    {
        return _recipes.Values.FirstOrDefault(r => r.GetCurrentState().Contains("Selected"));
    }

    /// <summary>
    /// Delete recipe
    /// </summary>
    public async Task<bool> DeleteRecipeAsync(string recipeId)
    {
        if (_recipes.TryRemove(recipeId, out var recipe))
        {
            await recipe.DeleteAsync();
            return true;
        }
        return false;
    }
}

/// <summary>
/// Individual recipe state machine using orchestrator
/// </summary>
public class RecipeMachine
{
    private readonly IPureStateMachine _machine;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly IStateMachine _underlyingMachine;
    private readonly string _instanceId;

    public string RecipeId { get; }
    public string Version { get; set; }
    public DateTime? DownloadTime { get; set; }
    public DateTime? VerificationTime { get; set; }
    public DateTime? SelectTime { get; set; }
    public Dictionary<string, object> RecipeBody { get; set; }
    public bool IsVerified { get; set; }
    public ConcurrentDictionary<string, object> Properties { get; }

    public string MachineId => $"E42_RECIPE_{RecipeId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    /// <summary>
    /// Event raised when the recipe state changes
    /// </summary>
    public event Action<string>? StateChanged
    {
        add => _underlyingMachine.StateChanged += value;
        remove => _underlyingMachine.StateChanged -= value;
    }

    public RecipeMachine(string recipeId, string version, string equipmentId, EventBusOrchestrator orchestrator)
    {
        RecipeId = recipeId;
        Version = version;
        RecipeBody = new Dictionary<string, object>();
        Properties = new ConcurrentDictionary<string, object>();
        _orchestrator = orchestrator;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8); // Short unique ID

        // Inline XState JSON definition (use MachineId for unique identification)
        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'NoRecipe',
            context: {
                recipeId: '',
                version: '',
                downloadTime: null,
                verificationTime: null
            },
            states: {
                NoRecipe: {
                    entry: 'logNoRecipe',
                    on: {
                        DOWNLOAD_REQUEST: 'Downloading'
                    }
                },
                Downloading: {
                    entry: 'logDownloading',
                    on: {
                        DOWNLOAD_SUCCESS: 'Downloaded',
                        DOWNLOAD_FAILED: 'NoRecipe'
                    }
                },
                Downloaded: {
                    entry: 'logDownloaded',
                    on: {
                        VERIFY: 'Verifying',
                        DELETE: 'NoRecipe',
                        DOWNLOAD_REQUEST: 'Downloading'
                    }
                },
                Verifying: {
                    entry: 'logVerifying',
                    on: {
                        VERIFY_SUCCESS: 'Verified',
                        VERIFY_FAILED: 'Downloaded'
                    }
                },
                Verified: {
                    entry: 'logVerified',
                    on: {
                        SELECT: 'Selected',
                        MODIFY: 'Downloaded',
                        DELETE: 'NoRecipe',
                        REVERIFY: 'Verifying'
                    }
                },
                Selected: {
                    entry: 'logSelected',
                    on: {
                        DESELECT: 'Verified',
                        PROCESS_START: 'Processing',
                        DELETE: 'NoRecipe'
                    }
                },
                Processing: {
                    entry: 'logProcessing',
                    on: {
                        PROCESS_COMPLETE: 'Selected',
                        PROCESS_ABORT: 'Selected'
                    }
                }
            }
        }
        """;

        // Orchestrated actions
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logNoRecipe"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📋 No recipe loaded");
            },

            ["logDownloading"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⬇️ Downloading recipe {RecipeId} v{Version}");

                ctx.RequestSend("RECIPE_SERVER", "REQUEST_RECIPE_DOWNLOAD", new JObject
                {
                    ["recipeId"] = RecipeId,
                    ["version"] = Version
                });
            },

            ["logDownloaded"] = (ctx) =>
            {
                DownloadTime = DateTime.UtcNow;
                Console.WriteLine($"[{MachineId}] ✅ Recipe downloaded at {DownloadTime}");

                ctx.RequestSend("E40_PROCESS_JOB", "RECIPE_DOWNLOADED", new JObject
                {
                    ["recipeId"] = RecipeId,
                    ["version"] = Version
                });
            },

            ["logVerifying"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔍 Verifying recipe...");

                ctx.RequestSend("VERIFICATION_SYSTEM", "VERIFY_RECIPE", new JObject
                {
                    ["recipeId"] = RecipeId,
                    ["version"] = Version
                });
            },

            ["logVerified"] = (ctx) =>
            {
                VerificationTime = DateTime.UtcNow;
                IsVerified = true;
                Console.WriteLine($"[{MachineId}] ✅ Recipe verified at {VerificationTime}");

                ctx.RequestSend("E40_PROCESS_JOB", "RECIPE_VERIFIED", new JObject
                {
                    ["recipeId"] = RecipeId,
                    ["version"] = Version
                });
            },

            ["logSelected"] = (ctx) =>
            {
                SelectTime = DateTime.UtcNow;
                Console.WriteLine($"[{MachineId}] ✅ Recipe selected at {SelectTime}");

                ctx.RequestSend("E40_PROCESS_JOB", "RECIPE_SELECTED", new JObject
                {
                    ["recipeId"] = RecipeId,
                    ["version"] = Version
                });

                ctx.RequestSend("E94_CONTROL_JOB", "RECIPE_READY", new JObject
                {
                    ["recipeId"] = RecipeId
                });
            },

            ["logProcessing"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔧 Processing with recipe {RecipeId}");

                ctx.RequestSend("E90_TRACKING", "RECIPE_PROCESSING_STARTED", new JObject
                {
                    ["recipeId"] = RecipeId
                });
            }
        };

        // Guards
        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["isDifferentRecipe"] = (sm) =>
            {
                // Check if requesting a different recipe
                return true; // Placeholder
            },

            ["canDeleteSelected"] = (sm) =>
            {
                // Check if selected recipe can be deleted
                return true; // Placeholder
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: MachineId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            guards: guards,
            enableGuidIsolation: false  // Already has GUID suffix in MachineId
        );

        // Get underlying machine for StateChanged event access
        _underlyingMachine = ((PureStateMachineAdapter)_machine).GetUnderlying();
    }

    public async Task<string> StartAsync()
    {
        return await _machine.StartAsync();
    }

    public string GetCurrentState()
    {
        return _machine.CurrentState;
    }

    // Public API methods - return EventResult for deterministic testing
    public async Task<EventResult> DownloadAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "DOWNLOAD_REQUEST", null);
    }

    public async Task<EventResult> DownloadSuccessAsync(Dictionary<string, object> recipeBody)
    {
        RecipeBody = recipeBody;
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "DOWNLOAD_SUCCESS", null);
    }

    public async Task<EventResult> DownloadFailedAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "DOWNLOAD_FAILED", null);
    }

    public async Task<EventResult> VerifyAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "VERIFY", null);
    }

    public async Task<EventResult> VerifySuccessAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "VERIFY_SUCCESS", null);
    }

    public async Task<EventResult> VerifyFailedAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "VERIFY_FAILED", null);
    }

    public async Task<EventResult> SelectAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SELECT", null);
    }

    public async Task<EventResult> DeselectAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "DESELECT", null);
    }

    public async Task<EventResult> StartProcessingAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PROCESS_START", null);
    }

    public async Task<EventResult> CompleteProcessingAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PROCESS_COMPLETE", null);
    }

    public async Task<EventResult> AbortProcessingAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PROCESS_ABORT", null);
    }

    public async Task<EventResult> DeleteAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "DELETE", null);
    }
}
