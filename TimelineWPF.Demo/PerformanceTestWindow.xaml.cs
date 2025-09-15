using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using TimelineWPF.PubSub;

namespace TimelineWPF.Demo
{
    public partial class PerformanceTestWindow : Window
    {
        public PerformanceTestWindow()
        {
            InitializeComponent();
        }

        private async void RunTestBtn_Click(object sender, RoutedEventArgs e)
        {
            RunTestBtn.IsEnabled = false;

            if (!int.TryParse(EventCountBox.Text, out int eventCount) || eventCount <= 0)
            {
                MessageBox.Show("Please enter a valid event count", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                RunTestBtn.IsEnabled = true;
                return;
            }

            if (!int.TryParse(SubscriberCountBox.Text, out int subscriberCount) || subscriberCount <= 0)
            {
                MessageBox.Show("Please enter a valid subscriber count", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                RunTestBtn.IsEnabled = true;
                return;
            }

            // Reset UI
            StandardProgress.Value = 0;
            OptimizedProgress.Value = 0;

            // Run tests in parallel
            var standardTask = RunStandardEventBusTest(eventCount, subscriberCount);
            var optimizedTask = RunOptimizedEventBusTest(eventCount, subscriberCount);

            await Task.WhenAll(standardTask, optimizedTask);

            RunTestBtn.IsEnabled = true;
        }

        private async Task RunStandardEventBusTest(int eventCount, int subscriberCount)
        {
            StandardStatusText.Text = "Running...";
            StandardProgress.Maximum = eventCount;

            var eventBus = new TimelineEventBus(enableAsyncPublishing: false);
            var receivedCount = 0;

            // Create test subscribers
            var subscribers = new TestSubscriber[subscriberCount];
            for (int i = 0; i < subscriberCount; i++)
            {
                subscribers[i] = new TestSubscriber(() =>
                {
                    receivedCount++;
                    if (receivedCount % 1000 == 0)
                    {
                        Dispatcher.Invoke(() => StandardProgress.Value = receivedCount / subscriberCount);
                    }
                });
                eventBus.Subscribe(subscribers[i]);
            }

            // Measure memory before
            var memoryBefore = GC.GetTotalMemory(true);

            // Start timing
            var sw = Stopwatch.StartNew();

            // Publish events
            await Task.Run(() =>
            {
                for (int i = 0; i < eventCount; i++)
                {
                    var message = new TestMessage
                    {
                        MachineName = $"Machine{i % 10}",
                        EventId = i,
                        Timestamp = DateTime.UtcNow.Ticks / 10.0 // Convert to microseconds
                    };
                    eventBus.Publish(message);
                }
            });

            // Wait for all messages to be received (with timeout)
            var timeoutTask = Task.Delay(30000); // 30 second timeout
            while (receivedCount < eventCount * subscriberCount && !timeoutTask.IsCompleted)
            {
                await Task.Delay(10);
            }

            sw.Stop();

            // Measure memory after
            var memoryAfter = GC.GetTotalMemory(false);
            var memoryUsed = (memoryAfter - memoryBefore) / (1024.0 * 1024.0);

            // Calculate throughput
            var throughput = receivedCount / sw.Elapsed.TotalSeconds;

            // Update UI
            StandardStatusText.Text = "Completed";
            StandardTimeText.Text = $"Time: {sw.ElapsedMilliseconds:N0} ms";
            StandardThroughputText.Text = $"Throughput: {throughput:N0} msg/sec";
            StandardMemoryText.Text = $"Memory: {memoryUsed:N2} MB";
            StandardProgress.Value = StandardProgress.Maximum;

            // Cleanup
            eventBus.ClearSubscriptions();
        }

        private async Task RunOptimizedEventBusTest(int eventCount, int subscriberCount)
        {
            OptimizedStatusText.Text = "Running...";
            OptimizedProgress.Maximum = eventCount;

            var eventBus = new OptimizedTimelineEventBus();
            var receivedCount = 0;

            // Create test subscribers
            var subscribers = new TestSubscriber[subscriberCount];
            for (int i = 0; i < subscriberCount; i++)
            {
                subscribers[i] = new TestSubscriber(() =>
                {
                    receivedCount++;
                    if (receivedCount % 1000 == 0)
                    {
                        Dispatcher.Invoke(() => OptimizedProgress.Value = receivedCount / subscriberCount);
                    }
                });
                eventBus.Subscribe(subscribers[i]);
            }

            // Measure memory before
            var memoryBefore = GC.GetTotalMemory(true);

            // Start timing
            var sw = Stopwatch.StartNew();

            // Publish events
            await Task.Run(() =>
            {
                for (int i = 0; i < eventCount; i++)
                {
                    var message = new TestMessage
                    {
                        MachineName = $"Machine{i % 10}",
                        EventId = i,
                        Timestamp = DateTime.UtcNow.Ticks / 10.0 // Convert to microseconds
                    };
                    eventBus.Publish(message);
                }
            });

            // Wait for all messages to be received (with timeout)
            var timeoutTask = Task.Delay(30000); // 30 second timeout
            while (receivedCount < eventCount * subscriberCount && !timeoutTask.IsCompleted)
            {
                await Task.Delay(10);
            }

            sw.Stop();

            // Measure memory after
            var memoryAfter = GC.GetTotalMemory(false);
            var memoryUsed = (memoryAfter - memoryBefore) / (1024.0 * 1024.0);

            // Calculate throughput
            var throughput = receivedCount / sw.Elapsed.TotalSeconds;

            // Update UI
            OptimizedStatusText.Text = "Completed";
            OptimizedTimeText.Text = $"Time: {sw.ElapsedMilliseconds:N0} ms";
            OptimizedThroughputText.Text = $"Throughput: {throughput:N0} msg/sec";
            OptimizedMemoryText.Text = $"Memory: {memoryUsed:N2} MB";
            OptimizedProgress.Value = OptimizedProgress.Maximum;

            // Cleanup
            eventBus.Dispose();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private class TestSubscriber : ITimelineSubscriber
        {
            private readonly Action _onMessage;

            public TestSubscriber(Action onMessage)
            {
                _onMessage = onMessage;
            }

            public void OnTimelineMessage(ITimelineMessage message)
            {
                _onMessage();
            }

            public void OnTimelineMessageBatch(IEnumerable<ITimelineMessage> messages)
            {
                foreach (var _ in messages)
                {
                    _onMessage();
                }
            }
        }

        private class TestMessage : ITimelineMessage
        {
            public Guid MessageId { get; } = Guid.NewGuid();
            public double Timestamp { get; set; }
            public string MachineName { get; set; } = "";
            public TimelineMessageType MessageType => TimelineMessageType.Event;
            public int EventId { get; set; }
        }
    }
}