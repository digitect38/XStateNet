using System.Windows;
using System.Windows.Media;

namespace CMPSimXS2.ViewModels;

/// <summary>
/// Abstract base class for visual objects in the simulation.
/// Represents a visual object composed of a Box (border/background) and a Color Circle inside.
/// </summary>
public abstract class VisualObject : ViewModelBase
{
    // Position properties
    private double _x;
    private double _y;

    // Size properties
    private double _width = 150;
    private double _height = 200;

    // Visual composition: Box + Circle
    private SolidColorBrush _boxBrush = new(Colors.White);
    private SolidColorBrush _circleBrush = new(Colors.Gray);
    private double _circleSize = 40;
    private Thickness _boxBorderThickness = new(2);
    private SolidColorBrush _boxBorderBrush = new(Color.FromRgb(51, 51, 51)); // #333

    // Identification
    private string _name = string.Empty;

    /// <summary>
    /// X coordinate position on canvas
    /// </summary>
    public double X
    {
        get => _x;
        set => SetProperty(ref _x, value);
    }

    /// <summary>
    /// Y coordinate position on canvas
    /// </summary>
    public double Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }

    /// <summary>
    /// Width of the visual object box
    /// </summary>
    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, value);
    }

    /// <summary>
    /// Height of the visual object box
    /// </summary>
    public double Height
    {
        get => _height;
        set => SetProperty(ref _height, value);
    }

    /// <summary>
    /// Name/Label of the visual object
    /// </summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>
    /// Background brush for the outer box
    /// </summary>
    public SolidColorBrush BoxBrush
    {
        get => _boxBrush;
        set => SetProperty(ref _boxBrush, value);
    }

    /// <summary>
    /// Fill brush for the status indicator circle inside the box
    /// </summary>
    public SolidColorBrush CircleBrush
    {
        get => _circleBrush;
        set => SetProperty(ref _circleBrush, value);
    }

    /// <summary>
    /// Size (diameter) of the status indicator circle
    /// </summary>
    public double CircleSize
    {
        get => _circleSize;
        set => SetProperty(ref _circleSize, value);
    }

    /// <summary>
    /// Border thickness of the outer box
    /// </summary>
    public Thickness BoxBorderThickness
    {
        get => _boxBorderThickness;
        set => SetProperty(ref _boxBorderThickness, value);
    }

    /// <summary>
    /// Border brush color of the outer box
    /// </summary>
    public SolidColorBrush BoxBorderBrush
    {
        get => _boxBorderBrush;
        set => SetProperty(ref _boxBorderBrush, value);
    }

    protected VisualObject(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Abstract method to update the visual state (box and circle colors)
    /// Must be implemented by derived classes based on their state logic
    /// </summary>
    protected abstract void UpdateVisualState();
}
