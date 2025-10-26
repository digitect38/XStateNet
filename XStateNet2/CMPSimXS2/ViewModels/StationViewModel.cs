using Akka.Actor;
using System.Windows.Media;
using XStateNet2.Core.Extensions;

namespace CMPSimXS2.ViewModels;

public class StationViewModel : ViewModelBase
{
    private IActorRef? _stateMachine;
    private string _name = string.Empty;
    private string _currentState = "Unknown";
    private int? _currentWafer;
    private SolidColorBrush _stateBrush = Brushes.Gray;
    private string _remainingTime = string.Empty;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

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
                UpdateStateColor();
            }
        }
    }

    public virtual int? CurrentWafer
    {
        get => _currentWafer;
        set => SetProperty(ref _currentWafer, value);
    }

    public SolidColorBrush StateBrush
    {
        get => _stateBrush;
        set => SetProperty(ref _stateBrush, value);
    }

    public virtual IActorRef? StateMachine
    {
        get => _stateMachine;
        set => _stateMachine = value;
    }

    public StationViewModel(string name)
    {
        Name = name;
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

    private void UpdateStateColor()
    {
        StateBrush = CurrentState.ToLowerInvariant() switch
        {
            "idle" or "empty" => new SolidColorBrush(Color.FromRgb(144, 238, 144)), // LightGreen
            "processing" or "cleaning" => new SolidColorBrush(Color.FromRgb(255, 99, 71)), // Tomato
            "done" or "completed" => new SolidColorBrush(Color.FromRgb(152, 251, 152)), // PaleGreen
            "occupied" or "carrying" => new SolidColorBrush(Color.FromRgb(255, 215, 0)), // Gold
            "loaded" => new SolidColorBrush(Color.FromRgb(100, 149, 237)), // CornflowerBlue
            "waiting" => new SolidColorBrush(Color.FromRgb(135, 206, 235)), // SkyBlue
            _ => new SolidColorBrush(Color.FromRgb(192, 192, 192)) // Silver
        };
    }
}
