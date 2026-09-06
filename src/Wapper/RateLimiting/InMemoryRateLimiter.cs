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

    /// <inheritdoc />
    /// <remarks>
    /// A permit is reserved in every budget first, so a budget that refuses leaves nothing
    /// spent in the others. The reservations are then waited out together and re-priced on
    /// every wake-up: a penalty recorded while this call slept pushes it back rather than
    /// letting it through into a block the Cloud API has just announced. Only once every
    /// reservation is due are they claimed — and a caller that gives up before then hands
    /// every one of them back.
    /// </remarks>
    public async ValueTask WaitAsync(
        IReadOnlyList<RateLimitRequest> requests,
        TimeSpan maxWait,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        Sweep();

        var started = time.GetTimestamp();
        var reservations = new TokenBucket.Reservation?[requests.Count];

        try
        {
            for (var i = 0; i < requests.Count; i++)
            {
                reservations[i] = Reserve(requests[i], maxWait);
            }

            while (true)
            {
                var wait = TimeSpan.Zero;

                for (var i = 0; i < reservations.Length; i++)
                {
                    var reservation = reservations[i];
                    var remaining = reservation!.Bucket.WaitFor(reservation);

                    if (remaining > TimeSpan.Zero && remaining > maxWait - time.GetElapsedTime(started))
                    {
                        throw new WhatsAppRateLimitedException(requests[i].Scope, remaining, maxWait);
                    }

                    if (remaining > wait)
                    {
                        wait = remaining;
                    }
                }

                if (wait == TimeSpan.Zero)
                {
                    break;
                }

                await Task.Delay(wait, time, cancellationToken).ConfigureAwait(false);
            }

            foreach (var reservation in reservations)
            {
                reservation!.Bucket.Claim(reservation);
            }
        }
        catch
        {
            // Nothing was sent, so nothing was spent. Every reservation taken so far goes
            // back — the ones a refusing budget left behind in the budgets before it, or
            // all of them when the caller gave up — and whoever queued behind moves up.
            foreach (var reservation in reservations)
            {
                reservation?.Bucket.Cancel(reservation);
            }

            throw;
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

        // A bucket the sweep has just retired refuses the hold, and it is kept for the
        // bucket that replaces it rather than lost with the old one.
        if (_buckets.TryGetValue(scope, out var bucket) && bucket.Penalise(duration))
        {
            return ValueTask.CompletedTask;
        }

        var until = time.GetTimestamp() + (long)(duration.TotalSeconds * time.TimestampFrequency);
        KeepHold(scope, until);

        // A bucket built between the lookup and here would never see the hold, because it is
        // only drained on the way in.
        if (_buckets.TryGetValue(scope, out var raced)
            && _pendingHolds.TryRemove(scope, out _)
            && !raced.Penalise(duration))
        {
            KeepHold(scope, until);
        }

        return ValueTask.CompletedTask;
    }

    private void KeepHold(RateLimitScope scope, long until) =>
        _pendingHolds.AddOrUpdate(scope, until, (_, existing) => existing > until ? existing : until);

    /// <summary>
    /// Reserves a permit in one budget, refusing the whole call when the wait is too long.
    /// </summary>
    private TokenBucket.Reservation Reserve(RateLimitRequest request, TimeSpan maxWait)
    {
        while (true)
        {
            var bucket = GetBucket(request);

            switch (bucket.TryReserve(maxWait, out var reservation, out var wait))
            {
                case ReserveOutcome.Reserved:
                    return reservation!;

                case ReserveOutcome.Refused:
                    // The refusing bucket took nothing, and still reported the wait it would
                    // have needed. The caller hands back what the earlier budgets gave.
                    throw new WhatsAppRateLimitedException(request.Scope, wait, maxWait);

                case ReserveOutcome.Retired:
                default:
                    // The sweep dropped it between the lookup and the reservation. The entry
                    // is removed by instance, so a bucket somebody else has already replaced
                    // it with is left alone.
                    _buckets.TryRemove(KeyValuePair.Create(request.Scope, bucket));
                    break;
            }
        }
    }

    private TokenBucket GetBucket(RateLimitRequest request)
    {
        while (true)
        {
            // The allowance is captured when the bucket is created. A tenant that changes
            // its configured throughput at runtime keeps the old pacing until the bucket
            // goes idle, which is a fair trade for not rebuilding state on every call.
            var bucket = _buckets.GetOrAdd(
                request.Scope,
                static (_, state) => new TokenBucket(state.Burst, state.PermitsPerSecond, state.Time),
                (request.Burst, request.PermitsPerSecond, Time: time));

            if (!_pendingHolds.TryRemove(request.Scope, out var until))
            {
                return bucket;
            }

            var remaining = time.GetElapsedTime(time.GetTimestamp(), until);

            if (remaining <= TimeSpan.Zero || bucket.Penalise(remaining))
            {
                return bucket;
            }

            // Retired by the sweep in the meantime. The hold goes back where it was found,
            // and the bucket that replaces this one collects it.
            KeepHold(request.Scope, until);
            _buckets.TryRemove(KeyValuePair.Create(request.Scope, bucket));
        }
    }

    /// <summary>
    /// Drops buckets nobody has used for a while. Without this, one bucket per recipient
    /// would accumulate for the lifetime of the process.
    /// </summary>
    /// <remarks>
    /// Only a bucket whose absence is indistinguishable from its presence goes: full, unheld
    /// and with nobody queued. The bucket decides that under its own lock and retires itself
    /// in the same breath, so a reservation racing the sweep either lands first and keeps
    /// it, or finds it retired and starts over on a fresh one.
    /// </remarks>
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
            if (bucket.TryRetire(IdleLifetime))
            {
                _buckets.TryRemove(KeyValuePair.Create(scope, bucket));
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
