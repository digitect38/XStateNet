using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Standards;

/// <summary>
/// E142 Wafer Map Machine - SEMI E142 Standard
/// Manages wafer map data lifecycle: Load, Apply, Update, Unload
/// Refactored to use ExtendedPureStateMachineFactory with EventBusOrchestrator
/// </summary>
public class E142WaferMapMachine
{
    private readonly string _equipmentId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly ConcurrentDictionary<string, WaferMapInstance> _waferMaps = new();

    public string MachineId => $"E142_WAFERMAP_MGMT_{_equipmentId}";

    public E142WaferMapMachine(string equipmentId, EventBusOrchestrator orchestrator)
    {
        _equipmentId = equipmentId;
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Create and register a wafer map
    /// </summary>
    public async Task<WaferMapInstance> CreateWaferMapAsync(string mapId, string waferId, string lotId)
    {
        if (_waferMaps.ContainsKey(mapId))
        {
            return _waferMaps[mapId];
        }

        var waferMap = new WaferMapInstance(mapId, waferId, lotId, _equipmentId, _orchestrator);
        _waferMaps[mapId] = waferMap;

        await waferMap.StartAsync();

        return waferMap;
    }

    /// <summary>
    /// Get wafer map
    /// </summary>
    public WaferMapInstance? GetWaferMap(string mapId)
    {
        return _waferMaps.TryGetValue(mapId, out var map) ? map : null;
    }

    /// <summary>
    /// Get all wafer maps
    /// </summary>
    public IEnumerable<WaferMapInstance> GetAllWaferMaps()
    {
        return _waferMaps.Values;
    }

    /// <summary>
    /// Get applied wafer map (if any)
    /// </summary>
    public WaferMapInstance? GetAppliedWaferMap()
    {
        return _waferMaps.Values.FirstOrDefault(m => m.IsApplied);
    }

    /// <summary>
    /// Delete wafer map
    /// </summary>
    public async Task<bool> DeleteWaferMapAsync(string mapId)
    {
        if (_waferMaps.TryRemove(mapId, out var map))
        {
            await map.UnloadAsync();
            await map.UnloadCompleteAsync();
            return true;
        }
        return false;
    }
}

/// <summary>
/// Individual wafer map state machine using orchestrator
/// </summary>
public class WaferMapInstance
{
    private readonly IPureStateMachine _machine;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly string _instanceId;
    private readonly ConcurrentDictionary<(int x, int y), DieData> _dieMap = new();
    private readonly List<BinDefinition> _binDefinitions = new();

    public string MapId { get; }
    public string WaferId { get; }
    public string LotId { get; }
    public int Version { get; set; }
    public DateTime? LoadTime { get; set; }
    public DateTime? ApplyTime { get; set; }
    public int UpdateCount { get; set; }
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public bool IsApplied => ApplyTime != null && GetCurrentState().Contains("Applied");

    public string MachineId => $"E142_WAFERMAP_{MapId}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public WaferMapInstance(string mapId, string waferId, string lotId, string equipmentId, EventBusOrchestrator orchestrator)
    {
        MapId = mapId;
        WaferId = waferId;
        LotId = lotId;
        _orchestrator = orchestrator;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);

