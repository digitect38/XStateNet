using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XStateNet.Semi.Transport;
using XStateNet.Semi.Secs;

namespace XStateNet.Semi.Testing
{
    /// <summary>
    /// Realistic photolithography equipment simulator demonstrating
    /// carrier management, recipe processing, and wafer handling
    /// </summary>
    public class RealisticEquipmentSimulator : EquipmentSimulator
    {
        private readonly ConcurrentDictionary<string, CarrierInfo> _carriers = new();
        private readonly ConcurrentDictionary<string, RecipeInfo> _recipes = new();
        private readonly ConcurrentDictionary<string, ProcessJobInfo> _processJobs = new();
        private readonly Random _random = new();
        
        private volatile ProcessingState _processingState = ProcessingState.Idle;
        private int _totalWafersProcessed = 0;
        private int _totalWafersFailed = 0;
        private double _temperature = 23.5;
        private double _pressure = 1013.25;
        
        public RealisticEquipmentSimulator(IPEndPoint endpoint, ILogger<EquipmentSimulator>? logger = null) 
            : base(endpoint, logger)
        {
            ModelName = "ASML-XT1900Gi";
            SoftwareRevision = "3.2.1";
            ResponseDelayMs = 50;
            
            InitializeRecipes();
            RegisterRealisticHandlers();
        }
        
        private void InitializeRecipes()
        {
            _recipes["PROC_193NM_STD"] = new RecipeInfo
            {
                RecipeId = "PROC_193NM_STD",
                RecipeName = "193nm Standard Process",
                ExposureTime = 2500,
                ExposureEnergy = 15.5
            };
            
            _recipes["PROC_193NM_CRIT"] = new RecipeInfo
            {
                RecipeId = "PROC_193NM_CRIT",
                RecipeName = "193nm Critical Layer",
                ExposureTime = 3200,
                ExposureEnergy = 18.2
            };
        }
        
        private void RegisterRealisticHandlers()
        {
            // Override S2F41 Host Command handler
            RegisterHandler("S2F41", HandleHostCommand);
            
            // Custom handler for carrier actions (using S3F17 as example)
            RegisterHandler("S3F17", HandleCarrierAction);
            
            // Recipe management
            RegisterHandler("S7F1", HandleProcessProgramLoad);
            RegisterHandler("S7F19", HandleRecipeListRequest);
        }
        
        private async Task<SecsMessage> HandleHostCommand(SecsMessage request)
        {
            await Task.Delay(ResponseDelayMs);
            
            // Extract command from message
            if (request.Data is SecsList list && list.Items.Count >= 2)
            {
                var commandItem = list.Items[0] as SecsAscii;
                var command = commandItem?.Value ?? "";
                
                // Log command received
                
                switch (command.ToUpper())
                {
                    case "START":
                        if (_processingState == ProcessingState.Idle && _carriers.Any())
                        {
                            _ = Task.Run(() => StartProcessingAsync());
                            return CreateS2F42Response(0); // OK
                        }
                        return CreateS2F42Response(1); // Cannot perform now
                        
                    case "STOP":
                        _processingState = ProcessingState.Stopping;
                        return CreateS2F42Response(0);
                        
                    case "PAUSE":
                        if (_processingState == ProcessingState.Processing)
                        {
                            _processingState = ProcessingState.Paused;
                            return CreateS2F42Response(0);
                        }
                        return CreateS2F42Response(1);
                        
                    case "RESUME":
                        if (_processingState == ProcessingState.Paused)
                        {
                            _processingState = ProcessingState.Processing;
                            return CreateS2F42Response(0);
                        }
                        return CreateS2F42Response(1);
                        
                    case "INIT":
                        await InitializeEquipmentAsync();
                        return CreateS2F42Response(0);
                        
                    case "REMOTE":
                        ControlState = ControlStateEnum.Remote;
                        await TriggerEventAsync(1002, new List<SecsItem>
                        {
                            new SecsAscii("EQUIPMENT_STATE"),
                            new SecsAscii(ControlState.ToString())
                        });
                        return CreateS2F42Response(0);
                        
                    case "LOCAL":
                        ControlState = ControlStateEnum.Local;
                        await TriggerEventAsync(1003, new List<SecsItem>
                        {
                            new SecsAscii("EQUIPMENT_STATE"),
                            new SecsAscii(ControlState.ToString())
                        });
                        return CreateS2F42Response(0);
                        
                    default:
                        return CreateS2F42Response(2); // Invalid command
                }
            }
            
            return CreateS2F42Response(2);
        }
        
        private async Task<SecsMessage> HandleCarrierAction(SecsMessage request)
        {
            await Task.Delay(ResponseDelayMs);
            
            // Parse carrier action from message
            if (request.Data is SecsList list && list.Items.Count >= 3)
            {
                var carrierId = (list.Items[0] as SecsAscii)?.Value ?? "";
                var action = (list.Items[1] as SecsAscii)?.Value ?? "";
                var loadPort = (list.Items[2] as SecsU4)?.Value ?? 0;
                
                // Log carrier action
                
                switch (action.ToUpper())
                {
                    case "LOAD":
                        return await LoadCarrierAsync(carrierId, (int)loadPort);
                        
                    case "UNLOAD":
                        return await UnloadCarrierAsync(carrierId);
                        
                    case "MAP":
                        return await MapCarrierAsync(carrierId);
                        
                    default:
                        return CreateCarrierActionResponse(1); // Error
                }
            }
            
            return CreateCarrierActionResponse(1);
        }
        
