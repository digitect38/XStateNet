using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CMPSimXS2.WPF.Models;

namespace CMPSimXS2.WPF.Controls;

/// <summary>
/// Robot station visual control for robot stations (R1, R2, R3) and Buffer
/// Displays wafer at center when present
/// </summary>
public partial class RobotStationControl : StationControl
{
    private const double WaferSize = 20;

    public RobotStationControl()
    {
        InitializeComponent();
        BackgroundColor = new SolidColorBrush(Color.FromRgb(208, 232, 255)); // Light blue
    }

    public override void UpdateWafers(IEnumerable<Wafer> allWafers)
    {
        WaferCanvas.Children.Clear();

        // Find wafer at this station
        var wafer = allWafers.FirstOrDefault(w => w.CurrentStation == StationName);

        if (wafer != null)
        {
            // Create wafer visual at center
            var ellipse = new Ellipse
            {
                Width = WaferSize,
                Height = WaferSize,
                Fill = wafer.Brush,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };

            ellipse.Effect = new DropShadowEffect
            {
                ShadowDepth = 1,
                BlurRadius = 3,
                Opacity = 0.5
            };

            // Position at center (relative to canvas)
            Canvas.SetLeft(ellipse, -WaferSize / 2);
            Canvas.SetTop(ellipse, -WaferSize / 2);
            WaferCanvas.Children.Add(ellipse);

            // Add wafer ID text - positioned at exact center
            var textBlock = new TextBlock
            {
                Text = wafer.Id.ToString(),
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = wafer.TextColor, // Uses E90 state-based color: Black ??Yellow ??White
                TextAlignment = TextAlignment.Center
            };

            textBlock.Effect = new DropShadowEffect
            {
                ShadowDepth = 0,
                BlurRadius = 3,
                Color = Colors.Black
            };

            // Measure text to center it properly
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = textBlock.DesiredSize.Width;
            double textHeight = textBlock.DesiredSize.Height;

            Canvas.SetLeft(textBlock, -(textWidth / 2));
            Canvas.SetTop(textBlock, -(textHeight / 2));
            WaferCanvas.Children.Add(textBlock);
        }
    }
}
