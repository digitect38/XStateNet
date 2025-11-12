using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SimpleTaskLoopCMP
{
    public enum StationState
    {
        Empty,
        HasNPW,
        HasPW,
        Idle,
        Processing,
        AlmostDone,
        Done
    }

    public interface IStation
    {
        string Name { get; set; }
        Wafer? Pick();
        void Place(Wafer w);
    }

    public class BufferStationBase : IStation
    {
        public string Name { get; set; }
        protected Wafer? _wafer;

        // Derive state from wafer field (lock-free read)
        public StationState CurrentState
        {
            get
            {
                var w = Interlocked.CompareExchange(ref _wafer, null, null);
                if (w == null) return StationState.Empty;
                return w.IsProcessed ? StationState.HasPW : StationState.HasNPW;
            }
        }

        public bool IsEmpty => CurrentState == StationState.Empty;
        public bool HasWafer => CurrentState == StationState.HasNPW || CurrentState == StationState.HasPW;

        // Lock-free validation read using Interlocked
        public Wafer? GetWaferForValidation()
        {
            return Interlocked.CompareExchange(ref _wafer, null, null);
        }

        // Lock-free Place using CAS
        public virtual void Place(Wafer w)
        {
            Interlocked.Exchange(ref _wafer, w);
            Logger.Log($"Wafer {w.Id} placed  on {Name} (Processed: {w.IsProcessed})");
        }

        // Lock-free Pick using CAS
        public virtual Wafer? Pick()
        {
            var w = Interlocked.Exchange(ref _wafer, null);
            return w;
        }
    }

    public abstract class ProcessStationBase : BufferStationBase
    {
        // Use int for atomic state operations
        private int _processState = (int)StationState.Empty;
        protected readonly object _stateLock = new(); // Minimal lock only for complex state transitions

        // Event infrastructure for true event-driven architecture
        public event Action<StationState>? StateChanged;

        // Override CurrentState to use process state instead of deriving from wafer
        public new StationState CurrentState
        {
            get => (StationState)Interlocked.CompareExchange(ref _processState, 0, 0);
            protected set
            {
                var oldState = CurrentState;
                Interlocked.Exchange(ref _processState, (int)value);

                // Fire event if state actually changed
                if (oldState != value)
                {
                    StateChanged?.Invoke(value);
                }
            }
        }

        public bool IsDone => CurrentState == StationState.Done;
        public bool IsIdle => CurrentState == StationState.Idle;
        public bool IsAlmostDone => CurrentState == StationState.AlmostDone;

        // Timeout monitoring
        protected DateTime _processStartTime;
        protected int _expectedProcessTimeMs = 1000; // Default 1 second (10 iterations * 100ms)

        // Lock-free Place with CAS and minimal locking for state check
        public override void Place(Wafer w)
        {
            lock (_stateLock)
            {
                // Can only place on empty station
                if (CurrentState != StationState.Empty)
                {
                    Logger.Log($"ERROR: Cannot place wafer {w.Id} on {Name} - Station is {CurrentState} with wafer {_wafer?.Id}");
                    return;
                }

                Interlocked.Exchange(ref _wafer, w);
                CurrentState = StationState.Idle;
            }
        }

        // Lock-free Pick with CAS and minimal locking for state check
        public override Wafer? Pick()
        {
            lock (_stateLock)
            {
                var state = CurrentState;

                // Cannot pick if still processing
                if (state == StationState.Processing || state == StationState.AlmostDone)
                {
                    return null;
                }

                // Only allow pick when Done
                if (state == StationState.Done)
                {
                    var w = Interlocked.Exchange(ref _wafer, null);
                    CurrentState = StationState.Empty;
                    return w;
                }

                return null;
            }
        }

        protected abstract void SetState_BeforeProcess();
        protected abstract void SetState_AfterProcess();

        public async Task ProcessWaferAsync(CancellationToken token)
        {
            lock (_stateLock)
            {
                SetState_BeforeProcess();
                _processStartTime = DateTime.Now;
            }

            for (int i = 0; i < 10; i++)
            {
                token.ThrowIfCancellationRequested();
                Logger.Log($"{Name} processing wafer {_wafer?.Id}: {(i * 10)}% done");
                await Task.Delay(100, token);

                // Timeout check
                var elapsed = (DateTime.Now - _processStartTime).TotalMilliseconds;
                var allowedTime = _expectedProcessTimeMs * 1.5; // Allow 50% margin
                if (elapsed > allowedTime)
                {
                    AlarmManager.RaiseAlarm(
                        AlarmLevel.Error,
                        "TIMEOUT",
                        $"{Name} processing timeout: {elapsed:F0}ms (expected: {_expectedProcessTimeMs}ms, wafer: {_wafer?.Id})"
                    );
                }

                // Set AlmostDone at 80% completion (200ms early warning for pre-positioning)
                if (i == 8)
                {
                    // Use property setter to fire event
                    CurrentState = StationState.AlmostDone;
                }
            }

            Logger.Log($"{Name} processing wafer {_wafer?.Id}: {(10 * 10)}% done");

            lock (_stateLock)
            {
                SetState_AfterProcess();
            }
        }
    }
}
