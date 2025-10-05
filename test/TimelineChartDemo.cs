using System.Windows;
using TimelineWPF;
using Xunit;

namespace XStateNet.Tests
{
    public class TimelineChartDemo
    {
        [Fact(Skip = "Manual test - run this to see Timeline chart visualization")]
        public void ShowTimelineChartDemo()
        {
            // This test is for manual demonstration
            // Remove Skip attribute to run and see the Timeline chart in action

            Thread thread = new Thread(() =>
            {
                var app = new Application();
                var window = new DemoWindow();
                app.Run(window);
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }
    }
}
