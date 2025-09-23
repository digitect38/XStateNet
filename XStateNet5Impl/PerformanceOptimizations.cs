using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace XStateNet;

/// <summary>
/// Performance optimizations for XStateNet
/// </summary>
public static class PerformanceOptimizations
{
    /// <summary>
    /// String cache for frequently used state paths
    /// </summary>
    private static readonly ConcurrentDictionary<(string?, string?), string> _transitionKeyCache = new();
    
    /// <summary>
    /// State cache to avoid repeated lookups
    /// </summary>
    private static readonly ConcurrentDictionary<(StateMachine, string), StateNode?> _stateCache = new();
    
    /// <summary>
    /// StringBuilder pool for string operations
    /// </summary>
    private static readonly ObjectPool<StringBuilder> _stringBuilderPool = new(() => new StringBuilder(256), 10);
    
    /// <summary>
    /// Pool for transition lists used in state machine transitions
    /// </summary>
    private static readonly ObjectPool<List<(CompoundState state, Transition transition, string @event)>> 
        _transitionListPool = new(() => new List<(CompoundState, Transition, string)>(16), 
            maxSize: 20, 
            resetAction: list => list.Clear());
    
    /// <summary>
    /// Pool for string lists used in path calculations
    /// </summary>
    private static readonly ObjectPool<List<string>> _stringListPool = 
        new(() => new List<string>(32), 
            maxSize: 50, 
            resetAction: list => list.Clear());
    
    /// <summary>
    /// Pool for HashSet used in transition execution tracking
    /// </summary>
    private static readonly ObjectPool<HashSet<(string?, string?)>> _transitionHashSetPool = 
        new(() => new HashSet<(string?, string?)>(), 
            maxSize: 20, 
            resetAction: set => set.Clear());
    
