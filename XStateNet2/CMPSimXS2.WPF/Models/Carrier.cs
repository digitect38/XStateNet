using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CMPSimXS2.WPF.Models;

/// <summary>
/// Represents a FOUP (Front Opening Unified Pod) carrier containing wafers
/// Aligns with SEMI E87 Carrier Management specification
/// </summary>
public class Carrier : INotifyPropertyChanged
{
    private string _currentState;
    private string _currentLoadPort;
    private bool _isProcessingComplete;

    /// <summary>
    /// Unique carrier identifier (e.g., "CARRIER_001", "CARRIER_002")
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// LoadPort where this carrier is currently docked (e.g., "LoadPort", "LoadPort2")
    /// Null if carrier is not at any LoadPort
    /// </summary>
    public string? CurrentLoadPort
    {
        get => _currentLoadPort;
        set
        {
            if (_currentLoadPort != value)
            {
                _currentLoadPort = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Current E87 state of the carrier
    /// (NotPresent, WaitingForHost, Mapping, ReadyToAccess, InAccess, Complete, CarrierOut)
    /// </summary>
    public string CurrentState
    {
        get => _currentState;
        set
        {
            if (_currentState != value)
            {
                _currentState = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Collection of wafers in this carrier
    /// </summary>
    public ObservableCollection<Wafer> Wafers { get; }

    /// <summary>
    /// Maximum slot capacity (typically 25 for 300mm FOUPs)
    /// </summary>
    public int Capacity { get; init; }

    /// <summary>
    /// Number of wafers currently in the carrier
    /// </summary>
    public int WaferCount => Wafers.Count;

    /// <summary>
    /// True when all wafers in this carrier have completed processing
    /// </summary>
    public bool IsProcessingComplete
    {
        get => _isProcessingComplete;
        set
        {
            if (_isProcessingComplete != value)
            {
                _isProcessingComplete = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Timestamp when carrier arrived at LoadPort
    /// </summary>
    public DateTime? ArrivedTime { get; set; }

    /// <summary>
    /// Timestamp when carrier slot mapping was completed
    /// </summary>
    public DateTime? MappingCompleteTime { get; set; }

    /// <summary>
    /// Timestamp when carrier processing was completed
    /// </summary>
    public DateTime? ProcessingCompleteTime { get; set; }

    /// <summary>
    /// Timestamp when carrier departed from LoadPort
    /// </summary>
    public DateTime? DepartedTime { get; set; }

    public Carrier(string id, int capacity = 25)
    {
        Id = id;
        Capacity = capacity;
        Wafers = new ObservableCollection<Wafer>();
        _currentState = "NotPresent";
        _currentLoadPort = null;
        _isProcessingComplete = false;
    }

    /// <summary>
    /// Add a wafer to this carrier
    /// </summary>
    public void AddWafer(Wafer wafer)
    {
        if (Wafers.Count >= Capacity)
            throw new InvalidOperationException($"Carrier {Id} is at full capacity ({Capacity} wafers)");

        Wafers.Add(wafer);
        wafer.OriginLoadPort = CurrentLoadPort ?? "LoadPort";
        OnPropertyChanged(nameof(WaferCount));
    }

    /// <summary>
    /// Remove a wafer from this carrier
    /// </summary>
    public bool RemoveWafer(Wafer wafer)
    {
        var removed = Wafers.Remove(wafer);
        if (removed)
        {
            OnPropertyChanged(nameof(WaferCount));
        }
        return removed;
    }

    /// <summary>
    /// Check if all wafers in this carrier have completed processing
    /// </summary>
    public bool CheckAllWafersCompleted()
    {
        var allCompleted = Wafers.All(w => w.IsCompleted);
        IsProcessingComplete = allCompleted;
        if (allCompleted && ProcessingCompleteTime == null)
        {
            ProcessingCompleteTime = DateTime.UtcNow;
        }
        return allCompleted;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
