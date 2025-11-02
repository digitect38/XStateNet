using Akka.Actor;
using CMPSimXS2.Console.Models;
using XStateNet2.Core.Messages;
using LoggerHelper;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Wafer Journey Scheduler - Master scheduler orchestrating 8-step wafer lifecycle
/// Journey: Carrier → R1 → Polisher → R2 → Cleaner → R3 → Buffer → R1 → Carrier
/// LOCK-BASED VERSION
/// </summary>
public class WaferJourneyScheduler : IWaferJourneyScheduler
{
    private readonly IRobotScheduler _robotScheduler;
    private readonly List<Wafer> _wafers;
    private readonly Dictionary<string, Station> _stations = new();
    private readonly object _lock = new();

    // Track which wafers are in each stage
    private readonly HashSet<int> _wafersInTransit = new(); // Currently being transferred
    private int _nextWaferToStart = 1;

    // Carrier tracking
    private string? _currentCarrierId = null;
    private readonly Dictionary<string, List<int>> _carrierWafers = new(); // CarrierId -> List of WaferIds
    private readonly Dictionary<string, bool> _carrierCompleted = new(); // Track if carrier processing is complete
    public event Action<string>? OnCarrierCompleted; // Event when all wafers in a carrier are processed

    public WaferJourneyScheduler(IRobotScheduler robotScheduler, List<Wafer> wafers)
    {
        _robotScheduler = robotScheduler;
        _wafers = wafers;
        Logger.Instance.Log("[WaferJourneyScheduler] Initialized - Ready to orchestrate 8-step wafer journey");
    }

    /// <summary>
    /// Register a station for monitoring
    /// </summary>
    public void RegisterStation(string stationName, Station station)
    {
        lock (_lock)
        {
            _stations[stationName] = station;
            Logger.Instance.Log($"[WaferJourneyScheduler] Registered station: {stationName}");
        }
    }

    /// <summary>
    /// Start wafer journey - called periodically to check and progress wafers
    /// Implements parallel_loop from scheduling rule - checks ALL robot conditions simultaneously
    ///
    /// SCHEDULING RULE (parallel_loop implementation):
    /// while (simulation_running) {
    ///     // Check all conditions in ONE iteration (not sequential)
    ///
    ///     // R2 operations: Polisher → Cleaner
    ///     if (platen.done && R2.is_empty && cleaner.is_empty) {
    ///         R2.pick()  // Pick from Polisher
    ///         R2.place() // Place to Cleaner
    ///     }
    ///
    ///     // R3 operations: Cleaner → Buffer
    ///     if (cleaner.done && R3.is_empty && buffer.is_empty) {
    ///         R3.pick()  // Pick from Cleaner
    ///         R3.place() // Place to Buffer
    ///     }
    ///
    ///     // R1 forward operation: Carrier → Polisher (start next wafer)
    ///     if (carrier.have.npw && platen.is_empty && R1.is_empty) {
    ///         R1.pick()  // Pick from Carrier (npw = non-processed wafer)
    ///         R1.place() // Place to Polisher
    ///     }
    ///
    ///     // R1 return operation: Buffer → Carrier (return completed wafer)
    ///     if (buffer.have && R1.is_empty) {
    ///         R1.pick()  // Pick from Buffer
    ///         R1.return() // Return to Carrier (pw = processed wafer)
    ///     }
    /// }
    /// </summary>
    public void ProcessWaferJourneys()
    {
        lock (_lock)
        {
            // PARALLEL_LOOP: Check all robot operations in one iteration
            // This ensures conditions are evaluated simultaneously, not sequentially

            // Step 1: Process wafers already in the pipeline (R2, R3 operations)
            foreach (var wafer in _wafers.ToList())
            {
                if (wafer.IsCompleted)
                    continue;

                ProcessWaferStage(wafer);
            }

            // Step 2: Try to start next wafer (R1 forward operation)
            // RULE: if(carrier.have.npw && platen.done) R1.pick()
            // RULE: if(R1.have.npw && platen.is_empty) R1.place()
            StartNextWaferIfPossible();

            // Step 3: Try to return completed wafers (R1 return operation)
            // RULE: if(buffer.have && R1.is_empty) R1.pick()
            // RULE: if(R1.have.pw) R1.return()
            // (Already handled by ProcessWaferStage for wafers in "InBuffer" stage)

            // Check if current carrier is complete
            IsCurrentCarrierComplete();
        }
    }

