using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CMPSimulator.Models;
using CMPSimulator.XStateStations;
using XStateNet.Orchestration;

namespace CMPSimulator.Controllers;

/// <summary>
/// Event-driven CMP controller using XStateNet
/// Each station is an independent XState machine communicating via EventBusOrchestrator
/// </summary>
public class XStateCMPController : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly Dictionary<string, StationPosition> _stations;
    private readonly Dictionary<int, int> _waferOriginalSlots;
    private readonly StreamWriter _debugLog;

    // XState Station Machines
    private readonly XLoadPortStation _loadPort;
    private readonly XPolishingStation _polisher;
    private readonly XCleanerStation _cleaner;
    private readonly XBufferStation _buffer;
    private readonly XWTRStation _wtr1;
    private readonly XWTRStation _wtr2;

    public ObservableCollection<Wafer> Wafers { get; }
    public Dictionary<string, StationPosition> Stations => _stations;

    public event EventHandler<string>? LogMessage;

    public XStateCMPController()
    {
        // Create debug log file
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CMPSimulator_Debug.log");
        _debugLog = new StreamWriter(logPath, false) { AutoFlush = true };
        DebugLog("=== CMP Simulator Debug Log Started ===");

        _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
        {
            PoolSize = 4,
            EnableLogging = true  // Enable logging for debugging
        });

        _stations = new Dictionary<string, StationPosition>();
        _waferOriginalSlots = new Dictionary<int, int>();
        Wafers = new ObservableCollection<Wafer>();

        DebugLog("Initializing stations...");
        InitializeStations();
        DebugLog("Initializing wafers...");
        InitializeWafers();

        // Create XState station machines
        DebugLog("Creating LoadPort station...");
        _loadPort = new XLoadPortStation("loadport", _orchestrator);
        DebugLog("Creating Polisher station...");
        _polisher = new XPolishingStation("polisher", _orchestrator);
        DebugLog("Creating Cleaner station...");
        _cleaner = new XCleanerStation("cleaner", _orchestrator);
        DebugLog("Creating Buffer station...");
        _buffer = new XBufferStation("buffer", _orchestrator);
        DebugLog("Creating WTR1 station...");
        _wtr1 = new XWTRStation("wtr1", _orchestrator);
        DebugLog("Creating WTR2 station...");
        _wtr2 = new XWTRStation("wtr2", _orchestrator);

        DebugLog("Wiring up stations...");
        WireUpStations();
        DebugLog("=== Controller Initialization Complete ===");
    }

    private void InitializeStations()
    {
        _stations["LoadPort"] = new StationPosition("LoadPort", 50, 150, 100, 400, 25);
        _stations["WTR1"] = new StationPosition("WTR1", 250, 300, 80, 80, 0);
        _stations["Polisher"] = new StationPosition("Polisher", 420, 250, 120, 120, 1);
        _stations["WTR2"] = new StationPosition("WTR2", 590, 300, 80, 80, 0);
        _stations["Cleaner"] = new StationPosition("Cleaner", 760, 250, 120, 120, 1);
        _stations["Buffer"] = new StationPosition("Buffer", 420, 420, 80, 80, 1);
    }

    private void InitializeWafers()
    {
        var colors = GenerateDistinctColors(25);

        for (int i = 0; i < 25; i++)
        {
            var wafer = new Wafer(i + 1, colors[i]);
            var loadPort = _stations["LoadPort"];
            var (x, y) = loadPort.GetWaferPosition(i);

            wafer.X = x;
            wafer.Y = y;
            wafer.CurrentStation = "LoadPort";

            Wafers.Add(wafer);
            _waferOriginalSlots[wafer.Id] = i;
            _stations["LoadPort"].AddWafer(wafer.Id);
        }
    }

    private List<Color> GenerateDistinctColors(int count)
    {
        var colors = new List<Color>();
        for (int i = 0; i < count; i++)
        {
            double hue = (360.0 / count) * i;
            var color = ColorFromHSV(hue, 0.8, 0.9);
            colors.Add(color);
        }
        return colors;
    }

    private Color ColorFromHSV(double hue, double saturation, double value)
    {
        int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
        double f = hue / 60 - Math.Floor(hue / 60);

        value = value * 255;
        byte v = Convert.ToByte(value);
        byte p = Convert.ToByte(value * (1 - saturation));
        byte q = Convert.ToByte(value * (1 - f * saturation));
        byte t = Convert.ToByte(value * (1 - (1 - f) * saturation));

        return hi switch
        {
            0 => Color.FromRgb(v, t, p),
            1 => Color.FromRgb(q, v, p),
            2 => Color.FromRgb(p, v, t),
            3 => Color.FromRgb(p, q, v),
            4 => Color.FromRgb(t, p, v),
            _ => Color.FromRgb(v, p, q)
        };
    }

    private void WireUpStations()
    {
        // Subscribe to log messages from all stations
        _loadPort.LogMessage += (s, msg) => Log(msg);
        _polisher.LogMessage += (s, msg) => Log(msg);
        _cleaner.LogMessage += (s, msg) => Log(msg);
        _buffer.LogMessage += (s, msg) => Log(msg);
        _wtr1.LogMessage += (s, msg) => Log(msg);
        _wtr2.LogMessage += (s, msg) => Log(msg);

        // Subscribe to wafer events for UI updates
        _loadPort.WaferDispatched += LoadPort_WaferDispatched;
        _loadPort.WaferReturned += LoadPort_WaferReturned;
        _wtr1.WaferInTransit += WTR_WaferInTransit;
        _wtr2.WaferInTransit += WTR_WaferInTransit;
        _polisher.WaferArrived += Station_WaferArrived;
        _polisher.WaferPickedUp += Station_WaferPickedUp;
        _cleaner.WaferArrived += Station_WaferArrived;
        _cleaner.WaferPickedUp += Station_WaferPickedUp;
        _buffer.WaferArrived += Station_WaferArrived;
        _buffer.WaferPickedUp += Station_WaferPickedUp;

        // Machines are already registered with orchestrator in their constructors
    }

    private void LoadPort_WaferDispatched(object? sender, WaferDispatchEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _stations["LoadPort"].RemoveWafer(e.WaferId);
        });
    }

    private void LoadPort_WaferReturned(object? sender, WaferReturnEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var wafer = Wafers.FirstOrDefault(w => w.Id == e.WaferId);
            if (wafer != null)
            {
                _stations["LoadPort"].AddWafer(e.WaferId);
                var originalSlot = _waferOriginalSlots[e.WaferId];
                var (x, y) = _stations["LoadPort"].GetWaferPosition(originalSlot);
                wafer.X = x;
                wafer.Y = y;
                wafer.CurrentStation = "LoadPort";
            }
        });
    }

    private void WTR_WaferInTransit(object? sender, WaferTransitEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var wafer = Wafers.FirstOrDefault(w => w.Id == e.WaferId);
            if (wafer != null)
            {
                var stationKey = e.RobotId == "wtr1" ? "WTR1" : "WTR2";
                var (x, y) = _stations[stationKey].GetWaferPosition(0);
                wafer.X = x;
                wafer.Y = y;
                wafer.CurrentStation = $"{stationKey} (transit)";
            }
        });
    }

    private void Station_WaferArrived(object? sender, WaferArrivedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var wafer = Wafers.FirstOrDefault(w => w.Id == e.WaferId);
            if (wafer != null)
            {
                var stationKey = char.ToUpper(e.StationId[0]) + e.StationId.Substring(1);
                if (_stations.TryGetValue(stationKey, out var station))
                {
                    station.AddWafer(e.WaferId);
                    var (x, y) = station.GetWaferPosition(0);
                    wafer.X = x;
                    wafer.Y = y;
                    wafer.CurrentStation = stationKey;
                }
            }
        });
    }

    private void Station_WaferPickedUp(object? sender, WaferPickedUpEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var stationKey = char.ToUpper(e.StationId[0]) + e.StationId.Substring(1);
            if (_stations.TryGetValue(stationKey, out var station))
            {
                station.RemoveWafer(e.WaferId);
            }
        });
    }

    public async Task StartSimulation()
    {
        DebugLog("=== StartSimulation Called ===");
        Log("═══════════════════════════════════════════════════════════");
        Log("🚀 STARTING XSTATE-DRIVEN SIMULATION");
        Log("Each station is an independent XState machine");
        Log("═══════════════════════════════════════════════════════════");

        // Start all state machines
        DebugLog("Starting LoadPort...");
        Log("Starting LoadPort...");
        var lpState = await _loadPort.StartAsync();
        DebugLog($"LoadPort.StartAsync() returned: {lpState}");
        var lpCurrentState = _loadPort.GetCurrentState();
        DebugLog($"LoadPort.GetCurrentState() returned: {lpCurrentState}");
        Log($"LoadPort started, state: {lpCurrentState}");

        DebugLog("Starting Polisher...");
        Log("Starting Polisher...");
        var pState = await _polisher.StartAsync();
        DebugLog($"Polisher.StartAsync() returned: {pState}");
        Log($"Polisher started, state: {_polisher.GetCurrentState()}");

        DebugLog("Starting Cleaner...");
        Log("Starting Cleaner...");
        var cState = await _cleaner.StartAsync();
        DebugLog($"Cleaner.StartAsync() returned: {cState}");
        Log($"Cleaner started, state: {_cleaner.GetCurrentState()}");

        DebugLog("Starting Buffer...");
        Log("Starting Buffer...");
        var bState = await _buffer.StartAsync();
        DebugLog($"Buffer.StartAsync() returned: {bState}");
        Log($"Buffer started, state: {_buffer.GetCurrentState()}");

        DebugLog("Starting WTR1...");
        Log("Starting WTR1...");
        var w1State = await _wtr1.StartAsync();
        DebugLog($"WTR1.StartAsync() returned: {w1State}");
        Log($"WTR1 started, state: {_wtr1.GetCurrentState()}");

        DebugLog("Starting WTR2...");
        Log("Starting WTR2...");
        var w2State = await _wtr2.StartAsync();
        DebugLog($"WTR2.StartAsync() returned: {w2State}");
        Log($"WTR2 started, state: {_wtr2.GetCurrentState()}");

        Log("✓ All state machines started");
        DebugLog("✓ All state machines started");

        // Trigger simulation start
        DebugLog("Calling _loadPort.StartSimulation()...");
        Log("Sending START_SIMULATION event to LoadPort...");
        await _loadPort.StartSimulation();
        DebugLog("_loadPort.StartSimulation() completed");
        Log("START_SIMULATION event sent");

        // Check state after sending event
        await Task.Delay(100); // Give orchestrator time to process
        DebugLog($"LoadPort state after START_SIMULATION: {_loadPort.GetCurrentState()}");
    }

    public void StopSimulation()
    {
        Log("⏸️  Simulation paused");
    }

    public void ResetSimulation()
    {
        Log("Reset not yet implemented in XState version");
    }

    private void Log(string message)
    {
        var msg = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        LogMessage?.Invoke(this, msg);
        DebugLog($"[UI] {message}");
    }

    private void DebugLog(string message)
    {
        var msg = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        _debugLog?.WriteLine(msg);
        Console.WriteLine(msg); // Also output to console
    }

    public void Dispose()
    {
        DebugLog("=== Disposing Controller ===");
        _orchestrator?.Dispose();
        _debugLog?.Close();
        _debugLog?.Dispose();
    }
}
