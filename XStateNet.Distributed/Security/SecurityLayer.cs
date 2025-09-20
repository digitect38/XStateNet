using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;

namespace XStateNet.Distributed.Security
{
    /// <summary>
    /// High-performance security layer with JWT authentication and role-based authorization
    /// </summary>
    public sealed class SecurityLayer : ISecurityLayer
    {
        private readonly SecurityOptions _options;
        private readonly JwtSecurityTokenHandler _tokenHandler;
        private readonly TokenValidationParameters _validationParameters;
        private readonly SigningCredentials _signingCredentials;

        // Cache for validated tokens to avoid repeated validation
        private readonly ConcurrentDictionary<string, (ClaimsPrincipal principal, DateTime expiry)> _tokenCache;
        private readonly Timer _cacheCleanupTimer;

        // Permission matrix for fast authorization checks
        private readonly ConcurrentDictionary<string, HashSet<Permission>> _rolePermissions;
        private readonly ConcurrentDictionary<string, RateLimitInfo> _rateLimits;

        // Metrics
        private long _authenticationAttempts;
        private long _authenticationSuccesses;
        private long _authorizationChecks;
        private long _rateLimitHits;

        public SecurityLayer(SecurityOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _tokenHandler = new JwtSecurityTokenHandler();
            _tokenCache = new ConcurrentDictionary<string, (ClaimsPrincipal, DateTime)>(StringComparer.Ordinal);
            _rolePermissions = new ConcurrentDictionary<string, HashSet<Permission>>(StringComparer.OrdinalIgnoreCase);
            _rateLimits = new ConcurrentDictionary<string, RateLimitInfo>(StringComparer.Ordinal);

            // Initialize signing credentials
            var key = Encoding.UTF8.GetBytes(options.SecretKey);
            _signingCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature);

            // Initialize validation parameters
            _validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _signingCredentials.Key,
                ValidateIssuer = true,
                ValidIssuer = options.Issuer,
                ValidateAudience = true,
                ValidAudience = options.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };

            // Initialize default roles and permissions
            InitializeDefaultPermissions();

            // Start cache cleanup timer
            _cacheCleanupTimer = new Timer(
                CleanupExpiredTokens,
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<AuthenticationResult> AuthenticateAsync(string token, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _authenticationAttempts);

            if (string.IsNullOrWhiteSpace(token))
            {
                return AuthenticationResult.Failure("Token is required");
            }

            try
            {
                // Check cache first
                if (_tokenCache.TryGetValue(token, out var cached))
                {
                    if (cached.expiry > DateTime.UtcNow)
                    {
                        Interlocked.Increment(ref _authenticationSuccesses);
                        return AuthenticationResult.Success(cached.principal);
                    }

                    // Remove expired entry
                    _tokenCache.TryRemove(token, out _);
                }

                // Validate token
                var principal = await Task.Run(() =>
                {
                    return _tokenHandler.ValidateToken(token, _validationParameters, out _);
                }, cancellationToken);

                // Extract expiry from claims
                var expiryClaim = principal.FindFirst(JwtRegisteredClaimNames.Exp);
                var expiry = expiryClaim != null
                    ? DateTimeOffset.FromUnixTimeSeconds(long.Parse(expiryClaim.Value)).UtcDateTime
                    : DateTime.UtcNow.AddMinutes(5);

                // Cache the validated token
                _tokenCache.TryAdd(token, (principal, expiry));

                Interlocked.Increment(ref _authenticationSuccesses);
                return AuthenticationResult.Success(principal);
            }
            catch (SecurityTokenExpiredException)
            {
                return AuthenticationResult.Failure("Token has expired");
            }
            catch (SecurityTokenInvalidSignatureException)
            {
                return AuthenticationResult.Failure("Invalid token signature");
            }
            catch (Exception ex)
            {
                return AuthenticationResult.Failure($"Authentication failed: {ex.Message}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal principal,
            string resource,
            string action,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _authorizationChecks);

            if (principal == null || !principal.Identity!.IsAuthenticated)
            {
                return Task.FromResult(AuthorizationResult.Failure("User is not authenticated"));
            }

            // Extract user roles
            var roles = GetUserRoles(principal);
            if (roles.Count == 0 && !_options.AllowAnonymousAccess)
            {
                return Task.FromResult(AuthorizationResult.Failure("User has no roles assigned"));
            }

            // Check if any role has the required permission
            var requiredPermission = new Permission(resource, action);
            foreach (var role in roles)
            {
                if (_rolePermissions.TryGetValue(role, out var permissions))
                {
                    if (permissions.Contains(requiredPermission) ||
                        permissions.Contains(new Permission("*", "*")) || // Super admin
                        permissions.Contains(new Permission(resource, "*")) || // Resource admin
                        permissions.Contains(new Permission("*", action))) // Action admin
                    {
                        return Task.FromResult(AuthorizationResult.Success());
                    }
                }
            }

            // Check user-specific permissions
            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                var userPermissionKey = $"user:{userId}";
                if (_rolePermissions.TryGetValue(userPermissionKey, out var userPermissions))
                {
                    if (userPermissions.Contains(requiredPermission))
                    {
                        return Task.FromResult(AuthorizationResult.Success());
                    }
                }
            }

            return Task.FromResult(AuthorizationResult.Failure(
                $"User lacks permission to perform '{action}' on '{resource}'"));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CheckRateLimit(string clientId, int requestsPerMinute = 60)
        {
            var now = DateTime.UtcNow;
            var rateLimitInfo = _rateLimits.AddOrUpdate(
                clientId,
                new RateLimitInfo { WindowStart = now, RequestCount = 1 },
                (key, existing) =>
                {
                    // Reset window if expired
                    if (now - existing.WindowStart > TimeSpan.FromMinutes(1))
                    {
                        existing.WindowStart = now;
                        existing.RequestCount = 1;
                    }
                    else
                    {
                        existing.RequestCount++;
                    }
                    return existing;
                });

            if (rateLimitInfo.RequestCount > requestsPerMinute)
            {
                Interlocked.Increment(ref _rateLimitHits);
                return false;
            }

            return true;
        }

        public string GenerateToken(ClaimsPrincipal principal, TimeSpan? expiry = null)
        {
            var tokenExpiry = expiry ?? _options.DefaultTokenExpiry;
            var now = DateTime.UtcNow;

            var claims = new List<Claim>(principal.Claims)
            {
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat,
                    new DateTimeOffset(now).ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64)
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = now.Add(tokenExpiry),
                Issuer = _options.Issuer,
                Audience = _options.Audience,
                SigningCredentials = _signingCredentials
            };

            var token = _tokenHandler.CreateToken(tokenDescriptor);
            return _tokenHandler.WriteToken(token);
        }