    /// <summary>
    /// Process a wafer based on its current journey stage
    /// </summary>
    private void ProcessWaferStage(Wafer wafer)
    {
        // Skip if wafer is currently in transit
        if (_wafersInTransit.Contains(wafer.Id))
            return;

        switch (wafer.JourneyStage)
        {
            case "InCarrier":
                // Ready to start - will be handled by StartNextWaferIfPossible
                break;

            case "ToPolisher":
                // Wait for R1 to complete transfer
                // (Transfer completion will update JourneyStage to "Polishing")
                break;

            case "Polishing":
                // Check if Polisher is done
                if (IsStationDone("Polisher", wafer.Id))
                {
                    // Unload from Polisher and request transfer to Cleaner
                    UnloadStationAndTransfer(wafer, "Polisher", "Cleaner", "ToCleaner");
                }
                break;

            case "ToCleaner":
                // Wait for R2 to complete transfer
                break;

            case "Cleaning":
                // Check if Cleaner is done
                if (IsStationDone("Cleaner", wafer.Id))
                {
                    // Unload from Cleaner and request transfer to Buffer
                    UnloadStationAndTransfer(wafer, "Cleaner", "Buffer", "ToBuffer");
                }
                break;

            case "ToBuffer":
                // Wait for R3 to complete transfer
                break;

            case "InBuffer":
                // Ready to return to Carrier - request transfer
                if (IsStationOccupied("Buffer", wafer.Id))
                {
                    RequestTransferToCarrier(wafer);
                }
                break;

            case "ToCarrier":
                // Wait for R1 to complete return transfer
                // (Transfer completion will mark wafer as completed)
                break;
        }
    }

    /// <summary>
    /// Start next wafer if Polisher is idle
    /// RULE: Polisher can only process ONE wafer at a time (must be idle)
    /// CARRIER RULE: Only process wafers from current carrier (if carrier tracking enabled)
    /// </summary>
    private void StartNextWaferIfPossible()
    {
        // Check if we have more wafers to start
        if (_nextWaferToStart > _wafers.Count)
            return;

        // If carrier tracking is enabled, check carrier constraints
        if (!string.IsNullOrEmpty(_currentCarrierId) && _carrierWafers.ContainsKey(_currentCarrierId))
        {
            var currentCarrierWafers = _carrierWafers[_currentCarrierId];

            // Check if we have more wafers to start from current carrier
            if (!currentCarrierWafers.Contains(_nextWaferToStart))
                return;
        }

        // ENFORCE RULE: Polisher must be idle (not processing any wafer)
        var polisher = GetStation("Polisher");
        if (polisher == null || polisher.CurrentState != "idle")
            return;

        // Safety check: Idle station should not have a wafer
        if (polisher.CurrentWafer.HasValue)
        {
            Logger.Instance.Log($"[WaferJourneyScheduler:WARNING] Polisher is idle but still has wafer {polisher.CurrentWafer}! Clearing...");
            polisher.CurrentWafer = null;
        }

        // Start next wafer
        var wafer = _wafers.FirstOrDefault(w => w.Id == _nextWaferToStart);
        if (wafer != null && wafer.JourneyStage == "InCarrier")
        {
            RequestTransferToPolisher(wafer);
            _nextWaferToStart++;
        }
    }

    /// <summary>
    /// Request transfer from Carrier to Polisher (Step 1-2)
    /// </summary>
    private void RequestTransferToPolisher(Wafer wafer)
    {
        Logger.Instance.Log($"[WaferJourneyScheduler] [Wafer {wafer.Id}] Starting journey: Carrier → Polisher");

        wafer.JourneyStage = "ToPolisher";
        wafer.CurrentStation = "Robot 1";
        _wafersInTransit.Add(wafer.Id);

        var request = new TransferRequest
        {
            WaferId = wafer.Id,
            From = "Carrier",
            To = "Polisher",
            Priority = 1,
            PreferredRobotId = "Robot 1",
            OnCompleted = (waferId) => OnTransferCompleted(waferId, "Polisher", "Polishing")
        };

        _robotScheduler.RequestTransfer(request);
    }

