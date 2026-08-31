using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Wapper.RateLimiting.Redis.Tests;

/// <summary>
/// The shared limiter against a real Redis. The point of this implementation is what happens
/// when two instances of the same application spend the same budget, and no fake proves that.
/// </summary>
public sealed class RedisRateLimiterTests : IAsyncLifetime
{
    private static readonly TimeSpan Forever = TimeSpan.FromMinutes(5);

    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    private IConnectionMultiplexer _connection = null!;

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync();
        _connection = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public void A_pair_key_does_not_spell_out_the_customer_s_number()
    {
        // Redis persists to disk, and nothing ever reads the key back: the limiter only needs
        // it to be the same on every instance. The business's own ids stay readable.
        var pair = RedisRateLimiter.KeyFor("wapper:rl:", RateLimitScope.RecipientPair("111", "79001234567"));
        var number = RedisRateLimiter.KeyFor("wapper:rl:", RateLimitScope.PhoneNumberThroughput("111"));

        Assert.DoesNotContain("79001234567", pair.ToString(), StringComparison.Ordinal);
        Assert.StartsWith("wapper:rl:RecipientPair:", pair.ToString(), StringComparison.Ordinal);
        Assert.Equal(pair, RedisRateLimiter.KeyFor("wapper:rl:", RateLimitScope.RecipientPair("111", "79001234567")));
        Assert.NotEqual(pair, RedisRateLimiter.KeyFor("wapper:rl:", RateLimitScope.RecipientPair("111", "79001234568")));

        Assert.Equal("wapper:rl:PhoneNumberThroughput:111", number.ToString());
    }

