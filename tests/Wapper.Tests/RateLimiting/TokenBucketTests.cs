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
            Assert.True(bucket.TryTake(Forever, out var wait));
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
            bucket.TryTake(Forever, out _);
        }

        Assert.True(bucket.TryTake(Forever, out var first));
        Assert.True(bucket.TryTake(Forever, out var second));
        Assert.True(bucket.TryTake(Forever, out var third));

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

        bucket.TryTake(Forever, out _);
        time.Advance(TimeSpan.FromSeconds(6));

        Assert.True(bucket.TryTake(Forever, out var wait));
        Assert.Equal(TimeSpan.Zero, wait);
    }

    [Fact]
    public void Refill_never_exceeds_the_burst()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(burst: 2, permitsPerSecond: 1d, time);

        time.Advance(TimeSpan.FromHours(1));

        Assert.True(bucket.TryTake(Forever, out _));
        Assert.True(bucket.TryTake(Forever, out _));
        Assert.True(bucket.TryTake(Forever, out var third));

        // An hour of idleness must not bank an hour of permits.
        Assert.Equal(TimeSpan.FromSeconds(1), third);
    }

    [Fact]
    public void A_wait_longer_than_the_caller_accepts_takes_nothing()
    {
        var bucket = new TokenBucket(burst: 1, permitsPerSecond: 1d / 6d, new FakeTimeProvider());

        bucket.TryTake(Forever, out _);

        Assert.False(bucket.TryTake(TimeSpan.FromSeconds(1), out var wait));
        Assert.Equal(TimeSpan.FromSeconds(6), wait);

        // Rejected callers must not spend a permit, or the next one pays for a call that was
        // never made.
        Assert.True(bucket.TryTake(TimeSpan.FromSeconds(6), out var afterRejection));
        Assert.Equal(TimeSpan.FromSeconds(6), afterRejection);
    }

    [Fact]
    public void A_returned_permit_is_available_again()
    {
        var bucket = new TokenBucket(burst: 1, permitsPerSecond: 1d / 6d, new FakeTimeProvider());

        bucket.TryTake(Forever, out _);
        bucket.Return();

        Assert.True(bucket.TryTake(Forever, out var wait));
        Assert.Equal(TimeSpan.Zero, wait);
    }

    [Fact]
    public void A_penalty_holds_the_bucket_even_when_permits_are_banked()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(burst: 80, permitsPerSecond: 80, time);

        bucket.Penalise(TimeSpan.FromSeconds(30));

        Assert.True(bucket.IsHeld);
        Assert.True(bucket.TryTake(Forever, out var wait));
        Assert.Equal(TimeSpan.FromSeconds(30), wait);
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
        Assert.True(bucket.TryTake(Forever, out _));
        Assert.True(bucket.TryTake(Forever, out var second));
        Assert.True(second > TimeSpan.Zero);
    }

    [Fact]
    public void The_longer_of_two_penalties_wins()
    {
        var bucket = new TokenBucket(burst: 80, permitsPerSecond: 80, new FakeTimeProvider());

        bucket.Penalise(TimeSpan.FromMinutes(5));
        bucket.Penalise(TimeSpan.FromSeconds(1));

        Assert.True(bucket.TryTake(Forever, out var wait));
        Assert.Equal(TimeSpan.FromMinutes(5), wait);
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
            Assert.True(bucket.TryTake(Forever, out var wait));
            Assert.Equal(TimeSpan.Zero, wait);
        }

        bucket.Penalise(TimeSpan.FromMinutes(1));

        Assert.True(bucket.TryTake(Forever, out var held));
        Assert.Equal(TimeSpan.FromMinutes(1), held);
    }
}
