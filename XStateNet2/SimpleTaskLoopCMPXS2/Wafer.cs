using System;

namespace SimpleTaskLoopCMPXS2
{
    public enum WaferState
    {
        New = 1,
        Polished = 2,
        Cleaned = 3,
        Processed = 3
    }

    public class Wafer
    {
        private static int _nextNumber = 1;
        public int Id { get; }
        WaferState waferState = WaferState.New;
        public bool IsProcessed => waferState == WaferState.Processed;

        public Wafer() { Id = _nextNumber++; }

        public void SetPolished() { waferState = WaferState.Polished; }
        public void SetCleaned() { waferState = WaferState.Cleaned; }
        public void SetProcessed() { waferState = WaferState.Processed; }
    }
}