    [Fact]
    public async Task A_call_within_the_budget_does_not_wait()
    {
        var limiter = CreateLimiter();

        var elapsed = await TimeAsync(() =>
            limiter.WaitAsync(Throughput("111", 80), Forever, TestContext.Current.CancellationToken));

        Assert.True(elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Two_instances_share_one_budget()
    {
        // This is the whole reason the package exists. With per-process limiters both would
        // grant the permit and the pair would send twice the allowance.
        var first = CreateLimiter();
        var second = CreateLimiter();

        var budget = Pair("111", "79000000001", burst: 1);

        await first.WaitAsync(budget, Forever, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<WhatsAppRateLimitedException>(async () =>
            await second.WaitAsync(budget, TimeSpan.Zero, TestContext.Current.CancellationToken));

        Assert.Equal(RateLimitBudget.RecipientPair, exception.Scope.Budget);
        // One message every six seconds to the same user.
        Assert.True(exception.RetryAfter > TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_burst_is_spent_once_across_instances()
    {
        var first = CreateLimiter();
        var second = CreateLimiter();

        var budget = Pair("222", "79000000002", burst: 4);

        await first.WaitAsync(budget, TimeSpan.Zero, TestContext.Current.CancellationToken);
        await second.WaitAsync(budget, TimeSpan.Zero, TestContext.Current.CancellationToken);
        await first.WaitAsync(budget, TimeSpan.Zero, TestContext.Current.CancellationToken);
        await second.WaitAsync(budget, TimeSpan.Zero, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<WhatsAppRateLimitedException>(async () =>
            await first.WaitAsync(budget, TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_penalty_from_one_instance_holds_the_others_back()
    {
        var first = CreateLimiter();
        var second = CreateLimiter();

        var scope = RateLimitScope.PhoneNumberThroughput("333");
        var budget = Throughput("333", 80);

        await first.WaitAsync(budget, Forever, TestContext.Current.CancellationToken);
        await first.PenaliseAsync(scope, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // The instance that never saw the rejection has to back off too, or it walks
        // straight into the same one and lengthens the block for everybody.
        var exception = await Assert.ThrowsAsync<WhatsAppRateLimitedException>(async () =>
            await second.WaitAsync(budget, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        Assert.True(exception.RetryAfter > TimeSpan.FromSeconds(25));
    }

    [Fact]
    public async Task Budgets_of_different_conversations_are_independent()
    {
        var limiter = CreateLimiter();

        await limiter.WaitAsync(
            Pair("444", "79000000001", burst: 1),
            TimeSpan.Zero,
            TestContext.Current.CancellationToken);

        // Hitting the pair limit for one chat says nothing about any other.
        await limiter.WaitAsync(
            Pair("444", "79000000002", burst: 1),
            TimeSpan.Zero,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_refused_call_gives_back_what_the_earlier_budgets_granted()
    {
        var limiter = CreateLimiter();

        // Refilling slowly on purpose. This test runs on the real clock, and a budget of 80 a
        // second tops itself up faster than the assertions can spend it.
        var budgets = new[]
        {
            new RateLimitRequest(RateLimitScope.PhoneNumberThroughput("555"), 1, 3),
            new RateLimitRequest(RateLimitScope.RecipientPair("555", "79000000001"), 1d / 6d, 1),
        };

        await limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken);

        // Refused by the pair budget, after the throughput budget has already handed one over.
        await Assert.ThrowsAsync<WhatsAppRateLimitedException>(async () =>
            await limiter.WaitAsync(budgets, TimeSpan.Zero, TestContext.Current.CancellationToken));

        // Two of the three throughput permits are left, not one: the refused call never went
        // anywhere, so what it took has to come back.
        var throughput = new[] { budgets[0] };
        await limiter.WaitAsync(throughput, TimeSpan.Zero, TestContext.Current.CancellationToken);
        await limiter.WaitAsync(throughput, TimeSpan.Zero, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<WhatsAppRateLimitedException>(async () =>
            await limiter.WaitAsync(throughput, TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_unpaced_application_budget_never_waits_until_it_is_penalised()
    {
        var limiter = CreateLimiter();
        var scope = RateLimitScope.ApplicationRequests("tenant-a");
        var budget = new[] { RateLimitRequest.Unpaced(scope) };

        for (var i = 0; i < 50; i++)
        {
            await limiter.WaitAsync(budget, TimeSpan.Zero, TestContext.Current.CancellationToken);
        }

        await limiter.PenaliseAsync(scope, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<WhatsAppRateLimitedException>(async () =>
            await limiter.WaitAsync(budget, TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Losing_Redis_falls_back_to_pacing_this_instance_alone()
    {
        // Degrading to local pacing means Meta rejects the overshoot, which the retry path
        // already handles. Failing every send instead would turn a Redis blip into an outage.
        var broken = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { { "127.0.0.1", 6399 } },
            AbortOnConnectFail = false,
            ConnectTimeout = 200,
            ConnectRetry = 0,
        });

        var limiter = new RedisRateLimiter(
            broken,
            new InMemoryRateLimiter(TimeProvider.System),
            Options.Create(new RedisRateLimiterOptions()),
            TimeProvider.System,
            NullLogger<RedisRateLimiter>.Instance);

        await limiter.WaitAsync(
            Throughput("666", 80),
            Forever,
            TestContext.Current.CancellationToken);

        await broken.DisposeAsync();
    }

    [Fact]
    public async Task Losing_Redis_can_be_made_fatal_instead()
    {
        var broken = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { { "127.0.0.1", 6399 } },
            AbortOnConnectFail = false,
            ConnectTimeout = 200,
            ConnectRetry = 0,
        });

        var limiter = new RedisRateLimiter(
            broken,
            new InMemoryRateLimiter(TimeProvider.System),
            Options.Create(new RedisRateLimiterOptions { FallBackToLocal = false }),
            TimeProvider.System,
            NullLogger<RedisRateLimiter>.Instance);

        await Assert.ThrowsAsync<WhatsAppException>(async () =>
            await limiter.WaitAsync(Throughput("777", 80), Forever, TestContext.Current.CancellationToken));

        await broken.DisposeAsync();
    }

    private static RateLimitRequest[] Throughput(string phoneNumberId, int perSecond) =>
        [new RateLimitRequest(RateLimitScope.PhoneNumberThroughput(phoneNumberId), perSecond, perSecond)];

    private static RateLimitRequest[] Pair(string phoneNumberId, string recipient, int burst) =>
        [new RateLimitRequest(RateLimitScope.RecipientPair(phoneNumberId, recipient), 1d / 6d, burst)];

    private static async Task<TimeSpan> TimeAsync(Func<ValueTask> operation)
    {
        var started = Stopwatch.GetTimestamp();
        await operation();
        return Stopwatch.GetElapsedTime(started);
    }

    private RedisRateLimiter CreateLimiter() => new(
        _connection,
        new InMemoryRateLimiter(TimeProvider.System),
        Options.Create(new RedisRateLimiterOptions()),
        TimeProvider.System,
        NullLogger<RedisRateLimiter>.Instance);
}