    /// <summary>
    /// Pool for System.Timers.Timer used in AfterTransitions
    /// </summary>
    private static readonly ObjectPool<System.Timers.Timer> _timerPool = 
        new(() => new System.Timers.Timer(), 
            maxSize: 50, 
            resetAction: timer => 
            {
                timer.Stop();
                timer.Enabled = false;
                timer.AutoReset = false;
                timer.Interval = 100; // Default interval
                // Remove all event handlers - use GetEvent instead of GetField
                var eventInfo = typeof(System.Timers.Timer).GetEvent("Elapsed");
                if (eventInfo != null)
                {
                    var field = typeof(System.Timers.Timer).GetField("Elapsed", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field == null)
                    {
                        // Try different field name for the backing field
                        field = typeof(System.Timers.Timer).GetField("onIntervalElapsed", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    }
                    if (field != null)
                    {
                        field.SetValue(timer, null);
                    }
                }
            });
    
    /// <summary>
    /// Get cached transition key
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetTransitionKey(string? source, string? target)
    {
        return _transitionKeyCache.GetOrAdd((source, target), 
            key => $"{key.Item1}->{key.Item2}");
    }
    
    /// <summary>
    /// Get cached state with single lookup
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StateNode? GetStateCached(StateMachine machine, string stateName)
    {
        return _stateCache.GetOrAdd((machine, stateName), 
            key => key.Item1.GetState(key.Item2));
    }
    
    /// <summary>
    /// Clear caches when state machine changes
    /// </summary>
    public static void ClearCache(StateMachine? machine = null)
    {
        if (machine == null)
        {
            _stateCache.Clear();
        }
        else
        {
            var keysToRemove = new List<(StateMachine, string)>();
            foreach (var key in _stateCache.Keys)
            {
                if (key.Item1 == machine)
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
            {
                _stateCache.TryRemove(key, out _);
            }
        }
    }
    
    /// <summary>
    /// Efficient string concatenation using pooled StringBuilder
    /// </summary>
    public static string BuildPath(params string?[] parts)
    {
        var sb = _stringBuilderPool.Rent();
        try
        {
            for (int i = 0; i < parts.Length; i++)
            {
                if (!string.IsNullOrEmpty(parts[i]))
                {
                    if (sb.Length > 0)
                        sb.Append('.');
                    sb.Append(parts[i]);
                }
            }
            return sb.ToString();
        }
        finally
        {
            _stringBuilderPool.Return(sb);
        }
    }
    
    /// <summary>
    /// Optimized logging that only builds strings when needed
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LogOptimized(Logger.LogLevel requiredLevel, Func<string> messageFactory,
        [System.Runtime.CompilerServices.CallerMemberName] string memberName = "",
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        if (Logger.CurrentLevel >= requiredLevel)
        {
            Logger.Log(messageFactory(), requiredLevel, memberName, filePath, lineNumber);
        }
    }
    
    /// <summary>
    /// Check if parallel processing is worth it based on collection size
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ShouldUseParallel<T>(ICollection<T> collection, int threshold = 4)
    {
        return collection.Count > threshold && Environment.ProcessorCount > 1;
    }
    
    /// <summary>
    /// Rent a transition list from the pool
    /// </summary>
    public static List<(CompoundState state, Transition transition, string @event)> RentTransitionList()
    {
        return _transitionListPool.Rent();
    }
    
    /// <summary>
    /// Return a transition list to the pool
    /// </summary>
    public static void ReturnTransitionList(List<(CompoundState state, Transition transition, string @event)> list)
    {
        _transitionListPool.Return(list);
    }
    
    /// <summary>
    /// Rent a string list from the pool
    /// </summary>
    public static List<string> RentStringList()
    {
        return _stringListPool.Rent();
    }
    
    /// <summary>
    /// Return a string list to the pool
    /// </summary>
    public static void ReturnStringList(List<string> list)
    {
        _stringListPool.Return(list);
    }
    
    /// <summary>
    /// Rent a HashSet for transition tracking from the pool
    /// </summary>
    public static HashSet<(string?, string?)> RentTransitionHashSet()
    {
        return _transitionHashSetPool.Rent();
    }
    
    /// <summary>
    /// Return a HashSet to the pool
    /// </summary>
    public static void ReturnTransitionHashSet(HashSet<(string?, string?)> set)
    {
        _transitionHashSetPool.Return(set);
    }
    
    /// <summary>
    /// Rent a Timer from the pool
    /// </summary>
    public static System.Timers.Timer RentTimer()
    {
        return _timerPool.Rent();
    }
    
    /// <summary>
    /// Return a Timer to the pool
    /// </summary>
    public static void ReturnTimer(System.Timers.Timer timer)
    {
        _timerPool.Return(timer);
    }
}

/// <summary>
/// Simple object pool implementation
/// </summary>
public class ObjectPool<T> where T : class
{
    private readonly ConcurrentBag<T> _objects = new();
    private readonly Func<T> _objectGenerator;
    private readonly int _maxSize;
    private readonly Action<T>? _resetAction;
    
    public ObjectPool(Func<T> objectGenerator, int maxSize = 50, Action<T>? resetAction = null)
    {
        _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
        _maxSize = maxSize;
        _resetAction = resetAction;
    }
    
    public T Rent()
    {
        return _objects.TryTake(out T? item) ? item : _objectGenerator();
    }
    
    public void Return(T item)
    {
        if (_resetAction != null)
        {
            _resetAction(item);
        }
        else if (item is StringBuilder sb)
        {
            sb.Clear();
        }
        
        if (_objects.Count < _maxSize)
        {
            _objects.Add(item);
        }
    }
}

/// <summary>
/// Optimized state lookup with batching
/// </summary>
public class StateLookupOptimizer
{
    private readonly ConcurrentDictionary<string, StateNode> _lookupCache = new();
    private readonly StateMachine _machine;
    
    public StateLookupOptimizer(StateMachine machine)
    {
        _machine = machine;
    }
    
    /// <summary>
    /// Pre-cache frequently accessed states
    /// </summary>
    public void PreCacheStates(IEnumerable<string> stateNames)
    {
        foreach (var name in stateNames)
        {
            if (!_lookupCache.ContainsKey(name))
            {
                try
                {
                    _lookupCache[name] = _machine.GetState(name);
                }
                catch
                {
                    // State not found, skip
                }
            }
        }
    }
    
    /// <summary>
    /// Get state from cache or fetch
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StateNode? GetState(string stateName)
    {
        if (_lookupCache.TryGetValue(stateName, out var cached))
            return cached;
        
        var state = _machine.GetState(stateName);
        _lookupCache[stateName] = state;
        return state;
    }
    
    public void Clear()
    {
        _lookupCache.Clear();
    }
}

/// <summary>
/// Lazy evaluation for expensive operations
/// </summary>
public class LazyStateInfo
{
    private readonly Lazy<string> _fullPath;
    private readonly Lazy<List<string>> _activeStates;
    private readonly CompoundState _state;
    
    public LazyStateInfo(CompoundState state)
    {
        _state = state;
        _fullPath = new Lazy<string>(() => ComputeFullPath());
        _activeStates = new Lazy<List<string>>(() => ComputeActiveStates());
    }
    
    public string FullPath => _fullPath.Value;
    public List<string> ActiveStates => _activeStates.Value;
    
    private string ComputeFullPath()
    {
        var parts = new List<string>();
        var current = _state;
        while (current != null)
        {
            parts.Insert(0, current.Name ?? "");
            current = current.Parent as CompoundState;
        }
        return string.Join(".", parts);
    }
    
    private List<string> ComputeActiveStates()
    {
        var list = new List<string>();
        _state.GetActiveSubStateNames(list);
        return list;
    }
}