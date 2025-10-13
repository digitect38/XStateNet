using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CMPSimulator.Controls;

namespace CMPSimulator.Helpers;

/// <summary>
/// Event args for station geometry changes
/// </summary>
public class StationGeometryChangedEventArgs : EventArgs
{
    public string StationName { get; set; } = string.Empty;
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

/// <summary>
/// Provides drag-to-move and resize functionality for station controls
/// </summary>
public static class StationEditor
{
    public static double GridSize { get; set; } = CMPSystemControl.GridSize;
    private static bool _isDragging = false;
    private static Point _dragStartPoint;
    private static double _originalLeft;
    private static double _originalTop;
    private static StationControl? _currentStation;

    private static bool _isResizing = false;
    private static Point _resizeStartPoint;
    private static double _originalWidth;
    private static double _originalHeight;

    public static bool IsEditMode { get; set; } = false;

    /// <summary>
    /// Event raised when a station's geometry changes (position or size)
    /// </summary>
    public static event EventHandler<StationGeometryChangedEventArgs>? GeometryChanged;

    /// <summary>
    /// Enable edit mode for a station control
    /// </summary>
    public static void EnableEditMode(StationControl station)
    {
        if (!IsEditMode) return;

        station.MouseLeftButtonDown += Station_MouseLeftButtonDown;
        station.MouseMove += Station_MouseMove;
        station.MouseLeftButtonUp += Station_MouseLeftButtonUp;
        station.Cursor = Cursors.SizeAll;
    }

    /// <summary>
    /// Disable edit mode for a station control
    /// </summary>
    public static void DisableEditMode(StationControl station)
    {
        station.MouseLeftButtonDown -= Station_MouseLeftButtonDown;
        station.MouseMove -= Station_MouseMove;
        station.MouseLeftButtonUp -= Station_MouseLeftButtonUp;
        station.Cursor = Cursors.Arrow;
    }

    private static void Station_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEditMode) return;

        var station = sender as StationControl;
        if (station == null) return;

        _currentStation = station;
        _isDragging = true;
        _dragStartPoint = e.GetPosition(station.Parent as UIElement);
        _originalLeft = Canvas.GetLeft(station);
        _originalTop = Canvas.GetTop(station);

        if (double.IsNaN(_originalLeft)) _originalLeft = 0;
        if (double.IsNaN(_originalTop)) _originalTop = 0;

        station.CaptureMouse();
        e.Handled = true;
    }

    private static void Station_MouseMove(object sender, MouseEventArgs e)
    {
        if (!IsEditMode || !_isDragging || _currentStation == null) return;

        var station = _currentStation;
        var currentPosition = e.GetPosition(station.Parent as UIElement);
        var offset = currentPosition - _dragStartPoint;

        // Calculate new position
        double newLeft = _originalLeft + offset.X;
        double newTop = _originalTop + offset.Y;

        // Snap to grid
        double snappedLeft = Math.Round(newLeft / GridSize) * GridSize;
        double snappedTop = Math.Round(newTop / GridSize) * GridSize;

        Canvas.SetLeft(station, snappedLeft);
        Canvas.SetTop(station, snappedTop);

        e.Handled = true;
    }

    private static void Station_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsEditMode) return;

        var station = sender as StationControl;
        if (station == null) return;

        if (_isDragging)
        {
            _isDragging = false;
            station.ReleaseMouseCapture();

            // Raise geometry changed event
            var left = Canvas.GetLeft(station);
            var top = Canvas.GetTop(station);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            GeometryChanged?.Invoke(null, new StationGeometryChangedEventArgs
            {
                StationName = station.StationName,
                Left = left,
                Top = top,
                Width = station.Width,
                Height = station.Height
            });
        }

        _currentStation = null;
        e.Handled = true;
    }

    /// <summary>
    /// Add resize handles to a station control
    /// </summary>
    public static void AddResizeHandles(StationControl station, Panel container)
    {
        if (!IsEditMode) return;

        // Create resize handle (bottom-right corner)
        var resizeHandle = new Rectangle
        {
            Width = 10,
            Height = 10,
            Fill = Brushes.DarkGray,
            Cursor = Cursors.SizeNWSE,
            Tag = station
        };

        // Position the resize handle at bottom-right corner
        var left = Canvas.GetLeft(station);
        var top = Canvas.GetTop(station);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;

        Canvas.SetLeft(resizeHandle, left + station.Width - 5);
        Canvas.SetTop(resizeHandle, top + station.Height - 5);

        resizeHandle.MouseLeftButtonDown += ResizeHandle_MouseLeftButtonDown;
        resizeHandle.MouseMove += ResizeHandle_MouseMove;
        resizeHandle.MouseLeftButtonUp += ResizeHandle_MouseLeftButtonUp;

        container.Children.Add(resizeHandle);
    }

    private static void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEditMode) return;

        var handle = sender as Rectangle;
        if (handle?.Tag is StationControl station)
        {
            _currentStation = station;
            _isResizing = true;
            _resizeStartPoint = e.GetPosition(handle.Parent as UIElement);
            _originalWidth = station.Width;
            _originalHeight = station.Height;

            handle.CaptureMouse();
            e.Handled = true;
        }
    }

    private static void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!IsEditMode || !_isResizing || _currentStation == null) return;

        var handle = sender as Rectangle;
        var currentPosition = e.GetPosition(handle?.Parent as UIElement);
        var offset = currentPosition - _resizeStartPoint;

        var newWidth = Math.Max(50, _originalWidth + offset.X);
        var newHeight = Math.Max(50, _originalHeight + offset.Y);

        _currentStation.Width = newWidth;
        _currentStation.Height = newHeight;

        // Update handle position
        var left = Canvas.GetLeft(_currentStation);
        var top = Canvas.GetTop(_currentStation);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;

        Canvas.SetLeft(handle, left + newWidth - 5);
        Canvas.SetTop(handle, top + newHeight - 5);

        e.Handled = true;
    }

    private static void ResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsEditMode) return;

        var handle = sender as Rectangle;
        if (handle != null && _isResizing && _currentStation != null)
        {
            _isResizing = false;
            handle.ReleaseMouseCapture();

            // Raise geometry changed event
            var left = Canvas.GetLeft(_currentStation);
            var top = Canvas.GetTop(_currentStation);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            GeometryChanged?.Invoke(null, new StationGeometryChangedEventArgs
            {
                StationName = _currentStation.StationName,
                Left = left,
                Top = top,
                Width = _currentStation.Width,
                Height = _currentStation.Height
            });
        }

        _currentStation = null;
        e.Handled = true;
    }
}
