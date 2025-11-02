using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace CMPSimXS2.WPF.Models;

/// <summary>
/// Represents a node in the hierarchical state tree
/// </summary>
public class StateTreeNode : INotifyPropertyChanged
{
    private string _currentState;
    private bool _isHighlighted;
    private bool _isActive;
    private Brush _stateBrush;

    /// <summary>
    /// Node identifier (e.g., "CARRIER_001", "E90_SUBSTRATE_1", "Polisher")
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Display name shown in tree
    /// </summary>
    public string DisplayName { get; set; }

    /// <summary>
    /// Node type (Carrier, Substrate, Station, Robot, LoadPort, State)
    /// </summary>
    public string NodeType { get; set; }

    /// <summary>
    /// Whether this is a state node representing the active state
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
                UpdateStateBrush();
            }
        }
    }

    /// <summary>
    /// Current state of this component
    /// </summary>
    public string CurrentState
    {
        get => _currentState;
        set
        {
            if (_currentState != value)
            {
                _currentState = value;
                OnPropertyChanged();
                UpdateStateBrush();
            }
        }
    }

    /// <summary>
    /// Whether this node is currently highlighted (active/processing)
    /// </summary>
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (_isHighlighted != value)
            {
                _isHighlighted = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Brush color for state display (based on state type)
    /// </summary>
    public Brush StateBrush
    {
        get => _stateBrush;
        set
        {
            if (_stateBrush != value)
            {
                _stateBrush = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Child nodes
    /// </summary>
    public ObservableCollection<StateTreeNode> Children { get; set; }

    /// <summary>
    /// Whether this node is expanded in the tree
    /// </summary>
    public bool IsExpanded { get; set; }

    public StateTreeNode(string id, string displayName, string nodeType, string currentState = "Unknown")
    {
        Id = id;
        DisplayName = displayName;
        NodeType = nodeType;
        _currentState = currentState;
        _isHighlighted = false;
        _isActive = false;
        _stateBrush = Brushes.Gray;
        Children = new ObservableCollection<StateTreeNode>();
        IsExpanded = true;

        UpdateStateBrush();
    }

    /// <summary>
    /// Update the state brush based on current state and active status
    /// </summary>
    private void UpdateStateBrush()
    {
        // If this is an active state node, use bright green
        if (NodeType == "State" && IsActive)
        {
            StateBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Bright green for active
            return;
        }

        // If this is an inactive state node, use gray
        if (NodeType == "State" && !IsActive)
        {
            StateBrush = new SolidColorBrush(Color.FromRgb(156, 163, 175)); // Gray for inactive
            return;
        }

        // Color coding based on state for machine nodes
        StateBrush = CurrentState switch
        {
            // E87 Carrier states & E90 Substrate states (shared colors for common states)
            "NotPresent" => Brushes.Gray,
            "WaitingForHost" => Brushes.Orange,  // Used by both E87 Carriers and E90 Substrates
            "Mapping" => new SolidColorBrush(Color.FromRgb(100, 149, 237)),  // Cornflower blue - no yellow
            "MappingVerification" => new SolidColorBrush(Color.FromRgb(100, 149, 237)),  // Cornflower blue - no yellow
            "ReadyToAccess" => Brushes.LightGreen,
            "InAccess" => Brushes.Green,
            "Complete" => new SolidColorBrush(Color.FromRgb(0, 100, 0)),  // Dark green - used by both
            "CarrierOut" => Brushes.DarkGray,

            // E87 LoadPort states
            "Empty" => Brushes.LightGray,
            "Reserved" => Brushes.Orange,
            "Loading" => new SolidColorBrush(Color.FromRgb(135, 206, 250)),  // Light sky blue - no yellow
            "Loaded" => Brushes.LightGreen,
            "Ready" => Brushes.Green,
            "Unloading" => new SolidColorBrush(Color.FromRgb(135, 206, 250)),  // Light sky blue - no yellow
            "Error" => Brushes.Red,

            // E90 Substrate-specific states (not shared with E87)
            "InCarrier" => new SolidColorBrush(Color.FromRgb(173, 216, 230)),        // Light blue - in FOUP
            "NeedsProcessing" => new SolidColorBrush(Color.FromRgb(255, 165, 0)),    // Orange - ready for processing
            "Aligning" => new SolidColorBrush(Color.FromRgb(100, 149, 237)),         // Cornflower blue - no yellow
            "ReadyToProcess" => new SolidColorBrush(Color.FromRgb(144, 238, 144)),   // Light green - ready
            "InProcess" => new SolidColorBrush(Color.FromRgb(50, 205, 50)),          // Lime - actively processing
            "Processed" => new SolidColorBrush(Color.FromRgb(34, 139, 34)),          // Forest green - successfully done
            "Aborted" => new SolidColorBrush(Color.FromRgb(220, 20, 60)),            // Crimson - aborted
            "Stopped" => new SolidColorBrush(Color.FromRgb(255, 140, 0)),            // Dark orange - paused
            "Rejected" => new SolidColorBrush(Color.FromRgb(139, 0, 0)),             // Dark red - rejected
            "Skipped" => new SolidColorBrush(Color.FromRgb(128, 128, 128)),          // Gray - skipped
            "Removed" => new SolidColorBrush(Color.FromRgb(64, 64, 64)),             // Dark gray - removed from tracking

            // Station states
            "Idle" => Brushes.LightGray,
            "Processing" => Brushes.Green,
            "Busy" => new SolidColorBrush(Color.FromRgb(100, 149, 237)),  // Cornflower blue - no yellow

            _ => Brushes.Gray
        };
    }

    /// <summary>
    /// Find a child node by ID (recursive)
    /// </summary>
    public StateTreeNode? FindNode(string id)
    {
        if (Id == id)
            return this;

        foreach (var child in Children)
        {
            var found = child.FindNode(id);
            if (found != null)
                return found;
        }

        return null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
