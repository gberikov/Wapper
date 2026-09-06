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
/// A caller that finds the bucket empty does not spin: it takes a <em>reservation</em>, the
/// balance goes negative, and the reservation is due once the bucket has earned the deficit
/// back. Reservations queue in arrival order, so the sustained rate comes out right under a
/// burst. The wait a reservation owes is recomputed every time it is asked for rather than
/// fixed when it was taken: a penalty recorded while it was waiting pushes it back, and a
/// reservation cancelled ahead of it moves it forward.
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

    /// <summary>
    /// Reservations taken and not yet claimed or cancelled, oldest first.
    /// </summary>
    /// <remarks>
    /// A list, scanned for position on every look: the position of a reservation is how
    /// many are ahead of it, and that is what its wait is computed from.
    /// </remarks>
    // ponytail: O(n) scan per wake-up; a linked list with cached ranks if buckets ever hold
    // thousands of waiters at once.
    private readonly List<Reservation> _pending = [];

    private double _tokens;
    private long _lastTimestamp;
    private long _lastUsedTimestamp;
    private long _heldUntilTimestamp;
    private bool _retired;

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

    /// <summary>
    /// Whether the bucket has been dropped by the limiter and must not be used again.
    /// </summary>
    /// <remarks>
    /// Set under the same lock that checked the bucket was empty of state, so a reservation
    /// racing the sweep either lands before it — and keeps the bucket alive — or finds this
    /// set and starts over on a fresh one.
    /// </remarks>
    public bool IsRetired
    {
        get
        {
            lock (_gate)
            {
                return _retired;
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
    /// Retires the bucket if forgetting it changes nothing: idle for <paramref name="age"/>,
    /// refilled to the brim, not held back, and with nobody waiting on it.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the bucket was retired and may be dropped. A bucket that
    /// still carries state — a deficit, a hold, a queue — is kept, however old: dropping a
    /// two-hundred-an-hour allowance after ten idle minutes would hand out a fresh two
    /// hundred that Meta's own counter is still forty minutes from earning.
    /// </returns>
    public bool TryRetire(TimeSpan age)
    {
        lock (_gate)
        {
            var now = Refill();

            if (_retired)
            {
                return true;
            }

            if (_time.GetElapsedTime(_lastUsedTimestamp, now) < age
                || _heldUntilTimestamp > now
                || _pending.Count > 0
                || _tokens < _burst)
            {
                return false;
            }

            _retired = true;
            return true;
        }
    }

    /// <summary>
    /// Reserves one permit if the wait it implies right now is acceptable.
    /// </summary>
    /// <param name="maxWait">The longest wait the caller will accept.</param>
    /// <param name="reservation">The reservation, when one was taken.</param>
    /// <param name="wait">
    /// How long the permit is from being due as things stand. Set even when the permit is
    /// refused, so the caller can report how long it would have had to wait. Ask
    /// <see cref="WaitFor"/> again after sleeping it out: a penalty may have arrived since.
    /// </param>
    /// <returns>
    /// <see cref="ReserveOutcome.Refused"/> when the wait would exceed
    /// <paramref name="maxWait"/>, in which case nothing is taken and the bucket is left
    /// untouched; <see cref="ReserveOutcome.Retired"/> when the bucket has been dropped and
    /// the caller has to fetch a fresh one.
    /// </returns>
    public ReserveOutcome TryReserve(TimeSpan maxWait, out Reservation? reservation, out TimeSpan wait)
    {
        lock (_gate)
        {
            reservation = null;

            if (_retired)
            {
                wait = TimeSpan.Zero;
                return ReserveOutcome.Retired;
            }

            var now = Refill();

            // What a newcomer at the back of the queue would owe: the whole deficit, after
            // whatever hold is in force, because nothing accrues while the bucket is held.
            var deficit = _tokens >= 1d ? 0d : 1d - _tokens;
            wait = RemainingHold(now) + ToWait(deficit);

            if (wait > maxWait)
            {
                return ReserveOutcome.Refused;
            }

            // Going negative is the point: the next caller inherits the deficit and waits
            // proportionally longer, which is what keeps the sustained rate honest.
            _tokens -= 1d;
            _lastUsedTimestamp = now;

            reservation = new Reservation(this);
            _pending.Add(reservation);

            return ReserveOutcome.Reserved;
        }
    }

    /// <summary>
    /// How long a reservation still has to wait, as things stand right now.
    /// </summary>
    /// <remarks>
    /// Zero means it is due. A reservation is due once the bucket, with every reservation
    /// ahead of it served, would hold a whole permit for it: the tokens it needs are its
    /// position in the queue, less what the bucket already has to spare over the queue.
    /// </remarks>
    public TimeSpan WaitFor(Reservation reservation)
    {
        lock (_gate)
        {
            var now = Refill();
            var position = _pending.IndexOf(reservation);

            if (position < 0)
            {
                // Already claimed or cancelled. Nothing left to wait for.
                return TimeSpan.Zero;
            }

            // The balance with the queue's own takings put back, minus the ones ahead of
            // this reservation and this one itself.
            var spare = _tokens + _pending.Count - (position + 1);
            var needed = spare >= 0d ? 0d : -spare;

            // A hold in force pushes even a due reservation back: the Cloud API has just
            // said the budget is spent, and a permit not yet used is a permit worth
            // holding. Nothing accrues under the hold, so the deficit is served after it.
            return RemainingHold(now) + ToWait(needed);
        }
    }

    /// <summary>Marks a due reservation as used. Its permit is already spent.</summary>
    public void Claim(Reservation reservation)
    {
        lock (_gate)
        {
            _pending.Remove(reservation);
            _lastUsedTimestamp = _time.GetTimestamp();
        }
    }

    /// <summary>
    /// Gives back the permit a reservation took, when the caller gave up before it was due.
    /// </summary>
    /// <remarks>
    /// Everything queued behind it moves up one place, because their waits are computed from
    /// their position rather than fixed when they were taken.
    /// </remarks>
    public void Cancel(Reservation reservation)
    {
        lock (_gate)
        {
            if (!_pending.Remove(reservation))
            {
                return;
            }

            _tokens += 1d;

            if (_tokens > _burst)
            {
                _tokens = _burst;
            }
        }
    }

    /// <summary>Holds the bucket back, after the Cloud API rejected a call.</summary>
    /// <returns>
    /// <see langword="false"/> when the bucket has been retired and the hold has to be kept
    /// for whatever replaces it.
    /// </returns>
    public bool Penalise(TimeSpan duration)
    {
        lock (_gate)
        {
            if (_retired)
            {
                return false;
            }

            var now = Refill();
            var until = now + ToTicks(duration);

            if (until > _heldUntilTimestamp)
            {
                _heldUntilTimestamp = until;
            }

            // Drain whatever is banked, and note that no permits accrue while the bucket is
            // held. Meta's counters kept running through the block, so releasing a full
            // burst the moment it lifts would walk straight back into the same rejection.
            // Reservations already queued keep their places: they were spent before the
            // block, and they are served at the sustained rate once it lifts.
            if (_tokens > 0d)
            {
                _tokens = 0d;
            }

            _lastUsedTimestamp = now;
            return true;
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

    private TimeSpan ToWait(double tokens) =>
        tokens <= 0d || double.IsPositiveInfinity(_permitsPerSecond)
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(tokens / _permitsPerSecond);

    private long ToTicks(TimeSpan duration) =>
        (long)(duration.TotalSeconds * _time.TimestampFrequency);

    /// <summary>One permit taken from a bucket and not yet used.</summary>
    public sealed class Reservation(TokenBucket bucket)
    {
        /// <summary>The bucket it was taken from.</summary>
        public TokenBucket Bucket { get; } = bucket;
    }
}

/// <summary>What <see cref="TokenBucket.TryReserve"/> did.</summary>
internal enum ReserveOutcome
{
    /// <summary>A permit was reserved; wait it out, then claim it.</summary>
    Reserved,

    /// <summary>The wait would exceed what the caller accepts. Nothing was taken.</summary>
    Refused,

    /// <summary>The bucket has been dropped by the limiter. Fetch a fresh one and retry.</summary>
    Retired,
}
