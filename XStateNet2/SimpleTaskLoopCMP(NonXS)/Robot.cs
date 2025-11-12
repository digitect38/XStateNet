using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimpleTaskLoopCMP
{

    public class Robot : IStation
    {
        public string Name { get; set; }
        private Wafer? _wafer;
        private IStation? _currentStation;

        // Lock-free property reads using volatile semantics
        public bool HasPW => _wafer?.IsProcessed ?? false;
        public bool HasNPW => _wafer != null && !_wafer.IsProcessed;
        public bool HasWafer => _wafer != null;
        public bool IsEmpty => _wafer == null;

        // Lock-free validation read using Interlocked
        public Wafer? GetWaferForValidation()
        {
            return Interlocked.CompareExchange(ref _wafer, null, null);
        }

        // Check if robot is already at the target station
        public bool AtStation(IStation station)
        {
            return _currentStation == station;
        }

        public Robot(string name) => Name = name;

        // MoveTo operation: 1 second to move to target station
        public async Task MoveToAsync(IStation target)
        {
            // Skip if already at target station
            if (_currentStation == target)
            {
                return;
            }

            Logger.Log($"{Name} moving to {target.Name}...");
            await Task.Delay(100); // 1 second move time
            _currentStation = target;
            Logger.Log($"{Name} arrived at {target.Name}");
        }

        // MoveToHome operation: 1 second to return to home position
        public async Task MoveToHomeAsync()
        {
            _currentStation = null; // Clear current station
            Logger.Log($"{Name} moving to Home...");
            await Task.Delay(100); // 1 second return to home
            Logger.Log($"{Name} arrived at Home");
        }

        // Lock-free Pick operation using CAS: 0.5 second (after already at station via MoveTo)
        public async Task PickAsync(IStation from)
        {
            await Task.Delay(50); // 0.5 second pick time

            // Atomically pick from station and set our wafer
            var wafer = from.Pick();
            Interlocked.Exchange(ref _wafer, wafer);

            if (wafer != null)
                Logger.Log($"{Name} picked wafer {wafer.Id} from {from.Name}");
        }

        // Lock-free Place operation using CAS: 1 second (after already at station via MoveTo)
        public async Task PlaceAsync(IStation to)
        {
            await Task.Delay(50); // 1 second place time

            // CAS loop to atomically read and clear _wafer
            while (true)
            {
                var current = _wafer;
                if (current == null) return;

                // Try to atomically swap current wafer with null
                if (Interlocked.CompareExchange(ref _wafer, null, current) == current)
                {
                    // Successfully cleared our wafer, now place it
                    to.Place(current);
                    Logger.Log($"{Name} placed wafer {current.Id} on {to.Name}");
                    return;
                }
                // If CAS failed, retry (another thread modified _wafer)
            }
        }

        // Lock-free IStation implementation using Interlocked operations
        Wafer? IStation.Pick()
        {
            return Interlocked.Exchange(ref _wafer, null);
        }

        void IStation.Place(Wafer w)
        {
            Interlocked.Exchange(ref _wafer, w);
        }
    }
}