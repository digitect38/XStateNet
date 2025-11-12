using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SimpleTaskLoopCMP
{
    public class Cleaner : ProcessStationBase
    {
        public Cleaner() => Name = "Cleaner";

        protected override void SetState_BeforeProcess()
        {
            lock (_stateLock)
            {
                if (_wafer == null || CurrentState != StationState.Idle)
                    return;

                CurrentState = StationState.Processing;
                Logger.Log($"{Name} cleaning wafer {_wafer.Id}");
            }
        }

        protected override void SetState_AfterProcess()
        {
            lock (_stateLock)
            {
                if (_wafer == null || (CurrentState != StationState.Processing && CurrentState != StationState.AlmostDone))
                //if (_wafer == null || CurrentState != StationState.Processing)
                    return;

                if (_wafer != null) _wafer.SetCleaned();

                CurrentState = StationState.Done;
                Logger.Log($"{Name} cleaned wafer {_wafer?.Id}");
            }
        }
    }
}