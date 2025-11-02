using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CMPSimXS2.WPF.Models;

namespace CMPSimXS2.WPF.Controls;

/// <summary>
/// Base class for all station visual controls
/// </summary>
public abstract class StationControl : UserControl
{
    public static readonly DependencyProperty StationNameProperty =
        DependencyProperty.Register(nameof(StationName), typeof(string), typeof(StationControl),
            new PropertyMetadata(string.Empty, OnStationNameChanged));

    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(StationControl),
            new PropertyMetadata("Empty", OnStatusTextChanged));

    public static readonly DependencyProperty BackgroundColorProperty =
        DependencyProperty.Register(nameof(BackgroundColor), typeof(Brush), typeof(StationControl),
            new PropertyMetadata(Brushes.White));

    public string StationName
    {
        get => (string)GetValue(StationNameProperty);
        set => SetValue(StationNameProperty, value);
    }

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public Brush BackgroundColor
    {
        get => (Brush)GetValue(BackgroundColorProperty);
        set => SetValue(BackgroundColorProperty, value);
    }

    /// <summary>
    /// Forward connections - stations moving away from LoadPort
    /// </summary>
    public ObservableCollection<StationControl> NextForward { get; } = new ObservableCollection<StationControl>();

    /// <summary>
    /// Backward connections - stations moving towards LoadPort
    /// </summary>
    public ObservableCollection<StationControl> NextBackward { get; } = new ObservableCollection<StationControl>();

    /// <summary>
    /// Routing strategy for Forward direction when multiple Next stations exist
    /// </summary>
    public StationRoutingStrategy ForwardRoutingStrategy { get; set; } = StationRoutingStrategy.FirstEmpty;

    /// <summary>
    /// Routing strategy for Backward direction when multiple Next stations exist
    /// </summary>
    public StationRoutingStrategy BackwardRoutingStrategy { get; set; } = StationRoutingStrategy.FirstEmpty;

    /// <summary>
    /// Round-robin index for Forward direction
    /// </summary>
    private int _forwardRoundRobinIndex = 0;

    /// <summary>
    /// Round-robin index for Backward direction
    /// </summary>
    private int _backwardRoundRobinIndex = 0;

    /// <summary>
    /// Get next station based on Forward routing strategy
    /// </summary>
    /// <returns>Next station in forward direction, or null if none available</returns>
    public StationControl? GetNextForwardStation()
    {
        if (NextForward.Count == 0)
            return null;

        if (NextForward.Count == 1)
            return NextForward[0];

        return ForwardRoutingStrategy switch
        {
            StationRoutingStrategy.RoundRobin => GetRoundRobinStation(NextForward, ref _forwardRoundRobinIndex),
            StationRoutingStrategy.FirstEmpty => GetFirstEmptyStation(NextForward),
            _ => NextForward[0]
        };
    }

    /// <summary>
    /// Get next station based on Backward routing strategy
    /// </summary>
    /// <returns>Next station in backward direction, or null if none available</returns>
    public StationControl? GetNextBackwardStation()
    {
        if (NextBackward.Count == 0)
            return null;

        if (NextBackward.Count == 1)
            return NextBackward[0];

        return BackwardRoutingStrategy switch
        {
            StationRoutingStrategy.RoundRobin => GetRoundRobinStation(NextBackward, ref _backwardRoundRobinIndex),
            StationRoutingStrategy.FirstEmpty => GetFirstEmptyStation(NextBackward),
            _ => NextBackward[0]
        };
    }

    private StationControl GetRoundRobinStation(ObservableCollection<StationControl> stations, ref int index)
    {
        var station = stations[index];
        index = (index + 1) % stations.Count;
        return station;
    }

    private StationControl? GetFirstEmptyStation(ObservableCollection<StationControl> stations)
    {
        // Find first station that is marked as "Empty" or "Idle"
        foreach (var station in stations)
        {
            if (station.StatusText.Contains("Empty") || station.StatusText.Contains("Idle"))
            {
                return station;
            }
        }

        // If no empty station found, return the first one
        return stations[0];
    }

    protected static void OnStationNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StationControl control)
        {
            control.OnStationNameChanged();
        }
    }

    protected static void OnStatusTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StationControl control)
        {
            control.OnStatusTextChanged();
        }
    }

    protected virtual void OnStationNameChanged() { }
    protected virtual void OnStatusTextChanged() { }

    /// <summary>
    /// Update wafer visualization based on current wafers
    /// </summary>
    public abstract void UpdateWafers(IEnumerable<Wafer> allWafers);
}
