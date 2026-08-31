namespace Wapper.RateLimiting;

/// <summary>
/// A token bucket whose only clock is a <see cref="TimeProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// The limiters in <c>System.Threading.RateLimiting</c> would otherwise do, but they read
/// the clock themselves, which makes a six-second pair limit a six-second test and Meta's
/// <c>4^X</c> backoff untestable past the third attempt. Everything here goes through the
/// injected provider, so the tests drive it with a fake one.
/// </para>
/// <para>
/// A caller that finds the bucket empty does not spin: the balance is allowed to go
/// negative, and the deficit is converted into the exact wait that caller owes. Callers
/// therefore queue in arrival order and the sustained rate comes out right under a burst.
/// </para>
/// <para>
/// Elapsed time is measured with timestamps rather than wall-clock readings, so a system
/// clock that jumps cannot hand out a windfall of permits.
/// </para>
/// </remarks>
internal sealed class TokenBucket
{
    private readonly object _gate = new();
    private readonly double _burst;
    private readonly double _permitsPerSecond;
    private readonly TimeProvider _time;

    private double _tokens;
    private long _lastTimestamp;
    private long _lastUsedTimestamp;
    private long _heldUntilTimestamp;

    public TokenBucket(double burst, double permitsPerSecond, TimeProvider time)
    {
        _burst = burst;
        _permitsPerSecond = permitsPerSecond;
        _time = time;
        _tokens = burst;
        _lastTimestamp = time.GetTimestamp();
        _lastUsedTimestamp = _lastTimestamp;
        _heldUntilTimestamp = _lastTimestamp;
    }

    /// <summary>Whether the bucket is currently being held back after a rejection.</summary>
    public bool IsHeld
    {
        get
        {
            lock (_gate)
            {
                return _heldUntilTimestamp > _time.GetTimestamp();
            }
        }
    }

    /// <summary>Whether nobody has touched the bucket for at least <paramref name="age"/>.</summary>
    public bool IsIdleFor(TimeSpan age)
    {
        lock (_gate)
        {
            return _time.GetElapsedTime(_lastUsedTimestamp, _time.GetTimestamp()) >= age;
        }
    }

    /// <summary>
    /// Takes one permit if the wait it implies is acceptable.
    /// </summary>
    /// <param name="maxWait">The longest wait the caller will accept.</param>
    /// <param name="wait">
    /// How long to wait before the permit is actually due. Set even when the permit is
    /// refused, so the caller can report how long it would have had to wait.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when the wait would exceed <paramref name="maxWait"/>, in
    /// which case no permit is taken and the bucket is left untouched.
    /// </returns>
    public bool TryTake(TimeSpan maxWait, out TimeSpan wait)
    {
        lock (_gate)
        {
            var now = Refill();

            var deficit = _tokens >= 1d ? TimeSpan.Zero : SecondsToRegain(1d - _tokens);
            var held = RemainingHold(now);
            wait = deficit > held ? deficit : held;

            if (wait > maxWait)
            {
                return false;
            }

            // Going negative is the point: the next caller inherits the deficit and waits
            // proportionally longer, which is what keeps the sustained rate honest.
            _tokens -= 1d;
            _lastUsedTimestamp = now;
            return true;
        }
    }

    /// <summary>
    /// Gives back a permit taken by <see cref="TryTake"/>. Used when a call spends several
    /// budgets and a later one turns out to be too expensive to wait for.
    /// </summary>
    public void Return()
    {
        lock (_gate)
        {
            _tokens += 1d;

            if (_tokens > _burst)
            {
                _tokens = _burst;
            }
        }
    }

    /// <summary>Holds the bucket back, after the Cloud API rejected a call.</summary>
    public void Penalise(TimeSpan duration)
    {
        lock (_gate)
        {
            var now = Refill();
            var until = now + ToTicks(duration);

            if (until > _heldUntilTimestamp)
            {
                _heldUntilTimestamp = until;
            }

            // Drain whatever is banked, and note that no permits accrue while the bucket is
            // held. Meta's counters kept running through the block, so releasing a full
            // burst the moment it lifts would walk straight back into the same rejection.
            if (_tokens > 0d)
            {
                _tokens = 0d;
            }

            _lastUsedTimestamp = now;
        }
    }

    /// <summary>Accrues the permits earned since the last look, and returns the current timestamp.</summary>
    private long Refill()
    {
        var now = _time.GetTimestamp();

        if (double.IsPositiveInfinity(_permitsPerSecond))
        {
            _tokens = _burst;
            _lastTimestamp = now;
            return now;
        }

        // Time spent under penalty earns nothing. Counting it would let a long hold bank a
        // burst that is released the instant the hold expires.
        var from = _lastTimestamp > _heldUntilTimestamp ? _lastTimestamp : _heldUntilTimestamp;

        if (now > from)
        {
            var elapsed = _time.GetElapsedTime(from, now).TotalSeconds;
            var replenished = _tokens + (elapsed * _permitsPerSecond);
            _tokens = replenished > _burst ? _burst : replenished;
        }

        _lastTimestamp = now;
        return now;
    }

    private TimeSpan RemainingHold(long now) =>
        _heldUntilTimestamp > now ? _time.GetElapsedTime(now, _heldUntilTimestamp) : TimeSpan.Zero;

    private TimeSpan SecondsToRegain(double tokens) =>
        double.IsPositiveInfinity(_permitsPerSecond)
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(tokens / _permitsPerSecond);

    private long ToTicks(TimeSpan duration) =>
        (long)(duration.TotalSeconds * _time.TimestampFrequency);
}
