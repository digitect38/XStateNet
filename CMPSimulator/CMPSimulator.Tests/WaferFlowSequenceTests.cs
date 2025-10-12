using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CMPSimulator.XStateStations;
using Xunit;
using Xunit.Abstractions;
using XStateNet.Orchestration;

namespace CMPSimulator.Tests;

/// <summary>
/// Tests to verify the exact wafer flow sequence:
/// @L(1~25) → @L(2~25), @R1(1) → @L(2~25), @P(1) → ... → @L(1~25)
/// </summary>
public class WaferFlowSequenceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly EventBusOrchestrator _orchestrator;

    // Station references
    private XLoadPortStation _loadPort;
    private XPolishingStation _polisher;
    private XCleanerStation _cleaner;
    private XBufferStation _buffer;
    private XWTRStation _wtr1;
    private XWTRStation _wtr2;

    // State tracking
    private Dictionary<string, HashSet<int>> _stationWafers;
    private List<string> _stateSnapshots;

    public WaferFlowSequenceTests(ITestOutputHelper output)
    {
        _output = output;
        _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
        {
            PoolSize = 4,
            EnableLogging = false
        });

        _stationWafers = new Dictionary<string, HashSet<int>>
        {
            ["L"] = new HashSet<int>(Enumerable.Range(1, 25)),  // @L(1~25)
            ["R1"] = new HashSet<int>(),
            ["P"] = new HashSet<int>(),
            ["R2"] = new HashSet<int>(),
            ["C"] = new HashSet<int>(),
            ["B"] = new HashSet<int>()
        };

        _stateSnapshots = new List<string>();
        InitializeStations();
    }

    private void InitializeStations()
    {
        _loadPort = new XLoadPortStation("loadport", _orchestrator);
        _polisher = new XPolishingStation("polisher", _orchestrator);
        _cleaner = new XCleanerStation("cleaner", _orchestrator);
        _buffer = new XBufferStation("buffer", _orchestrator);
        _wtr1 = new XWTRStation("wtr1", _orchestrator);
        _wtr2 = new XWTRStation("wtr2", _orchestrator);

        // Subscribe to events to track wafer movements
        _loadPort.WaferDispatched += (s, e) =>
        {
            lock (_stationWafers)
            {
                _stationWafers["L"].Remove(e.WaferId);
                RecordSnapshot($"LoadPort dispatched W{e.WaferId}");
            }
        };

        _loadPort.WaferReturned += (s, e) =>
        {
            lock (_stationWafers)
            {
                _stationWafers["L"].Add(e.WaferId);
                RecordSnapshot($"LoadPort received W{e.WaferId}");
            }
        };

        _wtr1.WaferInTransit += (s, e) =>
        {
            lock (_stationWafers)
            {
                _stationWafers["R1"].Clear();
                _stationWafers["R1"].Add(e.WaferId);
                RecordSnapshot($"WTR1 transiting W{e.WaferId}");
            }
        };

        _wtr2.WaferInTransit += (s, e) =>
        {
            lock (_stationWafers)
            {
                _stationWafers["R2"].Clear();
                _stationWafers["R2"].Add(e.WaferId);
                RecordSnapshot($"WTR2 transiting W{e.WaferId}");
            }
        };

        _polisher.WaferArrived += (s, e) =>
        {
            lock (_stationWafers)
            {
                _stationWafers["R1"].Clear();
                _stationWafers["P"].Clear();
                _stationWafers["P"].Add(e.WaferId);
                RecordSnapshot($"Polisher received W{e.WaferId}");
            }
        };

        _polisher.WaferPickedUp += (s, e) =>
        {
            lock (_stationWafers)
            {
                _stationWafers["P"].Clear();
                RecordSnapshot($"Polisher released W{e.WaferId}");
            }
        };

        _cleaner.WaferArrived += (s, e) =>
        {
            lock (_stationWafers)
            {
                _stationWafers["R2"].Clear();
                _stationWafers["C"].Clear();
                _stationWafers["C"].Add(e.WaferId);
                RecordSnapshot($"Cleaner received W{e.WaferId}");
            }
        };

        _cleaner.WaferPickedUp += (s, e) =>
        {
            lock (_stationWafers)
            {
                _stationWafers["C"].Clear();
                RecordSnapshot($"Cleaner released W{e.WaferId}");
            }
        };

        _buffer.WaferArrived += (s, e) =>
        {
            lock (_stationWafers)
            {
                _stationWafers["R2"].Clear();
                _stationWafers["B"].Clear();
                _stationWafers["B"].Add(e.WaferId);
                RecordSnapshot($"Buffer received W{e.WaferId}");
            }
        };

        _buffer.WaferPickedUp += (s, e) =>
        {
            lock (_stationWafers)
            {
                _stationWafers["B"].Clear();
                RecordSnapshot($"Buffer released W{e.WaferId}");
            }
        };
    }

    private void RecordSnapshot(string eventDescription)
    {
        var snapshot = FormatCurrentState();
        _stateSnapshots.Add($"{eventDescription,-40} => {snapshot}");
        _output.WriteLine(_stateSnapshots.Last());
    }

    private string FormatCurrentState()
    {
        var parts = new List<string>();

        // Format LoadPort
        if (_stationWafers["L"].Any())
        {
            var wafers = string.Join(",", _stationWafers["L"].OrderBy(x => x).Select(w =>
            {
                var list = _stationWafers["L"].OrderBy(x => x).ToList();
                if (list.Count > 3)
                {
                    // Compress format like "1~25" or "2~25,1"
                    if (IsConsecutive(list))
                    {
                        return $"{list.First()}~{list.Last()}";
                    }
                }
                return string.Join(",", list);
            }).Distinct());
            parts.Add($"@L({wafers})");
        }

        // Format other stations
        if (_stationWafers["R1"].Any()) parts.Add($"@R1({string.Join(",", _stationWafers["R1"])})");
        if (_stationWafers["P"].Any()) parts.Add($"@P({string.Join(",", _stationWafers["P"])})");
        if (_stationWafers["R2"].Any()) parts.Add($"@R2({string.Join(",", _stationWafers["R2"])})");
        if (_stationWafers["C"].Any()) parts.Add($"@C({string.Join(",", _stationWafers["C"])})");
        if (_stationWafers["B"].Any()) parts.Add($"@B({string.Join(",", _stationWafers["B"])})");

        return string.Join(", ", parts);
    }

    private bool IsConsecutive(List<int> numbers)
    {
        if (numbers.Count <= 1) return true;
        for (int i = 1; i < numbers.Count; i++)
        {
            if (numbers[i] != numbers[i - 1] + 1) return false;
        }
        return true;
    }

    public void Dispose()
    {
        _orchestrator?.Dispose();
    }

    [Fact]
    public async Task VerifyWaferFlowSequence_First4Wafers()
    {
        // Start all machines
        await _loadPort.StartAsync();
        await _polisher.StartAsync();
        await _cleaner.StartAsync();
        await _buffer.StartAsync();
        await _wtr1.StartAsync();
        await _wtr2.StartAsync();

        // Record initial state
        RecordSnapshot("Initial state");

        // Start simulation
        await _loadPort.StartSimulation();

        // Let first 4 wafers flow through the system
        // Each wafer takes: 300ms (pickup) + 600ms (transit) + 300ms (place) = 1200ms per transfer
        // Plus processing: 3000ms (polishing) + 2500ms (cleaning)
        // Total per wafer: ~7s
        await Task.Delay(30000); // 30 seconds for 4 wafers

        _output.WriteLine("\n=== STATE SEQUENCE ===");
        foreach (var snapshot in _stateSnapshots)
        {
            _output.WriteLine(snapshot);
        }

        // Verify key snapshots exist
        Assert.Contains(_stateSnapshots, s => s.Contains("@L(2~25), @R1(1)") || s.Contains("@L(2-25), @R1(1)"));
        Assert.Contains(_stateSnapshots, s => s.Contains("@P(1)") && s.Contains("@L(2~25)"));
        Assert.Contains(_stateSnapshots, s => s.Contains("@C(1)") && s.Contains("@L(2~25)"));
        Assert.Contains(_stateSnapshots, s => s.Contains("@B(1)") && s.Contains("@L(3~25)"));

        _output.WriteLine("\n✓ Wafer flow sequence verified!");
    }

    [Fact]
    public async Task VerifyBackwardPriority_InFlowSequence()
    {
        // Start all machines
        await _loadPort.StartAsync();
        await _polisher.StartAsync();
        await _cleaner.StartAsync();
        await _buffer.StartAsync();
        await _wtr1.StartAsync();
        await _wtr2.StartAsync();

        await _loadPort.StartSimulation();

        // Wait until we see the pattern: @L(3~25), @P(2), @R2(1)
        // This proves backward priority: R2 picks up W1 from Cleaner (P1)
        // while P has W2 and L is dispatching W3
        await Task.Delay(12000);

        _output.WriteLine("\n=== BACKWARD PRIORITY VERIFICATION ===");
        foreach (var snapshot in _stateSnapshots)
        {
            _output.WriteLine(snapshot);
        }

        // At some point we should see:
        // - Polisher has W2 (P2)
        // - R2 picks up W1 from Cleaner (meaning Cleaner→Buffer got priority)
        // - LoadPort ready to dispatch W3
        var hasBackwardPriorityPattern = _stateSnapshots.Any(s =>
            s.Contains("@P(2)") && s.Contains("@R2(1)"));

        Assert.True(hasBackwardPriorityPattern,
            "Should see backward priority: R2 handles W1 (from Cleaner) while P processes W2");

        _output.WriteLine("\n✓ Backward priority pattern verified in flow!");
    }

    [Fact]
    public async Task VerifyCompleteCirculation_SingleWafer()
    {
        // Track a single wafer through complete circulation
        var waferEvents = new List<string>();

        _loadPort.WaferDispatched += (s, e) => { if (e.WaferId == 1) waferEvents.Add($"W1: Dispatched from LoadPort"); };
        _polisher.WaferArrived += (s, e) => { if (e.WaferId == 1) waferEvents.Add($"W1: Arrived at Polisher"); };
        _polisher.WaferPickedUp += (s, e) => { if (e.WaferId == 1) waferEvents.Add($"W1: Left Polisher"); };
        _cleaner.WaferArrived += (s, e) => { if (e.WaferId == 1) waferEvents.Add($"W1: Arrived at Cleaner"); };
        _cleaner.WaferPickedUp += (s, e) => { if (e.WaferId == 1) waferEvents.Add($"W1: Left Cleaner"); };
        _buffer.WaferArrived += (s, e) => { if (e.WaferId == 1) waferEvents.Add($"W1: Arrived at Buffer"); };
        _buffer.WaferPickedUp += (s, e) => { if (e.WaferId == 1) waferEvents.Add($"W1: Left Buffer"); };
        _loadPort.WaferReturned += (s, e) => { if (e.WaferId == 1) waferEvents.Add($"W1: Returned to LoadPort"); };

        await _loadPort.StartAsync();
        await _polisher.StartAsync();
        await _cleaner.StartAsync();
        await _buffer.StartAsync();
        await _wtr1.StartAsync();
        await _wtr2.StartAsync();

        await _loadPort.StartSimulation();

        // Wait for first wafer to complete cycle
        // Need more time for WTR1 to return to LoadPort
        await Task.Delay(12000);

        _output.WriteLine("\n=== WAFER 1 JOURNEY ===");
        foreach (var eventMsg in waferEvents)
        {
            _output.WriteLine(eventMsg);
        }

        // Verify complete path
        Assert.Contains(waferEvents, e => e.Contains("Dispatched from LoadPort"));
        Assert.Contains(waferEvents, e => e.Contains("Arrived at Polisher"));
        Assert.Contains(waferEvents, e => e.Contains("Left Polisher"));
        Assert.Contains(waferEvents, e => e.Contains("Arrived at Cleaner"));
        Assert.Contains(waferEvents, e => e.Contains("Left Cleaner"));
        Assert.Contains(waferEvents, e => e.Contains("Arrived at Buffer"));
        Assert.Contains(waferEvents, e => e.Contains("Left Buffer"));
        Assert.Contains(waferEvents, e => e.Contains("Returned to LoadPort"));

        _output.WriteLine("\n✓ Wafer 1 completed full circulation: L→P→C→B→L");
    }
}
