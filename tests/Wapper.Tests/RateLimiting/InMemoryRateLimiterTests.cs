using Microsoft.Extensions.Time.Testing;
using Wapper.RateLimiting;
using Wapper.Tests.Fakes;

namespace Wapper.Tests.RateLimiting;

public class InMemoryRateLimiterTests
{
    private static readonly TimeSpan Forever = TimeSpan.FromDays(1);

    [Fact]
    public async Task A_new_penalty_cannot_extend_a_wait_past_its_original_budget()
    {
        var time = new FakeTimeProvider();
        var limiter = new InMemoryRateLimiter(time);
        var scope = RateLimitScope.PhoneNumberThroughput("deadline");
        var budgets = new[] { new RateLimitRequest(scope, 1, 1) };
        await limiter.WaitAsync(budgets, TimeSpan.Zero, TestContext.Current.CancellationToken);

        var queued = limiter.WaitAsync(budgets, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken).AsTask();
        await limiter.PenaliseAsync(scope, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));

        var error = await Assert.ThrowsAsync<WhatsAppRateLimitedException>(() => queued);
        Assert.Equal(scope, error.Scope);

        // Refusal also returns the reservation: after the hold, one earned permit suffices.
        time.Advance(TimeSpan.FromSeconds(10));
        await limiter.WaitAsync(budgets, TimeSpan.Zero, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Rechecking_uses_the_remaining_wait_budget_not_a_fresh_one()
    {
        var time = new FakeTimeProvider();
        var limiter = new InMemoryRateLimiter(time);
        var scope = RateLimitScope.PhoneNumberThroughput("elapsed-deadline");
        var budgets = new[] { new RateLimitRequest(scope, 1, 1) };
        await limiter.WaitAsync(budgets, TimeSpan.Zero, TestContext.Current.CancellationToken);
        var queued = limiter.WaitAsync(budgets, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken).AsTask();
        time.Advance(TimeSpan.FromMilliseconds(500));
        await limiter.PenaliseAsync(scope, TimeSpan.FromMilliseconds(1500), TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMilliseconds(500));

        // 1.5 seconds still owed, but only one second left in the original two seconds.
        var error = await Assert.ThrowsAsync<WhatsAppRateLimitedException>(() => queued);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), error.RetryAfter);
    }

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
    public async Task A_penalty_for_a_budget_nobody_has_spent_yet_is_kept_until_one_does()
    {
        var time = new FakeTimeProvider();
        var limiter = new InMemoryRateLimiter(time);
        var scope = RateLimitScope.BusinessAccountRequests("waba-1");

        // Exactly what a usage header does: it names the account allowance on the response to
        // a message, which does not spend that allowance and so has never built its bucket.
        // Dropping the hold here would let the next management call walk into a block this
        // one has already been told about.
        await limiter.PenaliseAsync(scope, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<WhatsAppRateLimitedException>(async () =>
            await limiter.WaitAsync(
                [new RateLimitRequest(scope, 200 / 3600d, 200)],
                TimeSpan.Zero,
                TestContext.Current.CancellationToken));

        Assert.Equal(RateLimitBudget.BusinessAccountRequests, exception.Scope.Budget);
        Assert.True(exception.RetryAfter > TimeSpan.FromSeconds(55));
    }

    [Fact]
    public async Task A_kept_penalty_is_handed_over_only_once()
    {
        var time = new FakeTimeProvider();
        var limiter = new InMemoryRateLimiter(time);
        var scope = RateLimitScope.PhoneNumberThroughput("111");
        var budgets = new[] { new RateLimitRequest(scope, 80, 80) };

        await limiter.PenaliseAsync(scope, TimeSpan.FromSeconds(16), TestContext.Current.CancellationToken);

        var started = time.GetUtcNow();
        await Clock.RunAsync(
            time,
            limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken).AsTask());

        Assert.True(time.GetUtcNow() - started >= TimeSpan.FromSeconds(16));

        // Handing it over a second time would restart a hold that has already been served.
        // The next call still pays back the permit the first one took out of a drained
        // bucket, which at 80 a second is a fraction of a second — nowhere near the hold.
        var afterwards = time.GetUtcNow();
        await Clock.RunAsync(
            time,
            limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken).AsTask());

        Assert.True(time.GetUtcNow() - afterwards < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_penalty_recorded_while_a_call_waits_holds_that_call_back_too()
    {
        var time = new FakeTimeProvider();
        var limiter = new InMemoryRateLimiter(time);
        var scope = RateLimitScope.PhoneNumberThroughput("111");
        var budgets = new[] { new RateLimitRequest(scope, 1, 1) };

        await limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken);

        // Queued for one second. Before that second is up the Cloud API rejects somebody
        // else's call, and the budget is held for ten. The queued call must not walk into
        // the block just because its wait was priced before the block was known.
        var queued = limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken).AsTask();
        await limiter.PenaliseAsync(scope, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var started = time.GetUtcNow();
        time.Advance(TimeSpan.FromSeconds(1));
        await Task.Yield();
        Assert.False(queued.IsCompleted);

        await Clock.RunAsync(time, queued);

        Assert.True(time.GetUtcNow() - started >= TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_penalty_on_the_application_budget_holds_a_call_waiting_on_another_budget()
    {
        var time = new FakeTimeProvider();
        var limiter = new InMemoryRateLimiter(time);
        var application = RateLimitScope.ApplicationRequests(WhatsAppTenant.Default);
        var budgets = new[]
        {
            RateLimitRequest.Unpaced(application),
            new RateLimitRequest(RateLimitScope.RecipientPair("111", "79000000001"), 1d / 6d, 1),
        };

        await limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken);

        // Waiting six seconds on the pair allowance. Meanwhile the whole application is
        // blocked for a minute; every budget of a call is looked at again before it goes.
        var queued = limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken).AsTask();
        await limiter.PenaliseAsync(application, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        var started = time.GetUtcNow();
        await Clock.RunAsync(time, queued);

        Assert.True(time.GetUtcNow() - started >= TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task A_call_that_gives_up_waiting_hands_its_permit_back()
    {
        var time = new FakeTimeProvider();
        var limiter = new InMemoryRateLimiter(time);
        var budgets = new[] { new RateLimitRequest(RateLimitScope.PhoneNumberThroughput("111"), 1, 1) };

        await limiter.WaitAsync(budgets, Forever, TestContext.Current.CancellationToken);

        using var cancellation = new CancellationTokenSource();
        var queued = limiter.WaitAsync(budgets, Forever, cancellation.Token).AsTask();
        await Task.Yield();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);

        // One second on, the bucket has earned one permit. The cancelled call sent nothing,
        // so that permit belongs to whoever asks next — not to a call that never went.
        time.Advance(TimeSpan.FromSeconds(1));
        await limiter.WaitAsync(budgets, TimeSpan.Zero, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_cancelled_call_gives_back_every_budget_it_reserved()
    {
        var time = new FakeTimeProvider();
        var limiter = new InMemoryRateLimiter(time);
        var throughput = new RateLimitRequest(RateLimitScope.PhoneNumberThroughput("111"), 80, 80);
        var pair = new RateLimitRequest(RateLimitScope.RecipientPair("111", "79000000001"), 1d / 6d, 1);

        await limiter.WaitAsync([pair], Forever, TestContext.Current.CancellationToken);

        // Throughput is granted at once; the pair allowance makes the call wait. Giving up
        // has to return the throughput permit as well as the pair one.
        using var cancellation = new CancellationTokenSource();
        var queued = limiter.WaitAsync([throughput, pair], Forever, cancellation.Token).AsTask();
        await Task.Yield();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);

        for (var i = 0; i < 80; i++)
        {
            await limiter.WaitAsync([throughput], TimeSpan.Zero, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task An_idle_budget_is_not_forgotten_before_it_has_recovered()
    {
        var time = new FakeTimeProvider();
        var limiter = new InMemoryRateLimiter(time);
        var budgets = new[]
        {
            new RateLimitRequest(RateLimitScope.BusinessAccountRequests("waba-1"), 200d / 3600, 200),
        };

        for (var i = 0; i < 200; i++)
        {
            await limiter.WaitAsync(budgets, TimeSpan.Zero, TestContext.Current.CancellationToken);
        }

        // Eleven idle minutes is past the sweep, and earns back thirty-six of the two
        // hundred. A limiter that dropped the bucket on age alone would hand out two
        // hundred again, and Meta's own counter would reject a hundred and sixty of them.
        time.Advance(TimeSpan.FromMinutes(11));

        var granted = 0;
        try
        {
            while (granted <= 200)
            {
                await limiter.WaitAsync(budgets, TimeSpan.Zero, TestContext.Current.CancellationToken);
                granted++;
            }
        }
        catch (WhatsAppRateLimitedException)
        {
            // The allowance ran out, which is the point.
        }

        Assert.InRange(granted, 30, 37);
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
