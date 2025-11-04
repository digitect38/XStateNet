namespace XStateNet2.Core.Builder;

/// <summary>
/// State machine optimization levels.
/// Controls the internal data structure used for O(N) → O(1) performance optimization.
/// </summary>
public enum OptimizationLevel
{
    /// <summary>
    /// Standard Dictionary-based state machine (baseline).
    /// Uses Dictionary&lt;string, T&gt; for state/event lookups (O(N) string hashing).
    /// Best for: Development, debugging, dynamic state machines.
    /// Performance: Baseline
    /// </summary>
    Dictionary = 0,

    /// <summary>
    /// FrozenDictionary optimization (default).
    /// Uses FrozenDictionary&lt;string, T&gt; for 10-15% faster lookups.
    /// Best for: Production workloads, balanced performance.
    /// Performance: +10-15% vs Dictionary
    /// </summary>
    FrozenDictionary = 1,

    /// <summary>
    /// Array-based byte-indexed state machine (maximum performance).
    /// Uses byte[] arrays for O(1) direct indexing (no string hashing).
    /// Best for: High-throughput systems, real-time control, benchmarks.
    /// Performance: +50-100% vs Dictionary, +40-85% vs FrozenDictionary
    /// Limitations: Requires static state machine structure
    /// </summary>
    Array = 2
}
