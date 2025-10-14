using CMPSimulator.Models;
using XStateNet.Orchestration;
using XStateNet.Semi.Standards;

namespace CMPSimulator.Services;

/// <summary>
/// Manages carriers and load ports according to SEMI E87/E90 specifications
/// Bridges between the CMP simulator domain and E87/E90 standards
/// </summary>
public class CarrierManager
{
    private readonly E87CarrierManagementMachine _e87Manager;
    private readonly E90SubstrateTrackingMachine _e90Tracker;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly Dictionary<string, Carrier> _carriers = new();
    private readonly Dictionary<string, string> _loadPortToCarrier = new();
    private readonly Dictionary<int, string> _waferToSubstrateId = new(); // Map wafer ID to E90 substrate ID

    public CarrierManager(string equipmentId, EventBusOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        _e87Manager = new E87CarrierManagementMachine(equipmentId, orchestrator);
        _e90Tracker = new E90SubstrateTrackingMachine(equipmentId, orchestrator);
    }

    /// <summary>
    /// Initialize load ports
    /// </summary>
    public async Task InitializeLoadPortsAsync(params string[] portIds)
    {
        foreach (var portId in portIds)
        {
            await _e87Manager.RegisterLoadPortAsync(portId, portId, capacity: 25);
        }
    }

    /// <summary>
    /// Create a new carrier with wafers and place it at a load port
    /// </summary>
    public async Task<Carrier> CreateAndPlaceCarrierAsync(string carrierId, string loadPortId, List<Wafer> wafers)
    {
        // Create CMP Carrier model
        var carrier = new Carrier(carrierId, capacity: 25)
        {
            CurrentLoadPort = loadPortId,
            ArrivedTime = DateTime.UtcNow
        };

        // Add wafers to carrier and register with E90 substrate tracking
        int slotNumber = 1;
        foreach (var wafer in wafers)
        {
            carrier.AddWafer(wafer);

            // Register each wafer as a substrate in E90
            var substrateId = $"{carrierId}_W{wafer.Id}";
            _waferToSubstrateId[wafer.Id] = substrateId;

            await _e90Tracker.RegisterSubstrateAsync(
                substrateId: substrateId,
                lotId: carrierId,
                slotNumber: slotNumber++
            );

            // Mark substrate as in carrier
            await _e90Tracker.UpdateLocationAsync(
                substrateId,
                loadPortId,
                SubstrateLocationType.Carrier
            );
        }

        _carriers[carrierId] = carrier;
        _loadPortToCarrier[loadPortId] = carrierId;

        // Register with E87 system
        var e87Carrier = await _e87Manager.CarrierArrivedAsync(carrierId, loadPortId, slotCount: wafers.Count);

        if (e87Carrier != null)
        {
            // Subscribe to E87 state changes
            e87Carrier.StateTransitioned += (sender, transition) =>
            {
                carrier.CurrentState = transition.newState;
                Console.WriteLine($"[CarrierManager] Carrier {carrierId}: {transition.oldState} → {transition.newState}");
            };

            // Automatically proceed with carrier processing
            await Task.Delay(100);
            await StartCarrierProcessingAsync(carrierId);
        }

        return carrier;
    }

    /// <summary>
    /// Start carrier processing (E87 workflow)
    /// </summary>
    public async Task<bool> StartCarrierProcessingAsync(string carrierId)
    {
        if (!_carriers.TryGetValue(carrierId, out var carrier))
            return false;

        // Start E87 processing
        var started = await _e87Manager.StartCarrierProcessingAsync(carrierId);

        if (started)
        {
            // Simulate slot mapping
            var slotMap = new Dictionary<int, SlotState>();
            for (int i = 0; i < carrier.Wafers.Count; i++)
            {
                slotMap[i + 1] = SlotState.Present;
            }

            await Task.Delay(100);
            await _e87Manager.UpdateSlotMapAsync(carrierId, slotMap);
            carrier.MappingCompleteTime = DateTime.UtcNow;

            await Task.Delay(100);
            await _e87Manager.VerifySlotMapAsync(carrierId, verified: true);

            await Task.Delay(100);
            await _e87Manager.StartAccessAsync(carrierId);
        }

        return started;
    }

    /// <summary>
    /// Mark carrier processing as complete
    /// </summary>
    public async Task<bool> CompleteCarrierProcessingAsync(string carrierId)
    {
        if (!_carriers.TryGetValue(carrierId, out var carrier))
            return false;

        carrier.CheckAllWafersCompleted();
        return await _e87Manager.CompleteAccessAsync(carrierId);
    }

    /// <summary>
    /// Remove carrier from load port
    /// </summary>
    public async Task<bool> RemoveCarrierAsync(string carrierId)
    {
        if (!_carriers.TryGetValue(carrierId, out var carrier))
            return false;

        carrier.DepartedTime = DateTime.UtcNow;

        // Remove from tracking
        if (carrier.CurrentLoadPort != null)
        {
            _loadPortToCarrier.Remove(carrier.CurrentLoadPort);
        }

        var removed = await _e87Manager.CarrierDepartedAsync(carrierId);

        if (removed)
        {
            _carriers.Remove(carrierId);
        }

        return removed;
    }

