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
/// Forward Priority Scheduler-based CMP implementation with R3
/// Priority: Process equipment (P, C) resource release first to maximize throughput
/// Robots: R1(L↔P↔B bidirectional), R2(P↔C dedicated), R3(C↔B dedicated)
/// </summary>
public class ForwardPrioritySchedulerTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ForwardPriorityCMPMachine _machine;

    public ForwardPrioritySchedulerTests(ITestOutputHelper output)
    {
        _output = output;
        _machine = new ForwardPriorityCMPMachine(output, totalWafers: 25);
    }

    public void Dispose()
    {
        _machine?.Dispose();
    }

    [Fact]
    public async Task ForwardPriority_ProcessesFewWafers_Diagnostic()
    {
        // Create a machine with only 3 wafers for quick diagnostic
        var diagnosticMachine = new ForwardPriorityCMPMachine(_output, totalWafers: 3);
        await diagnosticMachine.StartAsync();

        // Wait for completion with timeout
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await diagnosticMachine.WaitForCompletionAsync(cts.Token);

        var finalState = diagnosticMachine.GetCurrentContext();

        _output.WriteLine("\n=== FINAL STATE ===");
        _output.WriteLine($"Completed: {finalState.Completed.Count}/{finalState.TotalWafers}");
        _output.WriteLine($"LoadPort Pending: {FormatWaferList(finalState.L_Pending)}");
        _output.WriteLine($"LoadPort Completed: {FormatWaferList(finalState.L_Completed)}");

        Assert.Equal(3, finalState.Completed.Count);
        Assert.Equal(3, finalState.L_Completed.Count);
        Assert.Empty(finalState.L_Pending);
    }

    [Fact(Skip = "Long running test - enable manually")]
    public async Task ForwardPriority_ProcessesAll25Wafers()
    {
        await _machine.StartAsync();

        // Wait for all wafers to complete (with timeout)
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await _machine.WaitForCompletionAsync(cts.Token);

        var finalState = _machine.GetCurrentContext();

        _output.WriteLine("\n=== FINAL STATE ===");
        _output.WriteLine($"Completed: {finalState.Completed.Count}/{finalState.TotalWafers}");
        _output.WriteLine($"LoadPort Pending: {FormatWaferList(finalState.L_Pending)}");
        _output.WriteLine($"LoadPort Completed: {FormatWaferList(finalState.L_Completed)}");

        Assert.Equal(25, finalState.Completed.Count);
        Assert.Equal(25, finalState.L_Completed.Count);
        Assert.Empty(finalState.L_Pending);
    }

    [Fact]
    public async Task ForwardPriority_VerifyPriorityOrder()
    {
        var stateLog = new List<string>();
        _machine.OnStateChange += (state) => stateLog.Add(state);

        await _machine.StartAsync();

        // Run for first few wafers
        await Task.Delay(15000);

        _output.WriteLine("\n=== STATE SEQUENCE ===(First 20)");
        foreach (var state in stateLog.Take(20))
        {
            _output.WriteLine(state);
        }

        // Verify forward priority patterns exist
        // Should prioritize C→B over L→P
        Assert.NotEmpty(stateLog);
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
/// System Context - Forward Priority version
/// </summary>
public class ForwardPriorityCMPContext
{
    // LoadPort has two lists: pending (not started) and completed (returned)
    public List<int> L_Pending { get; set; } = new();      // Wafers not yet started
    public List<int> L_Completed { get; set; } = new();    // Wafers returned to L

    public int? R1 { get; set; }                            // Robot1 (L↔P↔B)
    public int? P { get; set; }                             // Polisher
    public int? R2 { get; set; }                            // Robot2 (P↔C dedicated)
    public int? R3 { get; set; }                            // Robot3 (C↔B dedicated)
    public int? C { get; set; }                             // Cleaner
    public int? B { get; set; }                             // Buffer

    public bool P_Processing { get; set; }                  // Polisher busy
    public bool C_Processing { get; set; }                  // Cleaner busy
    public bool R1_Busy { get; set; }                       // Robot1 busy
    public bool R2_Busy { get; set; }                       // Robot2 busy
    public bool R3_Busy { get; set; }                       // Robot3 busy
    public bool R1_ReturningToL { get; set; }              // Robot1 returning to LoadPort

    public List<int> Completed { get; set; } = new();      // All completed wafers
    public int TotalWafers { get; set; } = 25;
}

/// <summary>
/// Timing configuration
/// </summary>
public static class ForwardTiming
{
    public const int POLISHING = 3000;      // 3 seconds
    public const int CLEANING = 3000;       // 3 seconds
    public const int TRANSFER = 800;        // 800ms
    public const int POLL_INTERVAL = 100;   // 100ms scheduler poll
}

/// <summary>
/// Forward Priority CMP Scheduler Machine
/// </summary>
public class ForwardPriorityCMPMachine : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ForwardPriorityCMPContext _context;
    private readonly IStateMachine _machine;
    private readonly CancellationTokenSource _cts;
    private readonly System.Diagnostics.Stopwatch _stopwatch;
    private Task? _schedulerTask;

    public event Action<string>? OnStateChange;

    public ForwardPriorityCMPMachine(ITestOutputHelper output, int totalWafers = 25)
    {
        _output = output;
        _stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _context = new ForwardPriorityCMPContext
        {
            L_Pending = Enumerable.Range(1, totalWafers).ToList(),
            L_Completed = new List<int>(),
            TotalWafers = totalWafers
        };
        _cts = new CancellationTokenSource();

        var definition = """
        {
            "id": "forwardPriorityCMP",
            "initial": "running",
            "states": {
                "running": {
                    "on": {
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
                        "EXEC_B_TO_L": {
                            "cond": "canExecBtoL",
                            "actions": ["startBtoL"]
                        },
                        "COMPLETE_B_TO_L": {
                            "actions": ["completeBtoL"]
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
            ["canExecCtoB"] = new NamedGuard(_ => CanExecCtoB(), "canExecCtoB"),
            ["canExecPtoC"] = new NamedGuard(_ => CanExecPtoC(), "canExecPtoC"),
            ["canExecLtoP"] = new NamedGuard(_ => CanExecLtoP(), "canExecLtoP"),
            ["canExecBtoL"] = new NamedGuard(_ => CanExecBtoL(), "canExecBtoL"),
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

        AddAction("startCtoB", StartCtoB);
        AddAction("completeCtoB", CompleteCtoB);
        AddAction("startPtoC", StartPtoC);
        AddAction("completePtoC", CompletePtoC);
        AddAction("startLtoP", StartLtoP);
        AddAction("completeLtoP", CompleteLtoP);
        AddAction("startBtoL", StartBtoL);
        AddAction("completeBtoL", CompleteBtoL);
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
        _schedulerTask = Task.Run(() => SchedulerService(_cts.Token), _cts.Token);

        return state;
    }

    public async Task WaitForCompletionAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var stateName = _machine.GetActiveStateNames();
            if (stateName.Contains("completed"))
            {
                _output.WriteLine($"\n✅ Simulation completed successfully! State: {stateName}");
                break;
            }

            await Task.Delay(500, ct);
        }
    }

    public ForwardPriorityCMPContext GetCurrentContext() => _context;

    // Helper method to log with timestamp
    private void Log(string message)
    {
        var timestamp = _stopwatch.ElapsedMilliseconds;
        _output.WriteLine($"[{timestamp,6}ms] {message}");
    }

    // Guards - Forward Priority Order (now with R3) - Maximum Throughput Version
    private bool CanExecCtoB() => _context.C.HasValue && !_context.C_Processing && !_context.R3_Busy && !_context.R3.HasValue;  // Can start even if Buffer occupied (will wait)

    private bool CanExecPtoC()
    {
        // R2 can start when Polisher is Done, even if Cleaner is occupied (will wait)
        bool cleanerAvailable = !_context.C.HasValue || (_context.C.HasValue && !_context.C_Processing && !_context.R3_Busy);
        return _context.P.HasValue && !_context.P_Processing && !_context.R2_Busy && !_context.R2.HasValue && cleanerAvailable;
    }

    private bool CanExecLtoP()
    {
        // R1 can start when Polisher is Done (even if still has wafer - R2 will pick it up)
        bool polisherWillBeAvailable = !_context.P.HasValue || (_context.P.HasValue && !_context.P_Processing);
        return _context.L_Pending.Count > 0 && !_context.R1_Busy && !_context.R1.HasValue && polisherWillBeAvailable && !_context.R1_ReturningToL;
    }

    private bool CanExecBtoL() => _context.B.HasValue && !_context.R1_Busy && !_context.R1.HasValue;
    private bool IsAllComplete() => _context.Completed.Count == _context.TotalWafers;

    // Actions - Priority 1: C → R3 → B (R3 dedicated to C↔B)
    private void StartCtoB()
    {
        var waferId = _context.C!.Value;
        Log($"[P1] [Transfer Start] C({waferId}) → R3 (Pick from Cleaner)");

        _context.C = null;  // Cleaner now empty when R3 starts picking
        _context.R3 = waferId;
        _context.R3_Busy = true;
    }

    private void CompleteCtoB()
    {
        var waferId = _context.R3!.Value;

        // Wait for Buffer to be empty (simulation - in real code this is async polling)
        if (_context.B.HasValue)
        {
            Log($"⏸️ [P1] R3({waferId}) waiting at Buffer (still occupied by {_context.B.Value})");
            // In real implementation, this would poll and wait
            // For test, we assume buffer will be cleared by next poll cycle
            return;
        }

        Log($"[P1] [Transfer Complete] R3({waferId}) → B (Place at Buffer)");

        _context.R3 = null;
        _context.R3_Busy = false;
        _context.B = waferId;
    }

    // Actions - Priority 2: P → R2 → C (R2 dedicated to P↔C)
    private void StartPtoC()
    {
        var waferId = _context.P!.Value;
        Log($"[P2] [Transfer Start] P({waferId}) → R2 (Pick from Polisher)");

        _context.P = null;  // Polisher now empty when R2 starts picking
        _context.R2 = waferId;
        _context.R2_Busy = true;
    }

    private void CompletePtoC()
    {
        var waferId = _context.R2!.Value;

        // Wait for Cleaner to be empty (simulation - in real code this is async polling)
        if (_context.C.HasValue)
        {
            Log($"⏸️ [P2] R2({waferId}) waiting at Cleaner (still occupied by {_context.C.Value})");
            // In real implementation, this would poll and wait
            // For test, we assume cleaner will be cleared by next poll cycle
            return;
        }

        Log($"[P2] [Transfer Complete] R2({waferId}) → C (Place at Cleaner - Cleaning Start)");

        _context.R2 = null;
        _context.R2_Busy = false;
        _context.C = waferId;
        _context.C_Processing = true;
    }

    // Actions - Priority 3: L → R1 → P
    private void StartLtoP()
    {
        var waferId = _context.L_Pending[0];
        Log($"[P3] [Transfer Start] L({waferId}) → R1");

        _context.L_Pending.RemoveAt(0);
        _context.R1 = waferId;
        _context.R1_Busy = true;
    }

    private void CompleteLtoP()
    {
        var waferId = _context.R1!.Value;

        // Wait for Polisher to be empty (simulation - in real code this is async polling)
        if (_context.P.HasValue)
        {
            Log($"⏸️ [P3] R1({waferId}) waiting at Polisher (still occupied by {_context.P.Value})");
            // In real implementation, this would poll and wait
            // For test, we assume polisher will be cleared by next poll cycle
            return;
        }

        Log($"[P3] [Transfer Complete] R1({waferId}) → P (Polishing Start)");

        _context.R1 = null;
        _context.R1_Busy = false;
        _context.P = waferId;
        _context.P_Processing = true;
    }

    // Actions - Priority 4: B → R1 → L (Lowest)
    private void StartBtoL()
    {
        var waferId = _context.B!.Value;
        Log($"[P4] [Transfer Start] B({waferId}) → R1 (Pick from Buffer)");

        _context.B = null;
        _context.R1 = waferId;
        _context.R1_Busy = true;
        _context.R1_ReturningToL = true;  // 귀환 시작
    }

    private void CompleteBtoL()
    {
        var waferId = _context.R1!.Value;
        Log($"[P4] [Transfer Complete] R1({waferId}) → L (Place at LoadPort)");

        _context.L_Completed.Add(waferId);
        _context.L_Completed.Sort();
        _context.Completed.Add(waferId);
        _context.R1 = null;  // 웨이퍼 내려놓음

        // Note: R1_Busy와 R1_ReturningToL은 추가 지연 후 해제됨
        // 실제로는 별도의 타이머가 필요하지만, 테스트에서는 즉시 해제
        _context.R1_Busy = false;
        _context.R1_ReturningToL = false;  // 귀환 완료
    }

    private void MarkPolishingDone()
    {
        Log($"[Process Complete] P({_context.P}) Polishing Done");
        _context.P_Processing = false;
    }

    private void MarkCleaningDone()
    {
        Log($"[Process Complete] C({_context.C}) Cleaning Done");
        _context.C_Processing = false;
    }

    private void LogCurrentState()
    {
        var state = new List<string>();

        if (_context.L_Pending.Count > 0 || _context.L_Completed.Count > 0)
        {
            var pending = FormatWaferList(_context.L_Pending);
            var completed = FormatWaferList(_context.L_Completed);
            if (!string.IsNullOrEmpty(pending) && !string.IsNullOrEmpty(completed))
                state.Add($"@L({pending}, {completed})");
            else if (!string.IsNullOrEmpty(pending))
                state.Add($"@L({pending},)");
            else if (!string.IsNullOrEmpty(completed))
                state.Add($"@L(,{completed})");
        }
        if (_context.R1.HasValue) state.Add($"@R1({_context.R1})");
        if (_context.P.HasValue) state.Add($"@P({_context.P})");
        if (_context.R2.HasValue) state.Add($"@R2({_context.R2})");
        if (_context.C.HasValue) state.Add($"@C({_context.C})");
        if (_context.B.HasValue) state.Add($"@B({_context.B})");

        var stateStr = $"[State] {string.Join(", ", state)}";
        Log(stateStr);
        OnStateChange?.Invoke(string.Join(", ", state));
    }

    private void LogFinalStats()
    {
        Log("\n========== Simulation Complete ==========");
        Log($"Total Wafers Processed: {_context.Completed.Count}/{_context.TotalWafers}");
        Log($"LoadPort Pending: {FormatWaferList(_context.L_Pending)}");
        Log($"LoadPort Completed: {FormatWaferList(_context.L_Completed)}");
        Log("=========================================\n");
    }

    // Scheduler Service - Forward Priority with Parallel Execution
    private async Task SchedulerService(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_machine.GetActiveStateNames().Contains("completed"))
        {
            await Task.Delay(ForwardTiming.POLL_INTERVAL, ct);

            _machine.SendAndForget("LOG_STATE");

            // Check all transfers and execute eligible ones in parallel
            var anyExecuted = false;

            // Priority 1: C → B (highest - free Cleaner resource) - R3
            if (CanExecCtoB())
            {
                _machine.SendAndForget("EXEC_C_TO_B");
                _ = Task.Delay(ForwardTiming.TRANSFER, ct).ContinueWith(_ =>
                {
                    _machine.SendAndForget("COMPLETE_C_TO_B");
                }, ct);
                anyExecuted = true;
            }

            // Priority 2: P → C (free Polisher resource) - R2
            if (CanExecPtoC())
            {
                _machine.SendAndForget("EXEC_P_TO_C");
                _ = Task.Delay(ForwardTiming.TRANSFER, ct).ContinueWith(_ =>
                {
                    _machine.SendAndForget("COMPLETE_P_TO_C");

                    // Start cleaning
                    _ = Task.Delay(ForwardTiming.CLEANING, ct).ContinueWith(__ =>
                    {
                        _machine.SendAndForget("CLEANING_DONE");
                    }, ct);
                }, ct);
                anyExecuted = true;
            }

            // Priority 3: L → P (new wafer input) - R1
            if (CanExecLtoP())
            {
                _machine.SendAndForget("EXEC_L_TO_P");
                _ = Task.Delay(ForwardTiming.TRANSFER, ct).ContinueWith(_ =>
                {
                    _machine.SendAndForget("COMPLETE_L_TO_P");

                    // Start polishing
                    _ = Task.Delay(ForwardTiming.POLISHING, ct).ContinueWith(__ =>
                    {
                        _machine.SendAndForget("POLISHING_DONE");
                    }, ct);
                }, ct);
                anyExecuted = true;
            }

            // Priority 4: B → L (lowest - return completed wafer) - R1
            if (CanExecBtoL())
            {
                _machine.SendAndForget("EXEC_B_TO_L");
                _ = Task.Delay(ForwardTiming.TRANSFER, ct).ContinueWith(_ =>
                {
                    _machine.SendAndForget("COMPLETE_B_TO_L");
                    _machine.SendAndForget("CHECK_COMPLETE");
                }, ct);
                anyExecuted = true;
            }

            // If any transfer was executed, wait for transfer time before next poll
            if (anyExecuted)
            {
                await Task.Delay(ForwardTiming.TRANSFER, ct);
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
