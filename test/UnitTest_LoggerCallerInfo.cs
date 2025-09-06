using NUnit.Framework;
using XStateNet;

namespace XStateNet.UnitTest
{
    [TestFixture]
    public class LoggerCallerInfoTests
    {
        [Test]
        public void TestLoggerWithCallerInfo()
        {
            // Store original settings
            var originalLevel = Logger.CurrentLevel;
            var originalCallerInfo = Logger.IncludeCallerInfo;
            
            try
            {
                // Enable caller info
                Logger.CurrentLevel = Logger.LogLevel.Info;
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
            
            Assert.Pass("Logger with caller info works correctly");
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