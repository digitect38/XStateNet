using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SimpleTaskLoopCMP
{
    public class Polisher : ProcessStationBase
    {
        public Polisher() => Name = "Polisher";

        protected override void SetState_BeforeProcess()
        {
            lock (_stateLock)
            {
                if (_wafer == null || CurrentState != StationState.Idle)
                    return;

                CurrentState = StationState.Processing;
                Logger.Log($"{Name} polishing wafer {_wafer.Id}");
            }
        }

        protected override void SetState_AfterProcess()
        {
            lock (_stateLock)
            {
                if (_wafer == null || (CurrentState != StationState.Processing && CurrentState != StationState.AlmostDone))
                //if (_wafer == null || CurrentState != StationState.Processing)
                    return;

                if (_wafer != null) _wafer.SetPolished();

                CurrentState = StationState.Done;
                Logger.Log($"{Name} polished wafer {_wafer?.Id}");
            }
        }
    }
}