    /// <summary>
    /// Get carrier by ID
    /// </summary>
    public Carrier? GetCarrier(string carrierId)
    {
        return _carriers.TryGetValue(carrierId, out var carrier) ? carrier : null;
    }

    /// <summary>
    /// Get carrier currently at a load port
    /// </summary>
    public Carrier? GetCarrierAtLoadPort(string loadPortId)
    {
        if (_loadPortToCarrier.TryGetValue(loadPortId, out var carrierId))
        {
            return GetCarrier(carrierId);
        }
        return null;
    }

    /// <summary>
    /// Get all active carriers
    /// </summary>
    public IEnumerable<Carrier> GetAllCarriers()
    {
        return _carriers.Values;
    }

    /// <summary>
    /// Check if a load port is available (empty)
    /// </summary>
    public bool IsLoadPortAvailable(string loadPortId)
    {
        return !_loadPortToCarrier.ContainsKey(loadPortId);
    }

    /// <summary>
    /// Find first available carrier with pending wafers
    /// </summary>
    public Carrier? FindFirstAvailableCarrier(params string[] priorityLoadPorts)
    {
        // Check priority load ports first
        foreach (var loadPortId in priorityLoadPorts)
        {
            if (_loadPortToCarrier.TryGetValue(loadPortId, out var carrierId))
            {
                var carrier = GetCarrier(carrierId);
                if (carrier != null && !carrier.IsProcessingComplete &&
                    carrier.Wafers.Any(w => !w.IsCompleted))
                {
                    return carrier;
                }
            }
        }

        // Check all other carriers
        foreach (var carrier in _carriers.Values)
        {
            if (!carrier.IsProcessingComplete && carrier.Wafers.Any(w => !w.IsCompleted))
            {
                return carrier;
            }
        }

        return null;
    }

    /// <summary>
    /// Get E87 carrier state
    /// </summary>
    public string? GetCarrierE87State(string carrierId)
    {
        var e87Carrier = _e87Manager.GetCarrier(carrierId);
        return e87Carrier?.GetCurrentState();
    }

    /// <summary>
    /// Get E87 load port state
    /// </summary>
    public string? GetLoadPortE87State(string loadPortId)
    {
        var loadPort = _e87Manager.GetLoadPort(loadPortId);
        return loadPort?.GetCurrentState();
    }

    /// <summary>
    /// Track wafer movement to a processing station (E90)
    /// </summary>
    public async Task TrackWaferToProcessStationAsync(int waferId, string stationId)
    {
        if (_waferToSubstrateId.TryGetValue(waferId, out var substrateId))
        {
            // Determine location type
            var locationType = stationId switch
            {
                "Polisher" or "Cleaner" => SubstrateLocationType.ProcessModule,
                "Buffer" => SubstrateLocationType.Buffer,
                "R1" or "R2" or "R3" => SubstrateLocationType.TransferModule,
                _ => SubstrateLocationType.Other
            };

            await _e90Tracker.UpdateLocationAsync(substrateId, stationId, locationType);

            // If moving to process module, select for processing
            if (locationType == SubstrateLocationType.ProcessModule)
            {
                var substrate = _e90Tracker.GetSubstrate(substrateId);
                if (substrate != null)
                {
                    await substrate.SelectForProcessAsync();
                    await substrate.PlacedInProcessModuleAsync();
                }
            }
        }
    }

    /// <summary>
    /// Start wafer processing at a station (E90)
    /// </summary>
    public async Task StartWaferProcessingAsync(int waferId, string recipeId)
    {
        if (_waferToSubstrateId.TryGetValue(waferId, out var substrateId))
        {
            await _e90Tracker.StartProcessingAsync(substrateId, recipeId);
        }
    }

    /// <summary>
    /// Complete wafer processing at a station (E90)
    /// </summary>
    public async Task CompleteWaferProcessingAsync(int waferId)
    {
        if (_waferToSubstrateId.TryGetValue(waferId, out var substrateId))
        {
            await _e90Tracker.CompleteProcessingAsync(substrateId, success: true);
        }
    }

    /// <summary>
    /// Get E90 substrate state for a wafer
    /// </summary>
    public string? GetWaferE90State(int waferId)
    {
        if (_waferToSubstrateId.TryGetValue(waferId, out var substrateId))
        {
            var substrate = _e90Tracker.GetSubstrate(substrateId);
            return substrate?.GetCurrentState();
        }
        return null;
    }

    /// <summary>
    /// Get E90 substrate tracking history for a wafer
    /// </summary>
    public IReadOnlyList<SubstrateHistory> GetWaferHistory(int waferId)
    {
        if (_waferToSubstrateId.TryGetValue(waferId, out var substrateId))
        {
            return _e90Tracker.GetHistory(substrateId);
        }
        return new List<SubstrateHistory>().AsReadOnly();
    }
}
