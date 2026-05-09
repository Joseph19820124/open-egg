using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenEgg.Configuration;

namespace OpenEgg.Acp;

public sealed class AcpSessionPool
{
    private readonly ConcurrentDictionary<string, Lazy<Task<AcpConnection>>> _sessions = new();
    private readonly AgentOptions _agentOptions;
    private readonly PoolOptions _poolOptions;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<AcpSessionPool> _logger;

    public AcpSessionPool(
        IOptions<AgentOptions> agentOptions,
        IOptions<PoolOptions> poolOptions,
        ILoggerFactory loggerFactory,
        ILogger<AcpSessionPool> logger)
    {
        _agentOptions = agentOptions.Value;
        _poolOptions = poolOptions.Value;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public async Task<AcpConnection> GetOrCreateAsync(string sessionKey, CancellationToken cancellationToken)
    {
        if (_sessions.Count >= _poolOptions.MaxSessions && !_sessions.ContainsKey(sessionKey))
        {
            await CleanupIdleAsync(forceOne: true);
        }

        var lazy = _sessions.GetOrAdd(sessionKey, key => new Lazy<Task<AcpConnection>>(
            () => AcpConnection.SpawnAsync(
                _agentOptions,
                _loggerFactory.CreateLogger<AcpConnection>(),
                cancellationToken)));

        try
        {
            return await lazy.Value;
        }
        catch
        {
            _sessions.TryRemove(sessionKey, out _);
            throw;
        }
    }

    public async Task CleanupIdleAsync(bool forceOne = false)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_poolOptions.SessionTtlMinutes);
        var candidates = _sessions
            .Select(pair => new { pair.Key, pair.Value, Ready = pair.Value.IsValueCreated && pair.Value.Value.IsCompletedSuccessfully })
            .Where(item => item.Ready)
            .Select(item => new { item.Key, Connection = item.Value.Value.Result })
            .Where(item => forceOne || item.Connection.LastActiveUtc < cutoff)
            .OrderBy(item => item.Connection.LastActiveUtc)
            .Take(forceOne ? 1 : int.MaxValue)
            .ToList();

        foreach (var candidate in candidates)
        {
            if (_sessions.TryRemove(candidate.Key, out _))
            {
                _logger.LogInformation("Disposing idle ACP session for {SessionKey}.", candidate.Key);
                await candidate.Connection.DisposeAsync();
            }
        }
    }
}
