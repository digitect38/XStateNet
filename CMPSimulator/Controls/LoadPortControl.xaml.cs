using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CMPSimulator.Models;

namespace CMPSimulator.Controls;

/// <summary>
/// LoadPort visual control with 5x5 wafer grid (25 wafers max)
/// </summary>
public partial class LoadPortControl : StationControl
{
    private const int GridRows = 5;
    private const int GridCols = 5;
    private const int MaxWafers = 25;
    private const double CanvasWidth = 108;  // Canvas width in XAML (5*20 + 2*4)
    private const double CanvasHeight = 108; // Canvas height in XAML (5*20 + 2*4)
    private const double CellWidth = CanvasWidth / GridCols;   // 21.6 (densely packed)
    private const double CellHeight = CanvasHeight / GridRows;  // 21.6 (densely packed)
    private const double WaferSize = 20;

    public LoadPortControl()
    {
        InitializeComponent();
        BackgroundColor = Brushes.White;

        // Add design-time wafer display
        if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
        {
            DrawDesignTimeWafers();
        }
    }

    private void DrawDesignTimeWafers()
    {
        // Draw 5x5 grid of gray wafers for design-time visualization
        var grayBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180));

        for (int row = 0; row < GridRows; row++)
        {
            for (int col = 0; col < GridCols; col++)
            {
                double x = col * CellWidth + (CellWidth / 2) - (WaferSize / 2);
                double y = row * CellHeight + (CellHeight / 2) - (WaferSize / 2);

                var ellipse = new Ellipse
                {
                    Width = WaferSize,
                    Height = WaferSize,
                    Fill = grayBrush,
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

                // Add wafer number
                int waferNum = row * GridCols + col + 1;
                var textBlock = new TextBlock
                {
                    Text = waferNum.ToString(),
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black,
                    TextAlignment = TextAlignment.Center
                };

                textBlock.Effect = new DropShadowEffect
                {
                    ShadowDepth = 0,
                    BlurRadius = 3,
                    Color = Colors.Black
                };

                textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double textWidth = textBlock.DesiredSize.Width;
                double textHeight = textBlock.DesiredSize.Height;

                Canvas.SetLeft(textBlock, x + (WaferSize / 2) - (textWidth / 2));
                Canvas.SetTop(textBlock, y + (WaferSize / 2) - (textHeight / 2));
                WaferCanvas.Children.Add(textBlock);
            }
        }
    }

    /// <summary>
    /// Carrier information for drawing carrier boxes
    /// </summary>
    public Carrier? CurrentCarrier { get; set; }

    public override void UpdateWafers(IEnumerable<Wafer> allWafers)
    {
        WaferCanvas.Children.Clear();

        // Filter wafers that belong to this LoadPort
        var loadPortWafers = allWafers.Where(w => w.CurrentStation == "LoadPort").ToList();

        // Draw carrier box if carrier is present
        if (CurrentCarrier != null && CurrentCarrier.Wafers.Count > 0)
        {
            DrawCarrierBox(CurrentCarrier);
        }

        foreach (var wafer in loadPortWafers)
        {
            // Calculate grid position from original slot
            // Assuming wafer.Id corresponds to slot index (1-25 -> 0-24)
            int slotIndex = wafer.Id - 1;
            if (slotIndex < 0 || slotIndex >= MaxWafers)
                continue;

            int row = slotIndex / GridCols;
            int col = slotIndex % GridCols;

            // Calculate position within the canvas (center of each cell)
            double x = col * CellWidth + (CellWidth / 2) - (WaferSize / 2);
            double y = row * CellHeight + (CellHeight / 2) - (WaferSize / 2);

            // Create wafer visual
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

            Canvas.SetLeft(ellipse, x);
            Canvas.SetTop(ellipse, y);
            WaferCanvas.Children.Add(ellipse);

            // Add wafer ID text - positioned at exact center
            var textBlock = new TextBlock
            {
                Text = wafer.Id.ToString(),
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = wafer.TextColor, // Uses E90 state-based color: Black → Yellow → White
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

            Canvas.SetLeft(textBlock, x + (WaferSize / 2) - (textWidth / 2));
            Canvas.SetTop(textBlock, y + (WaferSize / 2) - (textHeight / 2));
            WaferCanvas.Children.Add(textBlock);
        }

        // Update status text (Pending/Completed count)
        int pendingCount = loadPortWafers.Count(w => !w.IsCompleted);
        int completedCount = loadPortWafers.Count(w => w.IsCompleted);
        StatusText = $"{pendingCount}/{completedCount}";
    }

    /// <summary>
    /// Draw a rounded rectangle box around wafers belonging to a carrier
    /// </summary>
    private void DrawCarrierBox(Carrier carrier)
    {
        var waferIds = carrier.Wafers.Select(w => w.Id).ToList();
        if (waferIds.Count == 0) return;

        // Calculate bounding box for all wafers in this carrier
        int minRow = int.MaxValue, maxRow = int.MinValue;
        int minCol = int.MaxValue, maxCol = int.MinValue;

        foreach (var waferId in waferIds)
        {
            int slotIndex = waferId - 1;
            if (slotIndex < 0 || slotIndex >= MaxWafers) continue;

            int row = slotIndex / GridCols;
            int col = slotIndex % GridCols;

            minRow = Math.Min(minRow, row);
            maxRow = Math.Max(maxRow, row);
            minCol = Math.Min(minCol, col);
            maxCol = Math.Max(maxCol, col);
        }

        // Calculate box position and size with padding
        const double innerPadding = 2;
        const double extraPadding = 4; // Additional 4pt padding in all directions
        double boxX = minCol * CellWidth + innerPadding - extraPadding;
        double boxY = minRow * CellHeight + innerPadding - extraPadding;
        double boxWidth = (maxCol - minCol + 1) * CellWidth - (innerPadding * 2) + (extraPadding * 2);
        double boxHeight = (maxRow - minRow + 1) * CellHeight - (innerPadding * 2) + (extraPadding * 2);

        // Create rounded rectangle for carrier box
        var carrierBox = new System.Windows.Shapes.Rectangle
        {
            Width = boxWidth,
            Height = boxHeight,
            Stroke = new SolidColorBrush(Color.FromRgb(0, 122, 204)), // Blue outline
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            RadiusX = 8,
            RadiusY = 8,
            StrokeDashArray = new DoubleCollection { 4, 2 } // Dashed line
        };

        carrierBox.Effect = new DropShadowEffect
        {
            ShadowDepth = 0,
            BlurRadius = 8,
            Opacity = 0.4,
            Color = Color.FromRgb(0, 122, 204)
        };

        Canvas.SetLeft(carrierBox, boxX);
        Canvas.SetTop(carrierBox, boxY);
        Canvas.SetZIndex(carrierBox, -1); // Put box behind wafers
        WaferCanvas.Children.Insert(0, carrierBox);

        // Add carrier label
        var labelText = new TextBlock
        {
            Text = carrier.Id,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
            Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255))
        };

        Canvas.SetLeft(labelText, boxX + 4);
        Canvas.SetTop(labelText, boxY - 12);
        Canvas.SetZIndex(labelText, 10); // Put label on top
        WaferCanvas.Children.Add(labelText);
    }
}
