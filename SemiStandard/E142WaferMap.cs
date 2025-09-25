using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SemiStandard;

namespace SemiStandard.E142
{
    /// <summary>
    /// SEMI E142 Wafer Map Data Management
    /// 웨이퍼 맵 데이터의 생명주기 관리(Load/Apply/Update/Unload)
    /// </summary>
    public class E142WaferMap
    {
        private readonly StateMachineAdapter _stateMachine;
        private readonly string _mapId;
        private WaferMapData? _mapData;
        private readonly List<BinData> _binDefinitions = new();
        private readonly ConcurrentDictionary<(int x, int y), DieData> _dieMap = new();
        private DateTime? _loadTime;
        private DateTime? _applyTime;
        private int _updateCount = 0;
        
        public string MapId => _mapId;
        public WaferMapState CurrentState { get; private set; }
        public WaferMapData? MapData => _mapData;
        public IReadOnlyList<BinData> BinDefinitions => _binDefinitions.AsReadOnly();
        
        public enum WaferMapState
        {
            NoMap,
            Loaded,
            Applied,
            Updating,
            Unloading
        }
        
        public E142WaferMap(string mapId)
        {
            _mapId = mapId;
            
            // Load configuration from JSON file
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "SemiStandard.XStateScripts.E142WaferMap.json";
            string config;

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    // Fallback to file system if embedded resource not found
                    var configPath = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? "", "XStateScripts", "E142WaferMap.json");
                    config = File.ReadAllText(configPath);
                }
                else
                {
                    using (var reader = new StreamReader(stream))
                    {
                        config = reader.ReadToEnd();
                    }
                }
            }

            _stateMachine = StateMachineFactory.Create(config);
            
            // Register conditions
            _stateMachine.RegisterCondition("isValidUpdate", (ctx, evt) =>
            {
                if (evt.Data is MapUpdateData update)
                {
                    return update.Version > (_mapData?.Version ?? 0);
                }
                return false;
            });
            
            _stateMachine.RegisterCondition("wasApplied", (ctx, evt) =>
            {
                return ctx["applyTime"] != null;
            });
            
            // Register actions
            _stateMachine.RegisterAction("validateMapData", (ctx, evt) =>
            {
                if (evt.Data is WaferMapData data)
                {
                    if (data.DieArray == null || data.DieArray.Length == 0)
                    {
                        throw new InvalidOperationException("Invalid wafer map: Die array is empty");
                    }
                    
                    if (data.BinDefinitions == null || data.BinDefinitions.Count == 0)
                    {
                        throw new InvalidOperationException("Invalid wafer map: No bin definitions");
                    }
                    
                    Console.WriteLine($"[E142] Map {_mapId}: Validated {data.DieArray.Length} dies, {data.BinDefinitions.Count} bins");
                }
            });
            
            _stateMachine.RegisterAction("storeMapData", (ctx, evt) =>
            {
                if (evt.Data is WaferMapData data)
                {
                    _mapData = data;
                    _binDefinitions.Clear();
                    _binDefinitions.AddRange(data.BinDefinitions);
                    
                    // Build die map for fast lookup
                    _dieMap.Clear();
                    foreach (var die in data.DieArray)
                    {
                        _dieMap[(die.X, die.Y)] = die;
                    }
                    
                    ctx["mapVersion"] = data.Version;
                }
            });
            
            _stateMachine.RegisterAction("recordLoadTime", (ctx, evt) =>
            {
                _loadTime = DateTime.UtcNow;
                ctx["loadTime"] = _loadTime;
            });
            
            _stateMachine.RegisterAction("recordApplyTime", (ctx, evt) =>
            {
                _applyTime = DateTime.UtcNow;
                ctx["applyTime"] = _applyTime;
            });
            
            _stateMachine.RegisterAction("applyMapToProcess", (ctx, evt) =>
            {
                Console.WriteLine($"[E142] Map {_mapId}: Applying to process - {_mapData?.DieArray.Length} dies");
                OnMapApplied?.Invoke(this, _mapData!);
            });
            
            _stateMachine.RegisterAction("releaseFromProcess", (ctx, evt) =>
            {
                Console.WriteLine($"[E142] Map {_mapId}: Releasing from process");
                _applyTime = null;
                ctx["applyTime"] = null;
                OnMapReleased?.Invoke(this, EventArgs.Empty);
            });
            
            _stateMachine.RegisterAction("prepareUpdate", (ctx, evt) =>
            {
                Console.WriteLine($"[E142] Map {_mapId}: Preparing for update");
            });
            
            _stateMachine.RegisterAction("applyUpdate", (ctx, evt) =>
            {
                if (evt.Data is MapUpdateData update)
                {
                    // Apply incremental updates
                    foreach (var dieUpdate in update.DieUpdates)
                    {
                        if (_dieMap.TryGetValue((dieUpdate.X, dieUpdate.Y), out var die))
                        {
                            die.BinCode = dieUpdate.NewBinCode;
                            die.TestResult = dieUpdate.TestResult;
                        }
                    }
                    
                    if (_mapData != null)
                    {
                        _mapData.Version = update.Version;
                    }
                    
                    Console.WriteLine($"[E142] Map {_mapId}: Applied {update.DieUpdates.Count} die updates");
                }
            });
            
            _stateMachine.RegisterAction("incrementUpdateCount", (ctx, evt) =>
            {
                var newCount = Interlocked.Increment(ref _updateCount);
                ctx["updateCount"] = newCount;
            });
            
            _stateMachine.RegisterAction("updateDieResult", (ctx, evt) =>
            {
                if (evt.Data is DieTestResult result)
                {
                    if (_dieMap.TryGetValue((result.X, result.Y), out var die))
                    {
                        die.BinCode = result.BinCode;
                        die.TestResult = result.TestResult;
                        Console.WriteLine($"[E142] Map {_mapId}: Die ({result.X},{result.Y}) -> Bin {result.BinCode}");
                    }
                }
            });
            
            _stateMachine.RegisterAction("provideDieInfo", (ctx, evt) =>
            {
                if (evt.Data is DieInfoRequest request)
                {
                    if (_dieMap.TryGetValue((request.X, request.Y), out var die))
                    {
                        OnDieInfoResponse?.Invoke(this, die);
                    }
                }
            });
            
            _stateMachine.RegisterAction("cleanupResources", (ctx, evt) =>
            {
                Console.WriteLine($"[E142] Map {_mapId}: Cleaning up resources");
            });
            
            _stateMachine.RegisterAction("clearMapData", (ctx, evt) =>
            {
                _mapData = null;
                _dieMap.Clear();
                _binDefinitions.Clear();
                _loadTime = null;
                _applyTime = null;
                _updateCount = 0;
                ctx["mapVersion"] = null;
                ctx["loadTime"] = null;
                ctx["applyTime"] = null;
                ctx["updateCount"] = 0;
            });
            
            _stateMachine.RegisterAction("logLoaded", (ctx, evt) =>
            {
                Console.WriteLine($"[E142] Map {_mapId}: Loaded - Version {_mapData?.Version}, {_mapData?.DieArray.Length} dies");
            });
            
            _stateMachine.RegisterAction("logApplied", (ctx, evt) =>
            {
                Console.WriteLine($"[E142] Map {_mapId}: Applied to process");
            });
            
            _stateMachine.RegisterAction("rejectUpdate", (ctx, evt) =>
            {
                Console.WriteLine($"[E142] Map {_mapId}: Update rejected - invalid version");
            });
            
            _stateMachine.Start();
            UpdateState();
        }
        
        private void UpdateState()
        {
            var state = _stateMachine.CurrentStates.FirstOrDefault()?.Name;
            if (string.IsNullOrEmpty(state) || state == "Unknown")
            {
                CurrentState = WaferMapState.NoMap;
            }
            else
            {
                CurrentState = Enum.Parse<WaferMapState>(state);
            }
        }
        
        // Map lifecycle operations
        public void Load(WaferMapData mapData)
        {
            _stateMachine.Send(new StateMachineEvent
            {
                Name = "LOAD",
                Data = mapData
            });
            UpdateState();
        }
        
        public void Apply()
        {
            _stateMachine.Send("APPLY");
            UpdateState();
        }
        
        public void Release()
        {
            _stateMachine.Send("RELEASE");
            UpdateState();
        }
        
        public void Update(MapUpdateData updateData)
        {
            _stateMachine.Send(new StateMachineEvent
            {
                Name = "UPDATE",
                Data = updateData
            });
            UpdateState();
        }
        
        public void UpdateComplete()
        {
            _stateMachine.Send("UPDATE_COMPLETE");
            UpdateState();
        }
        
        public void UpdateFailed()
        {
            _stateMachine.Send("UPDATE_FAILED");
            UpdateState();
        }
        
        public void Unload()
        {
            _stateMachine.Send("UNLOAD");
            UpdateState();
        }
        
        public void UnloadComplete()
        {
            _stateMachine.Send("UNLOAD_COMPLETE");
            UpdateState();
        }
        
        // Die operations
        public void UpdateDieTestResult(int x, int y, int binCode, string testResult)
        {
            _stateMachine.Send(new StateMachineEvent
            {
                Name = "DIE_TESTED",
                Data = new DieTestResult { X = x, Y = y, BinCode = binCode, TestResult = testResult }
            });
        }
        
        public DieData? GetDieInfo(int x, int y)
        {
            _dieMap.TryGetValue((x, y), out var die);
            return die;
        }
        
        // Statistics
        public WaferMapStatistics GetStatistics()
        {
            var stats = new WaferMapStatistics
            {
                TotalDies = _dieMap.Count,
                UpdateCount = _updateCount,
                LoadTime = _loadTime,
                ApplyTime = _applyTime
            };
            
            // Calculate bin statistics
            var binCounts = new ConcurrentDictionary<int, int>();
            foreach (var die in _dieMap.Values)
            {
                if (!binCounts.ContainsKey(die.BinCode))
                    binCounts[die.BinCode] = 0;
                binCounts[die.BinCode]++;
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
        
        // Events
        public event EventHandler<WaferMapData>? OnMapApplied;
        public event EventHandler? OnMapReleased;
        public event EventHandler<DieData>? OnDieInfoResponse;
        
        // Data classes
        public class WaferMapData
        {
            public int Version { get; set; }
            public string WaferId { get; set; } = string.Empty;
            public string LotId { get; set; } = string.Empty;
            public DieData[] DieArray { get; set; } = Array.Empty<DieData>();
            public List<BinData> BinDefinitions { get; set; } = new();
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
        
        public class BinData
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
        
        public class DieTestResult
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int BinCode { get; set; }
            public string TestResult { get; set; } = string.Empty;
        }
        
        public class DieInfoRequest
        {
            public int X { get; set; }
            public int Y { get; set; }
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
    }
}
