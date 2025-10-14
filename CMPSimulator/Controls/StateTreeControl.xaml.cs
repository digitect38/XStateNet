using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CMPSimulator.Models;

namespace CMPSimulator.Controls;

public partial class StateTreeControl : UserControl
{
    public ObservableCollection<StateTreeNode> RootNodes { get; set; }

    public StateTreeControl()
    {
        InitializeComponent();

        RootNodes = new ObservableCollection<StateTreeNode>();
        StateTree.ItemsSource = RootNodes;
    }

    /// <summary>
    /// Update the state of a node (when showing states as children)
    /// </summary>
    public void UpdateNodeState(string nodeId, string newState, bool highlight = false)
    {
        foreach (var root in RootNodes)
        {
            var node = root.FindNode(nodeId);
            if (node != null)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    node.CurrentState = newState;
                    node.IsHighlighted = highlight;

                    // If this node has state children, update which one is active (recursively)
                    if (node.Children.Any() && node.Children.First().NodeType == "State")
                    {
                        UpdateActiveStateRecursive(node, newState);
                    }
                });
                return;
            }
        }
    }

    /// <summary>
    /// Recursively update active state highlighting for all nested state children
    /// </summary>
    private void UpdateActiveStateRecursive(StateTreeNode parentNode, string targetState)
    {
        foreach (var stateNode in parentNode.Children)
        {
            // Check if this state matches the target
            bool isMatch = (stateNode.DisplayName == targetState);
            stateNode.IsActive = isMatch;

            // If this node has state children, recursively check them
            if (stateNode.Children.Any() && stateNode.Children.First().NodeType == "State")
            {
                UpdateActiveStateRecursive(stateNode, targetState);

                // If any child is active, mark this parent as active too (for path highlighting)
                if (stateNode.Children.Any(c => c.IsActive))
                {
                    stateNode.IsActive = true;
                }
            }
        }
    }

    /// <summary>
    /// Clear highlight from all nodes
    /// </summary>
    public void ClearAllHighlights()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            foreach (var root in RootNodes)
            {
                ClearHighlightsRecursive(root);
            }
        });
    }

    private void ClearHighlightsRecursive(StateTreeNode node)
    {
        node.IsHighlighted = false;
        foreach (var child in node.Children)
        {
            ClearHighlightsRecursive(child);
        }
    }
}

/// <summary>
/// Converter for highlighting selected nodes with blue outline
/// </summary>
public class HighlightToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isHighlighted && isHighlighted)
        {
            return new Thickness(2); // 2px blue border when highlighted
        }
        return new Thickness(0); // No border when not highlighted
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter for node type icons
/// </summary>
public class NodeTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string nodeType)
        {
            return nodeType switch
            {
                "Carrier" => "📦",
                "Substrate" => "💿",
                "LoadPort" => "🚪",
                "Station" => "⚙️",
                "Robot" => "🤖",
                "Polisher" => "🔵",
                "Cleaner" => "💧",
                "Buffer" => "📊",
                _ => "•"
            };
        }
        return "•";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