        // Inline XState JSON definition
        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'NoMap',
            context: {
                mapId: '',
                mapVersion: null,
                loadTime: null,
                applyTime: null,
                updateCount: 0
            },
            states: {
                NoMap: {
                    entry: 'logNoMap',
                    on: {
                        LOAD: {
                            target: 'Loaded',
                            actions: ['validateMapData', 'storeMapData', 'recordLoadTime']
                        }
                    }
                },
                Loaded: {
                    entry: 'logLoaded',
                    on: {
                        APPLY: {
                            target: 'Applied',
                            actions: ['applyMapToProcess', 'recordApplyTime']
                        },
                        UNLOAD: {
                            target: 'Unloading'
                        },
                        UPDATE: {
                            target: 'Updating',
                            actions: 'prepareUpdate'
                        }
                    }
                },
                Applied: {
                    entry: 'logApplied',
                    on: {
                        UPDATE: {
                            target: 'Updating',
                            actions: 'prepareUpdate'
                        },
                        RELEASE: {
                            target: 'Loaded',
                            actions: 'releaseFromProcess'
                        },
                        UNLOAD: {
                            target: 'Unloading',
                            actions: 'releaseFromProcess'
                        },
                        DIE_TESTED: {
                            target: 'Applied',
                            actions: 'updateDieResult'
                        }
                    }
                },
                Updating: {
                    entry: 'logUpdating',
                    on: {
                        UPDATE_COMPLETE: {
                            target: 'Applied',
                            actions: ['applyUpdate', 'incrementUpdateCount']
                        },
                        UPDATE_FAILED: {
                            target: 'Applied'
                        }
                    }
                },
                Unloading: {
                    entry: 'cleanupResources',
                    on: {
                        UNLOAD_COMPLETE: {
                            target: 'NoMap',
                            actions: 'clearMapData'
                        }
                    }
                }
            }
        }
        """;

        // Orchestrated actions
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logNoMap"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📋 No wafer map loaded");
            },

            ["validateMapData"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔍 Validating wafer map data for {WaferId}");
            },

            ["storeMapData"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 💾 Storing wafer map data");
            },

            ["recordLoadTime"] = (ctx) =>
            {
                LoadTime = DateTime.UtcNow;
                Console.WriteLine($"[{MachineId}] ⏰ Wafer map loaded at {LoadTime}");
            },

            ["logLoaded"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Wafer map loaded - {_dieMap.Count} dies");

                ctx.RequestSend("E90_TRACKING", "WAFERMAP_LOADED", new JObject
                {
                    ["mapId"] = MapId,
                    ["waferId"] = WaferId,
                    ["lotId"] = LotId,
                    ["dieCount"] = _dieMap.Count
                });
            },

            ["applyMapToProcess"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔧 Applying wafer map to process");

                ctx.RequestSend("INSPECTION_SYSTEM", "WAFERMAP_APPLIED", new JObject
                {
                    ["mapId"] = MapId,
                    ["waferId"] = WaferId,
                    ["dieCount"] = _dieMap.Count
                });
            },

            ["recordApplyTime"] = (ctx) =>
            {
                ApplyTime = DateTime.UtcNow;
                Console.WriteLine($"[{MachineId}] ⏰ Wafer map applied at {ApplyTime}");
            },

            ["logApplied"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Wafer map applied to process");

                ctx.RequestSend("E40_PROCESS_JOB", "WAFERMAP_READY", new JObject
                {
                    ["mapId"] = MapId,
                    ["waferId"] = WaferId
                });
            },

            ["prepareUpdate"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔄 Preparing wafer map update");
            },

            ["logUpdating"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔄 Updating wafer map");
            },

            ["applyUpdate"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Update applied");

                ctx.RequestSend("E90_TRACKING", "WAFERMAP_UPDATED", new JObject
                {
                    ["mapId"] = MapId,
                    ["waferId"] = WaferId,
                    ["updateCount"] = UpdateCount
                });
            },

            ["incrementUpdateCount"] = (ctx) =>
            {
                UpdateCount++;
                Console.WriteLine($"[{MachineId}] 📊 Update count: {UpdateCount}");
            },

            ["releaseFromProcess"] = (ctx) =>
            {
                ApplyTime = null;
                Console.WriteLine($"[{MachineId}] 🔓 Released from process");

                ctx.RequestSend("INSPECTION_SYSTEM", "WAFERMAP_RELEASED", new JObject
                {
                    ["mapId"] = MapId,
                    ["waferId"] = WaferId
                });
            },

            ["updateDieResult"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🎯 Die test result updated");
            },

            ["cleanupResources"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🧹 Cleaning up resources");
            },

            ["clearMapData"] = (ctx) =>
            {
                _dieMap.Clear();
                _binDefinitions.Clear();
                LoadTime = null;
                ApplyTime = null;
                UpdateCount = 0;
                Console.WriteLine($"[{MachineId}] 🗑️ Map data cleared");

                ctx.RequestSend("E90_TRACKING", "WAFERMAP_UNLOADED", new JObject
                {
                    ["mapId"] = MapId,
                    ["waferId"] = WaferId
                });
            }
        };

        // Guards
        var guards = new Dictionary<string, Func<StateMachine, bool>>
        {
            ["isValidUpdate"] = (sm) =>
            {
                // Placeholder for update validation
                return true;
            },

            ["wasApplied"] = (sm) =>
            {
                return ApplyTime != null;
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
    }

    public async Task<string> StartAsync()
    {
        return await _machine.StartAsync();
    }

    public string GetCurrentState()
    {
        return _machine.CurrentState;
    }

    // Public API methods
    public async Task<EventResult> LoadAsync(WaferMapData mapData)
    {
        // Store map data
        Version = mapData.Version;
        RowCount = mapData.RowCount;
        ColumnCount = mapData.ColumnCount;

        _dieMap.Clear();
        foreach (var die in mapData.DieArray)
        {
            _dieMap[(die.X, die.Y)] = die;
        }

        _binDefinitions.Clear();
        _binDefinitions.AddRange(mapData.BinDefinitions);

        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "LOAD", null);
        return result;
    }

    public async Task<EventResult> ApplyAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "APPLY", null);
        return result;
    }

    public async Task<EventResult> ReleaseAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "RELEASE", null);
        return result;
    }

    public async Task<EventResult> UpdateAsync(MapUpdateData updateData)
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "UPDATE", null);
        return result;
    }

    public async Task<EventResult> UpdateCompleteAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "UPDATE_COMPLETE", null);
        return result;
    }

    public async Task<EventResult> UpdateFailedAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "UPDATE_FAILED", null);
        return result;
    }

    public async Task<EventResult> UnloadAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "UNLOAD", null);
        return result;
    }

    public async Task<EventResult> UnloadCompleteAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "UNLOAD_COMPLETE", null);
        return result;
    }

    public async Task<EventResult> UpdateDieTestResultAsync(int x, int y, int binCode, string testResult)
    {
        if (_dieMap.TryGetValue((x, y), out var die))
        {
            die.BinCode = binCode;
            die.TestResult = testResult;
        }
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "DIE_TESTED", null);
        return result;
    }

    public DieData? GetDieInfo(int x, int y)
    {
        _dieMap.TryGetValue((x, y), out var die);
        return die;
    }

    public WaferMapStatistics GetStatistics()
    {
        var stats = new WaferMapStatistics
        {
            TotalDies = _dieMap.Count,
            UpdateCount = UpdateCount,
            LoadTime = LoadTime,
            ApplyTime = ApplyTime
        };

        var binCounts = new ConcurrentDictionary<int, int>();
        foreach (var die in _dieMap.Values)
        {
            binCounts.AddOrUpdate(die.BinCode, 1, (key, count) => count + 1);
        }

        stats.BinCounts = binCounts;
        stats.TestedDies = _dieMap.Values.Count(d => !string.IsNullOrEmpty(d.TestResult));
        stats.GoodDies = _dieMap.Values.Count(d => d.BinCode == 1); // Assuming bin 1 is good

        if (stats.TestedDies > 0)
        {
            stats.Yield = (double)stats.GoodDies / stats.TestedDies * 100;
        }

        return stats;
    }
}

// Data classes
public class WaferMapData
{
    public int Version { get; set; }
    public string WaferId { get; set; } = string.Empty;
    public string LotId { get; set; } = string.Empty;
    public DieData[] DieArray { get; set; } = Array.Empty<DieData>();
    public List<BinDefinition> BinDefinitions { get; set; } = new();
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public string OriginLocation { get; set; } = "UpperLeft";
}

public class DieData
{
    public int X { get; set; }
    public int Y { get; set; }
    public int BinCode { get; set; }
    public string TestResult { get; set; } = string.Empty;
    public bool IsReference { get; set; }
    public bool IsEdge { get; set; }
}

public class BinDefinition
{
    public int BinCode { get; set; }
    public string BinName { get; set; } = string.Empty;
    public string BinType { get; set; } = string.Empty; // Good, Bad, Retest, etc.
    public string BinColor { get; set; } = string.Empty;
}

public class MapUpdateData
{
    public int Version { get; set; }
    public List<DieUpdate> DieUpdates { get; set; } = new();
}

public class DieUpdate
{
    public int X { get; set; }
    public int Y { get; set; }
    public int NewBinCode { get; set; }
    public string TestResult { get; set; } = string.Empty;
}

public class WaferMapStatistics
{
    public int TotalDies { get; set; }
    public int TestedDies { get; set; }
    public int GoodDies { get; set; }
    public double Yield { get; set; }
    public int UpdateCount { get; set; }
    public DateTime? LoadTime { get; set; }
    public DateTime? ApplyTime { get; set; }
    public ConcurrentDictionary<int, int> BinCounts { get; set; } = new();
}
