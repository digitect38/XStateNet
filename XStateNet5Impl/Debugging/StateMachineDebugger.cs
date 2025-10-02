using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace XStateNet.Debugging
{
    /// <summary>
    /// Advanced debugging tools for state machines
    /// </summary>
    public class StateMachineDebugger
    {
        private readonly Dictionary<string, StateMachine> _machines = new();
        private readonly List<DebugEvent> _eventHistory = new();
        private readonly Dictionary<string, List<StateTransition>> _transitionHistory = new();
        private readonly object _lock = new();
        private bool _isEnabled = true;

        public event EventHandler<DebugEventArgs>? DebugEvent;
        public event EventHandler<StateTransitionDebugArgs>? StateTransition;
        public event EventHandler<ActionExecutionDebugArgs>? ActionExecution;

        /// <summary>
        /// Register a state machine for debugging
        /// </summary>
        public void RegisterMachine(string machineId, StateMachine machine)
        {
            lock (_lock)
            {
                _machines[machineId] = machine;
                _transitionHistory[machineId] = new List<StateTransition>();

                // Subscribe to machine events
                // Note: StateMachine has Action<string> StateChanged event
                // We'll need to adapt or skip event subscription for now
            }
        }

        /// <summary>
        /// Unregister a state machine from debugging
        /// </summary>
        public void UnregisterMachine(string machineId)
        {
            lock (_lock)
            {
                if (_machines.TryGetValue(machineId, out var machine))
                {
                    // Unsubscribe from events
                    // Note: Event subscription adapted or skipped
                    _machines.Remove(machineId);
                }
            }
        }

        /// <summary>
        /// Enable or disable debugging
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
        }

        /// <summary>
        /// Get current state of all registered machines
        /// </summary>
        public Dictionary<string, MachineDebugInfo> GetMachineStates()
        {
            lock (_lock)
            {
                var result = new Dictionary<string, MachineDebugInfo>();

                foreach (var (machineId, machine) in _machines)
                {
                    result[machineId] = new MachineDebugInfo
                    {
                        MachineId = machineId,
                        CurrentState = machine.GetActiveStateNames(),
                        IsRunning = machine.IsRunning,
                        ActiveStates = machine.GetActiveStates().Select(s => s.Name).ToArray(),
                        LastEventProcessed = GetLastEventForMachine(machineId),
                        TransitionCount = _transitionHistory[machineId].Count,
                        EventCount = _eventHistory.Count(e => e.MachineId == machineId)
                    };
                }

                return result;
            }
        }

        /// <summary>
        /// Get event history for a specific machine
        /// </summary>
        public List<DebugEvent> GetEventHistory(string machineId, int maxEvents = 100)
        {
            lock (_lock)
            {
                return _eventHistory
                    .Where(e => e.MachineId == machineId)
                    .TakeLast(maxEvents)
                    .ToList();
            }
        }

        /// <summary>
        /// Get state transition history for a specific machine
        /// </summary>
        public List<StateTransition> GetTransitionHistory(string machineId, int maxTransitions = 100)
        {
            lock (_lock)
            {
                return _transitionHistory.TryGetValue(machineId, out var transitions)
                    ? transitions.TakeLast(maxTransitions).ToList()
                    : new List<StateTransition>();
            }
        }

        /// <summary>
        /// Create a detailed debug report for a machine
        /// </summary>
        public MachineDebugReport GenerateDebugReport(string machineId)
        {
            lock (_lock)
            {
                if (!_machines.TryGetValue(machineId, out var machine))
                {
                    throw new ArgumentException($"Machine '{machineId}' is not registered for debugging");
                }

                var events = GetEventHistory(machineId, 1000);
                var transitions = GetTransitionHistory(machineId, 1000);

                return new MachineDebugReport
                {
                    MachineId = machineId,
                    Timestamp = DateTime.UtcNow,
                    CurrentState = machine.GetActiveStateNames(),
                    IsRunning = machine.IsRunning,
                    TotalEvents = events.Count,
                    TotalTransitions = transitions.Count,
                    RecentEvents = events.TakeLast(50).ToList(),
                    RecentTransitions = transitions.TakeLast(50).ToList(),
                    StateFrequency = CalculateStateFrequency(transitions),
                    EventFrequency = CalculateEventFrequency(events),
                    AverageEventProcessingTime = CalculateAverageProcessingTime(events),
                    MostFrequentTransitions = GetMostFrequentTransitions(transitions, 10)
                };
            }
        }

        /// <summary>
        /// Export debug data to JSON
        /// </summary>
        public void ExportDebugData(string filePath, string? machineId = null)
        {
            lock (_lock)
            {
                var exportData = new
                {
                    Timestamp = DateTime.UtcNow,
                    MachineStates = GetMachineStates(),
                    EventHistory = machineId != null
                        ? GetEventHistory(machineId, 10000)
                        : _eventHistory.TakeLast(10000).ToList(),
                    TransitionHistory = machineId != null
                        ? new Dictionary<string, List<StateTransition>> { [machineId] = GetTransitionHistory(machineId, 10000) }
                        : _transitionHistory.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.TakeLast(10000).ToList())
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(exportData, options);
                System.IO.File.WriteAllText(filePath, json);
            }
        }

        /// <summary>
        /// Clear debug history
        /// </summary>
        public void ClearHistory(string? machineId = null)
        {
            lock (_lock)
            {
                if (machineId != null)
                {
                    _eventHistory.RemoveAll(e => e.MachineId == machineId);
                    if (_transitionHistory.ContainsKey(machineId))
                    {
                        _transitionHistory[machineId].Clear();
                    }
                }
                else
                {
                    _eventHistory.Clear();
                    foreach (var transitions in _transitionHistory.Values)
                    {
                        transitions.Clear();
                    }
                }
            }
        }

        /// <summary>
        /// Start interactive debugging session
        /// </summary>
        public async Task StartInteractiveSession()
        {
            Console.WriteLine("🔍 XStateNet Interactive Debugger");
            Console.WriteLine("==================================");
            Console.WriteLine();

            while (true)
            {
                Console.WriteLine("Available commands:");
                Console.WriteLine("  1. show - Show current machine states");
                Console.WriteLine("  2. history <machineId> - Show event history");
                Console.WriteLine("  3. transitions <machineId> - Show transition history");
                Console.WriteLine("  4. report <machineId> - Generate debug report");
                Console.WriteLine("  5. export [machineId] - Export debug data");
                Console.WriteLine("  6. clear [machineId] - Clear history");
                Console.WriteLine("  7. watch <machineId> - Watch machine in real-time");
                Console.WriteLine("  8. help - Show this help");
                Console.WriteLine("  9. exit - Exit debugger");
                Console.WriteLine();

                Console.Write("debugger> ");
                var input = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(input))
                    continue;

                var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var command = parts[0].ToLower();

                try
                {
                    await ExecuteDebugCommand(command, parts.Skip(1).ToArray());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error: {ex.Message}");
                }

                if (command == "exit")
                    break;

                Console.WriteLine();
            }
        }

        private async Task ExecuteDebugCommand(string command, string[] args)
        {
            switch (command)
            {
                case "show":
                    ShowMachineStates();
                    break;

                case "history":
                    if (args.Length == 0)
                    {
                        Console.WriteLine("Usage: history <machineId>");
                        return;
                    }
                    ShowEventHistory(args[0]);
                    break;

                case "transitions":
                    if (args.Length == 0)
                    {
                        Console.WriteLine("Usage: transitions <machineId>");
                        return;
                    }
                    ShowTransitionHistory(args[0]);
                    break;

                case "report":
                    if (args.Length == 0)
                    {
                        Console.WriteLine("Usage: report <machineId>");
                        return;
                    }
                    ShowDebugReport(args[0]);
                    break;

                case "export":
                    var machineId = args.Length > 0 ? args[0] : null;
                    var fileName = $"debug_export_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                    ExportDebugData(fileName, machineId);
                    Console.WriteLine($"✅ Debug data exported to: {fileName}");
                    break;

                case "clear":
                    var targetMachine = args.Length > 0 ? args[0] : null;
                    ClearHistory(targetMachine);
                    Console.WriteLine($"✅ History cleared for {targetMachine ?? "all machines"}");
                    break;

                case "watch":
                    if (args.Length == 0)
                    {
                        Console.WriteLine("Usage: watch <machineId>");
                        return;
                    }
                    await WatchMachine(args[0]);
                    break;

                case "help":
                    // Help already shown above
                    break;

                default:
                    Console.WriteLine($"Unknown command: {command}");
                    break;
            }
        }

        private void ShowMachineStates()
        {
            var states = GetMachineStates();

            if (states.Count == 0)
            {
                Console.WriteLine("No machines registered for debugging.");
                return;
            }

            Console.WriteLine("Current Machine States:");
            Console.WriteLine("----------------------");

            foreach (var (machineId, info) in states)
            {
                Console.WriteLine($"🎯 {machineId}:");
                Console.WriteLine($"   State: {info.CurrentState}");
                Console.WriteLine($"   Running: {info.IsRunning}");
                Console.WriteLine($"   Events: {info.EventCount}");
                Console.WriteLine($"   Transitions: {info.TransitionCount}");

                if (info.ActiveStates.Length > 1)
                {
                    Console.WriteLine($"   Active States: [{string.Join(", ", info.ActiveStates)}]");
                }

                if (info.LastEventProcessed != null)
                {
                    Console.WriteLine($"   Last Event: {info.LastEventProcessed.EventName} at {info.LastEventProcessed.Timestamp:HH:mm:ss.fff}");
                }

                Console.WriteLine();
            }
        }

        private void ShowEventHistory(string machineId)
        {
            var events = GetEventHistory(machineId, 20);

            if (events.Count == 0)
            {
                Console.WriteLine($"No event history for machine '{machineId}'.");
                return;
            }

            Console.WriteLine($"Event History for '{machineId}' (last {events.Count}):");
            Console.WriteLine("------------------------------------------------------");

            foreach (var evt in events)
            {
                Console.WriteLine($"⚡ {evt.Timestamp:HH:mm:ss.fff} - {evt.EventName}");
                Console.WriteLine($"   Processing Time: {evt.ProcessingTime.TotalMilliseconds:F2}ms");

                if (!string.IsNullOrEmpty(evt.Data))
                {
                    Console.WriteLine($"   Data: {evt.Data}");
                }

                if (!string.IsNullOrEmpty(evt.Error))
                {
                    Console.WriteLine($"   ❌ Error: {evt.Error}");
                }

                Console.WriteLine();
            }
        }

        private void ShowTransitionHistory(string machineId)
        {
            var transitions = GetTransitionHistory(machineId, 20);

            if (transitions.Count == 0)
            {
                Console.WriteLine($"No transition history for machine '{machineId}'.");
                return;
            }

            Console.WriteLine($"Transition History for '{machineId}' (last {transitions.Count}):");
            Console.WriteLine("----------------------------------------------------------");

            foreach (var transition in transitions)
            {
                Console.WriteLine($"🔄 {transition.Timestamp:HH:mm:ss.fff} - {transition.FromState} → {transition.ToState}");
                Console.WriteLine($"   Event: {transition.EventName}");
                Console.WriteLine($"   Duration: {transition.TransitionTime.TotalMilliseconds:F2}ms");

                if (transition.Actions.Any())
                {
                    Console.WriteLine($"   Actions: [{string.Join(", ", transition.Actions)}]");
                }

                Console.WriteLine();
            }
        }

        private void ShowDebugReport(string machineId)
        {
            var report = GenerateDebugReport(machineId);

            Console.WriteLine($"Debug Report for '{machineId}':");
            Console.WriteLine("==============================");
            Console.WriteLine($"Generated: {report.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine($"Current State: {report.CurrentState}");
            Console.WriteLine($"Running: {report.IsRunning}");
            Console.WriteLine($"Total Events: {report.TotalEvents}");
            Console.WriteLine($"Total Transitions: {report.TotalTransitions}");
            Console.WriteLine($"Avg Processing Time: {report.AverageEventProcessingTime:F2}ms");
            Console.WriteLine();

            if (report.StateFrequency.Any())
            {
                Console.WriteLine("Most Visited States:");
                foreach (var (state, count) in report.StateFrequency.Take(5))
                {
                    Console.WriteLine($"  {state}: {count} times");
                }
                Console.WriteLine();
            }

            if (report.EventFrequency.Any())
            {
                Console.WriteLine("Most Frequent Events:");
                foreach (var (eventName, count) in report.EventFrequency.Take(5))
                {
                    Console.WriteLine($"  {eventName}: {count} times");
                }
                Console.WriteLine();
            }

            if (report.MostFrequentTransitions.Any())
            {
                Console.WriteLine("Most Frequent Transitions:");
                foreach (var (transition, count) in report.MostFrequentTransitions)
                {
                    Console.WriteLine($"  {transition}: {count} times");
                }
            }
        }

        private async Task WatchMachine(string machineId)
        {
            Console.WriteLine($"👁️  Watching machine '{machineId}' (Press any key to stop)...");
            Console.WriteLine();

            var lastEventCount = 0;
            var lastTransitionCount = 0;

            while (!Console.KeyAvailable)
            {
                var states = GetMachineStates();
                if (states.TryGetValue(machineId, out var info))
                {
                    if (info.EventCount != lastEventCount || info.TransitionCount != lastTransitionCount)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] State: {info.CurrentState}, Events: {info.EventCount}, Transitions: {info.TransitionCount}");
                        lastEventCount = info.EventCount;
                        lastTransitionCount = info.TransitionCount;
                    }
                }

                await Task.Delay(100);
            }

            Console.ReadKey(); // Consume the key press
            Console.WriteLine("\nStopped watching.");
        }

        private void OnStateChanged(object? sender, StateChangedEventArgs e)
        {
            if (!_isEnabled) return;

            var machine = sender as StateMachine;
            var machineId = machine?.machineId ?? "Unknown";

            lock (_lock)
            {
                if (_transitionHistory.TryGetValue(machineId, out var transitions))
                {
                    transitions.Add(new StateTransition
                    {
                        MachineId = machineId,
                        FromState = e.PreviousState ?? "Unknown",
                        ToState = e.NewState,
                        EventName = "State Change",
                        Timestamp = DateTime.UtcNow,
                        TransitionTime = TimeSpan.FromMilliseconds(1), // Placeholder
                        Actions = new List<string>() // Would need to be populated from actual actions
                    });
                }
            }

            StateTransition?.Invoke(this, new StateTransitionDebugArgs
            {
                MachineId = machineId,
                FromState = e.PreviousState ?? "Unknown",
                ToState = e.NewState,
                EventName = "State Change",
                Timestamp = DateTime.UtcNow
            });
        }

        private void OnEventProcessed(object? sender, EventProcessedEventArgs e)
        {
            if (!_isEnabled) return;

            var machine = sender as StateMachine;
            var machineId = machine?.machineId ?? "Unknown";

            lock (_lock)
            {
                _eventHistory.Add(new DebugEvent
                {
                    MachineId = machineId,
                    EventName = e.EventName,
                    Timestamp = DateTime.UtcNow,
                    ProcessingTime = e.ProcessingTime,
                    Data = e.EventData?.ToString() ?? "",
                    Error = e.Error?.Message
                });

                // Keep only recent events to prevent memory issues
                if (_eventHistory.Count > 10000)
                {
                    _eventHistory.RemoveRange(0, 1000);
                }
            }

            DebugEvent?.Invoke(this, new DebugEventArgs
            {
                MachineId = machineId,
                EventName = e.EventName,
                EventData = e.EventData,
                ProcessingTime = e.ProcessingTime,
                Error = e.Error
            });
        }

        private DebugEvent? GetLastEventForMachine(string machineId)
        {
            return _eventHistory.Where(e => e.MachineId == machineId).LastOrDefault();
        }

        private Dictionary<string, int> CalculateStateFrequency(List<StateTransition> transitions)
        {
            return transitions
                .GroupBy(t => t.ToState)
                .ToDictionary(g => g.Key, g => g.Count())
                .OrderByDescending(kvp => kvp.Value)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        private Dictionary<string, int> CalculateEventFrequency(List<DebugEvent> events)
        {
            return events
                .GroupBy(e => e.EventName)
                .ToDictionary(g => g.Key, g => g.Count())
                .OrderByDescending(kvp => kvp.Value)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        private double CalculateAverageProcessingTime(List<DebugEvent> events)
        {
            return events.Count > 0
                ? events.Average(e => e.ProcessingTime.TotalMilliseconds)
                : 0.0;
        }

        private List<(string, int)> GetMostFrequentTransitions(List<StateTransition> transitions, int count)
        {
            return transitions
                .GroupBy(t => $"{t.FromState} → {t.ToState}")
                .OrderByDescending(g => g.Count())
                .Take(count)
                .Select(g => (g.Key, g.Count()))
                .ToList();
        }
    }

    // Debug data models
    public record MachineDebugInfo
    {
        public string MachineId { get; init; } = "";
        public string CurrentState { get; init; } = "";
        public bool IsRunning { get; init; }
        public string[] ActiveStates { get; init; } = Array.Empty<string>();
        public DebugEvent? LastEventProcessed { get; init; }
        public int TransitionCount { get; init; }
        public int EventCount { get; init; }
    }

    public record DebugEvent
    {
        public string MachineId { get; init; } = "";
        public string EventName { get; init; } = "";
        public DateTime Timestamp { get; init; }
        public TimeSpan ProcessingTime { get; init; }
        public string Data { get; init; } = "";
        public string? Error { get; init; }
    }

    public record StateTransition
    {
        public string MachineId { get; init; } = "";
        public string FromState { get; init; } = "";
        public string ToState { get; init; } = "";
        public string EventName { get; init; } = "";
        public DateTime Timestamp { get; init; }
        public TimeSpan TransitionTime { get; init; }
        public List<string> Actions { get; init; } = new();
    }

    public record MachineDebugReport
    {
        public string MachineId { get; init; } = "";
        public DateTime Timestamp { get; init; }
        public string CurrentState { get; init; } = "";
        public bool IsRunning { get; init; }
        public int TotalEvents { get; init; }
        public int TotalTransitions { get; init; }
        public List<DebugEvent> RecentEvents { get; init; } = new();
        public List<StateTransition> RecentTransitions { get; init; } = new();
        public Dictionary<string, int> StateFrequency { get; init; } = new();
        public Dictionary<string, int> EventFrequency { get; init; } = new();
        public double AverageEventProcessingTime { get; init; }
        public List<(string, int)> MostFrequentTransitions { get; init; } = new();
    }

    // Event args for debugging events
    public class DebugEventArgs : EventArgs
    {
        public string MachineId { get; init; } = "";
        public string EventName { get; init; } = "";
        public object? EventData { get; init; }
        public TimeSpan ProcessingTime { get; init; }
        public Exception? Error { get; init; }
    }

    public class StateTransitionDebugArgs : EventArgs
    {
        public string MachineId { get; init; } = "";
        public string FromState { get; init; } = "";
        public string ToState { get; init; } = "";
        public string EventName { get; init; } = "";
        public DateTime Timestamp { get; init; }
    }

    public class EventProcessedEventArgs : EventArgs
    {
        public string EventName { get; init; } = "";
        public object? EventData { get; init; }
        public TimeSpan ProcessingTime { get; init; }
        public Exception? Error { get; init; }
    }

    public class ActionExecutionDebugArgs : EventArgs
    {
        public string MachineId { get; init; } = "";
        public string ActionName { get; init; } = "";
        public string CurrentState { get; init; } = "";
        public TimeSpan ExecutionTime { get; init; }
        public Exception? Error { get; init; }
    }
}