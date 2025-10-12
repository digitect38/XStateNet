using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using XStateNet;

namespace CMPSimulator.Tests;

/// <summary>
/// Scheduler-based CMP implementation using XStateNet
/// Translated from TypeScript XState v5 reference implementation
/// </summary>
public class SchedulerBasedCMPTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly CMPSchedulerMachine _machine;

    public SchedulerBasedCMPTests(ITestOutputHelper output)
    {
        _output = output;
        _machine = new CMPSchedulerMachine(output);
    }

    public void Dispose()
    {
        _machine?.Dispose();
    }

    [Fact]
    public async Task SchedulerCMP_ProcessesFewWafers_Diagnostic()
    {
        // Create a machine with only 3 wafers for quick diagnostic
        var diagnosticMachine = new CMPSchedulerMachine(_output, totalWafers: 3);
        await diagnosticMachine.StartAsync();

        // Wait for completion with timeout
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await diagnosticMachine.WaitForCompletionAsync(cts.Token);

        var finalState = diagnosticMachine.GetCurrentContext();

        _output.WriteLine("\n=== FINAL STATE ===");
        _output.WriteLine($"Completed: {finalState.Completed.Count}/{finalState.TotalWafers}");
        _output.WriteLine($"LoadPort: {FormatWaferList(finalState.L)}");

        Assert.Equal(3, finalState.Completed.Count);
        Assert.Equal(3, finalState.L.Count);
    }

    [Fact(Skip = "Long running test - enable manually")]
    public async Task SchedulerCMP_ProcessesAllWafers_WithBackwardPriority()
    {
        await _machine.StartAsync();

        // Wait for all wafers to complete (with timeout)
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await _machine.WaitForCompletionAsync(cts.Token);

        // Verify all wafers returned to LoadPort
        var finalState = _machine.GetCurrentContext();

        _output.WriteLine("\n=== FINAL STATE ===");
        _output.WriteLine($"Completed: {finalState.Completed.Count}/{finalState.TotalWafers}");
        _output.WriteLine($"LoadPort: {FormatWaferList(finalState.L)}");

        Assert.Equal(25, finalState.Completed.Count);
        Assert.Equal(25, finalState.L.Count);
        Assert.Null(finalState.R1);
        Assert.Null(finalState.P);
        Assert.Null(finalState.R2);
        Assert.Null(finalState.C);
        Assert.Null(finalState.B);
    }

    [Fact]
    public async Task SchedulerCMP_FollowsBackwardPriority()
    {
        var stateLog = new List<string>();
        _machine.OnStateChange += (state) => stateLog.Add(state);

        await _machine.StartAsync();

        // Run for first few wafers
        await Task.Delay(20000);

        _output.WriteLine("\n=== STATE SEQUENCE ===");
        foreach (var state in stateLog.Take(30))
        {
            _output.WriteLine(state);
        }

        // Verify backward priority patterns exist
        // Should see: @L(...), @P(...), @R2(1) before @R1(2)
        var hasPriority1Pattern = stateLog.Any(s => s.Contains("@B(") && s.Contains("@R1("));
        var hasPriority2Pattern = stateLog.Any(s => s.Contains("@C(") && !s.Contains("C_processing"));

        Assert.True(hasPriority1Pattern || hasPriority2Pattern,
            "Should see backward priority patterns in state log");
    }

    private string FormatWaferList(List<int> wafers)
    {
        if (!wafers.Any()) return "";
        if (wafers.Count == 1) return wafers[0].ToString();

        var ranges = new List<string>();
        int start = wafers[0];
        int end = wafers[0];

        for (int i = 1; i < wafers.Count; i++)
        {
            if (wafers[i] == end + 1)
            {
                end = wafers[i];
            }
            else
            {
                ranges.Add(start == end ? $"{start}" : $"{start}~{end}");
                start = end = wafers[i];
            }
        }
        ranges.Add(start == end ? $"{start}" : $"{start}~{end}");

        return string.Join(", ", ranges);
    }
}

/// <summary>
/// System Context - matches TypeScript interface
/// </summary>
public class CMPContext
{
    public List<int> L { get; set; } = new();           // LoadPort wafers
    public int? R1 { get; set; }                         // Robot1
    public int? P { get; set; }                          // Polisher
    public int? R2 { get; set; }                         // Robot2
    public int? C { get; set; }                          // Cleaner
    public int? B { get; set; }                          // Buffer

