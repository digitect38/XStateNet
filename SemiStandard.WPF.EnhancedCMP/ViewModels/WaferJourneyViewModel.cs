namespace SemiStandard.WPF.EnhancedCMP.ViewModels;

public class WaferJourneyViewModel : ViewModelBase
{
    private string _waferId = "";
    private string _jobId = "";
    private ProcessStage _currentStage = ProcessStage.NotStarted;
    private string _currentTool = "";
    private DateTime _startTime;
    private TimeSpan _elapsedTime;

    // Stage completion flags
    private bool _loadportLoaded;
    private bool _wtr1ToPolisher;
    private bool _polisherProcessing;
    private bool _wtr2ToCleaner;
    private bool _cleanerProcessing;
    private bool _wtr1ToLoadport;
    private bool _loadportUnloaded;

    public string WaferId
    {
        get => _waferId;
        set => SetProperty(ref _waferId, value);
    }

    public string JobId
    {
        get => _jobId;
        set => SetProperty(ref _jobId, value);
    }

    public ProcessStage CurrentStage
    {
        get => _currentStage;
        set
        {
            if (SetProperty(ref _currentStage, value))
            {
                UpdateStageCompletion();
            }
        }
    }

    public string CurrentTool
    {
        get => _currentTool;
        set => SetProperty(ref _currentTool, value);
    }

    public DateTime StartTime
    {
        get => _startTime;
        set => SetProperty(ref _startTime, value);
    }

    public TimeSpan ElapsedTime
    {
        get => _elapsedTime;
        set => SetProperty(ref _elapsedTime, value);
    }

    public bool LoadportLoaded
    {
        get => _loadportLoaded;
        set => SetProperty(ref _loadportLoaded, value);
    }

    public bool Wtr1ToPolisher
    {
        get => _wtr1ToPolisher;
        set => SetProperty(ref _wtr1ToPolisher, value);
    }

    public bool PolisherProcessing
    {
        get => _polisherProcessing;
        set => SetProperty(ref _polisherProcessing, value);
    }

    public bool Wtr2ToCleaner
    {
        get => _wtr2ToCleaner;
        set => SetProperty(ref _wtr2ToCleaner, value);
    }

    public bool CleanerProcessing
    {
        get => _cleanerProcessing;
        set => SetProperty(ref _cleanerProcessing, value);
    }

    public bool Wtr1ToLoadport
    {
        get => _wtr1ToLoadport;
        set => SetProperty(ref _wtr1ToLoadport, value);
    }

    public bool LoadportUnloaded
    {
        get => _loadportUnloaded;
        set => SetProperty(ref _loadportUnloaded, value);
    }

    public string StageDescription => CurrentStage switch
    {
        ProcessStage.NotStarted => "Waiting to start",
        ProcessStage.LoadportLoading => "Loading at Loadport",
        ProcessStage.WTR1ToPolisher => "Transfer to Polisher (WTR1)",
        ProcessStage.PolisherProcessing => "CMP Processing",
        ProcessStage.WTR2ToCleaner => "Transfer to Cleaner (WTR2)",
        ProcessStage.CleanerProcessing => "Post-CMP Cleaning",
        ProcessStage.WTR1ToLoadport => "Return to Loadport (WTR1)",
        ProcessStage.LoadportUnloading => "Unloading at Loadport",
        ProcessStage.Completed => "Process Complete",
        _ => "Unknown"
    };

    public int ProgressPercent => CurrentStage switch
    {
        ProcessStage.NotStarted => 0,
        ProcessStage.LoadportLoading => 10,
        ProcessStage.WTR1ToPolisher => 25,
        ProcessStage.PolisherProcessing => 40,
        ProcessStage.WTR2ToCleaner => 55,
        ProcessStage.CleanerProcessing => 70,
        ProcessStage.WTR1ToLoadport => 85,
        ProcessStage.LoadportUnloading => 95,
        ProcessStage.Completed => 100,
        _ => 0
    };

    private void UpdateStageCompletion()
    {
        LoadportLoaded = CurrentStage >= ProcessStage.WTR1ToPolisher;
        Wtr1ToPolisher = CurrentStage >= ProcessStage.PolisherProcessing;
        PolisherProcessing = CurrentStage >= ProcessStage.WTR2ToCleaner;
        Wtr2ToCleaner = CurrentStage >= ProcessStage.CleanerProcessing;
        CleanerProcessing = CurrentStage >= ProcessStage.WTR1ToLoadport;
        Wtr1ToLoadport = CurrentStage >= ProcessStage.LoadportUnloading;
        LoadportUnloaded = CurrentStage >= ProcessStage.Completed;
    }
}

public enum ProcessStage
{
    NotStarted,
    LoadportLoading,
    WTR1ToPolisher,
    PolisherProcessing,
    WTR2ToCleaner,
    CleanerProcessing,
    WTR1ToLoadport,
    LoadportUnloading,
    Completed
}
