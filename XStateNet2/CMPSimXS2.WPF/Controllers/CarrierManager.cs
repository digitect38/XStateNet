using CMPSimXS2.WPF.Models;

namespace CMPSimXS2.WPF.Controllers;

/// <summary>
/// Carrier manager stub for managing carrier state
/// TODO: Implement with XStateNet2.Core
/// </summary>
public class CarrierManager
{
    /// <summary>
    /// Get all carriers in the system
    /// </summary>
    public IEnumerable<Carrier> GetAllCarriers()
    {
        // TODO: Return actual carriers from state machine
        return Enumerable.Empty<Carrier>();
    }

    /// <summary>
    /// Get the carrier at a specific load port
    /// </summary>
    public Carrier? GetCarrierAtLoadPort(string loadPortId)
    {
        // TODO: Return actual carrier from state machine
        return null;
    }
}
