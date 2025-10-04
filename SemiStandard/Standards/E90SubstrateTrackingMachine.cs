using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Standards;

/// <summary>
/// E90 Substrate Tracking Machine - SEMI E90 Standard
/// Manages substrate (wafer) lifecycle tracking through processing
/// Refactored to use ExtendedPureStateMachineFactory with EventBusOrchestrator
/// </summary>
public class E90SubstrateTrackingMachine
{
    private readonly string _equipmentId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly ConcurrentDictionary<string, SubstrateMachine> _substrates = new();
    private readonly ConcurrentDictionary<string, SubstrateLocation> _locations = new();
    private readonly ConcurrentDictionary<string, List<SubstrateHistory>> _history = new();

    public string MachineId => $"E90_TRACKING_{_equipmentId}";

    public E90SubstrateTrackingMachine(string equipmentId, EventBusOrchestrator orchestrator)
    {
        _equipmentId = equipmentId;
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Register a new substrate for tracking
    /// </summary>
    public async Task<SubstrateMachine> RegisterSubstrateAsync(string substrateId, string? lotId = null, int? slotNumber = null)
    {
        if (_substrates.ContainsKey(substrateId))
        {
            return _substrates[substrateId];
        }

        var substrate = new SubstrateMachine(substrateId, lotId, slotNumber, _equipmentId, _orchestrator);
        _substrates[substrateId] = substrate;
        _history[substrateId] = new List<SubstrateHistory>();

        await substrate.StartAsync();

        AddHistory(substrateId, "WaitingForHost", null, "Substrate registered");

        // Notify E87 carrier management
        await _orchestrator.SendEventAsync(
            MachineId,
            "E87_CARRIER_MANAGEMENT",
            "SUBSTRATE_REGISTERED",
            new JObject
            {
                ["substrateId"] = substrateId,
                ["lotId"] = lotId,
                ["slotNumber"] = slotNumber
            }
        );

        return substrate;
    }

    /// <summary>
    /// Update substrate location
    /// </summary>
    public async Task UpdateLocationAsync(string substrateId, string locationId, SubstrateLocationType locationType)
    {
        if (!_substrates.TryGetValue(substrateId, out var substrate))
            return;

        var location = new SubstrateLocation(locationId, locationType);

        // Record location change in history
        if (_locations.TryGetValue(substrateId, out var prevLocation))
        {
            AddHistory(substrateId, null, locationId,
                $"Moved from {prevLocation.LocationId} to {locationId}");
        }
        else
        {
            AddHistory(substrateId, null, locationId,
                $"Located at {locationId}");
        }

        _locations[substrateId] = location;

        // Send location change event to substrate state machine
        switch (locationType)
        {
            case SubstrateLocationType.ProcessModule:
                await substrate.PlacedInProcessModuleAsync();
                break;
            case SubstrateLocationType.Carrier:
                await substrate.PlacedInCarrierAsync();
                break;
            case SubstrateLocationType.Aligner:
                await substrate.PlacedInAlignerAsync();
                break;
        }

        // Notify other systems of location change
        await _orchestrator.SendEventAsync(
            MachineId,
            "E87_CARRIER_MANAGEMENT",
            "SUBSTRATE_LOCATION_CHANGED",
            new JObject
            {
                ["substrateId"] = substrateId,
                ["locationId"] = locationId,
                ["locationType"] = locationType.ToString()
            }
        );
    }

    /// <summary>
    /// Start processing a substrate
    /// </summary>
    public async Task<bool> StartProcessingAsync(string substrateId, string? recipeId = null)
    {
        if (_substrates.TryGetValue(substrateId, out var substrate))
        {
            substrate.RecipeId = recipeId;
            var result = await substrate.StartProcessAsync();
            AddHistory(substrateId, "InProcess", null, $"Started processing with recipe {recipeId}");

            // Notify E40 process job
            await _orchestrator.SendEventAsync(
                MachineId,
                "E40_PROCESS_JOB",
                "SUBSTRATE_PROCESSING_STARTED",
                new JObject
                {
                    ["substrateId"] = substrateId,
                    ["recipeId"] = recipeId
                }
            );

            return result.Success;
        }
        return false;
    }

    /// <summary>
    /// Complete processing
    /// </summary>
    public async Task<bool> CompleteProcessingAsync(string substrateId, bool success = true)
    {
        if (_substrates.TryGetValue(substrateId, out var substrate))
        {
            var result = success
                ? await substrate.CompleteProcessAsync()
                : await substrate.AbortProcessAsync();

            AddHistory(substrateId, success ? "Processed" : "Aborted", null,
                success ? "Processing completed" : "Processing aborted");

            // Notify E40 process job
            await _orchestrator.SendEventAsync(
                MachineId,
                "E40_PROCESS_JOB",
                success ? "SUBSTRATE_PROCESSING_COMPLETE" : "SUBSTRATE_PROCESSING_ABORTED",
                new JObject
                {
                    ["substrateId"] = substrateId,
                    ["processTime"] = substrate.ProcessingTime?.TotalSeconds
                }
            );

            return result.Success;
        }
        return false;
    }

    /// <summary>
    /// Remove substrate from tracking
    /// </summary>
    public async Task<bool> RemoveSubstrateAsync(string substrateId)
    {
        if (_substrates.TryGetValue(substrateId, out var substrate))
        {
            await substrate.RemoveAsync();
            _substrates.TryRemove(substrateId, out _);
            _locations.TryRemove(substrateId, out _);
            AddHistory(substrateId, "Removed", null, "Substrate removed from tracking");

            // Notify E87 carrier management
            await _orchestrator.SendEventAsync(
                MachineId,
                "E87_CARRIER_MANAGEMENT",
                "SUBSTRATE_REMOVED",
                new JObject
                {
                    ["substrateId"] = substrateId
                }
            );

            return true;
        }
        return false;
    }

    /// <summary>
    /// Get substrate information
    /// </summary>
    public SubstrateMachine? GetSubstrate(string substrateId)
    {
        return _substrates.TryGetValue(substrateId, out var substrate) ? substrate : null;
    }

    /// <summary>
    /// Get all substrates in a specific state
    /// </summary>
    public IEnumerable<SubstrateMachine> GetSubstratesByState(string stateName)
    {
        return _substrates.Values.Where(s => s.GetCurrentState().Contains(stateName));
    }

    /// <summary>
    /// Get all substrates at a specific location
    /// </summary>
    public IEnumerable<string> GetSubstratesAtLocation(string locationId)
    {
        return _locations
            .Where(kvp => kvp.Value.LocationId == locationId)
            .Select(kvp => kvp.Key);
    }

    /// <summary>
    /// Get substrate history
    /// </summary>
    public IReadOnlyList<SubstrateHistory> GetHistory(string substrateId)
    {
        return _history.TryGetValue(substrateId, out var history)
            ? history.AsReadOnly()
            : new List<SubstrateHistory>().AsReadOnly();
    }

    /// <summary>
    /// Add history entry
    /// </summary>
    private void AddHistory(string substrateId, string? state, string? location, string description)
    {
        if (_history.TryGetValue(substrateId, out var history))
        {
            history.Add(new SubstrateHistory
            {
                Timestamp = DateTime.UtcNow,
                State = state,
                Location = location,
                Description = description
            });
        }
    }
}

/// <summary>
/// Individual substrate state machine using orchestrator
/// </summary>
public class SubstrateMachine
{
    private readonly IPureStateMachine _machine;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly string _instanceId;

