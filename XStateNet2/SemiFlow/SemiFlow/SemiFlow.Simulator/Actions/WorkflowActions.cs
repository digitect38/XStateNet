using SemiFlow.Simulator.Models;
using SemiFlow.Simulator.Simulation;
using Spectre.Console;

namespace SemiFlow.Simulator.Actions;

public class WorkflowActions
{
    private readonly SimulationState _state;
    private readonly Random _random = new();

    public WorkflowActions(SimulationState state)
    {
        _state = state;
    }

    #region System Actions

    public void InitializeSystem()
    {
        Log("🔧 Initializing CMP System...", Color.Cyan1);

        // Initialize FOUP
        _state.Stations["FOUP1"] = new Station
        {
            Id = "FOUP1",
            Role = "foup",
            Capacity = 25,
            State = StationState.Idle,
            Properties = new Dictionary<string, object>
            {
                ["position"] = "loadPort1",
                ["waferCount"] = _state.TotalWafers
            }
        };

        // Initialize Robot
        _state.Stations["ROBOT1"] = new Station
        {
            Id = "ROBOT1",
            Role = "robot",
            Capacity = 2,
            State = StationState.Idle,
            Properties = new Dictionary<string, object>
            {
                ["arm1"] = (object?)null!,
                ["arm2"] = (object?)null!,
                ["position"] = "home"
            }
        };

        // Initialize Platens
        _state.Stations["PLATEN1"] = new Station
        {
            Id = "PLATEN1",
            Role = "platen",
            Capacity = 1,
            State = StationState.Idle,
            Properties = new Dictionary<string, object>
            {
                ["temperature"] = 25,
                ["pressure"] = 0,
                ["rpm"] = 0
            }
        };

        _state.Stations["PLATEN2"] = new Station
        {
            Id = "PLATEN2",
            Role = "platen",
            Capacity = 1,
            State = StationState.Idle,
            Properties = new Dictionary<string, object>
            {
                ["temperature"] = 25,
                ["pressure"] = 0,
                ["rpm"] = 0
            }
        };

        Log("✓ System initialized successfully", Color.Green);
        Thread.Sleep(500);
    }

    public void StartLotProcessing()
    {
        Log($"📦 Starting lot processing: {_state.CurrentLot}", Color.Yellow);

        // Create wafers
        for (int i = 1; i <= _state.TotalWafers; i++)
        {
            var wafer = new Wafer
            {
                Id = i,
                LotId = _state.CurrentLot,
                State = WaferState.InFoup,
                ProcessType = DetermineProcessTypeForWafer(i),
                CurrentLocation = "FOUP1"
            };
            _state.WaferQueue.Enqueue(wafer);
        }

        Log($"✓ Created {_state.TotalWafers} wafers", Color.Green);
        Thread.Sleep(300);
    }

    public void FinalizeLotProcessing()
    {
        _state.EndTime = DateTime.Now;
        Log($"🏁 Lot processing complete!", Color.Green);
        Log($"   Processed: {_state.ProcessedWafers}/{_state.TotalWafers} wafers", Color.White);
        Log($"   Duration: {_state.ElapsedTime:hh\\:mm\\:ss}", Color.White);
        Log($"   1-Step: {_state.OneStepWafers}, 2-Step: {_state.TwoStepWafers}", Color.White);
    }

    public void CleanupSystem()
    {
        Log("🧹 Cleaning up system...", Color.Cyan1);
        foreach (var station in _state.Stations.Values)
        {
            station.State = StationState.Idle;
            station.CurrentWafer = null;
        }
        Log("✓ Cleanup complete", Color.Green);
    }

    #endregion

    #region Wafer Management

