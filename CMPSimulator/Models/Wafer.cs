using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using CMPSimulator.StateMachines;

namespace CMPSimulator.Models;

/// <summary>
/// Represents a wafer substrate with unique ID and color
/// Integrated with SEMI E90 substrate tracking state machine
/// </summary>
public class Wafer : INotifyPropertyChanged
{
    private double _x;
    private double _y;
    private string _currentStation;
    private bool _isCompleted;
    private string _e90State;
    private Color _color;
    private Brush _brush;

    public int Id { get; init; }

    public Color Color
    {
        get => _color;
        set
        {
            if (_color != value)
            {
                _color = value;
                _brush = new SolidColorBrush(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(Brush));
            }
        }
    }

    public Brush Brush
    {
        get => _brush;
        set
        {
            if (_brush != value)
            {
                _brush = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// E90 substrate tracking state machine for this wafer
    /// </summary>
    public WaferMachine? StateMachine { get; set; }

    /// <summary>
    /// Current E90 substrate state (WaitingForHost, InCarrier, NeedsProcessing, etc.)
    /// </summary>
    public string E90State
    {
        get => _e90State;
        set
        {
            if (_e90State != value)
            {
                _e90State = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TextColor)); // Update font color when E90 state changes
            }
        }
    }

    /// <summary>
    /// Origin LoadPort where this wafer came from (e.g., "LoadPort" or "LoadPort2")
    /// Used to return wafer to correct FOUP after processing
    /// </summary>
    public string OriginLoadPort { get; set; } = "LoadPort";

    /// <summary>
    /// Carrier ID this wafer belongs to (e.g., "CARRIER_001", "CARRIER_002")
    /// Used to track which carrier this wafer came from for state tree updates
    /// </summary>
    public string CarrierId { get; set; } = "CARRIER_001";

    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (_isCompleted != value)
            {
                _isCompleted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TextColor));
            }
        }
    }

    /// <summary>
    /// Font color based on E90 processing state:
    /// - Black: Not processed (WaitingForHost, InCarrier, NeedsProcessing, Aligning, ReadyToProcess, InProcess.Polishing sub-states)
    /// - Yellow: Polished (after Polishing complete, during Cleaning)
    /// - White: Cleaned (after Cleaning complete, Processed, Complete)
    /// </summary>
    public Brush TextColor
    {
        get
        {
            // Check if wafer has completed cleaning (white)
            if (E90State == "Processed" || E90State == "Complete")
            {
                return Brushes.White;
            }
            // Check if wafer has completed polishing but not cleaning yet (yellow)
            else if (E90State == "Cleaning")
            {
                return Brushes.Yellow;
            }
            // Wafer has not completed polishing yet (black)
            else
            {
                return Brushes.Black;
            }
        }
    }

    public double X
    {
        get => _x;
        set
        {
            if (_x != value)
            {
                _x = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TargetX));
            }
        }
    }

    public double Y
    {
        get => _y;
        set
        {
            if (_y != value)
            {
                _y = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TargetY));
            }
        }
    }

    // Target properties for animation binding
    public double TargetX => _x;
    public double TargetY => _y;

    public string CurrentStation
    {
        get => _currentStation;
        set
        {
            if (_currentStation != value)
            {
                _currentStation = value;
                OnPropertyChanged();
            }
        }
    }

    public Wafer(int id, Color color)
    {
        Id = id;
        _color = color;
        _brush = new SolidColorBrush(color);
        _currentStation = "LoadPort";
        _e90State = "WaitingForHost";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
