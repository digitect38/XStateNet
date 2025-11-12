using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimpleTaskLoopCMP
{
    public enum AlarmLevel
    {
        Warning,
        Error,
        Critical
    }

    public class AlarmManager
    {
        private static readonly object _lock = new();
        private static List<Alarm> _alarms = new();

        public static void RaiseAlarm(AlarmLevel level, string code, string message)
        {
            lock (_lock)
            {
                var alarm = new Alarm
                {
                    Level = level,
                    Code = code,
                    Message = message,
                    Timestamp = DateTime.Now
                };

                _alarms.Add(alarm);

                // Visual alarm indicator
                var levelSymbol = level switch
                {
                    AlarmLevel.Warning => "⚠️",
                    AlarmLevel.Error => "❌",
                    AlarmLevel.Critical => "🚨",
                    _ => "⚡"
                };

                Logger.Log($"{levelSymbol} ALARM [{level}] {code}: {message}");

                // For critical alarms, also log to console with sound (Windows beep)
                if (level == AlarmLevel.Critical)
                {
                    Console.Beep(1000, 200); // 1000Hz for 200ms
                }
            }
        }

        public static List<Alarm> GetAlarms()
        {
            lock (_lock)
            {
                return new List<Alarm>(_alarms);
            }
        }

        public static int GetAlarmCount(AlarmLevel? level = null)
        {
            lock (_lock)
            {
                if (level.HasValue)
                    return _alarms.Count(a => a.Level == level.Value);
                return _alarms.Count;
            }
        }

        public static void ClearAlarms()
        {
            lock (_lock)
            {
                _alarms.Clear();
            }
        }
    }

    public class Alarm
    {
        public AlarmLevel Level { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