        private async Task<SecsMessage> LoadCarrierAsync(string carrierId, int loadPort)
        {
            if (_carriers.ContainsKey(carrierId))
            {
                return CreateCarrierActionResponse(2); // Already loaded
            }
            
            var carrier = new CarrierInfo
            {
                CarrierId = carrierId,
                LoadPort = loadPort,
                State = CarrierState.WaitingForMapping,
                LoadTime = DateTime.Now,
                WaferCount = 25
            };
            
            _carriers[carrierId] = carrier;
            
            await TriggerEventAsync(2001, new List<SecsItem>
            {
                new SecsAscii("CARRIER_ID"),
                new SecsAscii(carrierId),
                new SecsAscii("LOAD_PORT"),
                new SecsU4((uint)loadPort),
                new SecsAscii("WAFER_COUNT"),
                new SecsU4((uint)carrier.WaferCount)
            });
            
            // Auto-map after load
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                await MapCarrierAsync(carrierId);
            });
            
            return CreateCarrierActionResponse(0); // Success
        }
        
        private async Task<SecsMessage> UnloadCarrierAsync(string carrierId)
        {
            if (!_carriers.TryRemove(carrierId, out var carrier))
            {
                return CreateCarrierActionResponse(3); // Not found
            }
            
            await TriggerEventAsync(2002, new List<SecsItem>
            {
                new SecsAscii("CARRIER_ID"),
                new SecsAscii(carrierId),
                new SecsAscii("WAFERS_PROCESSED"),
                new SecsU4((uint)carrier.ProcessedWafers),
                new SecsAscii("WAFERS_FAILED"),
                new SecsU4((uint)carrier.FailedWafers)
            });
            
            return CreateCarrierActionResponse(0);
        }
        
        private async Task<SecsMessage> MapCarrierAsync(string carrierId)
        {
            if (!_carriers.TryGetValue(carrierId, out var carrier))
            {
                return CreateCarrierActionResponse(3);
            }
            
            carrier.State = CarrierState.Mapping;
            await Task.Delay(3000); // Simulate mapping time
            carrier.State = CarrierState.Mapped;
            
            // Generate slot map
            carrier.SlotMap = new bool[carrier.WaferCount];
            for (int i = 0; i < carrier.WaferCount; i++)
            {
                carrier.SlotMap[i] = _random.NextDouble() > 0.05; // 95% occupied
            }
            
            await TriggerEventAsync(2003, new List<SecsItem>
            {
                new SecsAscii("CARRIER_ID"),
                new SecsAscii(carrierId),
                new SecsAscii("SLOTS_OCCUPIED"),
                new SecsU4((uint)carrier.SlotMap.Count(s => s))
            });
            
            return CreateCarrierActionResponse(0);
        }
        
        private async Task<SecsMessage> HandleProcessProgramLoad(SecsMessage request)
        {
            await Task.Delay(ResponseDelayMs);
            
            if (request.Data is SecsList list && list.Items.Count >= 1)
            {
                var recipeId = (list.Items[0] as SecsAscii)?.Value ?? "";
                
                if (!_recipes.ContainsKey(recipeId))
                {
                    return CreateS7F2Response(1); // Recipe not found
                }
                
                // Recipe loaded
                
                await TriggerEventAsync(3001, new List<SecsItem>
                {
                    new SecsAscii("RECIPE_ID"),
                    new SecsAscii(recipeId),
                    new SecsAscii("RECIPE_NAME"),
                    new SecsAscii(_recipes[recipeId].RecipeName)
                });
                
                return CreateS7F2Response(0); // Success
            }
            
            return CreateS7F2Response(1);
        }
        
        private async Task<SecsMessage> HandleRecipeListRequest(SecsMessage request)
        {
            await Task.Delay(ResponseDelayMs);
            
            var recipeItems = new List<SecsItem>();
            foreach (var recipe in _recipes.Values)
            {
                recipeItems.Add(new SecsList(
                    new SecsAscii(recipe.RecipeId),
                    new SecsAscii(recipe.RecipeName)
                ));
            }
            
            return new SecsMessage(7, 20, false)
            {
                Data = new SecsList(recipeItems.ToArray())
            };
        }
        
        private async Task InitializeEquipmentAsync()
        {
            // Initializing equipment
            
            _temperature = 23.5 + (_random.NextDouble() - 0.5) * 0.2;
            _pressure = 1013.25 + (_random.NextDouble() - 0.5) * 2.0;
            
            await TriggerEventAsync(5001, new List<SecsItem>
            {
                new SecsAscii("TEMPERATURE"),
                new SecsF8(_temperature),
                new SecsAscii("PRESSURE"),
                new SecsF8(_pressure)
            });
            
            await Task.Delay(2000);
            // Equipment initialization complete
        }
        
        private async Task StartProcessingAsync()
        {
            _processingState = ProcessingState.Processing;
            
            await TriggerEventAsync(6001, new List<SecsItem>
            {
                new SecsAscii("PROCESSING_STARTED"),
                new SecsAscii(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            });
            
            // Process carriers
            foreach (var carrier in _carriers.Values.Where(c => c.State == CarrierState.Mapped))
            {
                if (_processingState == ProcessingState.Stopping)
                    break;
                    
                await ProcessCarrierAsync(carrier);
            }
            
            _processingState = ProcessingState.Idle;
            
            await TriggerEventAsync(6002, new List<SecsItem>
            {
                new SecsAscii("PROCESSING_COMPLETE"),
                new SecsAscii("TOTAL_PROCESSED"),
                new SecsU4((uint)_totalWafersProcessed),
                new SecsAscii("TOTAL_FAILED"),
                new SecsU4((uint)_totalWafersFailed)
            });
        }
        
        private async Task ProcessCarrierAsync(CarrierInfo carrier)
        {
            carrier.State = CarrierState.Processing;
            
            // Process each wafer in the carrier
            for (int slot = 0; slot < carrier.SlotMap.Length; slot++)
            {
                if (!carrier.SlotMap[slot])
                    continue; // Skip empty slots
                    
                if (_processingState == ProcessingState.Stopping)
                    break;
                    
                while (_processingState == ProcessingState.Paused)
                {
                    await Task.Delay(500);
                }
                
                await ProcessWaferAsync(carrier, slot);
            }
            
            carrier.State = CarrierState.Complete;
        }
        
        private async Task ProcessWaferAsync(CarrierInfo carrier, int slot)
        {
            string waferId = $"{carrier.CarrierId}_W{slot:D2}";
            
            await TriggerEventAsync(7001, new List<SecsItem>
            {
                new SecsAscii("WAFER_START"),
                new SecsAscii(waferId),
                new SecsU4((uint)slot)
            });
            
            // Simulate processing with realistic timing
            await Task.Delay(2500); // Base exposure time
            
            // Simulate process variations
            _temperature += (_random.NextDouble() - 0.5) * 0.1;
            _pressure += (_random.NextDouble() - 0.5) * 0.5;
            
            // Determine pass/fail
            bool passed = _random.NextDouble() > 0.02; // 98% yield
            
            if (passed)
            {
                _totalWafersProcessed++;
                carrier.ProcessedWafers++;
            }
            else
            {
                _totalWafersFailed++;
                carrier.FailedWafers++;
                await TriggerAlarmAsync(8001, $"Wafer {waferId} failed quality check", true);
                await Task.Delay(100);
                await TriggerAlarmAsync(8001, $"Wafer {waferId} failed quality check", false);
            }
            
            await TriggerEventAsync(7002, new List<SecsItem>
            {
                new SecsAscii("WAFER_COMPLETE"),
                new SecsAscii(waferId),
                new SecsAscii(passed ? "PASS" : "FAIL")
            });
            
            // Processed wafer
        }
        
        // Helper methods to create response messages
        private SecsMessage CreateS2F42Response(byte hcack)
        {
            return new SecsMessage(2, 42, false)
            {
                Data = new SecsList(
                    new SecsU1(hcack),
                    new SecsList() // Empty parameter list
                )
            };
        }
        
        private SecsMessage CreateCarrierActionResponse(byte result)
        {
            return new SecsMessage(3, 18, false)
            {
                Data = new SecsU1(result)
            };
        }
        
        private SecsMessage CreateS7F2Response(byte ackc7)
        {
            return new SecsMessage(7, 2, false)
            {
                Data = new SecsU1(ackc7)
            };
        }
        
        // Enums and helper classes
        private enum ProcessingState
        {
            Idle,
            Processing,
            Paused,
            Stopping
        }
        
        private enum CarrierState
        {
            WaitingForMapping,
            Mapping,
            Mapped,
            Processing,
            Complete
        }
        
        private class CarrierInfo
        {
            public string CarrierId { get; set; } = "";
            public int LoadPort { get; set; }
            public CarrierState State { get; set; }
            public DateTime LoadTime { get; set; }
            public int WaferCount { get; set; }
            public bool[] SlotMap { get; set; } = Array.Empty<bool>();
            public int ProcessedWafers { get; set; }
            public int FailedWafers { get; set; }
        }
        
        private class RecipeInfo
        {
            public string RecipeId { get; set; } = "";
            public string RecipeName { get; set; } = "";
            public double ExposureTime { get; set; }
            public double ExposureEnergy { get; set; }
        }
        
        private class ProcessJobInfo
        {
            public string JobId { get; set; } = "";
            public string CarrierId { get; set; } = "";
            public string RecipeId { get; set; } = "";
            public List<int> WaferSlots { get; set; } = new();
            public DateTime CreatedTime { get; set; }
        }
    }
}