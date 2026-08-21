using System.Text.Json;
using Beacon.Application.Alerts;
using Beacon.Domain.Alerts;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using StackExchange.Redis;

namespace Beacon.Infrastructure.Alerts;

/// <summary>
/// Recent alerts for one fingerprint live in a Redis sorted set (score = ReceivedAt as a Unix
/// timestamp), so "give me everything in the last N minutes" is a single ZRANGEBYSCORE — this
/// needs a real data structure, not just a get/set cache, which is why this talks to
/// IConnectionMultiplexer directly instead of going through IDistributedCache.
///
/// Two layers of resilience here, added in that order for two different reasons:
///
/// 1. `abortConnect=false` + tightened `connectTimeout`/`syncTimeout` (DependencyInjection.cs)
///    stop the initial Connect() call, and each individual command, from throwing/hanging
///    indefinitely when Redis is unreachable.
/// 2. That alone still wasn't enough, discovered by actually timing a real request against this
///    machine's permanently-down Redis (Docker's daemon has never come up across this whole
///    project — see LEARNING_GUIDE.md's Phase 3 entry): every command still individually pays the
///    full connect/sync timeout before failing, because a disconnected multiplexer retries the
///    handshake on every call, not once. A single alert-ingestion request was taking 11+ seconds
///    end to end — for a tool whose whole job is being fast during an active incident, that's not
///    a survivable degradation, it's a different outage. A Polly circuit breaker fixes the actual
///    problem: after Redis fails once, stop even trying for a cooldown window and fail open
///    immediately, instead of re-paying the timeout on every single request.
/// </summary>
public sealed class RedisRecentAlertsStore : IRecentAlertsStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisRecentAlertsStore> _logger;
    private readonly ResiliencePipeline _breaker;

    public RedisRecentAlertsStore(IConnectionMultiplexer redis, ILogger<RedisRecentAlertsStore> logger)
    {
        _redis = redis;
        _logger = logger;

        _breaker = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new Polly.CircuitBreaker.CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<RedisConnectionException>().Handle<RedisTimeoutException>(),
                FailureRatio = 1.0,
                MinimumThroughput = 2, // Polly v8's floor - a single call can't statistically open a circuit
                SamplingDuration = TimeSpan.FromSeconds(1),
                // 30s: long enough to stop hammering a Redis that's genuinely down, short enough
                // that a real recovery (Docker finally starting, in this project's case) gets
                // picked back up well within a demo/dev session rather than needing a restart.
                BreakDuration = TimeSpan.FromSeconds(30),
                OnOpened = args =>
                {
                    _logger.LogWarning(
                        "Redis circuit breaker opened after a failure ({Exception}) — failing open for {BreakDuration}",
                        args.Outcome.Exception?.GetType().Name, args.BreakDuration);
                    return default;
                },
                OnClosed = _ =>
                {
                    _logger.LogInformation("Redis circuit breaker closed — dedup store reachable again");
                    return default;
                }
            })
            .Build();
    }

    private static string Key(string fingerprint) => $"beacon:recent-alerts:{fingerprint}";

    public async Task<IReadOnlyList<Alert>> GetRecentAsync(string fingerprint, TimeSpan window, CancellationToken ct = default)
    {
        try
        {
            return await _breaker.ExecuteAsync(async token =>
            {
                var db = _redis.GetDatabase();
                var cutoff = DateTimeOffset.UtcNow.Subtract(window).ToUnixTimeMilliseconds();

                var entries = await db.SortedSetRangeByScoreAsync(Key(fingerprint), cutoff);

                return (IReadOnlyList<Alert>)entries
                    .Select(entry => JsonSerializer.Deserialize<Alert>(entry!)!)
                    .ToList();
            }, ct);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or BrokenCircuitException)
        {
            _logger.LogWarning(ex, "Redis unavailable — treating {Fingerprint} as having no recent alerts on record", fingerprint);
            return [];
        }
    }

    public async Task RecordAsync(Alert alert, TimeSpan retention, CancellationToken ct = default)
    {
        try
        {
            await _breaker.ExecuteAsync(async token =>
            {
                var db = _redis.GetDatabase();
                var key = Key(alert.Fingerprint);
                var score = alert.ReceivedAt.ToUnixTimeMilliseconds();

                await db.SortedSetAddAsync(key, JsonSerializer.Serialize(alert), score);
                await db.KeyExpireAsync(key, retention);

                // Nothing older than the retention window is reachable by GetRecentAsync anyway —
                // trim it so the set doesn't grow unbounded for a fingerprint that fires forever.
                var expiredBefore = DateTimeOffset.UtcNow.Subtract(retention).ToUnixTimeMilliseconds();
                await db.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, expiredBefore);
            }, ct);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or BrokenCircuitException)
        {
            // Best-effort recording: losing one dedup entry during a Redis outage just means the
            // next occurrence of this same alert within the window won't be deduplicated either —
            // degraded, not incorrect, and never worth failing the alert-ingestion request over.
            _logger.LogWarning(ex, "Redis unavailable — could not record {Fingerprint} for future dedup", alert.Fingerprint);
        }
    }
}
