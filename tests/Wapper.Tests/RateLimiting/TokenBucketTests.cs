using Microsoft.Extensions.Time.Testing;
using Wapper.RateLimiting;

namespace Wapper.Tests.RateLimiting;

public class TokenBucketTests
{
    private static readonly TimeSpan Forever = TimeSpan.FromDays(1);

    [Fact]
    public void Burst_is_spent_without_waiting()
    {
        var bucket = new TokenBucket(burst: 45, permitsPerSecond: 1d / 6d, new FakeTimeProvider());

        for (var i = 0; i < 45; i++)
        {
            Assert.Equal(ReserveOutcome.Reserved, bucket.TryReserve(Forever, out _, out var wait));
            Assert.Equal(TimeSpan.Zero, wait);
        }
    }

    [Fact]
    public void Once_the_burst_is_gone_callers_are_spaced_by_the_sustained_rate()
    {
        // Meta's pair limit: one message every six seconds, after a burst of 45.
        var bucket = new TokenBucket(burst: 45, permitsPerSecond: 1d / 6d, new FakeTimeProvider());

        for (var i = 0; i < 45; i++)
        {
            bucket.TryReserve(Forever, out _, out _);
        }

        Assert.Equal(ReserveOutcome.Reserved, bucket.TryReserve(Forever, out _, out var first));
        Assert.Equal(ReserveOutcome.Reserved, bucket.TryReserve(Forever, out _, out var second));
        Assert.Equal(ReserveOutcome.Reserved, bucket.TryReserve(Forever, out _, out var third));

        // Each caller inherits the deficit of the one before it, which is what turns a
        // burst back into the sustained rate instead of a stampede.
        Assert.Equal(TimeSpan.FromSeconds(6), first);
        Assert.Equal(TimeSpan.FromSeconds(12), second);
        Assert.Equal(TimeSpan.FromSeconds(18), third);
    }

    [Fact]
    public void Waiting_refills_the_bucket()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(burst: 1, permitsPerSecond: 1d / 6d, time);

        bucket.TryReserve(Forever, out _, out _);
        time.Advance(TimeSpan.FromSeconds(6));

