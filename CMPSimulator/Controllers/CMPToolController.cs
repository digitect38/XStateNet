using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CMPSimulator.Models;
using XStateNet;
using XStateNet.Orchestration;

namespace CMPSimulator.Controllers;

/// <summary>
/// Main controller for CMP tool simulation
/// Coordinates wafer flow: LoadPort -> WTR1 -> Polisher -> WTR2 -> Cleaner -> WTR2 -> WTR1 -> LoadPort
/// </summary>
public class CMPToolController
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly Dictionary<string, IPureStateMachine> _machines;
    private readonly Dictionary<string, StationPosition> _stations;
    private readonly Queue<Wafer> _waferQueue;
    private readonly Dictionary<int, string> _waferLocations; // wafer ID -> station name
    private readonly Dictionary<int, int> _waferOriginalSlots; // wafer ID -> original slot in LoadPort

    public ObservableCollection<Wafer> Wafers { get; }
    public Dictionary<string, StationPosition> Stations => _stations;

    private bool _isRunning;
    private int _processedWafers;
    private DateTime _simulationStartTime;
    private int _polisherThroughput;
    private int _cleanerThroughput;

    public event EventHandler<string>? LogMessage;
    public event EventHandler<string>? StatisticsUpdate;

    public CMPToolController()
    {
        _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
        {
            PoolSize = 2,
            EnableLogging = false
        });

        _machines = new Dictionary<string, IPureStateMachine>();
        _stations = new Dictionary<string, StationPosition>();
        _waferQueue = new Queue<Wafer>();
        _waferLocations = new Dictionary<int, string>();
        _waferOriginalSlots = new Dictionary<int, int>();
        Wafers = new ObservableCollection<Wafer>();

        InitializeStations();
        InitializeWafers();
    }

    private void InitializeStations()
    {
        // Define station positions (X, Y, Width, Height, Capacity)
        _stations["LoadPort"] = new StationPosition("LoadPort", 50, 150, 100, 400, 25);

        _stations["WTR1"] = new StationPosition("WTR1", 250, 300, 80, 80, 0); // Transit only

        _stations["Polisher"] = new StationPosition("Polisher", 420, 250, 120, 120, 1);

        _stations["WTR2"] = new StationPosition("WTR2", 590, 300, 80, 80, 0); // Transit only

        _stations["Cleaner"] = new StationPosition("Cleaner", 760, 250, 120, 120, 1);

        // Buffer: Return path only (Cleaner -> WTR2 -> Buffer -> WTR1 -> LoadPort)
        _stations["Buffer"] = new StationPosition("Buffer", 420, 420, 80, 80, 1);
    }

    private void InitializeWafers()
    {
        // Create 25 wafers with unique IDs and colors
        var colors = GenerateDistinctColors(25);

        for (int i = 0; i < 25; i++)
        {
            var wafer = new Wafer(i + 1, colors[i]);

            // Position wafers in LoadPort
            var loadPort = _stations["LoadPort"];
            var (x, y) = loadPort.GetWaferPosition(i);
            wafer.X = x;
            wafer.Y = y;
            wafer.CurrentStation = "LoadPort";

            Wafers.Add(wafer);
            _waferQueue.Enqueue(wafer);
            _waferLocations[wafer.Id] = "LoadPort";
            _waferOriginalSlots[wafer.Id] = i; // Remember original slot position
            _stations["LoadPort"].AddWafer(wafer.Id);
        }
    }

    private List<Color> GenerateDistinctColors(int count)
    {
        var colors = new List<Color>();
        var random = new Random(42); // Fixed seed for consistency

        // Use HSV color space to generate distinct colors
        for (int i = 0; i < count; i++)
        {
            double hue = (i * 360.0 / count) % 360;
            double saturation = 0.7 + (random.NextDouble() * 0.3); // 70-100%
            double value = 0.7 + (random.NextDouble() * 0.3); // 70-100%

            colors.Add(HSVtoRGB(hue, saturation, value));
        }

        return colors;
    }

    private Color HSVtoRGB(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;

        double r = 0, g = 0, b = 0;

        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255)
        );
    }

    public async Task StartSimulation()
    {
        if (_isRunning) return;

        _isRunning = true;
        _simulationStartTime = DateTime.Now;
        _polisherThroughput = 0;
        _cleanerThroughput = 0;

        Log("═══════════════════════════════════════════════════════════");
        Log("Simulation started - PIPELINE MODE");
        Log("Key Feature: Polisher and Cleaner work in PARALLEL");
        Log("═══════════════════════════════════════════════════════════");

        // Start statistics reporter
        _ = Task.Run(async () =>
        {
            while (_isRunning)
            {
                await Task.Delay(5000); // Update every 5 seconds
                UpdateStatistics();
            }
        });

        // Start the pipeline - launch each wafer as independent task for true parallelism
        _ = Task.Run(async () =>
        {
            var activeTasks = new List<Task>();
            var launchedCount = 0;

            try
            {
                while (_isRunning && _waferQueue.Count > 0)
                {
                    var wafer = _waferQueue.Dequeue();
                    launchedCount++;
                    Log($"Launching Wafer {wafer.Id} (Total launched: {launchedCount}/25)");

                    var waferTask = ProcessWaferAsync(wafer);
                    activeTasks.Add(waferTask);

                    // Stagger wafer launches to prevent congestion
                    // This allows pipeline stages to fill up gradually
                    await Task.Delay(3500); // Launch interval (slightly longer than transfer time)

                    // Clean up completed tasks
                    activeTasks.RemoveAll(t => t.IsCompleted);
                }

                Log($"All {launchedCount} wafers launched. Waiting for completion...");

                // Wait for all active wafers to complete
                await Task.WhenAll(activeTasks);

                var totalTime = (DateTime.Now - _simulationStartTime).TotalSeconds;
                Log("═══════════════════════════════════════════════════════════");
                Log($"All wafers completed processing!");
                Log($"Total Time: {totalTime:F1}s");
                Log($"Average Throughput: {25.0 / totalTime:F2} wafers/sec");
                Log($"Polisher Processed: {_polisherThroughput} wafers");
                Log($"Cleaner Processed: {_cleanerThroughput} wafers");
                Log("═══════════════════════════════════════════════════════════");
            }
            catch (Exception ex)
            {
                Log($"ERROR in simulation loop: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
            }
        });
    }

    private void UpdateStatistics()
    {
        var elapsed = (DateTime.Now - _simulationStartTime).TotalSeconds;
        var stats = $"Runtime: {elapsed:F1}s | Completed: {_processedWafers}/25 | " +
                   $"Polisher: {_polisherThroughput} | Cleaner: {_cleanerThroughput}";
        StatisticsUpdate?.Invoke(this, stats);
    }

    private async Task ProcessWaferAsync(Wafer wafer)
    {
        try
        {
            Log($"═══════════════════════════════════════════════════════════");
            Log($"🚀 Wafer {wafer.Id} STARTING JOURNEY (Pipeline Task)");
            Log($"═══════════════════════════════════════════════════════════");

            // Check if simulation is still running before each major step
            if (!_isRunning) return;

            // Stage 1: LoadPort → WTR1 → Polisher (Forward - no buffer)
            Log($"📍 Wafer {wafer.Id} STAGE 1: LoadPort → Polisher");
            if (!_isRunning) return;
            await SimpleTransfer(wafer, "LoadPort", "WTR1");
            await SimpleTransfer(wafer, "WTR1", "Polisher");
            Log($"✓ Wafer {wafer.Id} arrived at Polisher");

            // Stage 2: Process in Polisher
            if (!_isRunning) return;
            Log($"📍 Wafer {wafer.Id} STAGE 2: POLISHING (3000ms)");
            Log($"⚙️  Polisher now occupied by Wafer {wafer.Id}");
            await Task.Delay(3000);
            Interlocked.Increment(ref _polisherThroughput);
            Log($"✓ Wafer {wafer.Id} polishing completed (Polisher now FREE)");

            // Stage 3: Polisher → WTR2 → Cleaner (Forward - no buffer)
            Log($"📍 Wafer {wafer.Id} STAGE 3: Polisher → Cleaner");
            if (!_isRunning) return;
            await SimpleTransfer(wafer, "Polisher", "WTR2");
            await SimpleTransfer(wafer, "WTR2", "Cleaner");
            Log($"✓ Wafer {wafer.Id} arrived at Cleaner");

            // Stage 4: Process in Cleaner
            if (!_isRunning) return;
            Log($"📍 Wafer {wafer.Id} STAGE 4: CLEANING (2500ms)");
            Log($"⚙️  Cleaner now occupied by Wafer {wafer.Id}");
            await Task.Delay(2500);
            Interlocked.Increment(ref _cleanerThroughput);
            Log($"✓ Wafer {wafer.Id} cleaning completed (Cleaner now FREE)");

            // Stage 5: Return path - Cleaner → WTR2 → Buffer → WTR1 → LoadPort
            Log($"📍 Wafer {wafer.Id} STAGE 5: Return to LoadPort via Buffer");
            if (!_isRunning) return;
            await SimpleTransfer(wafer, "Cleaner", "WTR2");
            await SimpleTransfer(wafer, "WTR2", "Buffer");
            await SimpleTransfer(wafer, "Buffer", "WTR1");
            await SimpleTransfer(wafer, "WTR1", "LoadPort");
            Log($"✓ Wafer {wafer.Id} returned to original slot");

            Interlocked.Increment(ref _processedWafers);
            Log($"═══════════════════════════════════════════════════════════");
            Log($"🏁 Wafer {wafer.Id} JOURNEY COMPLETE ({_processedWafers}/25)");
            Log($"═══════════════════════════════════════════════════════════");
        }
        catch (Exception ex)
        {
            Log($"❌❌❌ CRITICAL ERROR processing wafer {wafer.Id}: {ex.Message}");
            Log($"Stack: {ex.StackTrace}");
            LogStationState($"ERROR STATE for Wafer {wafer.Id}");
        }
    }

    /// <summary>
    /// Simple transfer: wafer moves from one station to another
    /// WTR stations: transit only (capacity 0)
    /// Other stations: wait if full
    /// </summary>
    private async Task SimpleTransfer(Wafer wafer, string fromStation, string toStation)
    {
        Log($"  ▶ Wafer {wafer.Id}: {fromStation} → {toStation}");

        // Remove from source station FIRST (if it has capacity, meaning it stores wafers)
        bool removedFromSource = false;
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_stations[fromStation].MaxCapacity > 0 && _stations[fromStation].WaferSlots.Contains(wafer.Id))
            {
                _stations[fromStation].RemoveWafer(wafer.Id);
                removedFromSource = true;
                Log($"    Wafer {wafer.Id}: Removed from {fromStation}");
            }
        });

        // For WTR stations (transit only), just pass through
        if (_stations[toStation].MaxCapacity == 0)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Update visual position (transit through WTR)
                var (x, y) = _stations[toStation].GetWaferPosition(0);
                wafer.X = x;
                wafer.Y = y;
                wafer.CurrentStation = $"{toStation} (transit)";
            });

            Log($"    Wafer {wafer.Id}: Transiting through {toStation}");
            await Task.Delay(600); // Transit time
            return;
        }

        // For regular stations, wait if full
        var waitTime = 0;
        while (!_stations[toStation].CanAcceptWafer() && _isRunning)
        {
            if (waitTime == 0)
            {
                Log($"    Wafer {wafer.Id}: Waiting for {toStation} (occupied by [{string.Join(", ", _stations[toStation].WaferSlots)}])");
            }
            await Task.Delay(100);
            waitTime += 100;
            if (waitTime % 1000 == 0)
            {
                Log($"    Wafer {wafer.Id}: Still waiting for {toStation} ({waitTime}ms)");
            }
        }

        if (!_isRunning) return;

        // Move to destination
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // Add to destination
            if (!_stations[toStation].CanAcceptWafer())
            {
                Log($"    ❌ ERROR: {toStation} cannot accept Wafer {wafer.Id}!");
                Log($"       Capacity: {_stations[toStation].MaxCapacity}, Current: {_stations[toStation].WaferSlots.Count}");
                throw new InvalidOperationException($"Station {toStation} is full!");
            }

            _stations[toStation].AddWafer(wafer.Id);

            // Calculate position
            double x, y;
            if (toStation == "LoadPort")
            {
                var originalSlot = _waferOriginalSlots[wafer.Id];
                (x, y) = _stations[toStation].GetWaferPosition(originalSlot);
            }
            else
            {
                var slot = _stations[toStation].WaferSlots.IndexOf(wafer.Id);
                (x, y) = _stations[toStation].GetWaferPosition(slot);
            }

            wafer.X = x;
            wafer.Y = y;
            wafer.CurrentStation = toStation;
        });

        Log($"    ✓ Wafer {wafer.Id}: Arrived at {toStation}");
        await Task.Delay(200); // Settling time
    }

    private string GetStationStatusInfo(string stationName)
    {
        var station = _stations[stationName];
        if (stationName == "Polisher" || stationName == "Cleaner")
        {
            var occupancy = station.WaferSlots.Count > 0 ?
                $"[Wafer {station.WaferSlots[0]}]" : "[IDLE]";
            return occupancy;
        }
        else if (stationName == "LoadPort")
        {
            return $"[{station.WaferSlots.Count}/25]";
        }
        return "";
    }

    public void StopSimulation()
    {
        _isRunning = false;
        Log("Simulation stopped");
    }

    public void ResetSimulation()
    {
        StopSimulation();
        _processedWafers = 0;
        _polisherThroughput = 0;
        _cleanerThroughput = 0;
        _waferQueue.Clear();
        _waferLocations.Clear();

        // Clear all stations
        foreach (var station in _stations.Values)
        {
            station.WaferSlots.Clear();
        }

        // Reset all wafers to LoadPort and rebuild queue
        Application.Current.Dispatcher.Invoke(() =>
        {
            for (int i = 0; i < Wafers.Count; i++)
            {
                var wafer = Wafers[i];
                var loadPort = _stations["LoadPort"];
                var (x, y) = loadPort.GetWaferPosition(i);

                wafer.X = x;
                wafer.Y = y;
                wafer.CurrentStation = "LoadPort";

                _waferQueue.Enqueue(wafer);
                _waferLocations[wafer.Id] = "LoadPort";
                loadPort.AddWafer(wafer.Id);
            }
        });

        Log("Simulation reset");
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    private void LogStationState(string context)
    {
        var state = $">>> STATION STATE ({context}):\n";
        foreach (var kvp in _stations)
        {
            var station = kvp.Value;
            if (station.WaferSlots.Count > 0)
            {
                state += $"    {kvp.Key}: [{string.Join(", ", station.WaferSlots)}] (Capacity: {station.MaxCapacity})\n";
            }
        }
        Log(state.TrimEnd());
    }

    public void Dispose()
    {
        StopSimulation();
        _orchestrator?.Dispose();
    }
}