    public string Id { get; }
    public string? LotId { get; set; }
    public int? SlotNumber { get; set; }
    public DateTime AcquiredTime { get; }
    public DateTime? ProcessStartTime { get; set; }
    public DateTime? ProcessEndTime { get; set; }
    public TimeSpan? ProcessingTime { get; set; }
    public string? RecipeId { get; set; }
    public ConcurrentDictionary<string, object> Properties { get; set; }

    public string MachineId => $"E90_SUBSTRATE_{Id}_{_instanceId}";
    public IPureStateMachine Machine => _machine;

    public SubstrateMachine(string id, string? lotId, int? slotNumber, string equipmentId, EventBusOrchestrator orchestrator)
    {
        Id = id;
        LotId = lotId;
        SlotNumber = slotNumber;
        AcquiredTime = DateTime.UtcNow;
        Properties = new ConcurrentDictionary<string, object>();
        _orchestrator = orchestrator;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);

        // Inline XState JSON definition (from E90SubstrateStates.json)
        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'WaitingForHost',
            context: {
                substrateId: '',
                lotId: '',
                slotNumber: 0
            },
            states: {
                WaitingForHost: {
                    entry: 'logWaitingForHost',
                    on: {
                        ACQUIRE: 'InCarrier',
                        PLACED_IN_CARRIER: 'InCarrier',
                        REMOVE: 'Removed'
                    }
                },
                InCarrier: {
                    entry: 'logInCarrier',
                    on: {
                        SELECT_FOR_PROCESS: 'NeedsProcessing',
                        SKIP: 'Skipped',
                        REJECT: 'Rejected',
                        REMOVE: 'Removed'
                    }
                },
                NeedsProcessing: {
                    entry: 'logNeedsProcessing',
                    on: {
                        PLACED_IN_PROCESS_MODULE: 'ReadyToProcess',
                        PLACED_IN_ALIGNER: 'Aligning',
                        ABORT: 'Aborted'
                    }
                },
                Aligning: {
                    entry: 'logAligning',
                    on: {
                        ALIGN_COMPLETE: 'ReadyToProcess',
                        ALIGN_FAIL: 'Rejected'
                    }
                },
                ReadyToProcess: {
                    entry: 'logReadyToProcess',
                    on: {
                        START_PROCESS: 'InProcess',
                        ABORT: 'Aborted'
                    }
                },
                InProcess: {
                    entry: 'recordProcessStart',
                    exit: 'recordProcessEnd',
                    on: {
                        PROCESS_COMPLETE: 'Processed',
                        PROCESS_ABORT: 'Aborted',
                        PROCESS_STOP: 'Stopped',
                        PROCESS_ERROR: 'Rejected'
                    }
                },
                Processed: {
                    entry: 'logProcessed',
                    on: {
                        PLACED_IN_CARRIER: 'Complete',
                        REMOVE: 'Removed'
                    }
                },
                Aborted: {
                    entry: 'logAborted',
                    on: {
                        PLACED_IN_CARRIER: 'Complete',
                        REMOVE: 'Removed'
                    }
                },
                Stopped: {
                    entry: 'logStopped',
                    on: {
                        RESUME: 'InProcess',
                        ABORT: 'Aborted'
                    }
                },
                Rejected: {
                    entry: 'logRejected',
                    on: {
                        PLACED_IN_CARRIER: 'Complete',
                        REMOVE: 'Removed'
                    }
                },
                Skipped: {
                    entry: 'logSkipped',
                    on: {
                        PLACED_IN_CARRIER: 'Complete',
                        REMOVE: 'Removed'
                    }
                },
                Complete: {
                    entry: 'logComplete',
                    on: {
                        REMOVE: 'Removed'
                    }
                },
                Removed: {
                    entry: 'logRemoved',
                    type: 'final'
                }
            }
        }
        """;

        // Orchestrated actions
        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logWaitingForHost"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏳ Substrate waiting for host acquisition");
            },

            ["logInCarrier"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📦 Substrate in carrier (Lot: {LotId}, Slot: {SlotNumber})");

                ctx.RequestSend("E87_CARRIER_MANAGEMENT", "SUBSTRATE_IN_CARRIER", new JObject
                {
                    ["substrateId"] = Id,
                    ["lotId"] = LotId,
                    ["slotNumber"] = SlotNumber
                });
            },

            ["logNeedsProcessing"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🎯 Substrate selected for processing");

                ctx.RequestSend("E40_PROCESS_JOB", "SUBSTRATE_NEEDS_PROCESSING", new JObject
                {
                    ["substrateId"] = Id,
                    ["lotId"] = LotId
                });
            },

            ["logAligning"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🔄 Substrate aligning");

                ctx.RequestSend("ALIGNER_MODULE", "ALIGN_SUBSTRATE", new JObject
                {
                    ["substrateId"] = Id
                });
            },

            ["logReadyToProcess"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Substrate ready to process");

                ctx.RequestSend("E40_PROCESS_JOB", "SUBSTRATE_READY", new JObject
                {
                    ["substrateId"] = Id
                });
            },

            ["recordProcessStart"] = (ctx) =>
            {
                ProcessStartTime = DateTime.UtcNow;
                Console.WriteLine($"[{MachineId}] 🔧 Processing started at {ProcessStartTime}");

                ctx.RequestSend("E40_PROCESS_JOB", "SUBSTRATE_PROCESSING", new JObject
                {
                    ["substrateId"] = Id,
                    ["recipeId"] = RecipeId,
                    ["startTime"] = ProcessStartTime
                });
            },

            ["recordProcessEnd"] = (ctx) =>
            {
                ProcessEndTime = DateTime.UtcNow;
                if (ProcessStartTime.HasValue)
                {
                    ProcessingTime = ProcessEndTime.Value - ProcessStartTime.Value;
                    Console.WriteLine($"[{MachineId}] ⏱️ Processing ended. Duration: {ProcessingTime}");
                }
            },

            ["logProcessed"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Processing completed successfully");

                ctx.RequestSend("E40_PROCESS_JOB", "SUBSTRATE_PROCESSED", new JObject
                {
                    ["substrateId"] = Id,
                    ["processTime"] = ProcessingTime?.TotalSeconds
                });

                ctx.RequestSend("E94_CONTROL_JOB", "SUBSTRATE_COMPLETE", new JObject
                {
                    ["substrateId"] = Id,
                    ["status"] = "SUCCESS"
                });
            },

            ["logAborted"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ❌ Processing aborted");

                ctx.RequestSend("E40_PROCESS_JOB", "SUBSTRATE_ABORTED", new JObject
                {
                    ["substrateId"] = Id
                });

                ctx.RequestSend("E94_CONTROL_JOB", "SUBSTRATE_COMPLETE", new JObject
                {
                    ["substrateId"] = Id,
                    ["status"] = "ABORTED"
                });
            },

            ["logStopped"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏸️ Processing stopped");

                ctx.RequestSend("E40_PROCESS_JOB", "SUBSTRATE_STOPPED", new JObject
                {
                    ["substrateId"] = Id
                });
            },

            ["logRejected"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⛔ Substrate rejected");

                ctx.RequestSend("E40_PROCESS_JOB", "SUBSTRATE_REJECTED", new JObject
                {
                    ["substrateId"] = Id
                });

                ctx.RequestSend("E94_CONTROL_JOB", "SUBSTRATE_COMPLETE", new JObject
                {
                    ["substrateId"] = Id,
                    ["status"] = "REJECTED"
                });
            },

            ["logSkipped"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏭️ Substrate skipped");

                ctx.RequestSend("E94_CONTROL_JOB", "SUBSTRATE_COMPLETE", new JObject
                {
                    ["substrateId"] = Id,
                    ["status"] = "SKIPPED"
                });
            },

            ["logComplete"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Substrate lifecycle complete");

                ctx.RequestSend("E87_CARRIER_MANAGEMENT", "SUBSTRATE_COMPLETE", new JObject
                {
                    ["substrateId"] = Id
                });
            },

            ["logRemoved"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 🗑️ Substrate removed from tracking");
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: MachineId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
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

    // Public API methods for substrate lifecycle

    public async Task<EventResult> AcquireAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "ACQUIRE", null);
        return result;
    }

    public async Task<EventResult> PlacedInCarrierAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PLACED_IN_CARRIER", null);
        return result;
    }

    public async Task<EventResult> SelectForProcessAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SELECT_FOR_PROCESS", null);
        return result;
    }

    public async Task<EventResult> PlacedInProcessModuleAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PLACED_IN_PROCESS_MODULE", null);
        return result;
    }

    public async Task<EventResult> PlacedInAlignerAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PLACED_IN_ALIGNER", null);
        return result;
    }

    public async Task<EventResult> AlignCompleteAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "ALIGN_COMPLETE", null);
        return result;
    }

    public async Task<EventResult> StartProcessAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "START_PROCESS", null);
        return result;
    }

    public async Task<EventResult> CompleteProcessAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PROCESS_COMPLETE", null);
        return result;
    }

    public async Task<EventResult> AbortProcessAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PROCESS_ABORT", null);
        return result;
    }

    public async Task<EventResult> StopProcessAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "PROCESS_STOP", null);
        return result;
    }

    public async Task<EventResult> ResumeAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "RESUME", null);
        return result;
    }

    public async Task<EventResult> RejectAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "REJECT", null);
        return result;
    }

    public async Task<EventResult> SkipAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SKIP", null);
        return result;
    }

    public async Task<EventResult> RemoveAsync()
    {
        var result = await _orchestrator.SendEventAsync("SYSTEM", MachineId, "REMOVE", null);
        return result;
    }
}

/// <summary>
/// Substrate location information
/// </summary>
public class SubstrateLocation
{
    public string LocationId { get; set; }
    public SubstrateLocationType Type { get; set; }
    public DateTime Timestamp { get; set; }

    public SubstrateLocation(string locationId, SubstrateLocationType type)
    {
        LocationId = locationId;
        Type = type;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// Substrate location types
/// </summary>
public enum SubstrateLocationType
{
    Carrier,
    ProcessModule,
    TransferModule,
    Aligner,
    Buffer,
    LoadPort,
    Other
}

/// <summary>
/// Substrate history entry
/// </summary>
public class SubstrateHistory
{
    public DateTime Timestamp { get; set; }
    public string? State { get; set; }
    public string? Location { get; set; }
    public string Description { get; set; } = "";
}
