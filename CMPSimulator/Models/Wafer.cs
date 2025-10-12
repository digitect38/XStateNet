using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace CMPSimulator.Models;

/// <summary>
/// Represents a wafer substrate with unique ID and color
/// </summary>
public class Wafer : INotifyPropertyChanged
{
    private double _x;
    private double _y;
    private string _currentStation;
    private bool _isCompleted;

    public int Id { get; init; }
    public Color Color { get; init; }
    public Brush Brush { get; init; }

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

    // Font color: Black before processing, White after completion
    public Brush TextColor => IsCompleted ? Brushes.White : Brushes.Black;

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
        Color = color;
        Brush = new SolidColorBrush(color);
        _currentStation = "LoadPort";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
