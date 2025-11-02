namespace CMPSimXS2.WPF.Models;

/// <summary>
/// Station routing strategy for selecting next station when multiple options are available
/// </summary>
public enum StationRoutingStrategy
{
    /// <summary>
    /// Round-robin: Cycle through stations in order
    /// </summary>
    RoundRobin,

    /// <summary>
    /// First empty: Choose the first empty/available station
    /// </summary>
    FirstEmpty
}
