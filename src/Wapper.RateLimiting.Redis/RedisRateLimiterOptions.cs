namespace Wapper.RateLimiting.Redis;

/// <summary>How the shared limiter talks to Redis.</summary>
public sealed class RedisRateLimiterOptions
{
    /// <summary>
    /// Prefix for every key the limiter writes, so it can share a database with other things.
    /// </summary>
    public string KeyPrefix { get; set; } = "wapper:rl:";

    /// <summary>
    /// How long an untouched budget survives in Redis.
    /// </summary>
    /// <remarks>
    /// A budget nobody has spent for this long is full anyway, so forgetting it costs
    /// nothing and keeps one key per recipient from accumulating forever. A budget under
    /// penalty always outlives its penalty regardless of this value.
    /// </remarks>
    public TimeSpan KeyLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Whether to fall back to per-process limiting when Redis cannot be reached.
    /// </summary>
    /// <remarks>
    /// On by default. Losing Redis then means each instance paces itself against the full
    /// allowance and Meta rejects the overshoot, which the retry path already handles.
    /// Turning it off makes a Redis outage a messaging outage.
    /// </remarks>
    public bool FallBackToLocal { get; set; } = true;
}
