using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CMPSimXS2.Models;

namespace CMPSimXS2.Controls;

/// <summary>
/// Control to display 5x5 wafer grid (25 wafers) with color-coded status
/// Font colors: Black (not processed) → Yellow (polished) → White (cleaned)
/// </summary>
public partial class WaferGridControl : UserControl
{
    private const int GridRows = 5;
    private const int GridCols = 5;
    private const int MaxWafers = 25;
    private const double CanvasWidth = 120;
    private const double CanvasHeight = 120;
    private const double CellWidth = CanvasWidth / GridCols;   // 24px
    private const double CellHeight = CanvasHeight / GridRows;  // 24px
    private const double WaferSize = 22;

    public static readonly DependencyProperty WafersProperty =
        DependencyProperty.Register(nameof(Wafers), typeof(IEnumerable<Wafer>), typeof(WaferGridControl),
            new PropertyMetadata(null, OnWafersChanged));

    public IEnumerable<Wafer>? Wafers
    {
        get => (IEnumerable<Wafer>?)GetValue(WafersProperty);
        set => SetValue(WafersProperty, value);
    }

    private static void OnWafersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaferGridControl control)
        {
            control.UpdateWaferDisplay();
        }
    }

    public WaferGridControl()
    {
        InitializeComponent();
    }

    private void UpdateWaferDisplay()
    {
        WaferCanvas.Children.Clear();

        if (Wafers == null) return;

        var waferList = Wafers.ToList();
        int processedCount = 0;
        int remainingCount = 0;

        // Draw all 25 wafer slots (5x5 grid)
        for (int i = 0; i < MaxWafers; i++)
        {
            int row = i / GridCols;
            int col = i % GridCols;

            // Calculate position within the canvas
            double x = col * CellWidth + (CellWidth / 2) - (WaferSize / 2);
            double y = row * CellHeight + (CellHeight / 2) - (WaferSize / 2);

            // Find wafer with this ID (1-25)
            var wafer = waferList.FirstOrDefault(w => w.Id == i + 1);

            // Determine wafer color based on state
            Brush waferFill;
            if (wafer == null || wafer.CurrentStation != "Carrier")
            {
                // Empty slot (wafer has been moved or doesn't exist)
                waferFill = Brushes.LightGray;
            }
            else
            {
                // Wafer present in carrier
                remainingCount++;
                if (wafer.IsCompleted)
                {
                    processedCount++;
                    waferFill = new SolidColorBrush(Color.FromRgb(152, 251, 152)); // Pale green (completed)
                }
                else
                {
                    waferFill = new SolidColorBrush(Color.FromRgb(100, 149, 237)); // Cornflower blue (pending)
                }
            }

            // Create wafer visual
            var ellipse = new Ellipse
            {
                Width = WaferSize,
                Height = WaferSize,
                Fill = waferFill,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };

            ellipse.Effect = new DropShadowEffect
            {
                ShadowDepth = 1,
                BlurRadius = 3,
                Opacity = 0.5
            };

            Canvas.SetLeft(ellipse, x);
            Canvas.SetTop(ellipse, y);
            WaferCanvas.Children.Add(ellipse);

            // Add wafer ID text with color-coded font
            var textBlock = new TextBlock
            {
                Text = (i + 1).ToString(),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center
            };

            // Set font color based on processing state
            if (wafer != null && wafer.CurrentStation == "Carrier")
            {
                textBlock.Foreground = wafer.TextColor; // Black → Yellow → White
            }
            else
            {
                textBlock.Foreground = Brushes.Gray; // Empty slot
            }

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

            Canvas.SetLeft(textBlock, x + (WaferSize / 2) - (textWidth / 2));
            Canvas.SetTop(textBlock, y + (WaferSize / 2) - (textHeight / 2));
            WaferCanvas.Children.Add(textBlock);
        }

        // Update status text
        StatusTextBlock.Text = $"{processedCount} Processed / {remainingCount} Remaining";
    }
}
