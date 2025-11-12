using System;

namespace SimpleTaskLoopCMPXS2
{
    public static class Logger
    {
        public static void Log(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.WriteLine($"[{timestamp}] {message}");
        }
    }
}
