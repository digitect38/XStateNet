using System.Collections.Concurrent;

namespace XStateNet.Extensions
{
    /// <summary>
    /// Extension methods for safe asynchronous event sending with correlation and timeout
    /// </summary>
    public static class SafeSendExtensions
    {
        // Global registry for pending responses
        private static readonly ConcurrentDictionary<Guid, TaskCompletionSource<TransitionResult>> PendingResponses = new();

        /// <summary>
        /// Safely send an event asynchronously with timeout and correlation tracking
        /// </summary>
        /// <param name="machine">The state machine to send the event to</param>
        /// <param name="eventName">The event name to send</param>
        /// <param name="eventData">Optional event data</param>
        /// <param name="timeoutMs">Timeout in milliseconds (default: 5000ms)</param>
        /// <returns>The transition result containing the new state</returns>
        public static async Task<TransitionResult> SendAsyncSafe(
            this IStateMachine machine,
            string eventName,
            object? eventData = null,
            int timeoutMs = 5000)
        {
            // Create correlation ID for this request
            var correlationId = Guid.NewGuid();
            var completionSource = new TaskCompletionSource<TransitionResult>();

            // Register the pending response
            if (!PendingResponses.TryAdd(correlationId, completionSource))
            {
                throw new InvalidOperationException($"Failed to register correlation ID: {correlationId}");
            }

            try
            {
                // Create the safe message with correlation ID
                var safeMessage = new SafeEventMessage
                {
                    EventName = eventName,
                    EventData = eventData,
                    CorrelationId = correlationId,
                    TimeoutMs = timeoutMs,
                    Timestamp = DateTime.UtcNow
                };

                // Send the event asynchronously (fire and forget)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Send the event and get the result
                        var result = await machine.SendAsync(eventName, safeMessage);

                        // Create response with correlation ID
                        var response = new TransitionResult
                        {
                            CorrelationId = correlationId,
                            NewState = result,
                            Success = true,
                            Timestamp = DateTime.UtcNow
                        };

                        // Complete the waiting task
                        if (PendingResponses.TryRemove(correlationId, out var tcs))
                        {
                            tcs.TrySetResult(response);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Handle errors
                        if (PendingResponses.TryRemove(correlationId, out var tcs))
                        {
                            tcs.TrySetException(ex);
                        }
                    }
                });

                // Wait for response with timeout
                using var cts = new CancellationTokenSource(timeoutMs);
                try
                {
                    return await completionSource.Task.WaitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Timeout occurred
                    PendingResponses.TryRemove(correlationId, out _);
                    throw new TimeoutException($"SendAsyncSafe timed out after {timeoutMs}ms waiting for event '{eventName}'");
                }
            }
            finally
            {
                // Cleanup
                PendingResponses.TryRemove(correlationId, out _);
            }
        }

        /// <summary>
        /// Process a safe event message and return the result through the event bus
        /// </summary>
        public static async Task ProcessSafeEvent(
            this IStateMachine machine,
            SafeEventMessage message)
        {
            try
            {
                // Process the actual event
                var result = await machine.SendAsync(message.EventName, message.EventData);

                // Send back the response with correlation ID
                var response = new TransitionResult
                {
                    CorrelationId = message.CorrelationId,
                    NewState = result,
                    Success = true,
                    Timestamp = DateTime.UtcNow
                };

                // Notify the waiting task
                if (PendingResponses.TryGetValue(message.CorrelationId, out var tcs))
                {
                    tcs.TrySetResult(response);
                }
            }
            catch (Exception ex)
            {
                // Send error response
                var errorResponse = new TransitionResult
                {
                    CorrelationId = message.CorrelationId,
                    Success = false,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow
                };

                if (PendingResponses.TryGetValue(message.CorrelationId, out var tcs))
                {
                    tcs.TrySetException(ex);
                }
            }
        }

        /// <summary>
        /// Clear all pending responses (useful for cleanup)
        /// </summary>
        public static void ClearPendingResponses()
        {
            foreach (var kvp in PendingResponses)
            {
                kvp.Value.TrySetCanceled();
            }
            PendingResponses.Clear();
        }
    }

    /// <summary>
    /// Message structure for safe event sending
    /// </summary>
    public class SafeEventMessage
    {
        public string EventName { get; set; } = string.Empty;
        public object? EventData { get; set; }
        public Guid CorrelationId { get; set; }
        public int TimeoutMs { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result structure for transition responses
    /// </summary>
    public class TransitionResult
    {
        public Guid CorrelationId { get; set; }
        public string NewState { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
    }
}