using System;
using System.Windows;
using System.Windows.Threading;

namespace TimelineWPF
{
    public partial class CircuitBreakerSimulatorWindow : Window
    {
        private readonly CircuitBreakerSimulatorViewModel _viewModel;
        private readonly DispatcherTimer _updateTimer;

        public CircuitBreakerSimulatorWindow()
        {
            InitializeComponent();
            _viewModel = new CircuitBreakerSimulatorViewModel();
            DataContext = _viewModel;

            // Setup timer for updating time-based metrics
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _updateTimer.Tick += (s, e) => _viewModel.UpdateTimeBasedMetrics();
            _updateTimer.Start();

            Closed += (s, e) =>
            {
                _updateTimer.Stop();
                _viewModel.Dispose();
            };
        }
    }
}