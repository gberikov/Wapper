using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Wapper.RateLimiting.Redis;

/// <summary>
/// Keeps the budgets in Redis, so every instance of the application spends the same
/// allowance.
/// </summary>
/// <remarks>
/// <para>
/// Meta counts per phone number on its side. Three replicas each pacing themselves against
/// the full allowance send three times the rate and have two thirds of it rejected, so the
/// counters have to be shared to be worth anything.
/// </para>
/// <para>
/// One call spends several budgets, and it spends all of them or none. That is why the whole
/// read-refill-decrement cycle for every budget happens inside a single Lua script, which
/// Redis runs atomically: separate round trips would let two instances read the same balance
/// and both spend it, and would leave permits stranded in the budgets that were granted when
/// a later one refused.
/// </para>
/// <para>
/// A call that has to wait is not trusted to its first estimate. Every budget keeps a running
/// count of the permits it has earned, and a granted call is told the count each budget has
/// to reach before its permit is due. When its wait runs out it asks again: a penalty
/// recorded in the meantime — by this instance or any other — has stopped the count, and
/// the call waits on rather than walking into a block the Cloud API has just announced.
/// That is one extra round trip per call that waited, and one more per penalty that
/// arrived while it did; a call granted at once makes none.
/// </para>
/// <para>
/// Time comes from Redis rather than from the callers. Instances disagree about the clock,
/// and a bucket refilled against a fast instance's clock hands out permits that were never
/// earned.
/// </para>
/// <para>
/// Every key of one call goes to one script, so on Redis Cluster every key has to hash to
/// the same slot: give <see cref="RedisRateLimiterOptions.KeyPrefix"/> a hash tag, such as
/// <c>{wapper}:rl:</c>. Without one the cluster refuses the script with <c>CROSSSLOT</c>,
/// which is reported as a configuration error rather than treated as Redis being away.
/// </para>
/// </remarks>
internal sealed class RedisRateLimiter(
    IConnectionMultiplexer redis,
    IWhatsAppRateLimiter fallback,
    IOptions<RedisRateLimiterOptions> options,
    TimeProvider time,
    ILogger<RedisRateLimiter> logger) : IWhatsAppRateLimiter
{
    /// <summary>
    /// Refills one budget in place: <c>t</c> is the balance, <c>a</c> the permits ever earned,
    /// <c>s</c> when either was last brought up to date, <c>h</c> the end of the hold.
    /// </summary>
    /// <remarks>
    /// Time under penalty earns nothing, or a long hold would bank a burst and release it the
    /// instant the hold expired. An unbounded budget is always full and never counts: it
    /// waits on its hold and nothing else.
    /// </remarks>
    private const string RefillFunction = """
        local function refill(key, burst, ratePerMs, now)
          local state = redis.call('HMGET', key, 't', 's', 'h', 'a')
          local t = tonumber(state[1])
          local stamp = tonumber(state[2])
          local hold = tonumber(state[3])
          local accrued = tonumber(state[4])

          if t == nil then t = burst end
          if stamp == nil then stamp = now end
          if hold == nil then hold = 0 end
          if accrued == nil then accrued = 0 end

          if ratePerMs >= 1000000000 then
            t = burst
          else
            local from = stamp
            if hold > from then from = hold end
            if now > from then
              local earned = (now - from) * ratePerMs
              accrued = accrued + earned
              t = t + earned
              if t > burst then t = burst end
            end
          end

          return t, hold, accrued
        end

        local function wait_for(t, hold, ratePerMs, now)
          local wait = 0
          if t < 1 then wait = (1 - t) / ratePerMs end
          if hold > now then wait = wait + (hold - now) end
          return wait
        end

        local function expire(key, hold, now, ttl)
          -- A budget under penalty has to outlive its penalty, whatever the configured
          -- lifetime says.
          local expiry = ttl
          if hold > now and (hold - now) + 60000 > expiry then
            expiry = (hold - now) + 60000
          end
          local balance = tonumber(redis.call('HGET', key, 't')) or 0
          local rate = tonumber(redis.call('HGET', key, 'r'))
          local burst = tonumber(redis.call('HGET', key, 'b'))
          if rate ~= nil and burst ~= nil and rate < 1000000000 then
            expiry = math.max(expiry, math.max(0, hold - now) + math.max(0, burst - balance) / rate + 60000)
          end
          -- A call can wait on another budget longer than this one takes to refill.
          local lease = tonumber(redis.call('HGET', key, 'l')) or 0
          expiry = math.max(expiry, lease - now)
          redis.call('PEXPIRE', key, math.ceil(expiry))
        end
        """;

    /// <summary>
    /// Takes one permit from every budget of a call, refilling each first and honouring any
    /// penalty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All or nothing: the first pass works out what each budget would cost and writes
    /// nothing, so a budget that refuses leaves the others untouched.
    /// </para>
    /// <para>
    /// Returns whether the permits were taken, the longest wait they imply in milliseconds,
    /// the one-based position of the budget with the longest wait (or the one that refused), and then — for a grant — the count
    /// of earned permits each budget has to reach before this call's permit is due. The wait
    /// is reported on a refusal too, so the caller can say how long it would have had to
    /// wait. A wait is the hold that is in force plus the deficit after it, because nothing
    /// is earned under the hold.
    /// </para>
    /// </remarks>
    private const string AcquireScript = RefillFunction + """

        local clock = redis.call('TIME')
        local now = (tonumber(clock[1]) * 1000) + math.floor(tonumber(clock[2]) / 1000)

        local maxWait = tonumber(ARGV[1])
        local ttl = tonumber(ARGV[2])

        local wait = 0
        local tokens = {}
        local limiting = 1
        local holds = {}
        local counts = {}
        local targets = {}

        -- First pass: price every budget, write nothing.
        for i = 1, #KEYS do
          local burst = tonumber(ARGV[1 + (i * 2)])
          local ratePerMs = tonumber(ARGV[2 + (i * 2)])

          local t, hold, accrued = refill(KEYS[i], burst, ratePerMs, now)
          local budgetWait = wait_for(t, hold, ratePerMs, now)

          if budgetWait > maxWait then
            return {0, math.floor(budgetWait), i}
          end

          if budgetWait > wait then
            wait = budgetWait
            limiting = i
          end

          local deficit = 0
          if t < 1 then deficit = 1 - t end

          tokens[i] = t - 1
          holds[i] = hold
          counts[i] = accrued
          targets[i] = accrued + deficit
        end

        -- Second pass: nothing refused, so spend them all. The rate is written alongside so
        -- a penalty, which knows nothing about rates, can bring the budget up to date.
        local result = {1, math.ceil(wait), limiting}
        for i = 1, #KEYS do
          local ratePerMs = tonumber(ARGV[2 + (i * 2)])
          local lease = tonumber(redis.call('HGET', KEYS[i], 'l')) or 0
          lease = math.max(lease, now + maxWait + 60000)
          redis.call('HSET', KEYS[i], 't', tokens[i], 's', now, 'h', holds[i], 'a', counts[i], 'r', ratePerMs,
            'b', tonumber(ARGV[1 + (i * 2)]), 'l', lease)
          expire(KEYS[i], holds[i], now, ttl)
          -- As text: a number handed back from Lua is truncated to an integer.
          result[3 + i] = string.format('%.17g', targets[i])
        end

        return result
        """;

    /// <summary>
    /// How much longer a granted call has to wait, as things stand now.
    /// </summary>
    /// <remarks>
    /// Reads only. Each budget is refilled on paper to see how far its count has got towards
    /// the target the grant named; a hold recorded since stops the count and is waited out
    /// on top. Zero means every permit is due.
    /// </remarks>
    private const string RecheckScript = RefillFunction + """

        local clock = redis.call('TIME')
        local now = (tonumber(clock[1]) * 1000) + math.floor(tonumber(clock[2]) / 1000)

        local wait = 0
        local limiting = 1

        for i = 1, #KEYS do
          local burst = tonumber(ARGV[(i * 3) - 2])
          local ratePerMs = tonumber(ARGV[(i * 3) - 1])
          local target = tonumber(ARGV[i * 3])

          local t, hold, accrued = refill(KEYS[i], burst, ratePerMs, now)

          local budgetWait = 0
          if ratePerMs < 1000000000 and accrued < target then
            budgetWait = (target - accrued) / ratePerMs
          end
          if hold > now then budgetWait = budgetWait + (hold - now) end

          if budgetWait > wait then
            wait = budgetWait
            limiting = i
          end
        end

        return {math.ceil(wait), limiting}
        """;

    /// <summary>
    /// Returns permits that can safely be reused when a call gives up waiting.
    /// </summary>
    /// <remarks>
    /// Refund a future permit only at the tail: reusing a cancelled place inside the queue
    /// would give a newcomer the same target as an existing waiter. Interior cancellations
    /// conservatively leave their permit spent; it refills at the configured rate.
    /// </remarks>
    private const string ReturnScript = """
        for i = 1, #KEYS do
          local burst = tonumber(ARGV[(i * 2) - 1])
          local target = tonumber(ARGV[i * 2])
          local t = tonumber(redis.call('HGET', KEYS[i], 't'))
          local accrued = tonumber(redis.call('HGET', KEYS[i], 'a')) or 0
          -- The target and balance travel through separate floating-point serializations.
          -- Allow rounding noise when recognizing the tail; queued targets are one permit apart.
          if t ~= nil and (t >= 0 or target >= accrued - t - 0.0000001) then
            t = t + 1
            if t > burst then t = burst end
            redis.call('HSET', KEYS[i], 't', t)
          end
        end
        return 1
        """;

    /// <summary>Holds a budget back after the Cloud API rejected a call.</summary>
    /// <remarks>
    /// Brought up to date first, with the rate the last grant wrote down, so what was earned
    /// before the hold is counted for the calls already queued and nothing under it is. A
    /// budget nobody has spent yet has no rate and nothing to bring up to date.
    /// </remarks>
    private const string PenaliseScript = RefillFunction + """

        local clock = redis.call('TIME')
        local now = (tonumber(clock[1]) * 1000) + math.floor(tonumber(clock[2]) / 1000)

        local duration = tonumber(ARGV[1])
        local ttl = tonumber(ARGV[2])

        local rate = tonumber(redis.call('HGET', KEYS[1], 'r'))
        local t, hold, accrued
        if rate == nil then
          local state = redis.call('HMGET', KEYS[1], 't', 'h', 'a')
          t = tonumber(state[1])
          hold = tonumber(state[2])
          accrued = tonumber(state[3])
          if t == nil then t = 0 end
          if hold == nil then hold = 0 end
          if accrued == nil then accrued = 0 end
        else
          -- The burst only caps what was earned, and a balance about to be drained does
          -- not need capping.
          t, hold, accrued = refill(KEYS[1], 1000000000, rate, now)
        end

        local until_ms = now + duration
        if until_ms > hold then hold = until_ms end

        -- Drained as well as held: Meta's counters kept running while we were blocked. A
        -- balance in deficit stays in deficit; those permits are spoken for.
        if t > 0 then t = 0 end

        redis.call('HSET', KEYS[1], 't', t, 's', now, 'h', hold, 'a', accrued)

        expire(KEYS[1], hold, now, ttl)

        return 1
        """;

    /// <summary>
    /// Stands in for an unbounded rate. Lua has no usable infinity to divide by, and a
    /// billion permits a millisecond is unreachable by anything the Cloud API allows.
    /// </summary>
    private const double Unbounded = 1e9;

    private readonly RedisRateLimiterOptions _options = options.Value;

    public async ValueTask WaitAsync(
        IReadOnlyList<RateLimitRequest> requests,
        TimeSpan maxWait,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var started = time.GetTimestamp();

        if (requests.Count == 0)
        {
            return;
        }

        var keys = new RedisKey[requests.Count];
        for (var i = 0; i < requests.Count; i++)
        {
            keys[i] = KeyFor(requests[i].Scope);
        }

        AcquireResult result;

        try
        {
            // Not cancellable: the script runs whether or not this side is still listening,
            // and a permit taken by a call that then stopped listening would be lost. It is
            // awaited, and handed back below if the caller has gone.
            result = await AcquireAsync(keys, requests, maxWait).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsCrossSlot(exception))
        {
            throw CrossSlot(exception);
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            await FallBackAsync(exception, requests, maxWait, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!result.Granted)
        {
            // Nothing was spent: the script prices every budget before it writes any of them.
            throw new WhatsAppRateLimitedException(
                requests[result.LimitingIndex].Scope,
                result.Wait,
                maxWait);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var wait = result.Wait;
            var limiting = result.LimitingIndex;

            while (wait > TimeSpan.Zero)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var estimateReceived = time.GetTimestamp();
                var remaining = maxWait - time.GetElapsedTime(started);
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(wait < remaining ? wait : remaining, time, cancellationToken).ConfigureAwait(false);
                }

                // Redis priced the wait before its response travelled back. It may
                // already be due, even when that stale estimate exceeds our remaining
                // time. Refuse only on a recheck issued at or after the deadline.
                var deadlineReached = time.GetElapsedTime(started) >= maxWait;
                var rechecked = await RecheckAsync(keys, requests, result.Targets).ConfigureAwait(false);
                if (rechecked is not { } current)
                {
                    // A capped sleep may not have served the last estimate in full.
                    // Redis being unavailable must not release that permit early.
                    wait -= time.GetElapsedTime(estimateReceived);
                    if (wait > TimeSpan.Zero)
                    {
                        throw new WhatsAppRateLimitedException(requests[limiting].Scope, wait, maxWait);
                    }

                    break;
                }

                (wait, limiting) = current;
                if (deadlineReached && wait > TimeSpan.Zero)
                {
                    throw new WhatsAppRateLimitedException(requests[limiting].Scope, wait, maxWait);
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or WhatsAppRateLimitedException)
        {
            await ReturnAsync(keys, requests, result.Targets).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask PenaliseAsync(
        RateLimitScope scope,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            await redis.GetDatabase().ScriptEvaluateAsync(
                    PenaliseScript,
                    [KeyFor(scope)],
                    [(long)duration.TotalMilliseconds, (long)_options.KeyLifetime.TotalMilliseconds])
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            // The redacted key, not the scope itself: a pair scope's ToString carries the
            // customer's number in full, and this line ends up in a log.
            logger.LogWarning(
                exception,
                "Could not record a rate limit penalty for the {Budget} scope '{Scope}' in Redis.",
                scope.Budget,
                scope.RedactedKey);

            if (_options.FallBackToLocal)
            {
                await fallback.PenaliseAsync(scope, duration, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Every way Redis says no.
    /// </summary>
    /// <remarks>
    /// Catching <see cref="RedisException"/> alone is not enough, and the gap is the failure
    /// that matters most in production: a Redis that has gone slow rather than away raises
    /// <see cref="RedisTimeoutException"/>, which derives from <see cref="TimeoutException"/>
    /// and not from <see cref="RedisException"/> at all. Letting that through would fail
    /// every send outright while the fallback stood unused.
    /// </remarks>
    private static bool IsRedisFailure(Exception exception) =>
        exception is RedisException or RedisTimeoutException or RedisCommandException;

    /// <summary>
    /// A cluster refusing to run one script over keys on different slots.
    /// </summary>
    /// <remarks>
    /// Permanent, not transient: no retry and no fallback fixes a key prefix without a hash
    /// tag, and pacing this instance alone in the meantime would hide the misconfiguration
    /// behind a warning about Redis being away. The server says <c>CROSSSLOT</c>; the client
    /// library, when it checks first, says the keys must be in a single slot.
    /// </remarks>
    private static bool IsCrossSlot(Exception exception) =>
        exception is RedisException or RedisCommandException
        && (exception.Message.Contains("CROSSSLOT", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("single slot", StringComparison.OrdinalIgnoreCase));

    private WhatsAppConfigurationException CrossSlot(Exception exception) =>
        new(
            "Redis Cluster refused the rate limiter's script because the budgets of one call " +
            "hash to different slots. Every key of a call has to share a slot: set " +
            $"{nameof(RedisRateLimiterOptions)}.{nameof(RedisRateLimiterOptions.KeyPrefix)} to a " +
            $"prefix with a hash tag, such as \"{{wapper}}:rl:\" (it is \"{_options.KeyPrefix}\"). " +
            "Change it on every instance at once, so no two of them pace against separate keys.",
            exception);

    private async Task<AcquireResult> AcquireAsync(
        RedisKey[] keys,
        IReadOnlyList<RateLimitRequest> requests,
        TimeSpan maxWait)
    {
        var values = new RedisValue[2 + (requests.Count * 2)];

        values[0] = (long)maxWait.TotalMilliseconds;
        values[1] = (long)_options.KeyLifetime.TotalMilliseconds;

        for (var i = 0; i < requests.Count; i++)
        {
            values[2 + (i * 2)] = BurstOf(requests[i]);
            values[3 + (i * 2)] = RatePerMillisecondOf(requests[i]);
        }

        var result = (RedisValue[]?)await redis.GetDatabase()
            .ScriptEvaluateAsync(AcquireScript, keys, values)
            .ConfigureAwait(false);

        if (result is not { Length: >= 3 })
        {
            throw Unexpected();
        }

        var granted = (long)result[0] == 1;
        var limiting = (int)result[2];

        if (granted && result.Length != 3 + requests.Count)
        {
            throw Unexpected();
        }

        var targets = new double[granted ? requests.Count : 0];
        for (var i = 0; i < targets.Length; i++)
        {
            targets[i] = double.Parse(result[3 + i].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        return new AcquireResult(
            granted,
            TimeSpan.FromMilliseconds((long)result[1]),
            // Lua counts from one.
            limiting - 1,
            targets);
    }

    /// <summary>
    /// Asks how much longer the permits already taken have to wait.
    /// </summary>
    /// <remarks>
    /// Null means Redis could not answer. The caller may proceed on its last estimate
    /// only if it has waited that estimate out in full.
    /// </remarks>
    private async Task<(TimeSpan Wait, int Limiting)?> RecheckAsync(
        RedisKey[] keys,
        IReadOnlyList<RateLimitRequest> requests,
        double[] targets)
    {
        var values = new RedisValue[requests.Count * 3];

        for (var i = 0; i < requests.Count; i++)
        {
            values[i * 3] = BurstOf(requests[i]);
            values[(i * 3) + 1] = RatePerMillisecondOf(requests[i]);
            values[(i * 3) + 2] = targets[i].ToString("R", CultureInfo.InvariantCulture);
        }

        try
        {
            var result = (RedisValue[]?)await redis.GetDatabase()
                .ScriptEvaluateAsync(RecheckScript, keys, values)
                .ConfigureAwait(false);

            if (result is not { Length: 2 })
            {
                throw Unexpected();
            }

            return (TimeSpan.FromMilliseconds((long)result[0]), (int)result[1] - 1);
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            logger.LogWarning(
                exception,
                "Could not ask Redis whether a paced call is still held back; proceeding on the " +
                "last wait estimate only if it has been waited out.");

            return null;
        }
    }

    /// <summary>Hands back what a cancelled call took. Best effort: the caller is leaving.</summary>
    private async Task ReturnAsync(RedisKey[] keys, IReadOnlyList<RateLimitRequest> requests, double[] targets)
    {
        var values = new RedisValue[requests.Count * 2];

        for (var i = 0; i < requests.Count; i++)
        {
            values[i * 2] = BurstOf(requests[i]);
            values[(i * 2) + 1] = targets[i].ToString("R", CultureInfo.InvariantCulture);
        }

        try
        {
            await redis.GetDatabase()
                .ScriptEvaluateAsync(ReturnScript, keys, values)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            logger.LogWarning(
                exception,
                "Could not hand back the permits of a cancelled call to Redis; they refill on " +
                "their own.");
        }
    }

    private static RedisValue BurstOf(RateLimitRequest request) =>
        double.IsPositiveInfinity(request.Burst) ? Unbounded : request.Burst;

    private static RedisValue RatePerMillisecondOf(RateLimitRequest request) =>
        double.IsPositiveInfinity(request.PermitsPerSecond)
            ? Unbounded
            : request.PermitsPerSecond / 1000d;

    private static WhatsAppException Unexpected() =>
        new(
            "The Redis rate limiter script returned an unexpected result. This normally " +
            "means the key is being written by something other than this library.");

    private ValueTask FallBackAsync(
        Exception exception,
        IReadOnlyList<RateLimitRequest> requests,
        TimeSpan maxWait,
        CancellationToken cancellationToken)
    {
        if (!_options.FallBackToLocal)
        {
            throw new WhatsAppException(
                "The shared rate limiter could not reach Redis, and falling back to per-process " +
                "limiting is switched off.",
                exception);
        }

        logger.LogWarning(
            exception,
            "The shared rate limiter could not reach Redis and is pacing this instance on its " +
            "own. While that lasts, every instance paces against the full allowance and the " +
            "Cloud API will reject the overshoot.");

        return fallback.WaitAsync(requests, maxWait, cancellationToken);
    }

    private RedisKey KeyFor(RateLimitScope scope) => KeyFor(_options.KeyPrefix, scope);

    /// <summary>
    /// The key a scope lives under.
    /// </summary>
    /// <remarks>
    /// A pair scope is keyed by the customer's phone number, and Redis persists to disk. The
    /// number has no business sitting there in the clear when nothing ever reads the key back
    /// — the limiter only needs it to be the same on every instance — so a pair is keyed by a
    /// digest of it instead. The other scopes name the business's own ids, which are worth
    /// being able to read in <c>redis-cli</c>.
    /// </remarks>
    internal static RedisKey KeyFor(string prefix, RateLimitScope scope)
    {
        if (scope.Budget != RateLimitBudget.RecipientPair)
        {
            return $"{prefix}{Name(scope.Budget)}:{scope.Key}";
        }

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(scope.Key), digest);

        // Half the digest is 128 bits: plenty to keep pairs apart, and half the key length.
        return $"{prefix}{Name(scope.Budget)}:{Convert.ToHexString(digest[..16])}";
    }

    /// <remarks>
    /// Written out rather than left to <c>ToString</c>, which reflects over the enum and
    /// allocates on a path that runs on every call. The names are part of the key format and
    /// have to stay put anyway, so spelling them here is what makes that promise checkable.
    /// </remarks>
    private static string Name(RateLimitBudget budget) => budget switch
    {
        RateLimitBudget.PhoneNumberThroughput => "PhoneNumberThroughput",
        RateLimitBudget.RecipientPair => "RecipientPair",
        RateLimitBudget.BusinessAccountRequests => "BusinessAccountRequests",
        RateLimitBudget.ApplicationRequests => "ApplicationRequests",
        _ => "Unknown",
    };

    private readonly record struct AcquireResult(bool Granted, TimeSpan Wait, int LimitingIndex, double[] Targets);
}
