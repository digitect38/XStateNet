using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace CMPSimXS2.Models;

/// <summary>
/// Represents a wafer with unique ID and processing state tracking
/// Font color changes based on processing progress: Black → Yellow → White
/// </summary>
public class Wafer : INotifyPropertyChanged
{
    private int _id;
    private string _currentStation = "Carrier";
    private string _processingState = "NotProcessed"; // NotProcessed, Polished, Cleaned
    private bool _isCompleted;
    private string _journeyStage = "InCarrier"; // InCarrier, ToPolisher, Polishing, ToCleaner, Cleaning, ToBuffer, InBuffer, ToCarrier

    public int Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string CurrentStation
    {
        get => _currentStation;
        set
        {
            if (SetProperty(ref _currentStation, value))
            {
                OnPropertyChanged(nameof(DisplayStation));
            }
        }
    }

    /// <summary>
    /// Processing state: NotProcessed, Polished, Cleaned
    /// </summary>
    public string ProcessingState
    {
        get => _processingState;
        set
        {
            if (SetProperty(ref _processingState, value))
            {
                OnPropertyChanged(nameof(TextColor));
                OnPropertyChanged(nameof(StatusSymbol));
            }
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set => SetProperty(ref _isCompleted, value);
    }

    /// <summary>
    /// Current stage in the wafer journey:
    /// InCarrier → ToPolisher → Polishing → ToCleaner → Cleaning → ToBuffer → InBuffer → ToCarrier
    /// </summary>
    public string JourneyStage
    {
        get => _journeyStage;
        set => SetProperty(ref _journeyStage, value);
    }

    /// <summary>
    /// Font color based on processing state:
    /// - Black: Not processed yet
    /// - Yellow: Polished (being cleaned or waiting for cleaning)
    /// - White: Cleaned (processing complete)
    /// </summary>
    public Brush TextColor
    {
        get
        {
            return ProcessingState switch
            {
                "Cleaned" => Brushes.White,
                "Polished" => Brushes.Yellow,
                _ => Brushes.Black // NotProcessed
            };
        }
    }

    /// <summary>
    /// Status symbol for visual indication
    /// </summary>
    public string StatusSymbol
    {
        get
        {
            return ProcessingState switch
            {
                "Cleaned" => "✓",
                "Polished" => "◐",
                _ => ""
            };
        }
    }

    /// <summary>
    /// Display station name for debugging
    /// </summary>
    public string DisplayStation => $"[{Id}] @ {CurrentStation}";

    public Wafer(int id)
    {
        _id = id;
        _currentStation = "Carrier";
        _processingState = "NotProcessed";
        _isCompleted = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
