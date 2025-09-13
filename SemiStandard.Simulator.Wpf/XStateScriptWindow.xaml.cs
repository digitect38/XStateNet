using System;
using System.Windows;
using System.Windows.Input;

namespace SemiStandard.Simulator.Wpf
{
    public partial class XStateScriptWindow : Window
    {
        public XStateScriptWindow(string machineName, string scriptContent)
        {
            InitializeComponent();
            
            MachineNameText.Text = machineName;
            ScriptTextBox.Text = scriptContent;
            
            // Update line count
            var lines = scriptContent.Split('\n').Length;
            LineCountText.Text = $"Lines: {lines}";
            
            // Add syntax highlighting (simple approach)
            ApplySimpleSyntaxHighlighting();
        }
        
        private void ApplySimpleSyntaxHighlighting()
        {
            // For a more complete solution, you'd use AvalonEdit or similar
            // This is a simple approach that formats the text
            var script = ScriptTextBox.Text;
            
            // Add some basic formatting
            script = script.Replace("states:", "states:")
                          .Replace("on:", "on:")
                          .Replace("target:", "target:")
                          .Replace("actions:", "actions:")
                          .Replace("guard:", "guard:")
                          .Replace("initial:", "initial:");
            
            ScriptTextBox.Text = script;
        }
        
        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            // Retry clipboard operation to handle CLIPBRD_E_CANT_OPEN errors
            bool success = false;
            int retries = 10;
            
            for (int i = 0; i < retries; i++)
            {
                try
                {
                    Clipboard.SetText(ScriptTextBox.Text);
                    success = true;
                    break;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // Clipboard is locked, wait and retry
                    System.Threading.Thread.Sleep(50);
                }
            }
            
            if (success)
            {
                MessageBox.Show("Script copied to clipboard!", "Copy Successful", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Could not access clipboard. Please try again.", "Clipboard Error", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            
            // Close on Escape
            if (e.Key == Key.Escape)
            {
                Close();
            }
            // Copy on Ctrl+C
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                bool success = false;
                int retries = 10;

                for (int i = 0; i < retries; i++)
                {
                    try
                    {
                        Clipboard.SetText(ScriptTextBox.Text);
                        success = true;
                        break;
                    }
                    catch (System.Runtime.InteropServices.COMException)
                    {
                        System.Threading.Thread.Sleep(50); // Wait and retry
                    }
                }

                if (!success)
                {
                    MessageBox.Show("Could not access clipboard. Please try again.", "Clipboard Error",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }
}