    /// <summary>
    /// Unload station and request transfer to next destination
    /// </summary>
    private void UnloadStationAndTransfer(Wafer wafer, string fromStation, string toStation, string newJourneyStage)
    {
        Logger.Instance.Log($"[WaferJourneyScheduler] [Wafer {wafer.Id}] Unloading from {fromStation} → {toStation}");

        // Update wafer processing state if moving from Polisher
        if (fromStation == "Polisher")
        {
            wafer.ProcessingState = "Polished"; // Font becomes YELLOW
        }
        else if (fromStation == "Cleaner")
        {
            wafer.ProcessingState = "Cleaned"; // Font becomes WHITE
        }

        // Unload from station - update state immediately for pipeline flow
        var station = GetStation(fromStation);
        if (station?.StateMachine != null)
        {
            station.StateMachine.Tell(new SendEvent("UNLOAD_WAFER", null));

            // Immediately clear station state so next wafer can start
            station.CurrentWafer = null;
            station.CurrentState = "idle";
            Logger.Instance.Log($"[WaferJourneyScheduler:DEBUG] {fromStation} now idle, ready for next wafer");
        }

        // Update wafer journey stage and request transfer
        wafer.JourneyStage = newJourneyStage;
        _wafersInTransit.Add(wafer.Id);

        // Determine preferred robot
        string? preferredRobot = DetermineRobotForRoute(fromStation, toStation);
        string nextStageAfterTransfer = GetNextStageAfterTransfer(toStation);

        var request = new TransferRequest
        {
            WaferId = wafer.Id,
            From = fromStation,
            To = toStation,
            Priority = 1,
            PreferredRobotId = preferredRobot,
            OnCompleted = (waferId) => OnTransferCompleted(waferId, toStation, nextStageAfterTransfer)
        };

        _robotScheduler.RequestTransfer(request);
    }

    /// <summary>
    /// Request transfer from Buffer to Carrier (Step 8)
    /// </summary>
    private void RequestTransferToCarrier(Wafer wafer)
    {
        Logger.Instance.Log($"[WaferJourneyScheduler] [Wafer {wafer.Id}] Returning to Carrier: Buffer → Carrier");

        wafer.JourneyStage = "ToCarrier";
        wafer.CurrentStation = "Robot 1";
        _wafersInTransit.Add(wafer.Id);

        // Send RETRIEVE_WAFER event to Buffer (robot will pick up wafer)
        var buffer = GetStation("Buffer");
        if (buffer?.StateMachine != null)
        {
            buffer.StateMachine.Tell(new SendEvent("RETRIEVE_WAFER", null));
        }

        var request = new TransferRequest
        {
            WaferId = wafer.Id,
            From = "Buffer",
            To = "Carrier",
            Priority = 2, // Higher priority for completed wafers
            PreferredRobotId = "Robot 1",
            OnCompleted = (waferId) =>
            {
                // Clear buffer state when transfer completes
                if (buffer != null)
                {
                    buffer.CurrentWafer = null;
                    buffer.CurrentState = "idle";
                    Logger.Instance.Log("[WaferJourneyScheduler:DEBUG] Buffer now idle after wafer retrieval");
                }
                OnWaferCompleted(waferId);
            }
        };

        _robotScheduler.RequestTransfer(request);
    }

    /// <summary>
    /// Callback when transfer is completed
    /// RULE: Station can only hold ONE wafer at a time
    /// </summary>
    private void OnTransferCompleted(int waferId, string arrivedAt, string nextStage)
    {
        var wafer = _wafers.FirstOrDefault(w => w.Id == waferId);
        if (wafer == null) return;

        Logger.Instance.Log($"[WaferJourneyScheduler] [Wafer {waferId}] Arrived at {arrivedAt}, transitioning to {nextStage}");

        wafer.CurrentStation = arrivedAt;
        wafer.JourneyStage = nextStage;
        _wafersInTransit.Remove(waferId);

        // Load into destination station if needed
        var station = GetStation(arrivedAt);
        if (station?.StateMachine != null && (arrivedAt == "Polisher" || arrivedAt == "Cleaner" || arrivedAt == "Buffer"))
        {
            // ENFORCE RULE: Station must be idle before loading a wafer
            if (station.CurrentWafer.HasValue && station.CurrentWafer.Value != waferId)
            {
                Logger.Instance.Log($"[WaferJourneyScheduler:ERROR] RULE VIOLATION: {arrivedAt} already has wafer {station.CurrentWafer}, cannot load wafer {waferId}!");
                return;
            }

            // CRITICAL FIX: Update station state IMMEDIATELY to prevent race condition
            // This ensures the next timer tick sees the station as occupied
            // Buffer uses "occupied" state, others use "processing"
            var newState = arrivedAt == "Buffer" ? "occupied" : "processing";
            station.CurrentWafer = waferId;
            station.CurrentState = newState;
            Logger.Instance.Log($"[WaferJourneyScheduler:DEBUG] {arrivedAt} state updated immediately: {newState} (wafer {waferId})");

            var eventName = arrivedAt == "Buffer" ? "STORE_WAFER" : "LOAD_WAFER";
            var eventData = new Dictionary<string, object> { ["wafer"] = waferId };
            station.StateMachine.Tell(new SendEvent(eventName, eventData));
        }
    }

