namespace SemiStandard.WPF.EnhancedCMP.ViewModels;

public class ToolViewModel : ViewModelBase
{
    private string _toolId = "";
    private string _state = "Idle";
    private int _wafersProcessed;
    private double _slurryLevel = 100.0;
    private double _padWear;
    private double _avgCycleTime;
    private string? _currentJobId;
    private string? _currentWaferId;

    public string ToolId
    {
        get => _toolId;
        set => SetProperty(ref _toolId, value);
    }

    public string State
    {
        get => _state;
        set => SetProperty(ref _state, value);
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

    public string StatusColor => State switch
    {
        "Idle" or "idle" => "#4CAF50",
        "Processing" or "processing" => "#2196F3",
        "Loading" or "loading" => "#FF9800",
        "Unloading" or "unloading" => "#FF9800",
        "Maintenance" or "maintenance" => "#9C27B0",
        "Error" or "error" => "#F44336",
        _ => "#757575"
    };

    public void RefreshStatusColor()
    {
        OnPropertyChanged(nameof(StatusColor));
    }
}
