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
    public async Task Cancelling_inside_the_queue_does_not_give_a_newcomer_an_existing_target()
    {
        var limiter = CreateLimiter();
        var scope = RateLimitScope.PhoneNumberThroughput("cancel-interior");
        var budgets = new[] { new RateLimitRequest(scope, 1, 1) };
        await limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var first = limiter.WaitAsync(budgets, Forever, cancellation.Token).AsTask();
        await WaitForBalanceAsync(scope, -0.5);

        var second = limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken).AsTask();
        await WaitForBalanceAsync(scope, -1.5);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        var third = limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken).AsTask();
        await second;
        Assert.False(third.IsCompleted);
        var remaining = Stopwatch.StartNew();
        await third;
        Assert.True(remaining.Elapsed >= TimeSpan.FromMilliseconds(700));
    }

    [Fact]
    public async Task A_new_penalty_cannot_extend_a_wait_past_its_original_budget()
    {
        var limiter = CreateLimiter();
        var scope = RateLimitScope.PhoneNumberThroughput("deadline");
        var budgets = new[]
        {
            RateLimitRequest.Unpaced(RateLimitScope.ApplicationRequests("deadline-app")),
            new RateLimitRequest(scope, 1, 1),
        };
        await limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken);
        var queued = limiter.WaitAsync(budgets, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken).AsTask();
        await WaitForBalanceAsync(scope, -0.5);
        await limiter.PenaliseAsync(scope, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<WhatsAppRateLimitedException>(() => queued);
        Assert.Equal(scope, error.Scope);
        var balance = (double)await _connection.GetDatabase().HashGetAsync(RedisRateLimiter.KeyFor("wapper:rl:", scope), "t");
        Assert.True(balance >= 0, $"The refused reservation left a balance of {balance}.");
    }

    [Fact]
    public async Task A_short_key_lifetime_does_not_expire_a_waiting_reservation()
    {
        var limiter = CreateLimiter(new RedisRateLimiterOptions { KeyLifetime = TimeSpan.FromMilliseconds(50) });
        var scope = RateLimitScope.PhoneNumberThroughput("short-lifetime");
        var budgets = new[] { new RateLimitRequest(scope, 2, 1) };
        await limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));

        var elapsed = await TimeAsync(() => limiter.WaitAsync(budgets, TimeSpan.FromSeconds(10), cancellation.Token));
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(400));
    }

    private async Task WaitForBalanceAsync(RateLimitScope scope, double maximum)
    {
        var key = RedisRateLimiter.KeyFor("wapper:rl:", scope);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var balance = await _connection.GetDatabase().HashGetAsync(key, "t");
            if (!balance.IsNull && (double)balance <= maximum)
            {
                return;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Fail("The reservation was not recorded in Redis.");
    }

    [Fact]
    public async Task Every_budget_retains_state_for_a_call_waiting_on_another_budget()
    {
        var limiter = CreateLimiter(new RedisRateLimiterOptions { KeyLifetime = TimeSpan.FromMilliseconds(50) });
        var application = RateLimitScope.ApplicationRequests("retention-app");
        var throughput = RateLimitScope.PhoneNumberThroughput("retention-number");
        var budgets = new[]
        {
            RateLimitRequest.Unpaced(application),
            new RateLimitRequest(throughput, 1, 1),
        };
        await limiter.WaitAsync(budgets, TimeSpan.Zero, TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var queued = limiter.WaitAsync(budgets, TimeSpan.FromMinutes(2), cancellation.Token).AsTask();
        await WaitForBalanceAsync(throughput, -0.5);
        await limiter.PenaliseAsync(application, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        var lifetime = await _connection.GetDatabase().KeyTimeToLiveAsync(RedisRateLimiter.KeyFor("wapper:rl:", application));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.True(lifetime > TimeSpan.FromMinutes(2), $"The other budget would expire after {lifetime}.");
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
    public async Task A_penalty_from_another_instance_holds_back_a_call_already_waiting()
    {
        var first = CreateLimiter();
        var second = CreateLimiter();

        var scope = RateLimitScope.PhoneNumberThroughput("666");
        var budget = new[] { new RateLimitRequest(scope, 1, 1) };

        await first.WaitAsync(budget, Forever, TestContext.Current.CancellationToken);

        // Queued for one second on the first instance. Before it is up, the second instance
        // sees the Cloud API reject a call and holds the budget for three. The queued call
        // has to look again before it goes, not trust the second it was first promised.
        var queued = first.WaitAsync(budget, Forever, TestContext.Current.CancellationToken).AsTask();
        await second.PenaliseAsync(scope, TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        var elapsed = await TimeAsync(() => new ValueTask(queued));

        Assert.True(elapsed >= TimeSpan.FromSeconds(3), $"Released after {elapsed}.");
    }

    [Fact]
    public async Task A_call_that_gives_up_waiting_hands_its_permit_back()
    {
        var limiter = CreateLimiter();
        var budget = new[] { new RateLimitRequest(RateLimitScope.PhoneNumberThroughput("777"), 1, 1) };

        await limiter.WaitAsync(budget, Forever, TestContext.Current.CancellationToken);

        using var cancellation = new CancellationTokenSource();
        var queued = limiter.WaitAsync(budget, Forever, cancellation.Token).AsTask();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);

        // A second on, the budget has earned one permit, and the cancelled call sent nothing.
        await Task.Delay(1100, TestContext.Current.CancellationToken);
        await limiter.WaitAsync(budget, TimeSpan.Zero, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Calls_taken_under_a_penalty_are_spread_after_it_rather_than_released_together()
    {
        var limiter = CreateLimiter();
        var scope = RateLimitScope.PhoneNumberThroughput("888");
        var budget = new[] { new RateLimitRequest(scope, 2, 4) };

        await limiter.PenaliseAsync(scope, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        // Four calls queued during a one-second hold, at two a second: the hold drained the
        // bucket, so they owe their places on top of it and are released over two seconds.
        var started = Stopwatch.GetTimestamp();
        var calls = Enumerable.Range(0, 4)
            .Select(_ => limiter.WaitAsync(budget, Forever, TestContext.Current.CancellationToken).AsTask())
            .ToArray();
        await Task.WhenAll(calls);

        var elapsed = Stopwatch.GetElapsedTime(started);
        Assert.True(elapsed >= TimeSpan.FromSeconds(2.5), $"All released after {elapsed}.");
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

    private RedisRateLimiter CreateLimiter(RedisRateLimiterOptions? options = null) => new(
        _connection,
        new InMemoryRateLimiter(TimeProvider.System),
        Options.Create(options ?? new RedisRateLimiterOptions()),
        TimeProvider.System,
        NullLogger<RedisRateLimiter>.Instance);
}
