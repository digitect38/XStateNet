using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XStateNet.Distributed.EventBus;

namespace XStateNet.Distributed.Testing
{
    /// <summary>
    /// Provides deterministic test mode for XStateNet to eliminate race conditions
    /// and ensure predictable behavior during testing
    /// </summary>
    public static class DeterministicTestMode
    {
        private static readonly AsyncLocal<bool> _isEnabled = new AsyncLocal<bool>();
        private static readonly AsyncLocal<DeterministicEventProcessor> _processor = new AsyncLocal<DeterministicEventProcessor>();

        /// <summary>
        /// Gets whether deterministic test mode is enabled for the current async context
        /// </summary>
        public static bool IsEnabled => _isEnabled.Value;

        /// <summary>
        /// Gets the current deterministic event processor
        /// </summary>
        public static DeterministicEventProcessor Processor => _processor.Value;

        /// <summary>
        /// Enables deterministic test mode for the current async context
        /// </summary>
        public static IDisposable Enable()
        {
            _isEnabled.Value = true;
            _processor.Value = new DeterministicEventProcessor();
            return new DeterministicModeScope();
        }

        private class DeterministicModeScope : IDisposable
        {
            public void Dispose()
            {
                _isEnabled.Value = false;
                _processor.Value?.Dispose();
                _processor.Value = null;
            }
        }
    }

    /// <summary>
    /// Processes events synchronously in deterministic order
    /// </summary>
    public class DeterministicEventProcessor : IDisposable
    {
        private readonly Queue<PendingEvent> _eventQueue = new Queue<PendingEvent>();
        private readonly List<EventRecord> _processedEvents = new List<EventRecord>();
        private readonly SemaphoreSlim _processingLock = new SemaphoreSlim(1, 1);
        private int _eventCounter = 0;

        /// <summary>
        /// Enqueues an event for processing
        /// </summary>
        public Task EnqueueEventAsync(string eventName, object payload, Func<Task> handler)
        {
            if (!DeterministicTestMode.IsEnabled)
            {
                // In non-deterministic mode, just execute immediately
                return handler();
            }

            _processingLock.Wait();
            try
            {
                var eventId = Interlocked.Increment(ref _eventCounter);
                var pendingEvent = new PendingEvent
                {
                    Id = eventId,
                    Name = eventName,
                    Payload = payload,
                    Handler = handler,
                    EnqueuedAt = DateTime.UtcNow
                };

                _eventQueue.Enqueue(pendingEvent);
            }
            finally
            {
                _processingLock.Release();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Processes all pending events synchronously in order
        /// </summary>
        public async Task ProcessAllPendingEventsAsync()
        {
            await _processingLock.WaitAsync();
            try
            {
                while (_eventQueue.Count > 0)
                {
                    var evt = _eventQueue.Dequeue();
                    var startTime = DateTime.UtcNow;

                    try
                    {
                        await evt.Handler();
                        _processedEvents.Add(new EventRecord
                        {
                            Id = evt.Id,
                            Name = evt.Name,
                            Payload = evt.Payload,
                            StartTime = startTime,
                            EndTime = DateTime.UtcNow,
                            Success = true
                        });
                    }
                    catch (Exception ex)
                    {
                        _processedEvents.Add(new EventRecord
                        {
                            Id = evt.Id,
                            Name = evt.Name,
                            Payload = evt.Payload,
                            StartTime = startTime,
                            EndTime = DateTime.UtcNow,
                            Success = false,
                            Exception = ex
                        });
                        throw;
                    }
                }
            }
            finally
            {
                _processingLock.Release();
            }
        }

        /// <summary>
        /// Waits for a specific number of events to be processed
        /// </summary>
        public async Task WaitForEventCountAsync(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                await ProcessAllPendingEventsAsync();

                if (_processedEvents.Count >= count)
                {
                    return;
                }

                await Task.Delay(10);
            }

            throw new TimeoutException($"Timed out waiting for {count} events. Processed: {_processedEvents.Count}");
        }

        /// <summary>
        /// Gets the number of processed events
        /// </summary>
        public int ProcessedEventCount => _processedEvents.Count;

        /// <summary>
        /// Gets the number of pending events
        /// </summary>
        public int PendingEventCount => _eventQueue.Count;

        /// <summary>
        /// Gets all processed events
        /// </summary>
        public IReadOnlyList<EventRecord> ProcessedEvents => _processedEvents.AsReadOnly();

        public void Dispose()
        {
            _processingLock?.Dispose();
        }

        private class PendingEvent
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public object Payload { get; set; }
            public Func<Task> Handler { get; set; }
            public DateTime EnqueuedAt { get; set; }
        }
    }

    public class EventRecord
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public object Payload { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool Success { get; set; }
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// Extension methods for deterministic testing
    /// </summary>
    public static class DeterministicTestExtensions
    {
        /// <summary>
        /// Sends an event and waits for it to be processed deterministically
        /// </summary>
        public static async Task SendAndWaitAsync(this IStateMachine machine, string eventName)
        {
            if (DeterministicTestMode.IsEnabled)
            {
                var processor = DeterministicTestMode.Processor;
                var beforeCount = processor.ProcessedEventCount;

                machine.Send(eventName);

                // Process all events that were triggered
                await processor.ProcessAllPendingEventsAsync();

                // Wait for at least one new event to be processed
                await processor.WaitForEventCountAsync(beforeCount + 1, TimeSpan.FromSeconds(5));
            }
            else
            {
                machine.Send(eventName);
            }
        }

        /// <summary>
        /// Publishes an event and waits for all subscribers to process it
        /// </summary>
        public static async Task PublishAndWaitAsync(this IStateMachineEventBus eventBus,
            string targetMachineId, string eventName, object payload = null)
        {
            if (DeterministicTestMode.IsEnabled)
            {
                var processor = DeterministicTestMode.Processor;
                var beforeCount = processor.ProcessedEventCount;

                await eventBus.PublishEventAsync(targetMachineId, eventName, payload);

                // Process all events synchronously
                await processor.ProcessAllPendingEventsAsync();
            }
            else
            {
                await eventBus.PublishEventAsync(targetMachineId, eventName, payload);
            }
        }
    }
}