using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CMPSimulator.Models;

namespace CMPSimulator.Controls
{
    public enum GridDisplayMode
    {
        Line,
        Dot,
        Both
    }

    public partial class CMPSystemControl : UserControl
    {
        public const double GridSize = 8; // Grid spacing in pixels

        private GridDisplayMode _gridDisplayMode = GridDisplayMode.Dot;
        public GridDisplayMode GridMode
        {
            get => _gridDisplayMode;
            set
            {
                _gridDisplayMode = value;
                DrawGrid(); // Redraw grid when mode changes
            }
        }
        public static readonly DependencyProperty TotalWaferCountProperty =
            DependencyProperty.Register(
                nameof(TotalWaferCount),
                typeof(int),
                typeof(CMPSystemControl),
                new PropertyMetadata(0, OnTotalWaferCountChanged));

        public static readonly DependencyProperty PreProcessCountProperty =
            DependencyProperty.Register(
                nameof(PreProcessCount),
                typeof(int),
                typeof(CMPSystemControl),
                new PropertyMetadata(0, OnPreProcessCountChanged));

        public static readonly DependencyProperty PostProcessCountProperty =
            DependencyProperty.Register(
                nameof(PostProcessCount),
                typeof(int),
                typeof(CMPSystemControl),
                new PropertyMetadata(0, OnPostProcessCountChanged));

        public int TotalWaferCount
        {
            get => (int)GetValue(TotalWaferCountProperty);
            set => SetValue(TotalWaferCountProperty, value);
        }

        public int PreProcessCount
        {
            get => (int)GetValue(PreProcessCountProperty);
            set => SetValue(PreProcessCountProperty, value);
        }

        public int PostProcessCount
        {
            get => (int)GetValue(PostProcessCountProperty);
            set => SetValue(PostProcessCountProperty, value);
        }