    /// <summary>
    /// Callback when wafer completes full journey
    /// </summary>
    private void OnWaferCompleted(int waferId)
    {
        var wafer = _wafers.FirstOrDefault(w => w.Id == waferId);
        if (wafer == null) return;

        Logger.Instance.Log($"[WaferJourneyScheduler] [Wafer {waferId}] ✓ COMPLETED - Journey finished, returned to Carrier");

        wafer.CurrentStation = "Carrier";
        wafer.JourneyStage = "InCarrier";
        wafer.IsCompleted = true;
        _wafersInTransit.Remove(waferId);
    }

    /// <summary>
    /// Check if station is done processing
    /// </summary>
    private bool IsStationDone(string stationName, int expectedWaferId)
    {
        var station = GetStation(stationName);
        return station?.CurrentState == "done" && station.CurrentWafer == expectedWaferId;
    }

    /// <summary>
    /// Check if station is occupied with wafer
    /// </summary>
    private bool IsStationOccupied(string stationName, int expectedWaferId)
    {
        var station = GetStation(stationName);
        return station?.CurrentState == "occupied" && station.CurrentWafer == expectedWaferId;
    }

    /// <summary>
    /// Get station by name
    /// </summary>
    private Station? GetStation(string stationName)
    {
        return _stations.GetValueOrDefault(stationName);
    }

    /// <summary>
    /// Determine which robot should handle a route
    /// </summary>
    private string? DetermineRobotForRoute(string from, string to)
    {
        // R1: Carrier ↔ Polisher, Buffer ↔ Carrier
        if ((from == "Carrier" && to == "Polisher") || (from == "Buffer" && to == "Carrier"))
            return "Robot 1";

        // R2: Polisher ↔ Cleaner
        if ((from == "Polisher" && to == "Cleaner"))
            return "Robot 2";

        // R3: Cleaner ↔ Buffer
        if ((from == "Cleaner" && to == "Buffer"))
            return "Robot 3";

        return null;
    }

    /// <summary>
    /// Get next journey stage after transfer arrives
    /// </summary>
    private string GetNextStageAfterTransfer(string destination)
    {
        return destination switch
        {
            "Polisher" => "Polishing",
            "Cleaner" => "Cleaning",
            "Buffer" => "InBuffer",
            "Carrier" => "InCarrier",
            _ => "InCarrier"
        };
    }

    /// <summary>
    /// Handle carrier arrival event - registers carrier and its wafers
    /// </summary>
    public void OnCarrierArrival(string carrierId, List<int> waferIds)
    {
        lock (_lock)
        {
            _currentCarrierId = carrierId;
            _carrierWafers[carrierId] = waferIds;
            _carrierCompleted[carrierId] = false;

            // Reset next wafer to start to first wafer of this carrier
            if (waferIds.Count > 0)
            {
                _nextWaferToStart = waferIds[0];
            }

            Logger.Instance.Log($"[WaferJourneyScheduler] Carrier {carrierId} arrived with {waferIds.Count} wafers (IDs: {string.Join(", ", waferIds)})");
        }
    }

    /// <summary>
    /// Handle carrier departure event - marks carrier as departed
    /// </summary>
    public void OnCarrierDeparture(string carrierId)
    {
        lock (_lock)
        {
            if (_carrierCompleted.ContainsKey(carrierId))
            {
                _carrierCompleted[carrierId] = true;
            }

            Logger.Instance.Log($"[WaferJourneyScheduler] Carrier {carrierId} departed with all wafers processed");

            // Clear current carrier if it's the one departing
            if (_currentCarrierId == carrierId)
            {
                _currentCarrierId = null;
            }
        }
    }

    /// <summary>
    /// Check if current carrier has completed all wafers
    /// </summary>
    public bool IsCurrentCarrierComplete()
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_currentCarrierId) || !_carrierWafers.ContainsKey(_currentCarrierId))
                return false;

            var carrierWaferIds = _carrierWafers[_currentCarrierId];
            var allComplete = carrierWaferIds.All(waferId =>
            {
                var wafer = _wafers.FirstOrDefault(w => w.Id == waferId);
                return wafer?.IsCompleted == true;
            });

            if (allComplete && !_carrierCompleted[_currentCarrierId])
            {
                Logger.Instance.Log($"[WaferJourneyScheduler] All wafers in Carrier {_currentCarrierId} completed!");

                // Trigger completion event
                OnCarrierCompleted?.Invoke(_currentCarrierId);
            }

            return allComplete;
        }
    }

    /// <summary>
    /// Get current carrier ID
    /// </summary>
    public string? GetCurrentCarrierId() => _currentCarrierId;

    /// <summary>
    /// Reset scheduler for new simulation
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _wafersInTransit.Clear();
            _nextWaferToStart = 1;
            _currentCarrierId = null;
            _carrierWafers.Clear();
            _carrierCompleted.Clear();
            Logger.Instance.Log("[WaferJourneyScheduler] Reset - Ready for new simulation");
        }
    }
}