    public void GetNextWaferFromFoup()
    {
        if (_state.WaferQueue.Count > 0)
        {
            _state.CurrentWafer = _state.WaferQueue.Dequeue();
            _state.CurrentWafer.StartProcessTime = DateTime.Now;
            _state.ActiveWafers.Add(_state.CurrentWafer);

            AddHistory(_state.CurrentWafer, "Retrieved from FOUP", "FOUP1");
            Log($"📋 Wafer {_state.CurrentWafer.Id}: Retrieved from FOUP ({_state.CurrentWafer.ProcessType})", Color.Blue);
        }
    }

    public void DetermineProcessType()
    {
        // Process type already determined during wafer creation
        if (_state.CurrentWafer != null)
        {
            var type = _state.CurrentWafer.ProcessType == ProcessType.TwoStep ? "2-Step" : "1-Step";
            Log($"🔍 Wafer {_state.CurrentWafer.Id}: Process type = {type}", Color.Grey);
        }
    }

    public void MarkWaferComplete()
    {
        if (_state.CurrentWafer != null)
        {
            _state.CurrentWafer.State = WaferState.Completed;
            _state.CurrentWafer.EndProcessTime = DateTime.Now;
            _state.CompletedWafers.Add(_state.CurrentWafer);

            var cycleTime = (_state.CurrentWafer.EndProcessTime!.Value - _state.CurrentWafer.StartProcessTime!.Value).TotalSeconds;

            AddHistory(_state.CurrentWafer, "Completed", "FOUP1");
            Log($"✅ Wafer {_state.CurrentWafer.Id}: Complete (Cycle: {cycleTime:F1}s)", Color.Green);

            // Update metrics
            UpdateMetrics(cycleTime);

            _state.CurrentWafer = null;
        }
    }

    public void IncrementProcessedCount()
    {
        _state.ProcessedWafers++;
    }

    #endregion

    #region Robot Operations

    public void PickWaferFromFoup()
    {
        if (_state.CurrentWafer != null)
        {
            var robot = _state.Stations["ROBOT1"];
            robot.State = StationState.Busy;
            robot.Properties["arm1"] = _state.CurrentWafer.Id;

            _state.CurrentWafer.State = WaferState.OnRobot;
            _state.CurrentWafer.CurrentLocation = "ROBOT1";

            AddHistory(_state.CurrentWafer, "Picked from FOUP", "ROBOT1");
            Log($"🤖 Robot: Picked wafer {_state.CurrentWafer.Id} from FOUP", Color.Magenta1);
            SimulateDelay(1000, 2000);
        }
    }

    public void PickWaferFromPlaten()
    {
        if (_state.CurrentWafer != null && _state.SelectedPlaten != null)
        {
            var platen = _state.Stations[_state.SelectedPlaten];
            var robot = _state.Stations["ROBOT1"];

            platen.State = StationState.Idle;
            platen.CurrentWafer = null;
            platen.ProcessedWafers++;

            robot.State = StationState.Busy;
            robot.Properties["arm1"] = _state.CurrentWafer.Id;

            _state.CurrentWafer.State = WaferState.OnRobot;
            _state.CurrentWafer.CurrentLocation = "ROBOT1";

            AddHistory(_state.CurrentWafer, $"Picked from {_state.SelectedPlaten}", "ROBOT1");
            Log($"🤖 Robot: Picked wafer {_state.CurrentWafer.Id} from {_state.SelectedPlaten}", Color.Magenta1);
            SimulateDelay(1000, 2000);
        }
    }

    public void PlaceWaferOnPlaten()
    {
        if (_state.CurrentWafer != null && _state.SelectedPlaten != null)
        {
            var platen = _state.Stations[_state.SelectedPlaten];
            var robot = _state.Stations["ROBOT1"];

            platen.CurrentWafer = _state.CurrentWafer;
            platen.State = StationState.Busy;
            robot.Properties["arm1"] = (object?)null!;

            _state.CurrentWafer.State = WaferState.OnPlaten;
            _state.CurrentWafer.CurrentLocation = _state.SelectedPlaten;

            AddHistory(_state.CurrentWafer, $"Placed on {_state.SelectedPlaten}", _state.SelectedPlaten);
            Log($"🤖 Robot: Placed wafer {_state.CurrentWafer.Id} on {_state.SelectedPlaten}", Color.Magenta1);
            SimulateDelay(1500, 2500);
        }
    }

