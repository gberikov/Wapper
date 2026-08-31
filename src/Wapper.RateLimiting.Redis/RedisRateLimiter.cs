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
/// The whole read-refill-decrement cycle happens inside a Lua script, which Redis runs
/// atomically. Doing it with separate round trips would let two instances read the same
/// balance and both spend it.
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
    /// Takes one permit, refilling first and honouring any penalty.
    /// </summary>
    /// <remarks>
    /// Returns whether the permit was taken, and the wait it implies in milliseconds. The
    /// wait is reported even when the permit is refused, so the caller can say how long it
    /// would have had to wait.
    /// </remarks>
    private const string AcquireScript = """
        local clock = redis.call('TIME')
        local now = (tonumber(clock[1]) * 1000) + math.floor(tonumber(clock[2]) / 1000)

        local burst = tonumber(ARGV[1])
        local ratePerMs = tonumber(ARGV[2])
        local maxWait = tonumber(ARGV[3])
        local ttl = tonumber(ARGV[4])

        local state = redis.call('HMGET', KEYS[1], 't', 's', 'h')
        local tokens = tonumber(state[1])
        local stamp = tonumber(state[2])
        local hold = tonumber(state[3])

        if tokens == nil then tokens = burst end
        if stamp == nil then stamp = now end
        if hold == nil then hold = 0 end

        -- Time under penalty earns nothing, or a long hold would bank a burst and release
        -- it the instant the hold expired.
        local from = stamp
        if hold > from then from = hold end
        if now > from then
          tokens = tokens + ((now - from) * ratePerMs)
          if tokens > burst then tokens = burst end
        end

        local wait = 0
        if tokens < 1 then wait = (1 - tokens) / ratePerMs end
        if hold > now and (hold - now) > wait then wait = hold - now end

        if wait > maxWait then
          return {0, math.floor(wait)}
        end

        tokens = tokens - 1
        redis.call('HSET', KEYS[1], 't', tokens, 's', now, 'h', hold)

        -- A budget under penalty has to outlive its penalty, whatever the configured
        -- lifetime says.
        local expiry = ttl
        if hold > now and (hold - now) + 60000 > expiry then expiry = (hold - now) + 60000 end
        redis.call('PEXPIRE', KEYS[1], expiry)

        return {1, math.floor(wait)}
        """;

    /// <summary>Gives back a permit taken by <see cref="AcquireScript"/>.</summary>
    private const string ReturnScript = """
        local burst = tonumber(ARGV[1])
        local tokens = tonumber(redis.call('HGET', KEYS[1], 't'))
        if tokens == nil then return 0 end

        tokens = tokens + 1
        if tokens > burst then tokens = burst end
        redis.call('HSET', KEYS[1], 't', tokens)
        return 1
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

        IDatabase database;

        try
        {
            database = redis.GetDatabase();
        }
        catch (RedisException exception)
        {
            await FallBackAsync(exception, requests, maxWait, cancellationToken).ConfigureAwait(false);
            return;
        }

        var wait = TimeSpan.Zero;
        List<RateLimitRequest>? taken = null;

        for (var i = 0; i < requests.Count; i++)
        {
            var request = requests[i];

            AcquireResult result;

            try
            {
                result = await AcquireAsync(database, request, maxWait).ConfigureAwait(false);
            }
            catch (RedisException exception)
            {
                await ReturnAllAsync(database, taken).ConfigureAwait(false);
                await FallBackAsync(exception, requests, maxWait, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!result.Granted)
            {
                // Hand back what the earlier budgets already gave, or this call would spend
                // permits it never used.
                await ReturnAllAsync(database, taken).ConfigureAwait(false);

                throw new WhatsAppRateLimitedException(request.Scope, result.Wait, maxWait);
            }

            (taken ??= new List<RateLimitRequest>(requests.Count)).Add(request);

            if (result.Wait > wait)
            {
                wait = result.Wait;
            }
        }

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, time, cancellationToken).ConfigureAwait(false);
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
        catch (RedisException exception)
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

    private async Task<AcquireResult> AcquireAsync(
        IDatabase database,
        RateLimitRequest request,
        TimeSpan maxWait)
    {
        var burst = double.IsPositiveInfinity(request.Burst) ? Unbounded : request.Burst;
        var ratePerMillisecond = double.IsPositiveInfinity(request.PermitsPerSecond)
            ? Unbounded
            : request.PermitsPerSecond / 1000d;

        var result = (RedisValue[]?)await database.ScriptEvaluateAsync(
                AcquireScript,
                [KeyFor(request.Scope)],
                [
                    burst,
                    ratePerMillisecond,
                    (long)maxWait.TotalMilliseconds,
                    (long)_options.KeyLifetime.TotalMilliseconds,
                ])
            .ConfigureAwait(false);

        if (result is not { Length: 2 })
        {
            throw new WhatsAppException(
                "The Redis rate limiter script returned an unexpected result. This normally " +
                "means the key is being written by something other than this library.");
        }

        return new AcquireResult(
            (long)result[0] == 1,
            TimeSpan.FromMilliseconds((long)result[1]));
    }

    private async Task ReturnAllAsync(IDatabase database, List<RateLimitRequest>? requests)
    {
        if (requests is null)
        {
            return;
        }

        foreach (var request in requests)
        {
            var burst = double.IsPositiveInfinity(request.Burst) ? Unbounded : request.Burst;

            try
            {
                await database.ScriptEvaluateAsync(ReturnScript, [KeyFor(request.Scope)], [burst])
                    .ConfigureAwait(false);
            }
            catch (RedisException exception)
            {
                // Losing a permit costs one message of throughput and nothing else. Failing
                // the call over it would be worse.
                logger.LogWarning(
                    exception,
                    "Could not return a rate limit permit for {Scope} to Redis.",
                    request.Scope);
            }
        }
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
        $"{_options.KeyPrefix}{scope.Budget}:{scope.Key}";

    private readonly record struct AcquireResult(bool Granted, TimeSpan Wait);
}
