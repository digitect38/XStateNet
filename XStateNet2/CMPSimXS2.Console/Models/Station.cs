using Akka.Actor;

namespace CMPSimXS2.Console.Models;

/// <summary>
/// Simple console-friendly station model without WPF dependencies
/// </summary>
public class Station
{
    private string _currentState = "idle";
    private int? _currentWafer;

    public string Name { get; }
    public IActorRef? StateMachine { get; set; }

    public string CurrentState
    {
        get => _currentState;
        set
        {
            if (_currentState != value)
            {
                _currentState = value;
                OnStateChanged?.Invoke(_currentState, _currentWafer);
            }
        }
    }

    public int? CurrentWafer
    {
        get => _currentWafer;
        set
        {
            if (_currentWafer != value)
            {
                _currentWafer = value;
                OnStateChanged?.Invoke(_currentState, _currentWafer);
            }
        }
    }

    /// <summary>
    /// Callback invoked when station state or wafer changes
    /// </summary>
    public Action<string, int?>? OnStateChanged { get; set; }

    public Station(string name)
    {
        Name = name;
    }
}
