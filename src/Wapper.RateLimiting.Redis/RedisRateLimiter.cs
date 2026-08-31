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
/// Time comes from Redis rather than from the callers. Instances disagree about the clock,
/// and a bucket refilled against a fast instance's clock hands out permits that were never
/// earned.
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
    /// Takes one permit from every budget of a call, refilling each first and honouring any
    /// penalty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All or nothing: the first pass works out what each budget would cost and writes
    /// nothing, so a budget that refuses leaves the others untouched. Doing it the other way
    /// round — spend, then hand back what the refused call took — is what the previous
    /// version did across three round trips, and it could lose a permit whenever the process
    /// died in between.
    /// </para>
    /// <para>
    /// Returns whether the permits were taken, the longest wait they imply in milliseconds,
    /// and the one-based position of the budget that refused. The wait is reported on a
    /// refusal too, so the caller can say how long it would have had to wait.
    /// </para>
    /// </remarks>
    private const string AcquireScript = """
        local clock = redis.call('TIME')
        local now = (tonumber(clock[1]) * 1000) + math.floor(tonumber(clock[2]) / 1000)

        local maxWait = tonumber(ARGV[1])
        local ttl = tonumber(ARGV[2])

        local wait = 0
        local tokens = {}
        local holds = {}

        -- First pass: price every budget, write nothing.
        for i = 1, #KEYS do
          local burst = tonumber(ARGV[1 + (i * 2)])
          local ratePerMs = tonumber(ARGV[2 + (i * 2)])

          local state = redis.call('HMGET', KEYS[i], 't', 's', 'h')
          local t = tonumber(state[1])
          local stamp = tonumber(state[2])
          local hold = tonumber(state[3])

          if t == nil then t = burst end
          if stamp == nil then stamp = now end
          if hold == nil then hold = 0 end

          -- Time under penalty earns nothing, or a long hold would bank a burst and release
          -- it the instant the hold expired.
          local from = stamp
          if hold > from then from = hold end
          if now > from then
            t = t + ((now - from) * ratePerMs)
            if t > burst then t = burst end
          end

          local budgetWait = 0
          if t < 1 then budgetWait = (1 - t) / ratePerMs end
          if hold > now and (hold - now) > budgetWait then budgetWait = hold - now end

          if budgetWait > maxWait then
            return {0, math.floor(budgetWait), i}
          end

          if budgetWait > wait then wait = budgetWait end

          tokens[i] = t - 1
          holds[i] = hold
        end

        -- Second pass: nothing refused, so spend them all.
        for i = 1, #KEYS do
          redis.call('HSET', KEYS[i], 't', tokens[i], 's', now, 'h', holds[i])

          -- A budget under penalty has to outlive its penalty, whatever the configured
          -- lifetime says.
          local expiry = ttl
          if holds[i] > now and (holds[i] - now) + 60000 > expiry then
            expiry = (holds[i] - now) + 60000
          end
          redis.call('PEXPIRE', KEYS[i], expiry)
        end

        return {1, math.floor(wait), 0}
        """;

    /// <summary>Holds a budget back after the Cloud API rejected a call.</summary>
    private const string PenaliseScript = """
        local clock = redis.call('TIME')
        local now = (tonumber(clock[1]) * 1000) + math.floor(tonumber(clock[2]) / 1000)

        local duration = tonumber(ARGV[1])
        local ttl = tonumber(ARGV[2])

        local hold = tonumber(redis.call('HGET', KEYS[1], 'h'))
        if hold == nil then hold = 0 end

        local until_ms = now + duration
        if until_ms > hold then hold = until_ms end

        -- Drained as well as held: Meta's counters kept running while we were blocked.
        redis.call('HSET', KEYS[1], 't', 0, 's', now, 'h', hold)

        local expiry = ttl
        if (hold - now) + 60000 > expiry then expiry = (hold - now) + 60000 end
        redis.call('PEXPIRE', KEYS[1], expiry)

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

        if (requests.Count == 0)
        {
            return;
        }

        AcquireResult result;

        try
        {
            result = await AcquireAsync(requests, maxWait).ConfigureAwait(false);
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
                requests[result.RefusedIndex].Scope,
                result.Wait,
                maxWait);
        }

        if (result.Wait > TimeSpan.Zero)
        {
            await Task.Delay(result.Wait, time, cancellationToken).ConfigureAwait(false);
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
            logger.LogWarning(
                exception,
                "Could not record a rate limit penalty for {Scope} in Redis.",
                scope);

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

    private async Task<AcquireResult> AcquireAsync(
        IReadOnlyList<RateLimitRequest> requests,
        TimeSpan maxWait)
    {
        var keys = new RedisKey[requests.Count];
        var values = new RedisValue[2 + (requests.Count * 2)];

        values[0] = (long)maxWait.TotalMilliseconds;
        values[1] = (long)_options.KeyLifetime.TotalMilliseconds;

        for (var i = 0; i < requests.Count; i++)
        {
            var request = requests[i];

            keys[i] = KeyFor(request.Scope);
            values[2 + (i * 2)] = double.IsPositiveInfinity(request.Burst) ? Unbounded : request.Burst;
            values[3 + (i * 2)] = double.IsPositiveInfinity(request.PermitsPerSecond)
                ? Unbounded
                : request.PermitsPerSecond / 1000d;
        }

        var result = (RedisValue[]?)await redis.GetDatabase()
            .ScriptEvaluateAsync(AcquireScript, keys, values)
            .ConfigureAwait(false);

        if (result is not { Length: 3 })
        {
            throw new WhatsAppException(
                "The Redis rate limiter script returned an unexpected result. This normally " +
                "means the key is being written by something other than this library.");
        }

        var refused = (int)result[2];

        return new AcquireResult(
            (long)result[0] == 1,
            TimeSpan.FromMilliseconds((long)result[1]),
            // Lua counts from one, and reports zero when nothing refused.
            refused > 0 ? refused - 1 : 0);
    }

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

    private RedisKey KeyFor(RateLimitScope scope) =>
        $"{_options.KeyPrefix}{Name(scope.Budget)}:{scope.Key}";

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

    private readonly record struct AcquireResult(bool Granted, TimeSpan Wait, int RefusedIndex);
}
