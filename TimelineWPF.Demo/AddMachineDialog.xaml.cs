using System.Windows;

namespace TimelineWPF.Demo
{
    public partial class AddMachineDialog : Window
    {
        public string MachineName { get; private set; } = string.Empty;
        public string States { get; private set; } = string.Empty;

        public AddMachineDialog()
        {
            InitializeComponent();
            MachineNameText.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(MachineNameText.Text))
            {
                MessageBox.Show("Please enter a machine name.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(StatesText.Text))
            {
                MessageBox.Show("Please enter at least one state.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MachineName = MachineNameText.Text.Trim();
            States = StatesText.Text.Trim();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}