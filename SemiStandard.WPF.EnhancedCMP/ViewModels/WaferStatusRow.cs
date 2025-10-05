namespace SemiStandard.WPF.EnhancedCMP.ViewModels;

public class WaferStatusRow : ViewModelBase
{
    private string _waferId = "";
    private string _tool1Status = "";
    private string _tool2Status = "";
    private string _tool3Status = "";
    private string _tool4Status = "";
    private string _tool5Status = "";
    private string _tool6Status = "";
    private string _tool7Status = "";
    private string _currentLocation = "";
    private string _overallState = "";

    public string WaferId
    {
        get => _waferId;
        set => SetProperty(ref _waferId, value);
    }

    public string Tool1Status
    {
        get => _tool1Status;
        set => SetProperty(ref _tool1Status, value);
    }

    public string Tool2Status
    {
        get => _tool2Status;
        set => SetProperty(ref _tool2Status, value);
    }

    public string Tool3Status
    {
        get => _tool3Status;
        set => SetProperty(ref _tool3Status, value);
    }

    public string Tool4Status
    {
        get => _tool4Status;
        set => SetProperty(ref _tool4Status, value);
    }

    public string Tool5Status
    {
        get => _tool5Status;
        set => SetProperty(ref _tool5Status, value);
    }

    public string Tool6Status
    {
        get => _tool6Status;
        set => SetProperty(ref _tool6Status, value);
    }

    public string Tool7Status
    {
        get => _tool7Status;
        set => SetProperty(ref _tool7Status, value);
    }

    public string CurrentLocation
    {
        get => _currentLocation;
        set => SetProperty(ref _currentLocation, value);
    }

    public string OverallState
    {
        get => _overallState;
        set => SetProperty(ref _overallState, value);
    }
}
