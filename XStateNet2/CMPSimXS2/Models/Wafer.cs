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
        set
        {
            if (SetProperty(ref _journeyStage, value))
            {
                OnPropertyChanged(nameof(TextColor));
                OnPropertyChanged(nameof(StatusSymbol));
            }
        }
    }

    /// <summary>
    /// Font color based on 5-stage journey progression:
    /// - Black (⚫): Raw wafer (InCarrier, not started)
    /// - Blue (🔵): Being polished (ToPolisher, Polishing)
    /// - Green (🟢): Polished, ready for cleaning (ToCleaner)
    /// - Yellow (🟡): Being cleaned (Cleaning)
    /// - White (⚪): Cleaned and completed (ToBuffer, InBuffer, ToCarrier, returned to Carrier)
    /// </summary>
    public Brush TextColor
    {
        get
        {
            // 5-stage color progression based on journey stage
            return JourneyStage switch
            {
                // Stage 5: White - Cleaned wafer (ready to return or returned)
                "InCarrier" when ProcessingState == "Cleaned" => Brushes.White,
                "ToCarrier" or "InBuffer" or "ToBuffer" => Brushes.White,

                // Stage 4: Yellow - Being cleaned
                "Cleaning" => Brushes.Yellow,

                // Stage 3: Green - Polished (waiting for cleaning)
                "ToCleaner" => Brushes.LimeGreen,

                // Stage 2: Blue - Being polished or ready to polish
                "Polishing" or "ToPolisher" => Brushes.DodgerBlue,

                // Stage 1: Black - Raw wafer
                _ => Brushes.Black
            };
        }
    }

    /// <summary>
    /// Status symbol for visual indication of journey stage
    /// </summary>
    public string StatusSymbol
    {
        get
        {
            return JourneyStage switch
            {
                "InCarrier" when ProcessingState == "Cleaned" => "✓",  // Completed
                "ToCarrier" or "InBuffer" or "ToBuffer" => "→",        // Returning
                "Cleaning" => "◐",                                     // Being cleaned
                "ToCleaner" => "◑",                                    // Polished
                "Polishing" or "ToPolisher" => "○",                    // Being polished
                _ => ""                                                // Raw
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
