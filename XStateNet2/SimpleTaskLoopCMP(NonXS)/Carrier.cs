using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimpleTaskLoopCMP
{

    public class Carrier : IStation
    {
        public string Name { get; set; }

        public bool IsArrived;
        private Wafer?[] _wafers;
        private readonly int _waferCount;

        public Carrier(int waferCount = Config.W_COUNT)
        {
            Name = "Carrier";
            _waferCount = waferCount;
            _wafers = new Wafer[_waferCount];
            for (int i = 0; i < _waferCount; i++)
                _wafers[i] = new Wafer();
        }

        public bool IsAttached { get; private set; }

        // Lock-free read: safe because reference reads are atomic
        public bool HaveNPW
        {
            get
            {
                foreach (var w in _wafers)
                    if (w != null && !w.IsProcessed) return true;
                return false;
            }
        }

        // Lock-free read: safe because reference reads are atomic
        public bool AllProcessed
        {
            get
            {
                foreach (var w in _wafers)
                    if (w == null || !w.IsProcessed) return false;
                return true;
            }
        }

        // Lock-free Pick using CAS with retry
        public Wafer? Pick()
        {
            for (int i = 0; i < _waferCount; i++)
            {
                var current = _wafers[i];
                if (current != null && !current.IsProcessed)
                {
                    // Try to atomically swap this slot with null
                    if (Interlocked.CompareExchange(ref _wafers[i], null, current) == current)
                    {
                        return current;
                    }
                    // If CAS failed, this slot was modified by another thread
                    // Continue to next iteration (i will stay the same in this retry)
                    i--;
                }
            }
            return null;
        }

        // Lock-free Place using CAS with retry
        public void Place(Wafer wafer)
        {
            for (int i = 0; i < _waferCount; i++)
            {
                var current = _wafers[i];
                if (current == null)
                {
                    // Try to atomically swap null slot with wafer
                    if (Interlocked.CompareExchange(ref _wafers[i], wafer, null) == null)
                    {
                        return;
                    }
                    // If CAS failed, this slot was filled by another thread
                    // Continue to next slot
                }
            }
        }

        public void Attach(Loadport lp)
        {
            IsAttached = true;
            Logger.Log("Carrier attached.");
        }

        public void Detach(Loadport lp)
        {
            IsAttached = false;
            Logger.Log("Carrier detached.");
        }

        // Count total wafers in carrier
        public int CountWafers()
        {
            int count = 0;
            for (int i = 0; i < _waferCount; i++)
            {
                if (_wafers[i] != null)
                    count++;
            }
            return count;
        }

        // Get all wafer IDs for validation
        public List<int> GetWaferIds()
        {
            var ids = new List<int>();
            for (int i = 0; i < _waferCount; i++)
            {
                if (_wafers[i] != null)
                    ids.Add(_wafers[i].Id);
            }
            return ids;
        }
    }

}