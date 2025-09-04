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
    public static void LogOptimized(Logger.LogLevel requiredLevel, Func<string> messageFactory)
    {
        if (Logger.CurrentLevel >= requiredLevel)
        {
            Logger.Log(messageFactory(), requiredLevel);
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
    private readonly Dictionary<string, StateNode> _lookupCache = new();
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