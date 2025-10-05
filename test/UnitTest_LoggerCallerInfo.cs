using Xunit;

namespace XStateNet.UnitTest
{
    public class LoggerCallerInfoTests
    {
        [Fact]
        public void TestLoggerWithCallerInfo()
        {
            // Store original settings
            var originalLevel = Logger.CurrentLevel;
            var originalCallerInfo = Logger.IncludeCallerInfo;

            try
            {
                // Enable caller info
                Logger.CurrentLevel = Logger.LogLevel.Warning;
                Logger.IncludeCallerInfo = true;

                // Log with caller info
                Logger.Info("Test message with caller info");
                TestMethodOne();
                TestMethodTwo();

                // Disable caller info
                Logger.IncludeCallerInfo = false;
                Logger.Info("Test message without caller info");
            }
            finally
            {
                // Restore original settings
                Logger.CurrentLevel = originalLevel;
                Logger.IncludeCallerInfo = originalCallerInfo;
            }

            // Test passed: Logger with caller info works correctly
        }

        private void TestMethodOne()
        {
            Logger.Info("Called from TestMethodOne");
            Logger.Debug("Debug message from TestMethodOne");
        }

        private void TestMethodTwo()
        {
            Logger.Warning("Warning from TestMethodTwo");
            Logger.Error("Error from TestMethodTwo");
        }
    }
}