    public void PlaceWaferInFoup()
    {
        if (_state.CurrentWafer != null)
        {
            var robot = _state.Stations["ROBOT1"];
            robot.Properties["arm1"] = (object?)null!;

            _state.CurrentWafer.State = WaferState.InFoup;
            _state.CurrentWafer.CurrentLocation = "FOUP1";

            AddHistory(_state.CurrentWafer, "Returned to FOUP", "FOUP1");
            Log($"🤖 Robot: Returned wafer {_state.CurrentWafer.Id} to FOUP", Color.Magenta1);
            SimulateDelay(1000, 2000);
        }
    }

    public void MoveRobotToPlaten()
    {
        if (_state.SelectedPlaten != null)
        {
            var robot = _state.Stations["ROBOT1"];
            robot.Properties["position"] = _state.SelectedPlaten;

            Log($"🚶 Robot: Moving to {_state.SelectedPlaten}", Color.Grey);
            SimulateDelay(1000, 1500);
        }
    }

    public void MoveRobotToFoup()
    {
        var robot = _state.Stations["ROBOT1"];
        robot.Properties["position"] = "FOUP1";

        Log($"🚶 Robot: Moving to FOUP", Color.Grey);
        SimulateDelay(1000, 1500);
    }

    public void RobotReturnHome()
    {
        var robot = _state.Stations["ROBOT1"];
        robot.State = StationState.Idle;
        robot.Properties["position"] = "home";

        Log($"🏠 Robot: Returned home", Color.Grey);
        SimulateDelay(500, 1000);
    }

    public void ExecuteRobotMovement()
    {
        // Generic robot movement tracking
        var robot = _state.Stations["ROBOT1"];
        robot.UtilizationTime += 1.0;
    }

    #endregion

    #region Platen Operations

    public void SelectAvailablePlaten()
    {
        // Select first available platen
        var platen1 = _state.Stations["PLATEN1"];
        var platen2 = _state.Stations["PLATEN2"];

        if (platen1.State == StationState.Idle)
        {
            _state.SelectedPlaten = "PLATEN1";
            _state.FirstPlaten = "PLATEN1";
        }
        else if (platen2.State == StationState.Idle)
        {
            _state.SelectedPlaten = "PLATEN2";
            _state.FirstPlaten = "PLATEN2";
        }
        else
        {
            // Wait for first available (simplified - pick platen1)
            _state.SelectedPlaten = "PLATEN1";
            _state.FirstPlaten = "PLATEN1";
        }

        Log($"🎯 Selected platen: {_state.SelectedPlaten}", Color.Yellow);
    }

    public void SelectOtherPlaten()
    {
        // For 2-step process, select the platen NOT used in step 1
        _state.SelectedPlaten = _state.FirstPlaten == "PLATEN1" ? "PLATEN2" : "PLATEN1";
        _state.SecondPlaten = _state.SelectedPlaten;

        Log($"🎯 Selected platen for step 2: {_state.SelectedPlaten}", Color.Yellow);
    }