        Assert.Equal(ReserveOutcome.Reserved, bucket.TryReserve(Forever, out _, out var wait));
        Assert.Equal(TimeSpan.Zero, wait);
    }

    [Fact]
    public void Refill_never_exceeds_the_burst()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(burst: 2, permitsPerSecond: 1d, time);

        time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(ReserveOutcome.Reserved, bucket.TryReserve(Forever, out _, out _));
        Assert.Equal(ReserveOutcome.Reserved, bucket.TryReserve(Forever, out _, out _));
        Assert.Equal(ReserveOutcome.Reserved, bucket.TryReserve(Forever, out _, out var third));

        // An hour of idleness must not bank an hour of permits.
        Assert.Equal(TimeSpan.FromSeconds(1), third);
    }

    [Fact]
    public void A_wait_longer_than_the_caller_accepts_takes_nothing()
    {
        var bucket = new TokenBucket(burst: 1, permitsPerSecond: 1d / 6d, new FakeTimeProvider());

        bucket.TryReserve(Forever, out _, out _);

        Assert.Equal(ReserveOutcome.Refused, bucket.TryReserve(TimeSpan.FromSeconds(1), out var none, out var wait));
        Assert.Null(none);
        Assert.Equal(TimeSpan.FromSeconds(6), wait);

        // Rejected callers must not spend a permit, or the next one pays for a call that was
        // never made.
        Assert.Equal(ReserveOutcome.Reserved, bucket.TryReserve(TimeSpan.FromSeconds(6), out _, out var afterRejection));
        Assert.Equal(TimeSpan.FromSeconds(6), afterRejection);
    }

    [Fact]
    public void A_cancelled_reservation_is_available_again()
    {
        var bucket = new TokenBucket(burst: 1, permitsPerSecond: 1d / 6d, new FakeTimeProvider());

        bucket.TryReserve(Forever, out var reservation, out _);
        bucket.Cancel(reservation!);

        Assert.Equal(ReserveOutcome.Reserved, bucket.TryReserve(Forever, out _, out var wait));
        Assert.Equal(TimeSpan.Zero, wait);
    }

    [Fact]
    public void A_cancelled_reservation_moves_the_queue_behind_it_forward()
    {
        var bucket = new TokenBucket(burst: 1, permitsPerSecond: 1d, new FakeTimeProvider());

        bucket.TryReserve(Forever, out _, out _);
        bucket.TryReserve(Forever, out var second, out _);
        bucket.TryReserve(Forever, out var third, out var thirdWait);

        Assert.Equal(TimeSpan.FromSeconds(2), thirdWait);

        // The second caller gave up. The third is now next in line, and its wait says so
        // when it is asked again rather than staying fixed at what it was told at first.
        bucket.Cancel(second!);

        Assert.Equal(TimeSpan.FromSeconds(1), bucket.WaitFor(third!));
    }

    [Fact]
    public void A_penalty_recorded_while_a_reservation_waits_pushes_it_back()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(burst: 1, permitsPerSecond: 1d, time);

        bucket.TryReserve(Forever, out _, out _);
        bucket.TryReserve(Forever, out var queued, out var wait);
        Assert.Equal(TimeSpan.FromSeconds(1), wait);

        bucket.Penalise(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromSeconds(1));

        // The second's wait has run out, but the Cloud API has said the budget is spent in
        // the meantime. It is not due until the hold lifts and its deficit is earned back —
        // and nothing was earned under the hold.
        Assert.Equal(TimeSpan.FromSeconds(10), bucket.WaitFor(queued!));
    }

    [Fact]
    public void Reservations_taken_under_a_penalty_are_spread_at_the_sustained_rate_after_it()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(burst: 80, permitsPerSecond: 80, time);

        bucket.Penalise(TimeSpan.FromSeconds(10));

        var waits = new List<TimeSpan>();
        for (var i = 0; i < 80; i++)
        {
            Assert.Equal(ReserveOutcome.Reserved, bucket.TryReserve(Forever, out _, out var wait));
            waits.Add(wait);
        }

        // Not eighty callers released together the instant the hold lifts: the hold drained
        // the bucket, so each of them owes its place in the queue on top of the hold.
        Assert.Equal(TimeSpan.FromSeconds(10) + TimeSpan.FromSeconds(1d / 80), waits[0]);
        Assert.Equal(TimeSpan.FromSeconds(10) + TimeSpan.FromSeconds(1), waits[79]);
        Assert.Equal(80, waits.Distinct().Count());
    }

    [Fact]
    public void A_penalty_holds_the_bucket_even_when_permits_are_banked()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(burst: 80, permitsPerSecond: 80, time);

        bucket.Penalise(TimeSpan.FromSeconds(30));

        Assert.True(bucket.IsHeld);
        Assert.Equal(ReserveOutcome.Reserved, bucket.TryReserve(Forever, out _, out var wait));
        // The hold, plus the one permit the drained bucket has to earn for this caller.
        Assert.Equal(TimeSpan.FromSeconds(30) + TimeSpan.FromSeconds(1d / 80), wait);
    }

    [Fact]
    public void A_penalty_drains_what_was_banked()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(burst: 80, permitsPerSecond: 80, time);

        bucket.Penalise(TimeSpan.FromSeconds(30));
        time.Advance(TimeSpan.FromSeconds(30));

        // Meta kept counting while we were held back, so the moment the penalty lifts is the
        // worst possible moment to release a full burst.
        Assert.False(bucket.IsHeld);
        Assert.Equal(ReserveOutcome.Reserved, bucket.TryReserve(Forever, out _, out var first));
        Assert.Equal(ReserveOutcome.Reserved, bucket.TryReserve(Forever, out _, out var second));
        Assert.True(first > TimeSpan.Zero);
        Assert.True(second > first);
    }

    [Fact]
    public void The_longer_of_two_penalties_wins()
    {
        var bucket = new TokenBucket(burst: 80, permitsPerSecond: 80, new FakeTimeProvider());

        bucket.Penalise(TimeSpan.FromMinutes(5));
        bucket.Penalise(TimeSpan.FromSeconds(1));

        Assert.Equal(ReserveOutcome.Reserved, bucket.TryReserve(Forever, out _, out var wait));
        Assert.True(wait >= TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void An_unpaced_bucket_never_waits_unless_it_is_penalised()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(
            burst: double.PositiveInfinity,
            permitsPerSecond: double.PositiveInfinity,
            time);

        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(ReserveOutcome.Reserved, bucket.TryReserve(Forever, out var reservation, out var wait));
            Assert.Equal(TimeSpan.Zero, wait);
            bucket.Claim(reservation!);
        }

        bucket.Penalise(TimeSpan.FromMinutes(1));

        Assert.Equal(ReserveOutcome.Reserved, bucket.TryReserve(Forever, out _, out var held));
        Assert.Equal(TimeSpan.FromMinutes(1), held);
    }

    [Fact]
    public void A_bucket_is_only_retired_once_forgetting_it_changes_nothing()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(burst: 200, permitsPerSecond: 200d / 3600, time);

        for (var i = 0; i < 200; i++)
        {
            bucket.TryReserve(Forever, out var reservation, out _);
            bucket.Claim(reservation!);
        }

        // Idle for longer than any sweep waits, but forty-nine minutes from full: a fresh
        // bucket would hand out two hundred that Meta's own counter is still earning.
        time.Advance(TimeSpan.FromMinutes(11));
        Assert.False(bucket.TryRetire(TimeSpan.FromMinutes(10)));

        time.Advance(TimeSpan.FromMinutes(50));
        Assert.True(bucket.TryRetire(TimeSpan.FromMinutes(10)));
        Assert.True(bucket.IsRetired);

        // Retired means nothing more is handed out: the caller fetches a fresh one.
        Assert.Equal(ReserveOutcome.Retired, bucket.TryReserve(Forever, out _, out _));
        Assert.False(bucket.Penalise(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void A_bucket_with_somebody_queued_is_not_retired()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(burst: 1, permitsPerSecond: 1d / 3600, time);

        bucket.TryReserve(Forever, out var first, out _);
        bucket.Claim(first!);
        bucket.TryReserve(Forever, out _, out _);

        time.Advance(TimeSpan.FromHours(2));

        Assert.False(bucket.TryRetire(TimeSpan.FromMinutes(10)));
    }
}