        public string HashPassword(string password)
        {
            using var hasher = SHA256.Create();
            var salt = GenerateSalt();
            var combined = Encoding.UTF8.GetBytes(password + salt);
            var hash = hasher.ComputeHash(combined);
            return $"{salt}:{Convert.ToBase64String(hash)}";
        }

        public bool VerifyPassword(string password, string hashedPassword)
        {
            var parts = hashedPassword.Split(':');
            if (parts.Length != 2)
                return false;

            var salt = parts[0];
            var storedHash = parts[1];

            using var hasher = SHA256.Create();
            var combined = Encoding.UTF8.GetBytes(password + salt);
            var hash = hasher.ComputeHash(combined);
            var computedHash = Convert.ToBase64String(hash);

            return string.Equals(storedHash, computedHash, StringComparison.Ordinal);
        }

        public void GrantPermission(string role, string resource, string action)
        {
            var permission = new Permission(resource, action);
            _rolePermissions.AddOrUpdate(
                role,
                new HashSet<Permission> { permission },
                (key, existing) =>
                {
                    existing.Add(permission);
                    return existing;
                });
        }

        public void RevokePermission(string role, string resource, string action)
        {
            if (_rolePermissions.TryGetValue(role, out var permissions))
            {
                permissions.Remove(new Permission(resource, action));
            }
        }

        public SecurityMetrics GetMetrics()
        {
            return new SecurityMetrics
            {
                AuthenticationAttempts = _authenticationAttempts,
                AuthenticationSuccesses = _authenticationSuccesses,
                AuthorizationChecks = _authorizationChecks,
                RateLimitHits = _rateLimitHits,
                CachedTokens = _tokenCache.Count,
                ActiveRateLimits = _rateLimits.Count
            };
        }

        private void InitializeDefaultPermissions()
        {
            // Admin role - full access
            GrantPermission("Admin", "*", "*");

            // Operator role - manage machines
            GrantPermission("Operator", "StateMachine", "Start");
            GrantPermission("Operator", "StateMachine", "Stop");
            GrantPermission("Operator", "StateMachine", "SendEvent");
            GrantPermission("Operator", "StateMachine", "Read");

            // Viewer role - read-only access
            GrantPermission("Viewer", "StateMachine", "Read");
            GrantPermission("Viewer", "Metrics", "Read");
            GrantPermission("Viewer", "Logs", "Read");

            // Developer role - development operations
            GrantPermission("Developer", "StateMachine", "*");
            GrantPermission("Developer", "Debug", "*");
            GrantPermission("Developer", "Metrics", "Read");
            GrantPermission("Developer", "Logs", "Read");
        }

