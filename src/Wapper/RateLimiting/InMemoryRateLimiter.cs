using System.Collections.Concurrent;

namespace Wapper.RateLimiting;

/// <summary>
/// Keeps the budgets in the memory of one process.
/// </summary>
/// <remarks>
/// Correct for a single instance. Run the same application in several replicas and each one
/// paces itself against the full allowance, so together they overshoot and Meta rejects the
/// difference — register the Redis-backed limiter instead.
/// </remarks>
internal sealed class InMemoryRateLimiter(TimeProvider time) : IWhatsAppRateLimiter
{
    /// <summary>How long a bucket may sit unused before it is dropped.</summary>
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(10);

    /// <summary>How often idle buckets are looked for.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<RateLimitScope, TokenBucket> _buckets = new();

    /// <summary>
    /// Holds recorded against a budget no call has spent yet.
    /// </summary>
    /// <remarks>
    /// A usage header can name a budget before any call has paced against it — the business
    /// account allowance turns up on the response to a message, which does not spend it — and
    /// dropping the hold on the floor because there was nowhere to put it would let the next
    /// management call walk into a block this one already saw. Kept as the timestamp the hold
    /// runs to, and handed to the bucket the moment one is built.
    /// </remarks>
    private readonly ConcurrentDictionary<RateLimitScope, long> _pendingHolds = new();

    private long _lastSweepTimestamp = time.GetTimestamp();

    public async ValueTask WaitAsync(
        IReadOnlyList<RateLimitRequest> requests,
        TimeSpan maxWait,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        Sweep();

        var wait = TimeSpan.Zero;
        List<TokenBucket>? taken = null;

        for (var i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            var bucket = GetBucket(request);

            if (!bucket.TryTake(maxWait, out var bucketWait))
            {
                // Hand back what the earlier budgets already gave, or this call would spend
                // permits it never used and quietly throttle the next one. The rejected
                // bucket took nothing, and still reported the wait it would have needed.
                ReturnAll(taken);

                throw new WhatsAppRateLimitedException(request.Scope, bucketWait, maxWait);
            }

            (taken ??= new List<TokenBucket>(requests.Count)).Add(bucket);

            if (bucketWait > wait)
            {
                wait = bucketWait;
            }
        }

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, time, cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask PenaliseAsync(
        RateLimitScope scope,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero)
        {
            return ValueTask.CompletedTask;
        }

        if (_buckets.TryGetValue(scope, out var bucket))
        {
            bucket.Penalise(duration);
            return ValueTask.CompletedTask;
        }

        var until = time.GetTimestamp() + (long)(duration.TotalSeconds * time.TimestampFrequency);
        _pendingHolds.AddOrUpdate(scope, until, (_, existing) => existing > until ? existing : until);

        // A bucket built between the lookup and here would never see the hold, because it is
        // only drained on the way in.
        if (_buckets.TryGetValue(scope, out var raced) && _pendingHolds.TryRemove(scope, out _))
        {
            raced.Penalise(duration);
        }

        return ValueTask.CompletedTask;
    }

    private TokenBucket GetBucket(RateLimitRequest request)
    {
        // The allowance is captured when the bucket is created. A tenant that changes its
        // configured throughput at runtime keeps the old pacing until the bucket goes idle,
        // which is a fair trade for not rebuilding state on every call.
        var bucket = _buckets.GetOrAdd(
            request.Scope,
            static (_, state) => new TokenBucket(state.Burst, state.PermitsPerSecond, state.Time),
            (request.Burst, request.PermitsPerSecond, Time: time));

        if (_pendingHolds.TryRemove(request.Scope, out var until))
        {
            var remaining = time.GetElapsedTime(time.GetTimestamp(), until);

            if (remaining > TimeSpan.Zero)
            {
                bucket.Penalise(remaining);
            }
        }

        return bucket;
    }

    private static void ReturnAll(List<TokenBucket>? buckets)
    {
        if (buckets is null)
        {
            return;
        }

        foreach (var bucket in buckets)
        {
            bucket.Return();
        }
    }

    /// <summary>
    /// Drops buckets nobody has used for a while. Without this, one bucket per recipient
    /// would accumulate for the lifetime of the process.
    /// </summary>
    private void Sweep()
    {
        var now = time.GetTimestamp();
        var since = Interlocked.Read(ref _lastSweepTimestamp);

        if (time.GetElapsedTime(since, now) < SweepInterval)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastSweepTimestamp, now, since) != since)
        {
            // Another call is already sweeping.
            return;
        }

        foreach (var (scope, bucket) in _buckets)
        {
            // A bucket still serving a penalty has to survive: a business account held back
            // for an hour would otherwise be forgotten and immediately overrun again.
            if (bucket.IsIdleFor(IdleLifetime) && !bucket.IsHeld)
            {
                _buckets.TryRemove(scope, out _);
            }
        }

        foreach (var (scope, until) in _pendingHolds)
        {
            // A hold nobody ever came to collect. It has run out, so it would be handed over
            // as a zero-length penalty anyway.
            if (until <= now)
            {
                _pendingHolds.TryRemove(scope, out _);
            }
        }
    }
}
