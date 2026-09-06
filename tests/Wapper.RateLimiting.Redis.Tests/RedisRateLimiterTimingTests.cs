using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StackExchange.Redis;
using Xunit;

namespace Wapper.RateLimiting.Redis.Tests;

/// <summary>Controls response delivery independently of Redis's wait estimates.</summary>
public sealed class RedisRateLimiterTimingTests
{
    private static readonly RateLimitRequest[] Budgets =
        [new(RateLimitScope.PhoneNumberThroughput("delayed-response"), 1, 1)];

    [Fact]
    public async Task A_delayed_acquire_response_does_not_refuse_a_permit_due_before_the_deadline()
    {
        var time = new FakeTimeProvider();
        var acquire = new TaskCompletionSource<RedisResult>();
        var recheck = new TaskCompletionSource<RedisResult>();
        var limiter = CreateLimiter(time, acquire.Task, recheck.Task);

        var call = limiter.WaitAsync(Budgets, TimeSpan.FromMilliseconds(1200), TestContext.Current.CancellationToken).AsTask();
        time.Advance(TimeSpan.FromMilliseconds(300));
        acquire.SetResult(Result(1, 1000, 1, "1"));
        time.Advance(TimeSpan.FromMilliseconds(900));
        recheck.SetResult(Result(0, 1));

        await call;
    }

    [Fact]
    public async Task A_delayed_recheck_response_does_not_refuse_a_permit_due_before_the_deadline()
    {
        var time = new FakeTimeProvider();
        var recheck = new TaskCompletionSource<RedisResult>();
        var limiter = CreateLimiter(time, Task.FromResult(Result(1, 1000, 1, "1")),
            recheck.Task, Task.FromResult(Result(0, 1)));

        var call = limiter.WaitAsync(Budgets, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken).AsTask();
        time.Advance(TimeSpan.FromSeconds(1));
        // Redis priced another 600 ms at t=1s; that response arrives at t=1.8s.
        time.Advance(TimeSpan.FromMilliseconds(800));
        recheck.SetResult(Result(600, 1));
        time.Advance(TimeSpan.FromMilliseconds(200));

        await call;
    }

    [Fact]
    public async Task A_positive_recheck_at_the_deadline_refuses_and_returns_the_reservation()
    {
        var time = new FakeTimeProvider();
        var acquire = new TaskCompletionSource<RedisResult>();
        var refunded = new TaskCompletionSource<RedisResult>();
        var limiter = CreateLimiter(time, acquire.Task, Task.FromResult(Result(500, 1)), refunded.Task);

        var call = limiter.WaitAsync(Budgets, TimeSpan.FromMilliseconds(1200), TestContext.Current.CancellationToken).AsTask();
        time.Advance(TimeSpan.FromMilliseconds(300));
        acquire.SetResult(Result(1, 1000, 1, "1"));
        time.Advance(TimeSpan.FromMilliseconds(900));
        Assert.False(call.IsCompleted);
        refunded.SetResult(Result(1));

        var error = await Assert.ThrowsAsync<WhatsAppRateLimitedException>(() => call);
        Assert.Equal(Budgets[0].Scope, error.Scope);
        Assert.Equal(TimeSpan.FromMilliseconds(500), error.RetryAfter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_recheck_proceeds_only_after_the_full_estimate_was_waited(bool capped)
    {
        var time = new FakeTimeProvider();
        var acquire = new TaskCompletionSource<RedisResult>();
        var recheck = new TaskCompletionSource<RedisResult>();
        var limiter = CreateLimiter(time, acquire.Task, recheck.Task, Task.FromResult(Result(1)));

        var call = limiter.WaitAsync(Budgets, TimeSpan.FromMilliseconds(capped ? 1200 : 2000),
            TestContext.Current.CancellationToken).AsTask();
        time.Advance(TimeSpan.FromMilliseconds(300));
        acquire.SetResult(Result(1, 1000, 1, "1"));
        time.Advance(TimeSpan.FromMilliseconds(capped ? 900 : 1000));
        recheck.SetException(new RedisTimeoutException("Response unavailable.", CommandStatus.Sent));

        if (capped)
        {
            var error = await Assert.ThrowsAsync<WhatsAppRateLimitedException>(() => call);
            Assert.Equal(TimeSpan.FromMilliseconds(100), error.RetryAfter);
        }
        else
        {
            await call;
        }
    }

    private static RedisResult Result(params RedisValue[] values) => RedisResult.Create(values);

    private static RedisRateLimiter CreateLimiter(FakeTimeProvider time, params Task<RedisResult>[] responses)
    {
        var pending = new Queue<Task<RedisResult>>(responses);
        var database = RedisProxy.Stub<IDatabase>(method =>
        {
            Assert.Equal(nameof(IDatabase.ScriptEvaluateAsync), method.Name);
            Assert.NotEmpty(pending);
            return pending.Dequeue();
        });
        var connection = RedisProxy.Stub<IConnectionMultiplexer>(method =>
        {
            Assert.Equal(nameof(IConnectionMultiplexer.GetDatabase), method.Name);
            return database;
        });
        return new RedisRateLimiter(connection, new InMemoryRateLimiter(time),
            Options.Create(new RedisRateLimiterOptions()), time, NullLogger<RedisRateLimiter>.Instance);
    }

    public class RedisProxy : DispatchProxy
    {
        private Func<MethodInfo, object?> _invoke = null!;

        public static T Stub<T>(Func<MethodInfo, object?> invoke) where T : class
        {
            var stub = Create<T, RedisProxy>();
            ((RedisProxy)(object)stub)._invoke = invoke;
            return stub;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => _invoke(targetMethod!);
    }
}
