using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace XStateNet.Semi.Transport
{
    /// <summary>
    /// Connection pool for managing multiple HSMS connections efficiently
    /// </summary>
    public class HsmsConnectionPool : IDisposable
    {
        private readonly ConcurrentDictionary<string, ConnectionPoolEntry> _pools = new();
        private readonly ILogger<HsmsConnectionPool>? _logger;
        private readonly Timer _cleanupTimer;
        private readonly object _statsLock = new();
        private bool _disposed;
        
        // Pool configuration
        public int MinConnectionsPerEndpoint { get; set; } = 1;
        public int MaxConnectionsPerEndpoint { get; set; } = 10;
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);
        public bool EnableStatistics { get; set; } = true;
        
        // Statistics
        public PoolStatistics Statistics { get; private set; } = new();
        
        public HsmsConnectionPool(ILogger<HsmsConnectionPool>? logger = null)
        {
            _logger = logger;
            _cleanupTimer = new Timer(CleanupIdleConnections, null, CleanupInterval, CleanupInterval);
        }
        
        /// <summary>
        /// Get or create a connection from the pool
        /// </summary>
        public async Task<PooledConnection> GetConnectionAsync(
            IPEndPoint endpoint,
            HsmsConnection.HsmsConnectionMode mode,
            CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HsmsConnectionPool));
                
            var key = GetPoolKey(endpoint, mode);
            var pool = _pools.GetOrAdd(key, k => new ConnectionPoolEntry(endpoint, mode, this));
            
            return await pool.GetConnectionAsync(cancellationToken);
        }
        
        /// <summary>
        /// Return a connection to the pool
        /// </summary>
        internal void ReturnConnection(PooledConnection connection)
        {
            if (_disposed || !connection.IsHealthy)
            {
                connection.Dispose();
                return;
            }
            
            var key = GetPoolKey(connection.Endpoint, connection.Mode);
            if (_pools.TryGetValue(key, out var pool))
            {
                pool.ReturnConnection(connection);
            }
            else
            {
                connection.Dispose();
            }
        }
        
        /// <summary>
        /// Clean up idle connections periodically
        /// </summary>
        private void CleanupIdleConnections(object? state)
        {
            try
            {
                var now = DateTime.UtcNow;
                var removedCount = 0;
                
                foreach (var pool in _pools.Values)
                {
                    removedCount += pool.CleanupIdleConnections(now, IdleTimeout);
                }
                
                if (removedCount > 0)
                {
                    _logger?.LogInformation("Cleaned up {Count} idle connections", removedCount);
                    Statistics.IdleConnectionsCleaned += removedCount;
                }
                
                // Remove empty pools
                var emptyPools = _pools.Where(kvp => kvp.Value.IsEmpty).Select(kvp => kvp.Key).ToList();
                foreach (var key in emptyPools)
                {
                    if (_pools.TryRemove(key, out var pool))
                    {
                        pool.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during cleanup");
            }
        }
        
        private string GetPoolKey(IPEndPoint endpoint, HsmsConnection.HsmsConnectionMode mode)
        {
            return $"{endpoint.Address}:{endpoint.Port}:{mode}";
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            _disposed = true;
            _cleanupTimer?.Dispose();
            
            foreach (var pool in _pools.Values)
            {
                pool.Dispose();
            }
            
            _pools.Clear();
        }
        
        /// <summary>
        /// Individual connection pool for a specific endpoint
        /// </summary>
        private class ConnectionPoolEntry : IDisposable
        {
            private readonly IPEndPoint _endpoint;
            private readonly HsmsConnection.HsmsConnectionMode _mode;
            private readonly HsmsConnectionPool _parent;
            private readonly ConcurrentBag<PooledConnection> _available = new();
            private readonly HashSet<PooledConnection> _inUse = new();
            private readonly SemaphoreSlim _connectionSemaphore;
            private readonly object _lock = new();
            private int _totalConnections;
            private bool _disposed;
            
            public bool IsEmpty => _totalConnections == 0;
            
            public ConnectionPoolEntry(IPEndPoint endpoint, HsmsConnection.HsmsConnectionMode mode, HsmsConnectionPool parent)
            {
                _endpoint = endpoint;
                _mode = mode;
                _parent = parent;
                _connectionSemaphore = new SemaphoreSlim(parent.MaxConnectionsPerEndpoint);
            }
            
            public async Task<PooledConnection> GetConnectionAsync(CancellationToken cancellationToken)
            {
                // Try to get an existing connection
                while (_available.TryTake(out var existing))
                {
                    if (existing.IsHealthy && existing.Connection.IsConnected)
                    {
                        lock (_lock)
                        {
                            _inUse.Add(existing);
                        }
                        
                        existing.LastUsedTime = DateTime.UtcNow;
                        _parent.Statistics.ConnectionsReused++;
                        return existing;
                    }
                    else
                    {
                        existing.Dispose();
                        Interlocked.Decrement(ref _totalConnections);
                    }
                }
                
                // Create new connection if under limit
                if (_totalConnections < _parent.MaxConnectionsPerEndpoint)
                {
                    await _connectionSemaphore.WaitAsync(cancellationToken);
                    try
                    {
                        if (_totalConnections < _parent.MaxConnectionsPerEndpoint)
                        {
                            var connection = await CreateConnectionAsync(cancellationToken);
                            var pooled = new PooledConnection(connection, _endpoint, _mode, _parent);
                            
                            lock (_lock)
                            {
                                _inUse.Add(pooled);
                            }
                            
                            Interlocked.Increment(ref _totalConnections);
                            _parent.Statistics.ConnectionsCreated++;
                            return pooled;
                        }
                    }
                    finally
                    {
                        _connectionSemaphore.Release();
                    }
                }
                
                // Wait for available connection
                var waitStart = DateTime.UtcNow;
                while (DateTime.UtcNow - waitStart < _parent.ConnectionTimeout)
                {
                    if (_available.TryTake(out var available))
                    {
                        if (available.IsHealthy && available.Connection.IsConnected)
                        {
                            lock (_lock)
                            {
                                _inUse.Add(available);
                            }
                            
                            available.LastUsedTime = DateTime.UtcNow;
                            _parent.Statistics.ConnectionsReused++;
                            return available;
                        }
                        else
                        {
                            available.Dispose();
                            Interlocked.Decrement(ref _totalConnections);
                        }
                    }
                    
                    await Task.Delay(100, cancellationToken);
                }
                
                throw new TimeoutException($"Could not obtain connection within {_parent.ConnectionTimeout}");
            }
            
            public void ReturnConnection(PooledConnection connection)
            {
                lock (_lock)
                {
                    _inUse.Remove(connection);
                }
                
                if (connection.IsHealthy && !_disposed)
                {
                    connection.LastUsedTime = DateTime.UtcNow;
                    _available.Add(connection);
                    _parent.Statistics.ConnectionsReturned++;
                }
                else
                {
                    connection.Dispose();
                    Interlocked.Decrement(ref _totalConnections);
                }
            }
            
            public int CleanupIdleConnections(DateTime now, TimeSpan idleTimeout)
            {
                var removed = 0;
                var toRemove = new List<PooledConnection>();
                
                // Check available connections
                var temp = new List<PooledConnection>();
                while (_available.TryTake(out var connection))
                {
                    if (now - connection.LastUsedTime > idleTimeout || !connection.IsHealthy)
                    {
                        toRemove.Add(connection);
                        removed++;
                    }
                    else
                    {
                        temp.Add(connection);
                    }
                }
                
                // Return healthy connections
                foreach (var connection in temp)
                {
                    _available.Add(connection);
                }
                
                // Dispose idle connections
                foreach (var connection in toRemove)
                {
                    connection.Dispose();
                    Interlocked.Decrement(ref _totalConnections);
                }
                
                // Ensure minimum connections
                while (_totalConnections < _parent.MinConnectionsPerEndpoint && !_disposed)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            var connection = await CreateConnectionAsync(CancellationToken.None);
                            var pooled = new PooledConnection(connection, _endpoint, _mode, _parent);
                            _available.Add(pooled);
                            Interlocked.Increment(ref _totalConnections);
                        }
                        catch
                        {
                            // Ignore errors during background creation
                        }
                    });
                    
                    Interlocked.Increment(ref _totalConnections); // Optimistic increment
                }
                
                return removed;
            }
            
            private async Task<ResilientHsmsConnection> CreateConnectionAsync(CancellationToken cancellationToken)
            {
                var connection = new ResilientHsmsConnection(_endpoint, _mode, null);
                await connection.ConnectAsync(cancellationToken);
                return connection;
            }
            
            public void Dispose()
            {
                if (_disposed)
                    return;
                    
                _disposed = true;
                
                lock (_lock)
                {
                    foreach (var connection in _inUse)
                    {
                        connection.Dispose();
                    }
                    _inUse.Clear();
                }
                
                while (_available.TryTake(out var connection))
                {
                    connection.Dispose();
                }
                
                _connectionSemaphore?.Dispose();
            }
        }
        
        /// <summary>
        /// Pool statistics
        /// </summary>
        public class PoolStatistics
        {
            public int ConnectionsCreated { get; set; }
            public int ConnectionsReused { get; set; }
            public int ConnectionsReturned { get; set; }
            public int IdleConnectionsCleaned { get; set; }
            public int ActiveConnections => ConnectionsCreated - IdleConnectionsCleaned;
            public double ReuseRate => ConnectionsCreated > 0 
                ? (double)ConnectionsReused / (ConnectionsCreated + ConnectionsReused) 
                : 0;
        }
    }
    
    /// <summary>
    /// Wrapper for pooled connections
    /// </summary>
    public class PooledConnection : IDisposable
    {
        private readonly HsmsConnectionPool _pool;
        private bool _disposed;
        
        public ResilientHsmsConnection Connection { get; }
        public IPEndPoint Endpoint { get; }
        public HsmsConnection.HsmsConnectionMode Mode { get; }
        public DateTime CreatedTime { get; }
        public DateTime LastUsedTime { get; set; }
        public bool IsHealthy => Connection.IsConnected && Connection.Health != ConnectionHealth.Critical;
        
        internal PooledConnection(
            ResilientHsmsConnection connection,
            IPEndPoint endpoint,
            HsmsConnection.HsmsConnectionMode mode,
            HsmsConnectionPool pool)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            Endpoint = endpoint;
            Mode = mode;
            _pool = pool;
            CreatedTime = DateTime.UtcNow;
            LastUsedTime = DateTime.UtcNow;
        }
        
        /// <summary>
        /// Return connection to pool when disposed
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
                
            _disposed = true;
            
            if (_pool != null && IsHealthy)
            {
                _pool.ReturnConnection(this);
            }
            else
            {
                Connection?.Dispose();
            }
        }
    }
}