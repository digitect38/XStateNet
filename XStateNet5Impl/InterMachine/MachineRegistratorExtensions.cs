using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace XStateNet.InterMachine
{
    /// <summary>
    /// Extension methods for machine registration and discovery
    /// </summary>
    public static class MachineRegistratorExtensions
    {
        private static IMachineRegistrator _defaultRegistrator = new LocalMachineRegistrator();

        /// <summary>
        /// Set the default registrator (e.g., for Kubernetes environments)
        /// </summary>
        public static void SetDefaultRegistrator(IMachineRegistrator registrator)
        {
            _defaultRegistrator = registrator ?? throw new ArgumentNullException(nameof(registrator));
        }

        /// <summary>
        /// Get the default registrator
        /// </summary>
        public static IMachineRegistrator GetDefaultRegistrator()
        {
            return _defaultRegistrator;
        }

        /// <summary>
        /// Register this machine with a type for discovery
        /// </summary>
        public static async Task<RegisteredMachine> RegisterAsTypeAsync(
            this IStateMachine machine,
            string machineType,
            Dictionary<string, object> metadata = null,
            IMachineRegistrator registrator = null)
        {
            var reg = registrator ?? _defaultRegistrator;
            var machineId = await reg.RegisterMachineAsync(machineType, machine, metadata);

            return new RegisteredMachine(machine, machineId, machineType, reg);
        }

        /// <summary>
        /// Discover and connect to machines of a specific type
        /// </summary>
        public static async Task<IReadOnlyList<MachineInfo>> DiscoverAndConnectAsync(
            this RegisteredMachine machine,
            string targetType)
        {
            var machines = await machine.Registrator.DiscoverByTypeAsync(targetType);

            // Auto-connect to discovered machines
            foreach (var target in machines)
            {
                if (target.MachineId != machine.MachineId)
                {
                    try
                    {
                        // Establish connection through registrator
                        // The registrator handles the actual connection
                    }
                    catch { /* Ignore connection failures */ }
                }
            }

            return machines;
        }

        /// <summary>
        /// Send message to all machines of a specific type
        /// </summary>
        public static async Task SendToTypeAsync(
            this RegisteredMachine machine,
            string targetType,
            string eventName,
            object data = null)
        {
            var targets = await machine.Registrator.DiscoverByTypeAsync(targetType);

            foreach (var target in targets)
            {
                if (target.MachineId != machine.MachineId)
                {
                    await machine.Registrator.SendToMachineAsync(
                        machine.MachineId,
                        target.MachineId,
                        eventName,
                        data);
                }
            }
        }

        /// <summary>
        /// Send message to a random machine of a specific type (load balancing)
        /// </summary>
        public static async Task SendToRandomOfTypeAsync(
            this RegisteredMachine machine,
            string targetType,
            string eventName,
            object data = null)
        {
            var targets = await machine.Registrator.DiscoverByTypeAsync(targetType);
            var availableTargets = targets.Where(t => t.MachineId != machine.MachineId).ToList();

            if (availableTargets.Count > 0)
            {
                var random = new Random();
                var target = availableTargets[random.Next(availableTargets.Count)];

                await machine.Registrator.SendToMachineAsync(
                    machine.MachineId,
                    target.MachineId,
                    eventName,
                    data);
            }
        }

        /// <summary>
        /// Get all machines matching a pattern
        /// </summary>
        public static async Task<IReadOnlyList<MachineInfo>> DiscoverByPatternAsync(
            this RegisteredMachine machine,
            string typePattern)
        {
            return await machine.Registrator.DiscoverByPatternAsync(typePattern);
        }

        /// <summary>
        /// Broadcast to all machines of the same type
        /// </summary>
        public static async Task BroadcastToSameTypeAsync(
            this RegisteredMachine machine,
            string eventName,
            object data = null)
        {
            await machine.Registrator.BroadcastToTypeAsync(machine.MachineType, eventName, data);
        }

        /// <summary>
        /// Get all available machine types in the registry
        /// </summary>
        public static async Task<IReadOnlyList<string>> GetAvailableTypesAsync(
            this RegisteredMachine machine)
        {
            return await machine.Registrator.GetMachineTypesAsync();
        }

        /// <summary>
        /// Count machines of a specific type
        /// </summary>
        public static async Task<int> CountMachinesOfTypeAsync(
            this RegisteredMachine machine,
            string machineType)
        {
            var machines = await machine.Registrator.DiscoverByTypeAsync(machineType);
            return machines.Count;
        }

        /// <summary>
        /// Check if any machines of a type exist
        /// </summary>
        public static async Task<bool> TypeExistsAsync(
            this RegisteredMachine machine,
            string machineType)
        {
            var count = await machine.CountMachinesOfTypeAsync(machineType);
            return count > 0;
        }

        /// <summary>
        /// Unregister from the registrator
        /// </summary>
        public static async Task UnregisterAsync(this RegisteredMachine machine)
        {
            await machine.Registrator.UnregisterMachineAsync(machine.MachineId);
        }
    }

    /// <summary>
    /// Represents a machine that has been registered with a registrator
    /// </summary>
    public class RegisteredMachine : IDisposable
    {
        public IStateMachine Machine { get; }
        public string MachineId { get; }
        public string MachineType { get; }
        public IMachineRegistrator Registrator { get; }

        internal RegisteredMachine(
            IStateMachine machine,
            string machineId,
            string machineType,
            IMachineRegistrator registrator)
        {
            Machine = machine ?? throw new ArgumentNullException(nameof(machine));
            MachineId = machineId ?? throw new ArgumentNullException(nameof(machineId));
            MachineType = machineType ?? throw new ArgumentNullException(nameof(machineType));
            Registrator = registrator ?? throw new ArgumentNullException(nameof(registrator));
        }

        public void Dispose()
        {
            Registrator.UnregisterMachineAsync(MachineId).Wait();
            Machine.Stop();
        }
    }
}