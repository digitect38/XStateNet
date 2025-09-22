using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using XStateNet;

namespace XStateNet.GPU.Core
{
    /// <summary>
    /// Handles action execution for GPU-accelerated state machines
    /// Since GPU kernels can't execute arbitrary code, actions are:
    /// 1. Identified during transitions on GPU
    /// 2. Queued for execution on CPU
    /// 3. Executed asynchronously after GPU processing
    /// </summary>
    public class ActionExecutor
    {
        private readonly Dictionary<int, Func<object, Task>> _actionMap;
        private readonly ConcurrentQueue<PendingAction> _actionQueue;
        private readonly Dictionary<string, int> _actionNameToId;
        private int _nextActionId;

        public ActionExecutor()
        {
            _actionMap = new Dictionary<int, Func<object, Task>>();
            _actionQueue = new ConcurrentQueue<PendingAction>();
            _actionNameToId = new Dictionary<string, int>();
            _nextActionId = 0;
        }

        /// <summary>
        /// Register an action handler
        /// </summary>
        public int RegisterAction(string actionName, Func<object, Task> handler)
        {
            if (_actionNameToId.TryGetValue(actionName, out int existingId))
            {
                return existingId;
            }

            int actionId = _nextActionId++;
            _actionMap[actionId] = handler;
            _actionNameToId[actionName] = actionId;
            return actionId;
        }

        /// <summary>
        /// Queue action for execution (called from GPU results)
        /// </summary>
        public void QueueAction(int instanceId, int actionId, object context = null)
        {
            _actionQueue.Enqueue(new PendingAction
            {
                InstanceId = instanceId,
                ActionId = actionId,
                Context = context
            });
        }

        /// <summary>
        /// Execute all pending actions
        /// </summary>
        public async Task ExecutePendingActionsAsync()
        {
            var tasks = new List<Task>();

            while (_actionQueue.TryDequeue(out var pending))
            {
                if (_actionMap.TryGetValue(pending.ActionId, out var handler))
                {
                    tasks.Add(ExecuteActionAsync(pending, handler));
                }
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
        }

        private async Task ExecuteActionAsync(PendingAction pending, Func<object, Task> handler)
        {
            try
            {
                await handler(new ActionContext
                {
                    InstanceId = pending.InstanceId,
                    Data = pending.Context
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Action execution failed for instance {pending.InstanceId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Get action ID by name
        /// </summary>
        public int GetActionId(string actionName)
        {
            return _actionNameToId.TryGetValue(actionName, out int id) ? id : -1;
        }

        /// <summary>
        /// Clear all registered actions
        /// </summary>
        public void Clear()
        {
            _actionMap.Clear();
            _actionNameToId.Clear();
            while (_actionQueue.TryDequeue(out _)) { }
        }

        private class PendingAction
        {
            public int InstanceId { get; set; }
            public int ActionId { get; set; }
            public object Context { get; set; }
        }
    }

    public class ActionContext
    {
        public int InstanceId { get; set; }
        public object Data { get; set; }
    }
}