    public void ProcessWaferOnPlaten()
    {
        if (_state.CurrentWafer != null && _state.SelectedPlaten != null)
        {
            var platen = _state.Stations[_state.SelectedPlaten];
            platen.State = StationState.Processing;

            _state.CurrentWafer.State = WaferState.Processing;
            _state.CurrentWafer.ProcessStep++;

            AddHistory(_state.CurrentWafer, $"Processing on {_state.SelectedPlaten} (step {_state.CurrentWafer.ProcessStep})", _state.SelectedPlaten);
            Log($"⚙️  {_state.SelectedPlaten}: Processing wafer {_state.CurrentWafer.Id} (step {_state.CurrentWafer.ProcessStep})", Color.Orange1);

            // Simulate processing time (45-75 seconds)
            int processTime = _random.Next(45000, 75000);
            SimulateDelay(processTime / 10, processTime / 10); // Scaled down for demo

            platen.State = StationState.Idle;
            platen.UtilizationTime += processTime / 1000.0;

            Log($"✓ {_state.SelectedPlaten}: Wafer {_state.CurrentWafer.Id} processing complete", Color.Green);
        }
    }

    public void InitializePlaten1()
    {
        Log("🔧 Initializing Platen 1", Color.Grey);
    }

    public void InitializePlaten2()
    {
        Log("🔧 Initializing Platen 2", Color.Grey);
    }

    public void UpdatePlatenState()
    {
        // Update platen state tracking
        foreach (var platen in _state.Stations.Values.Where(s => s.Role == "platen"))
        {
            var utilization = CalculateUtilization(platen);
            _state.Metrics[$"{platen.Id.ToLower()}_utilization"] = utilization;
        }
    }

    #endregion

    #region Error Handling

    public void HandleProcessTimeout()
    {
        _state.ErrorCount++;
        Log($"⚠️  Process timeout detected", Color.Red);
    }

    public void HandleGlobalError()
    {
        _state.ErrorCount++;
        Log($"❌ Global error occurred", Color.Red);
    }

    public void HandleGlobalTimeout()
    {
        Log($"⏱️  Global timeout", Color.Red);
    }

    public void PauseSystem()
    {
        _state.SystemRunning = false;
        Log($"⏸️  System paused", Color.Yellow);
    }

    public void LogError()
    {
        Log($"📝 Error logged", Color.Red);
    }

    #endregion

    #region Metrics

    private void UpdateMetrics(double cycleTime)
    {
        // Update average cycle time
        var avgCycleTime = _state.Metrics["avg_cycle_time"];
        _state.Metrics["avg_cycle_time"] = (avgCycleTime * (_state.ProcessedWafers - 1) + cycleTime) / _state.ProcessedWafers;

        // Update throughput
        _state.Metrics["throughput"] = _state.ThroughputWafersPerHour;

        // Update utilizations
        foreach (var station in _state.Stations.Values)
        {
            if (station.Role == "platen" || station.Role == "robot")
            {
                var key = $"{station.Id.ToLower()}_utilization";
                _state.Metrics[key] = CalculateUtilization(station);
            }
        }
    }

    private double CalculateUtilization(Station station)
    {
        var elapsed = _state.ElapsedTime.TotalSeconds;
        return elapsed > 0 ? (station.UtilizationTime / elapsed) * 100.0 : 0.0;
    }

    #endregion

    #region Helpers

    private ProcessType DetermineProcessTypeForWafer(int waferId)
    {
        // Alternate between 1-step and 2-step for demo
        // In real scenario, this would be based on wafer properties
        if (waferId % 3 == 0) // Every 3rd wafer is 2-step
        {
            _state.TwoStepWafers++;
            return ProcessType.TwoStep;
        }
        else
        {
            _state.OneStepWafers++;
            return ProcessType.OneStep;
        }
    }

    private void AddHistory(Wafer wafer, string action, string location)
    {
        wafer.History.Add(new ProcessHistory
        {
            Timestamp = DateTime.Now,
            Action = action,
            Location = location,
            Details = ""
        });
    }

    private void SimulateDelay(int minMs, int maxMs)
    {
        // Scale down for demo (divide by 10)
        Thread.Sleep(_random.Next(minMs / 10, maxMs / 10));
    }

    private void Log(string message, Color color)
    {
        var markup = new Markup($"[{color}]{message}[/]");
        AnsiConsole.Write(markup);
        AnsiConsole.WriteLine();
    }

    #endregion
}
