using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace XStateNet;

/// <summary>
/// Minimal concurrency fixes for existing issues
/// </summary>
public static class ConcurrentFixes
{
    /// <summary>
    /// Thread-safe list wrapper for transition lists
    /// </summary>
    public class ConcurrentTransitionList
    {
        private readonly List<(CompoundState state, Transition transition, string eventName)> _list = new();
        private readonly ReaderWriterLockSlim _lock = new();
        
        public void Add((CompoundState state, Transition transition, string eventName) item)
        {
            _lock.EnterWriteLock();
            try
            {
                _list.Add(item);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
        
        public List<(CompoundState state, Transition transition, string eventName)> ToList()
        {
            _lock.EnterReadLock();
            try
            {
                return new List<(CompoundState state, Transition transition, string eventName)>(_list);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
        
        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                _list.Clear();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
    }
    
    /// <summary>
    /// Thread-safe wrapper for BuildTransitionList in parallel states
    /// </summary>
    public static void SafeBuildTransitionList(
        ParallelState parallelState,
        string eventName, 
        List<(CompoundState state, Transition transition, string eventName)> transitionList)
    {
        // Use thread-safe collection for parallel processing
        var concurrentList = new ConcurrentBag<(CompoundState state, Transition transition, string eventName)>();
        
        // Parent first (single-threaded)
        var parentTransitions = new List<(CompoundState state, Transition transition, string eventName)>();
        parallelState.BuildTransitionListBase(eventName, parentTransitions);
        
        foreach (var item in parentTransitions)
        {
            concurrentList.Add(item);
        }
        
        // Children in parallel (multi-threaded)
        Parallel.ForEach(parallelState.SubStateNames, subStateName =>
        {
            var subState = parallelState.GetState(subStateName);
            if (subState != null)
            {
                var localList = new List<(CompoundState state, Transition transition, string eventName)>();
                subState.BuildTransitionList(eventName, localList);
                
                foreach (var item in localList)
                {
                    concurrentList.Add(item);
                }
            }
        });
        
        // Add all to the final list
        transitionList.AddRange(concurrentList);
    }
}

/// <summary>
/// Extension methods for thread-safety
/// </summary>
public static class ThreadSafeExtensions
{
    private static readonly ConcurrentDictionary<object, ReaderWriterLockSlim> _locks = new();
    
    /// <summary>
    /// Get or create a lock for an object
    /// </summary>
    public static ReaderWriterLockSlim GetLock(this object obj)
    {
        return _locks.GetOrAdd(obj, _ => new ReaderWriterLockSlim());
    }
    
    /// <summary>
    /// Thread-safe property setter
    /// </summary>
    public static void SetThreadSafe<T>(this object obj, ref T field, T value)
    {
        var lockObj = obj.GetLock();
        lockObj.EnterWriteLock();
        try
        {
            field = value;
        }
        finally
        {
            lockObj.ExitWriteLock();
        }
    }
    
    /// <summary>
    /// Thread-safe property getter
    /// </summary>
    public static T GetThreadSafe<T>(this object obj, ref T field)
    {
        var lockObj = obj.GetLock();
        lockObj.EnterReadLock();
        try
        {
            return field;
        }
        finally
        {
            lockObj.ExitReadLock();
        }
    }
}