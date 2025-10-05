using MaterialDesignThemes.Wpf;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SemiStandard.Simulator.Wpf
{
    public partial class SimulatorSelectionWindow : Window
    {
        public string SelectedSimulator { get; private set; } = "";

        public SimulatorSelectionWindow()
        {
            Logger.Log("[SELECTION] SimulatorSelectionWindow constructor");
            InitializeComponent();
            Logger.Log("[SELECTION] SimulatorSelectionWindow initialized");
        }

        private void Card_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Card card)
            {
                card.Background = new SolidColorBrush(Color.FromArgb(20, 0, 150, 200));
            }
        }

        private void Card_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Card card)
            {
                card.Background = Brushes.Transparent;
            }
        }

        private void XStateCard_Click(object sender, MouseButtonEventArgs e)
        {
            Logger.Log("[SELECTION] XState card clicked");
            SelectedSimulator = "XState";
            DialogResult = true;
            Close();
        }

        private void RealisticCard_Click(object sender, MouseButtonEventArgs e)
        {
            Logger.Log("[SELECTION] Realistic card clicked");
            SelectedSimulator = "Realistic";
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log("[SELECTION] Cancel button clicked");
            DialogResult = false;
            Close();
        }
    }
}