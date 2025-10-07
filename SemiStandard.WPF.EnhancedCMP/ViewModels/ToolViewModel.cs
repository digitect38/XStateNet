namespace SemiStandard.WPF.EnhancedCMP.ViewModels;

public class ToolViewModel : ViewModelBase
{
    private string _toolId = "";
    private string _state = "Idle";
    private string _mainState = "Idle";
    private string _subState = "";
    private int _wafersProcessed;
    private double _slurryLevel = 100.0;
    private double _padWear;
    private double _avgCycleTime;
    private string? _currentJobId;
    private string? _currentWaferId;
    private bool _hasWafer;

    // Cycle history (last 3 processing cycles)
    private readonly List<CycleInfo> _cycleHistory = new();
    private string _cycle1Text = "Empty";
    private string _cycle2Text = "Empty";
    private string _cycle3Text = "Empty";
    private string _cycle1Color = "#3E3E42";
    private string _cycle2Color = "#3E3E42";
    private string _cycle3Color = "#3E3E42";

    public string ToolId
    {
        get => _toolId;
        set => SetProperty(ref _toolId, value);
    }

    public bool HasWafer
    {
        get => _hasWafer;
        set => SetProperty(ref _hasWafer, value);
    }

    public string State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                ParseState(value);
            }
        }
    }

    public string MainState
    {
        get => _mainState;
        private set => SetProperty(ref _mainState, value);
    }

    public string SubState
    {
        get => _subState;
        private set => SetProperty(ref _subState, value);
    }

    private void ParseState(string fullState)
    {
        // State format: #CMP_TOOL_1_abc123.mainState.subState
        if (string.IsNullOrEmpty(fullState))
        {
            MainState = "unknown";
            SubState = "";
            return;
        }

        var firstDot = fullState.IndexOf('.');
        if (firstDot < 0)
        {
            MainState = fullState;
            SubState = "";
            return;
        }

        var stateHierarchy = fullState.Substring(firstDot + 1);
        var parts = stateHierarchy.Split('.');

        if (parts.Length == 1)
        {
            MainState = parts[0];
            SubState = "";
        }
        else
        {
            MainState = parts[0];
            SubState = string.Join(" → ", parts.Skip(1));
        }
    }

    public int WafersProcessed
    {
        get => _wafersProcessed;
        set => SetProperty(ref _wafersProcessed, value);
    }

    public double SlurryLevel
    {
        get => _slurryLevel;
        set => SetProperty(ref _slurryLevel, value);
    }

    public double PadWear
    {
        get => _padWear;
        set => SetProperty(ref _padWear, value);
    }

    public double AvgCycleTime
    {
        get => _avgCycleTime;
        set => SetProperty(ref _avgCycleTime, value);
    }

    public string? CurrentJobId
    {
        get => _currentJobId;
        set => SetProperty(ref _currentJobId, value);
    }

    public string? CurrentWaferId
    {
        get => _currentWaferId;
        set => SetProperty(ref _currentWaferId, value);
    }

    public string StatusColor => MainState.ToLower() switch
    {
        "idle" => "#4CAF50",
        "processing" => "#2196F3",
        "loading" => "#FF9800",
        "unloading" => "#FF9800",
        "maintenance" => "#9C27B0",
        "error" => "#F44336",
        "requestingconsumables" => "#FF5722",
        "reportingcomplete" => "#00BCD4",
        _ => "#757575"
    };

    public void RefreshStatusColor()
    {
        OnPropertyChanged(nameof(StatusColor));
    }

    // Cycle properties
    public string Cycle1Text
    {
        get => _cycle1Text;
        set => SetProperty(ref _cycle1Text, value);
    }

    public string Cycle2Text
    {
        get => _cycle2Text;
        set => SetProperty(ref _cycle2Text, value);
    }

    public string Cycle3Text
    {
        get => _cycle3Text;
        set => SetProperty(ref _cycle3Text, value);
    }

    public string Cycle1Color
    {
        get => _cycle1Color;
        set => SetProperty(ref _cycle1Color, value);
    }

    public string Cycle2Color
    {
        get => _cycle2Color;
        set => SetProperty(ref _cycle2Color, value);
    }

    public string Cycle3Color
    {
        get => _cycle3Color;
        set => SetProperty(ref _cycle3Color, value);
    }

    public void UpdateCycles(string? currentStage = null)
    {
        // Current cycle (Cycle 1) - shows current wafer and E90 stage
        if (HasWafer && !string.IsNullOrEmpty(CurrentWaferId))
        {
            var stageText = currentStage ?? MainState;
            Cycle1Text = $"{CurrentWaferId}\n{stageText}";
            Cycle1Color = StatusColor;
        }
        else
        {
            Cycle1Text = "Empty";
            Cycle1Color = "#3E3E42";
        }

        // Update cycle history when wafer completes
        if (_lastWaferId != CurrentWaferId && !string.IsNullOrEmpty(_lastWaferId))
        {
            // Wafer changed - record previous cycle
            var prevCycle = new CycleInfo
            {
                WaferId = _lastWaferId,
                State = _lastStage ?? _lastMainState,
                Color = GetColorForState(_lastMainState)
            };

            _cycleHistory.Insert(0, prevCycle);
            if (_cycleHistory.Count > 2) _cycleHistory.RemoveAt(2);

            // Update display
            if (_cycleHistory.Count > 0)
            {
                Cycle2Text = $"{_cycleHistory[0].WaferId}\n{_cycleHistory[0].State}";
                Cycle2Color = _cycleHistory[0].Color;
            }
            if (_cycleHistory.Count > 1)
            {
                Cycle3Text = $"{_cycleHistory[1].WaferId}\n{_cycleHistory[1].State}";
                Cycle3Color = _cycleHistory[1].Color;
            }
        }

        _lastWaferId = CurrentWaferId;
        _lastMainState = MainState;
        _lastStage = currentStage;
    }

    private string? _lastWaferId;
    private string _lastMainState = "";
    private string? _lastStage;

    private string GetColorForState(string state) => state.ToLower() switch
    {
        "idle" => "#4CAF50",
        "processing" => "#2196F3",
        "loading" => "#FF9800",
        "unloading" => "#FF9800",
        "reportingcomplete" => "#00BCD4",
        _ => "#757575"
    };
}

public class CycleInfo
{
    public string WaferId { get; set; } = "";
    public string State { get; set; } = "";
    public string Color { get; set; } = "";
}