        private static void OnTotalWaferCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CMPSystemControl control)
            {
                control.TotalWaferCountText.Text = e.NewValue.ToString();
            }
        }

        private static void OnPreProcessCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CMPSystemControl control)
            {
                control.PreProcessCountText.Text = e.NewValue.ToString();
            }
        }

        private static void OnPostProcessCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CMPSystemControl control)
            {
                control.PostProcessCountText.Text = e.NewValue.ToString();
            }
        }

        public CMPSystemControl()
        {
            InitializeComponent();

            // Subscribe to size changes to handle variable sizing
            this.SizeChanged += OnSizeChanged;

            // Subscribe to Loaded event to draw grid after control is loaded
            this.Loaded += (s, e) => DrawGrid();

            // Add design-time visual indicators
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                DrawDesignTimeStations();
            }
        }

        private void DrawDesignTimeStations()
        {
            // Load settings for design-time geometry
            var settings = Helpers.SettingsManager.LoadSettings();
            var converter = new System.Windows.Media.BrushConverter();

            // LoadPort (using geometry from settings)
            var loadPort = new LoadPortControl
            {
                StationName = "LoadPort",
                StatusText = "0/25",
                BackgroundColor = System.Windows.Media.Brushes.White,
                Width = settings.LoadPort.Width,
                Height = settings.LoadPort.Height
            };
            AddStation(loadPort, settings.LoadPort.Left, settings.LoadPort.Top);

            // R1 (using geometry from settings)
            var r1 = new RobotStationControl
            {
                StationName = "R1",
                StatusText = "Empty",
                BackgroundColor = (System.Windows.Media.Brush)converter.ConvertFromString("#D0E8FF")!,
                Width = settings.R1.Width,
                Height = settings.R1.Height
            };
            AddStation(r1, settings.R1.Left, settings.R1.Top);

            // Polisher (using geometry from settings)
            var polisher = new ProcessStationControl
            {
                StationName = "Polisher",
                StatusText = "Idle",
                BackgroundColor = (System.Windows.Media.Brush)converter.ConvertFromString("#FFFACD")!,
                Width = settings.Polisher.Width,
                Height = settings.Polisher.Height
            };
            AddStation(polisher, settings.Polisher.Left, settings.Polisher.Top);

            // R2 (using geometry from settings)
            var r2 = new RobotStationControl
            {
                StationName = "R2",
                StatusText = "Empty",
                BackgroundColor = (System.Windows.Media.Brush)converter.ConvertFromString("#D0E8FF")!,
                Width = settings.R2.Width,
                Height = settings.R2.Height
            };
            AddStation(r2, settings.R2.Left, settings.R2.Top);

            // Cleaner (using geometry from settings)
            var cleaner = new ProcessStationControl
            {
                StationName = "Cleaner",
                StatusText = "Idle",
                BackgroundColor = (System.Windows.Media.Brush)converter.ConvertFromString("#E0FFFF")!,
                Width = settings.Cleaner.Width,
                Height = settings.Cleaner.Height
            };
            AddStation(cleaner, settings.Cleaner.Left, settings.Cleaner.Top);

            // R3 (using geometry from settings)
            var r3 = new RobotStationControl
            {
                StationName = "R3",
                StatusText = "Empty",
                BackgroundColor = (System.Windows.Media.Brush)converter.ConvertFromString("#FFD0E8")!,
                Width = settings.R3.Width,
                Height = settings.R3.Height
            };
            AddStation(r3, settings.R3.Left, settings.R3.Top);

            // Buffer (using geometry from settings)
            var buffer = new RobotStationControl
            {
                StationName = "Buffer",
                StatusText = "Empty",
                BackgroundColor = (System.Windows.Media.Brush)converter.ConvertFromString("#FFE0B2")!,
                Width = settings.Buffer.Width,
                Height = settings.Buffer.Height
            };
            AddStation(buffer, settings.Buffer.Left, settings.Buffer.Top);

            // Enable edit mode for design-time stations
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                Helpers.StationEditor.IsEditMode = true;
                Helpers.StationEditor.EnableEditMode(loadPort);
                Helpers.StationEditor.EnableEditMode(r1);
                Helpers.StationEditor.EnableEditMode(polisher);
                Helpers.StationEditor.EnableEditMode(r2);
                Helpers.StationEditor.EnableEditMode(cleaner);
                Helpers.StationEditor.EnableEditMode(r3);
                Helpers.StationEditor.EnableEditMode(buffer);
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Ensure StationCanvas matches the size of the control
            StationCanvas.Width = e.NewSize.Width;
            StationCanvas.Height = e.NewSize.Height;

            // Redraw grid when size changes
            DrawGrid();
        }

        /// <summary>
        /// Draw grid lines and dots at intersections based on display mode
        /// Uses optimized rendering for better performance
        /// </summary>
        private void DrawGrid()
        {
            try
            {
                GridCanvas.Children.Clear();

                double width = this.ActualWidth > 0 ? this.ActualWidth : this.Width;
                double height = this.ActualHeight > 0 ? this.ActualHeight : this.Height;

                // Limit to UHD resolution to prevent performance issues
                width = Math.Min(width, 1920);
                height = Math.Min(height, 1080);

                if (double.IsNaN(width) || double.IsNaN(height) || width <= 0 || height <= 0)
                {
                    return;
                }

                // Use DrawingVisual for better performance with many shapes
                var drawingVisual = new DrawingVisual();

                // Disable anti-aliasing for sharper dots
                RenderOptions.SetEdgeMode(drawingVisual, EdgeMode.Aliased);

                using (var dc = drawingVisual.RenderOpen())
                {
                    var linePen = new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), 0.25);
                    linePen.Freeze(); // Freeze for better performance

                    var dotBrush = new SolidColorBrush(Colors.Black);
                    dotBrush.Freeze(); // Freeze for better performance

                    // Draw lines if mode is Line or Both
                    if (_gridDisplayMode == GridDisplayMode.Line || _gridDisplayMode == GridDisplayMode.Both)
                    {
                        // Draw vertical lines
                        for (double x = 0; x <= width; x += GridSize)
                        {
                            dc.DrawLine(linePen, new Point(x, 0), new Point(x, height));
                        }

                        // Draw horizontal lines
                        for (double y = 0; y <= height; y += GridSize)
                        {
                            dc.DrawLine(linePen, new Point(0, y), new Point(width, y));
                        }
                    }

                    // Draw dots if mode is Dot or Both
                    if (_gridDisplayMode == GridDisplayMode.Dot || _gridDisplayMode == GridDisplayMode.Both)
                    {
                        const double dotSize = 1.0; // 1 pixel dot

                        for (double x = 0; x <= width; x += GridSize)
                        {
                            for (double y = 0; y <= height; y += GridSize)
                            {
                                // Draw 1x1 pixel rectangle for sharp black dot
                                dc.DrawRectangle(dotBrush, null, new Rect(x, y, dotSize, dotSize));
                            }
                        }
                    }
                }

                // Convert DrawingVisual to Image and add to canvas
                var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(drawingVisual);
                bitmap.Freeze(); // Freeze for better performance

                var image = new System.Windows.Controls.Image
                {
                    Source = bitmap,
                    Width = width,
                    Height = height
                };

                GridCanvas.Children.Add(image);
            }
            catch (Exception ex)
            {
                // Log error but don't crash the application
                System.Diagnostics.Debug.WriteLine($"Error drawing grid: {ex.Message}");
            }
        }

        /// <summary>
        /// Add a station control to the system at the specified position
        /// </summary>
        public void AddStation(UIElement station, double left, double top)
        {
            Canvas.SetLeft(station, left);
            Canvas.SetTop(station, top);
            StationCanvas.Children.Add(station);
        }

        /// <summary>
        /// Remove a station control from the system
        /// </summary>
        public void RemoveStation(UIElement station)
        {
            StationCanvas.Children.Remove(station);
        }

        /// <summary>
        /// Clear all stations from the system
        /// </summary>
        public void ClearStations()
        {
            StationCanvas.Children.Clear();
        }

        /// <summary>
        /// Calculate total wafer count from all LoadPort stations and update Pre/Post counts
        /// </summary>
        public void UpdateTotalWaferCount(IEnumerable<Wafer>? wafers = null)
        {
            int totalCount = 0;
            int preProcessCount = 0;
            int postProcessCount = 0;

            // Find all LoadPortControl instances in the canvas
            foreach (var child in StationCanvas.Children)
            {
                if (child is LoadPortControl loadPort)
                {
                    // Parse the status text which is in format "X/Y" where X is current, Y is total
                    var statusText = loadPort.StatusText;
                    if (!string.IsNullOrEmpty(statusText))
                    {
                        var parts = statusText.Split('/');
                        if (parts.Length == 2 && int.TryParse(parts[1], out int capacity))
                        {
                            totalCount += capacity;
                        }
                    }
                }
            }

            TotalWaferCount = totalCount;

            // Calculate Pre/Post counts if wafers are provided
            if (wafers != null)
            {
                foreach (var wafer in wafers)
                {
                    if (wafer.IsCompleted)
                    {
                        postProcessCount++;
                    }
                    else
                    {
                        preProcessCount++;
                    }
                }
            }

            PreProcessCount = preProcessCount;
            PostProcessCount = postProcessCount;
        }

        /// <summary>
        /// Get all stations in the system
        /// </summary>
        public IEnumerable<UIElement> GetStations()
        {
            return StationCanvas.Children.Cast<UIElement>();
        }

        /// <summary>
        /// Get all LoadPort stations in the system
        /// </summary>
        public IEnumerable<LoadPortControl> GetLoadPorts()
        {
            return StationCanvas.Children.OfType<LoadPortControl>();
        }

        /// <summary>
        /// Access the internal canvas for advanced operations
        /// </summary>
        public Canvas Canvas => StationCanvas;
    }
}
