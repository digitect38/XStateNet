using Akka.Actor;
using System.Windows;
using System.Windows.Media;
using XStateNet2.Core.Extensions;

namespace CMPSimXS2.ViewModels;

/// <summary>
/// Station view model representing a processing station in the CMP simulator.
/// Inherits from VisualObject (Box + Circle composition) where:
/// - Box: Outer border representing the station boundary (white background)
/// - Circle: Inner status indicator showing current state (color-coded)
/// </summary>
public class StationViewModel : VisualObject
{
    private IActorRef? _stateMachine;
    private string _currentState = "Unknown";
    private int? _currentWafer;
    private string _remainingTime = string.Empty;

    public string RemainingTime
    {
        get => _remainingTime;
        set => SetProperty(ref _remainingTime, value);
    }

    public virtual string CurrentState
    {
        get => _currentState;
        set
        {
            if (SetProperty(ref _currentState, value))
            {
                UpdateVisualState();
            }
        }
    }

    public virtual int? CurrentWafer
    {
        get => _currentWafer;
        set => SetProperty(ref _currentWafer, value);
    }

    /// <summary>
    /// Alias for CircleBrush to maintain backward compatibility with XAML bindings
    /// </summary>
    public SolidColorBrush StateBrush
    {
        get => CircleBrush;
        set => CircleBrush = value;
    }

    public virtual IActorRef? StateMachine
    {
        get => _stateMachine;
        set => _stateMachine = value;
    }

    public StationViewModel(string name) : base(name)
    {
        // Initialize visual object with default station appearance
        BoxBrush = new SolidColorBrush(Colors.White);
        CircleBrush = new SolidColorBrush(Colors.Gray);
        BoxBorderThickness = new Thickness(2);
        BoxBorderBrush = new SolidColorBrush(Color.FromRgb(51, 51, 51)); // #333
        Width = 150;
        Height = 200;
        CircleSize = 40;
    }

    public void UpdateState()
    {
        if (_stateMachine == null) return;

        try
        {
            var snapshot = _stateMachine.GetStateSnapshot();
            CurrentState = snapshot.CurrentState ?? "Unknown";

            // Try to get current wafer from context
            if (snapshot.Context.ContainsKey("currentWafer"))
            {
                var waferValue = snapshot.Context["currentWafer"];
                if (waferValue is int wafer)
                    CurrentWafer = wafer;
                else if (waferValue is null)
                    CurrentWafer = null;
            }
        }
        catch
        {
            CurrentState = "Error";
        }
    }

    /// <summary>
    /// Updates the visual state by changing the circle color based on station state.
    /// The box remains white, while the circle indicates the current processing state.
    /// </summary>
    protected override void UpdateVisualState()
    {
        // Update circle color based on state (status indicator)
        CircleBrush = CurrentState.ToLowerInvariant() switch
        {
            "idle" or "empty" => new SolidColorBrush(Color.FromRgb(144, 238, 144)), // LightGreen
            "processing" or "cleaning" => new SolidColorBrush(Color.FromRgb(255, 99, 71)), // Tomato
            "done" or "completed" => new SolidColorBrush(Color.FromRgb(152, 251, 152)), // PaleGreen
            "occupied" or "carrying" => new SolidColorBrush(Color.FromRgb(255, 215, 0)), // Gold
            "loaded" => new SolidColorBrush(Color.FromRgb(100, 149, 237)), // CornflowerBlue
            "waiting" => new SolidColorBrush(Color.FromRgb(135, 206, 235)), // SkyBlue
            _ => new SolidColorBrush(Color.FromRgb(192, 192, 192)) // Silver
        };

        // Box remains white background (not changed by state)
        BoxBrush = new SolidColorBrush(Colors.White);
    }
}

