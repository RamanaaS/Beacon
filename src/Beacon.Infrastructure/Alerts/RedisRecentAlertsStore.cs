using System.Text.Json;
using Beacon.Application.Alerts;
using Beacon.Domain.Alerts;
using StackExchange.Redis;

namespace Beacon.Infrastructure.Alerts;

/// <summary>
/// Recent alerts for one fingerprint live in a Redis sorted set (score = ReceivedAt as a Unix
/// timestamp), so "give me everything in the last N minutes" is a single ZRANGEBYSCORE — this
/// needs a real data structure, not just a get/set cache, which is why this talks to
/// IConnectionMultiplexer directly instead of going through IDistributedCache.
/// </summary>
public sealed class RedisRecentAlertsStore(IConnectionMultiplexer redis) : IRecentAlertsStore
{
    private static string Key(string fingerprint) => $"beacon:recent-alerts:{fingerprint}";

    public async Task<IReadOnlyList<Alert>> GetRecentAsync(string fingerprint, TimeSpan window, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var cutoff = DateTimeOffset.UtcNow.Subtract(window).ToUnixTimeMilliseconds();

        var entries = await db.SortedSetRangeByScoreAsync(Key(fingerprint), cutoff);

        return entries
            .Select(entry => JsonSerializer.Deserialize<Alert>(entry!)!)
            .ToList();
    }

    public async Task RecordAsync(Alert alert, TimeSpan retention, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var key = Key(alert.Fingerprint);
        var score = alert.ReceivedAt.ToUnixTimeMilliseconds();

        await db.SortedSetAddAsync(key, JsonSerializer.Serialize(alert), score);
        await db.KeyExpireAsync(key, retention);

        // Nothing older than the retention window is reachable by GetRecentAsync anyway —
        // trim it so the set doesn't grow unbounded for a fingerprint that fires forever.
        var expiredBefore = DateTimeOffset.UtcNow.Subtract(retention).ToUnixTimeMilliseconds();
        await db.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, expiredBefore);
    }
}