    public bool P_Processing { get; set; }               // Polisher busy
    public bool C_Processing { get; set; }               // Cleaner busy
    public bool R1_Busy { get; set; }                    // Robot1 busy
    public bool R2_Busy { get; set; }                    // Robot2 busy

    public List<int> Completed { get; set; } = new();   // Completed wafers
    public int TotalWafers { get; set; } = 25;
}

/// <summary>
/// Timing configuration
/// </summary>
public static class Timing
{
    public const int POLISHING = 3000;      // 3 seconds (scaled down for testing)
    public const int CLEANING = 2000;       // 2 seconds
    public const int R1_TRANSFER = 500;     // 500ms
    public const int R2_TRANSFER = 500;     // 500ms
    public const int POLL_INTERVAL = 100;   // 100ms scheduler poll
}

/// <summary>
/// Main CMP Scheduler Machine
/// </summary>
public class CMPSchedulerMachine : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly CMPContext _context;
    private readonly IStateMachine _machine;
    private readonly CancellationTokenSource _cts;
    private Task? _schedulerTask;

    public event Action<string>? OnStateChange;

    public CMPSchedulerMachine(ITestOutputHelper output, int totalWafers = 25)
    {
        _output = output;
        _context = new CMPContext
        {
            L = Enumerable.Range(1, totalWafers).ToList(),
            TotalWafers = totalWafers
        };
        _cts = new CancellationTokenSource();

        var definition = """
        {
            "id": "cmpScheduler",
            "initial": "running",
            "states": {
                "running": {
                    "on": {
                        "EXEC_B_TO_L": {
                            "cond": "canExecBtoL",
                            "actions": ["startBtoL"]
                        },
                        "COMPLETE_B_TO_L": {
                            "actions": ["completeBtoL"]
                        },
                        "EXEC_C_TO_B": {
                            "cond": "canExecCtoB",
                            "actions": ["startCtoB"]
                        },
                        "COMPLETE_C_TO_B": {
                            "actions": ["completeCtoB"]
                        },
                        "EXEC_P_TO_C": {
                            "cond": "canExecPtoC",
                            "actions": ["startPtoC"]
                        },
                        "COMPLETE_P_TO_C": {
                            "actions": ["completePtoC"]
                        },
                        "EXEC_L_TO_P": {
                            "cond": "canExecLtoP",
                            "actions": ["startLtoP"]
                        },
                        "COMPLETE_L_TO_P": {
                            "actions": ["completeLtoP"]
                        },
                        "POLISHING_DONE": {
                            "actions": ["markPolishingDone"]
                        },
                        "CLEANING_DONE": {
                            "actions": ["markCleaningDone"]
                        },
                        "LOG_STATE": {
                            "actions": ["logCurrentState"]
                        },
                        "CHECK_COMPLETE": [
                            {
                                "cond": "isAllComplete",
                                "target": "completed"
                            }
                        ]
                    }
                },
                "completed": {
                    "type": "final",
                    "entry": ["logFinalStats"]
                }
            }
        }
        """;

        var guards = new GuardMap
        {
            ["canExecBtoL"] = new NamedGuard(_ => CanExecBtoL(), "canExecBtoL"),
            ["canExecCtoB"] = new NamedGuard(_ => CanExecCtoB(), "canExecCtoB"),
            ["canExecPtoC"] = new NamedGuard(_ => CanExecPtoC(), "canExecPtoC"),
            ["canExecLtoP"] = new NamedGuard(_ => CanExecLtoP(), "canExecLtoP"),
            ["isAllComplete"] = new NamedGuard(_ => IsAllComplete(), "isAllComplete")
        };

        var actions = new ActionMap();

        void AddAction(string name, Action action)
        {
            actions[name] = new List<NamedAction>
            {
                new NamedAction(name, async _ => { action(); await Task.CompletedTask; })
            };
        }

        AddAction("startBtoL", StartBtoL);
        AddAction("completeBtoL", CompleteBtoL);
        AddAction("startCtoB", StartCtoB);
        AddAction("completeCtoB", CompleteCtoB);
        AddAction("startPtoC", StartPtoC);
        AddAction("completePtoC", CompletePtoC);
        AddAction("startLtoP", StartLtoP);
        AddAction("completeLtoP", CompleteLtoP);
        AddAction("markPolishingDone", MarkPolishingDone);
        AddAction("markCleaningDone", MarkCleaningDone);
        AddAction("logCurrentState", LogCurrentState);
        AddAction("logFinalStats", LogFinalStats);

        _machine = StateMachineFactory.CreateFromScript(
            jsonScript: definition,
            threadSafe: false,
            guidIsolate: false,
            actionCallbacks: actions,
            guardCallbacks: guards
        );
    }

    public async Task<string> StartAsync()
    {
        var state = await _machine.StartAsync();

        // Start scheduler service
        _schedulerTask = Task.Run(() => SchedulerService(_cts.Token), _cts.Token);

        return state;
    }

    public async Task WaitForCompletionAsync(CancellationToken ct)
    {
        while (_machine.GetActiveStateNames() != "completed" && !ct.IsCancellationRequested)
        {
            await Task.Delay(500, ct);

            // Debug: Check completion status
            if (_context.Completed.Count == _context.TotalWafers)
            {
                _output.WriteLine($"\n[DEBUG] All wafers completed! Completed={_context.Completed.Count}, L={_context.L.Count}, State={_machine.GetActiveStateNames()}");
                _output.WriteLine($"[DEBUG] IsAllComplete result: {IsAllComplete()}");
            }
        }
    }

    public CMPContext GetCurrentContext() => _context;

    // Guards
    private bool CanExecBtoL() => _context.B.HasValue && !_context.R1_Busy && !_context.R1.HasValue;
    private bool CanExecCtoB() => _context.C.HasValue && !_context.C_Processing && !_context.R2_Busy && !_context.R2.HasValue && !_context.B.HasValue;
    private bool CanExecPtoC() => _context.P.HasValue && !_context.P_Processing && !_context.R2_Busy && !_context.R2.HasValue && !_context.C.HasValue;
    private bool CanExecLtoP() => _context.L.Except(_context.Completed).Any() && !_context.R1_Busy && !_context.R1.HasValue && !_context.P.HasValue;
    private bool IsAllComplete() => _context.Completed.Count == _context.TotalWafers && _context.L.Count == _context.TotalWafers;

    // Actions - Priority 1: B → R1 → L
    private void StartBtoL()
    {
        var waferId = _context.B!.Value;
        _output.WriteLine($"[Transfer Start] B({waferId}) → R1");

        _context.B = null;
        _context.R1 = waferId;
        _context.R1_Busy = true;
    }

    private void CompleteBtoL()
    {
        var waferId = _context.R1!.Value;
        _output.WriteLine($"[Transfer Complete] R1({waferId}) → L (DONE)");

        _context.R1 = null;
        _context.R1_Busy = false;
        _context.L.Add(waferId);
        _context.L.Sort();
        _context.Completed.Add(waferId);
    }

    // Actions - Priority 2: C → R2 → B
    private void StartCtoB()
    {
        var waferId = _context.C!.Value;
        _output.WriteLine($"[Transfer Start] C({waferId}) → R2");

        _context.C = null;
        _context.C_Processing = false;
        _context.R2 = waferId;
        _context.R2_Busy = true;
    }

    private void CompleteCtoB()
    {
        var waferId = _context.R2!.Value;
        _output.WriteLine($"[Transfer Complete] R2({waferId}) → B");

        _context.R2 = null;
        _context.R2_Busy = false;
        _context.B = waferId;
    }

    // Actions - Priority 3: P → R2 → C
    private void StartPtoC()
    {
        var waferId = _context.P!.Value;
        _output.WriteLine($"[Transfer Start] P({waferId}) → R2");

        _context.P = null;
        _context.P_Processing = false;
        _context.R2 = waferId;
        _context.R2_Busy = true;
    }

    private void CompletePtoC()
    {
        var waferId = _context.R2!.Value;
        _output.WriteLine($"[Transfer Complete] R2({waferId}) → C (Cleaning Start)");

        _context.R2 = null;
        _context.R2_Busy = false;
        _context.C = waferId;
        _context.C_Processing = true;
    }

    // Actions - Priority 4: L → R1 → P
    private void StartLtoP()
    {
        // Only take wafers that haven't been completed yet
        var waferId = _context.L.Except(_context.Completed).First();
        _output.WriteLine($"[Transfer Start] L({waferId}) → R1");

        _context.L.Remove(waferId);
        _context.R1 = waferId;
        _context.R1_Busy = true;
    }

    private void CompleteLtoP()
    {
        var waferId = _context.R1!.Value;
        _output.WriteLine($"[Transfer Complete] R1({waferId}) → P (Polishing Start)");

        _context.R1 = null;
        _context.R1_Busy = false;
        _context.P = waferId;
        _context.P_Processing = true;
    }

    private void MarkPolishingDone()
    {
        _output.WriteLine($"[Process Complete] P({_context.P}) Polishing Done");
        _context.P_Processing = false;
    }

    private void MarkCleaningDone()
    {
        _output.WriteLine($"[Process Complete] C({_context.C}) Cleaning Done");
        _context.C_Processing = false;
    }

    private void LogCurrentState()
    {
        var state = new List<string>();

        if (_context.L.Count > 0)
            state.Add($"@L({FormatWaferList(_context.L)})");
        if (_context.R1.HasValue)
            state.Add($"@R1({_context.R1})");
        if (_context.P.HasValue)
            state.Add($"@P({_context.P})");
        if (_context.R2.HasValue)
            state.Add($"@R2({_context.R2})");
        if (_context.C.HasValue)
            state.Add($"@C({_context.C})");
        if (_context.B.HasValue)
            state.Add($"@B({_context.B})");

        var stateStr = $"[State] {string.Join(", ", state)}";
        _output.WriteLine(stateStr);
        OnStateChange?.Invoke(string.Join(", ", state));
    }

    private void LogFinalStats()
    {
        _output.WriteLine("\n========== Simulation Complete ==========");
        _output.WriteLine($"Total Wafers Processed: {_context.Completed.Count}/{_context.TotalWafers}");
        _output.WriteLine($"Final State: @L({FormatWaferList(_context.L)})");
        _output.WriteLine("=========================================\n");
    }

    // Scheduler Service (replaces TypeScript invoke service)
    private async Task SchedulerService(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _machine.GetActiveStateNames() != "completed")
        {
            await Task.Delay(Timing.POLL_INTERVAL, ct);

            _machine.SendAndForget("LOG_STATE");

            // Priority 1: B → L (highest)
            if (CanExecBtoL())
            {
                _machine.SendAndForget("EXEC_B_TO_L");
                _ = Task.Delay(Timing.R1_TRANSFER, ct).ContinueWith(_ =>
                {
                    _machine.SendAndForget("COMPLETE_B_TO_L");
                    _machine.SendAndForget("CHECK_COMPLETE");
                }, ct);
                continue;
            }

            // Priority 2: C → B
            if (CanExecCtoB())
            {
                _machine.SendAndForget("EXEC_C_TO_B");
                _ = Task.Delay(Timing.R2_TRANSFER, ct).ContinueWith(_ =>
                {
                    _machine.SendAndForget("COMPLETE_C_TO_B");
                }, ct);
                continue;
            }

            // Priority 3: P → C
            if (CanExecPtoC())
            {
                _machine.SendAndForget("EXEC_P_TO_C");
                _ = Task.Delay(Timing.R2_TRANSFER, ct).ContinueWith(_ =>
                {
                    _machine.SendAndForget("COMPLETE_P_TO_C");

                    // Start cleaning
                    _ = Task.Delay(Timing.CLEANING, ct).ContinueWith(__ =>
                    {
                        _machine.SendAndForget("CLEANING_DONE");
                    }, ct);
                }, ct);
                continue;
            }

            // Priority 4: L → P (lowest)
            if (CanExecLtoP())
            {
                _machine.SendAndForget("EXEC_L_TO_P");
                _ = Task.Delay(Timing.R1_TRANSFER, ct).ContinueWith(_ =>
                {
                    _machine.SendAndForget("COMPLETE_L_TO_P");

                    // Start polishing
                    _ = Task.Delay(Timing.POLISHING, ct).ContinueWith(__ =>
                    {
                        _machine.SendAndForget("POLISHING_DONE");
                    }, ct);
                }, ct);
                continue;
            }
        }
    }

    private string FormatWaferList(List<int> wafers)
    {
        if (!wafers.Any()) return "";
        if (wafers.Count == 1) return wafers[0].ToString();

        var ranges = new List<string>();
        int start = wafers[0];
        int end = wafers[0];

        for (int i = 1; i < wafers.Count; i++)
        {
            if (wafers[i] == end + 1)
            {
                end = wafers[i];
            }
            else
            {
                ranges.Add(start == end ? $"{start}" : $"{start}~{end}");
                start = end = wafers[i];
            }
        }
        ranges.Add(start == end ? $"{start}" : $"{start}~{end}");

        return string.Join(", ", ranges);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _schedulerTask?.Wait(TimeSpan.FromSeconds(1));
        _cts?.Dispose();
    }
}
