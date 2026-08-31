using Microsoft.Extensions.Time.Testing;
using Wapper.RateLimiting;
using Wapper.Tests.Fakes;

namespace Wapper.Tests.RateLimiting;

public class InMemoryRateLimiterTests
{
    private static readonly TimeSpan Forever = TimeSpan.FromDays(1);

    [Fact]
    public async Task A_call_within_every_budget_does_not_wait()
    {
        var time = new FakeTimeProvider();
        var limiter = new InMemoryRateLimiter(time);
        var started = time.GetUtcNow();

        await limiter.WaitAsync(Budgets("111", "79000000001"), Forever, TestContext.Current.CancellationToken);

        Assert.Equal(started, time.GetUtcNow());
    }

    [Fact]
    public async Task Two_messages_to_the_same_recipient_are_spaced_by_the_pair_interval()
    {
        var time = new FakeTimeProvider();
        var limiter = new InMemoryRateLimiter(time);
        var budgets = PairOnly("111", "79000000001", burst: 1);

        await limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken);

        var started = time.GetUtcNow();
        await Clock.RunAsync(
            time,
            limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken).AsTask());

        // Meta allows one message every six seconds to the same user.
        Assert.True(time.GetUtcNow() - started >= TimeSpan.FromSeconds(6));
    }

    [Fact]
    public async Task A_busy_conversation_does_not_hold_up_a_different_one()
    {
        var time = new FakeTimeProvider();
        var limiter = new InMemoryRateLimiter(time);

        await limiter.WaitAsync(
            PairOnly("111", "79000000001", burst: 1),
            Forever,
            TestContext.Current.CancellationToken);

        var started = time.GetUtcNow();
        await limiter.WaitAsync(
            PairOnly("111", "79000000002", burst: 1),
            Forever,
            TestContext.Current.CancellationToken);

        // The pair limit is counted per conversation. Stalling the whole queue on it would
        // punish every other recipient for one busy chat.
        Assert.Equal(started, time.GetUtcNow());
    }

    [Fact]
    public async Task A_wait_longer_than_the_caller_accepts_raises_instead_of_blocking()
    {
        var time = new FakeTimeProvider();
        var limiter = new InMemoryRateLimiter(time);
        var budgets = PairOnly("111", "79000000001", burst: 1);

        await limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<WhatsAppRateLimitedException>(async () =>
            await limiter.WaitAsync(
                budgets,
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken));

        Assert.Equal(RateLimitBudget.RecipientPair, exception.Scope.Budget);
        Assert.Equal(TimeSpan.FromSeconds(6), exception.RetryAfter);
        Assert.Null(exception.Error);
    }

    [Fact]
    public async Task A_refused_call_gives_back_the_permits_the_earlier_budgets_granted()
    {
        var time = new FakeTimeProvider();
        var limiter = new InMemoryRateLimiter(time);

        // Throughput is plentiful, the pair allowance is exhausted. The refusal comes from
        // the second budget, after the first has already handed over a permit.
        var budgets = new[]
        {
            new RateLimitRequest(RateLimitScope.PhoneNumberThroughput("111"), 80, 80),
            new RateLimitRequest(RateLimitScope.RecipientPair("111", "79000000001"), 1d / 6d, 1),
        };

        await limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<WhatsAppRateLimitedException>(async () =>
            await limiter.WaitAsync(budgets, TimeSpan.Zero, TestContext.Current.CancellationToken));

        // The throughput budget must be back to 79 of 80, not 78: the refused call never
        // went anywhere.
        var throughput = new[] { budgets[0] };
        for (var i = 0; i < 79; i++)
        {
            await limiter.WaitAsync(throughput, TimeSpan.Zero, TestContext.Current.CancellationToken);
        }

        await Assert.ThrowsAsync<WhatsAppRateLimitedException>(async () =>
            await limiter.WaitAsync(throughput, TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_penalised_budget_holds_calls_back()
    {
        var time = new FakeTimeProvider();
        var limiter = new InMemoryRateLimiter(time);
        var budgets = Budgets("111", "79000000001");

        await limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken);

        await limiter.PenaliseAsync(
            RateLimitScope.PhoneNumberThroughput("111"),
            TimeSpan.FromSeconds(16),
            TestContext.Current.CancellationToken);

        var started = time.GetUtcNow();
        await Clock.RunAsync(
            time,
            limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken).AsTask());

        Assert.True(time.GetUtcNow() - started >= TimeSpan.FromSeconds(16));
    }

    [Fact]
    public async Task The_unpaced_application_budget_still_honours_a_penalty()
    {
        var time = new FakeTimeProvider();
        var limiter = new InMemoryRateLimiter(time);
        var budgets = new[]
        {
            RateLimitRequest.Unpaced(RateLimitScope.ApplicationRequests(WhatsAppTenant.Default)),
        };

        // The platform budget has no published size, so it is never paced ahead of time --
        // but once Meta says the application is blocked, everything has to stop.
        await limiter.WaitAsync(budgets, TimeSpan.Zero, TestContext.Current.CancellationToken);

        await limiter.PenaliseAsync(
            RateLimitScope.ApplicationRequests(WhatsAppTenant.Default),
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<WhatsAppRateLimitedException>(async () =>
            await limiter.WaitAsync(budgets, TimeSpan.Zero, TestContext.Current.CancellationToken));

        Assert.Equal(RateLimitBudget.ApplicationRequests, exception.Scope.Budget);
    }

    [Fact]
    public async Task Penalising_a_budget_nobody_has_used_does_nothing()
    {
        var limiter = new InMemoryRateLimiter(new FakeTimeProvider());

        await limiter.PenaliseAsync(
            RateLimitScope.PhoneNumberThroughput("never-seen"),
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);
    }

    private static RateLimitRequest[] Budgets(string phoneNumberId, string recipient) =>
    [
        RateLimitRequest.Unpaced(RateLimitScope.ApplicationRequests(WhatsAppTenant.Default)),
        new RateLimitRequest(RateLimitScope.PhoneNumberThroughput(phoneNumberId), 80, 80),
        new RateLimitRequest(RateLimitScope.RecipientPair(phoneNumberId, recipient), 1d / 6d, 45),
    ];

    private static RateLimitRequest[] PairOnly(string phoneNumberId, string recipient, int burst) =>
    [
        new RateLimitRequest(RateLimitScope.RecipientPair(phoneNumberId, recipient), 1d / 6d, burst),
    ];
}