        private HashSet<string> GetUserRoles(ClaimsPrincipal principal)
        {
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var claim in principal.Claims)
            {
                if (claim.Type == ClaimTypes.Role || claim.Type == "role")
                {
                    roles.Add(claim.Value);
                }
            }

            return roles;
        }

        private static string GenerateSalt()
        {
            var saltBytes = new byte[16];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(saltBytes);
            return Convert.ToBase64String(saltBytes);
        }

        private void CleanupExpiredTokens(object? state)
        {
            var now = DateTime.UtcNow;
            var expiredTokens = new List<string>();

            foreach (var kvp in _tokenCache)
            {
                if (kvp.Value.expiry <= now)
                {
                    expiredTokens.Add(kvp.Key);
                }
            }

            foreach (var token in expiredTokens)
            {
                _tokenCache.TryRemove(token, out _);
            }

            // Cleanup old rate limit entries
            var oldRateLimits = new List<string>();
            foreach (var kvp in _rateLimits)
            {
                if (now - kvp.Value.WindowStart > TimeSpan.FromMinutes(2))
                {
                    oldRateLimits.Add(kvp.Key);
                }
            }

            foreach (var clientId in oldRateLimits)
            {
                _rateLimits.TryRemove(clientId, out _);
            }
        }

        public void Dispose()
        {
            _cacheCleanupTimer?.Dispose();
            _tokenCache.Clear();
            _rateLimits.Clear();
        }
    }

    public interface ISecurityLayer : IDisposable
    {
        Task<AuthenticationResult> AuthenticateAsync(string token, CancellationToken cancellationToken = default);
        Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal principal, string resource, string action, CancellationToken cancellationToken = default);
        bool CheckRateLimit(string clientId, int requestsPerMinute = 60);
        string GenerateToken(ClaimsPrincipal principal, TimeSpan? expiry = null);
        string HashPassword(string password);
        bool VerifyPassword(string password, string hashedPassword);
        void GrantPermission(string role, string resource, string action);
        void RevokePermission(string role, string resource, string action);
        SecurityMetrics GetMetrics();
    }

    public class SecurityOptions
    {
        public string SecretKey { get; set; } = string.Empty;
        public string Issuer { get; set; } = "XStateNet";
        public string Audience { get; set; } = "XStateNet.Client";
        public TimeSpan DefaultTokenExpiry { get; set; } = TimeSpan.FromHours(1);
        public bool AllowAnonymousAccess { get; set; } = false;
    }

    public class AuthenticationResult
    {
        public bool IsAuthenticated { get; }
        public ClaimsPrincipal? Principal { get; }
        public string? ErrorMessage { get; }

        private AuthenticationResult(bool isAuthenticated, ClaimsPrincipal? principal, string? errorMessage)
        {
            IsAuthenticated = isAuthenticated;
            Principal = principal;
            ErrorMessage = errorMessage;
        }

        public static AuthenticationResult Success(ClaimsPrincipal principal) =>
            new(true, principal, null);

        public static AuthenticationResult Failure(string errorMessage) =>
            new(false, null, errorMessage);
    }

    public class AuthorizationResult
    {
        public bool IsAuthorized { get; }
        public string? ErrorMessage { get; }

        private AuthorizationResult(bool isAuthorized, string? errorMessage)
        {
            IsAuthorized = isAuthorized;
            ErrorMessage = errorMessage;
        }

        public static AuthorizationResult Success() => new(true, null);
        public static AuthorizationResult Failure(string errorMessage) => new(false, errorMessage);
    }

    internal class Permission : IEquatable<Permission>
    {
        public string Resource { get; }
        public string Action { get; }

        public Permission(string resource, string action)
        {
            Resource = resource ?? throw new ArgumentNullException(nameof(resource));
            Action = action ?? throw new ArgumentNullException(nameof(action));
        }

        public bool Equals(Permission? other)
        {
            if (other == null) return false;
            return string.Equals(Resource, other.Resource, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Action, other.Action, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj) => Equals(obj as Permission);
        public override int GetHashCode() => HashCode.Combine(Resource.ToUpperInvariant(), Action.ToUpperInvariant());
    }

    internal class RateLimitInfo
    {
        public DateTime WindowStart { get; set; }
        public int RequestCount { get; set; }
    }

    public class SecurityMetrics
    {
        public long AuthenticationAttempts { get; set; }
        public long AuthenticationSuccesses { get; set; }
        public long AuthorizationChecks { get; set; }
        public long RateLimitHits { get; set; }
        public int CachedTokens { get; set; }
        public int ActiveRateLimits { get; set; }
    }
}