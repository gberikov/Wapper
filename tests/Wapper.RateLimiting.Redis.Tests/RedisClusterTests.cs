using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace Wapper.RateLimiting.Redis.Tests;

/// <summary>
/// The limiter against Redis in cluster mode, where a script may only touch keys that hash
/// to one slot.
/// </summary>
/// <remarks>
/// One node holding every slot is enough to enforce the rule — the <c>CROSSSLOT</c> refusal
/// comes from cluster mode, not from there being several nodes — and it is the only cluster
/// a client on the far side of Docker's port mapping can reach, because a node announces
/// its own address to the client and a container's address is not routable from the host.
/// The node is told to announce the loopback address and a fixed port instead.
/// </remarks>
public sealed class RedisClusterTests : IAsyncLifetime
{
    private const int Port = 7100;

    private readonly IContainer _redis = new ContainerBuilder("redis:7-alpine")
        .WithCommand(
            "redis-server",
            "--port", Port.ToString(),
            "--cluster-enabled", "yes",
            "--cluster-announce-ip", "127.0.0.1",
            "--cluster-announce-port", Port.ToString())
        .WithPortBinding(Port, Port)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "-p", Port.ToString(), "ping"))
        .Build();

    private IConnectionMultiplexer _connection = null!;

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync();
        await _redis.ExecAsync(["redis-cli", "-p", Port.ToString(), "cluster", "addslotsrange", "0", "16383"]);

        // The node takes a moment to see itself as a whole cluster.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var info = await _redis.ExecAsync(["redis-cli", "-p", Port.ToString(), "cluster", "info"]);
            if (info.Stdout.Contains("cluster_state:ok", StringComparison.Ordinal))
            {
                break;
            }

            await Task.Delay(100);
        }

        _connection = await ConnectionMultiplexer.ConnectAsync($"127.0.0.1:{Port}");
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task Without_a_hash_tag_the_cluster_s_refusal_is_a_configuration_error()
    {
        // Two budgets, two slots. Not a Redis outage: falling back to per-process pacing
        // would hide a setting that no retry fixes, so it is named instead.
        var limiter = CreateLimiter(new RedisRateLimiterOptions());

        var exception = await Assert.ThrowsAsync<WhatsAppConfigurationException>(async () =>
            await limiter.WaitAsync(Budgets("111", "79000000001"), TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));

        Assert.Contains("{wapper}:rl:", exception.Message, StringComparison.Ordinal);
        Assert.Contains("KeyPrefix", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_a_hash_tag_every_budget_of_a_call_is_spent_in_one_script()
    {
        var limiter = CreateLimiter(new RedisRateLimiterOptions { KeyPrefix = "{wapper}:rl:" });
        var budgets = Budgets("222", "79000000002");
        var scope = RateLimitScope.PhoneNumberThroughput("222");

        await limiter.WaitAsync(budgets, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        await limiter.PenaliseAsync(scope, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // The penalty landed on the same slot as the grant, and the next call sees it.
        var exception = await Assert.ThrowsAsync<WhatsAppRateLimitedException>(async () =>
            await limiter.WaitAsync(budgets, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        Assert.Equal(RateLimitBudget.PhoneNumberThroughput, exception.Scope.Budget);
        Assert.True(exception.RetryAfter > TimeSpan.FromSeconds(25));
    }

    private static RateLimitRequest[] Budgets(string phoneNumberId, string recipient) =>
    [
        RateLimitRequest.Unpaced(RateLimitScope.ApplicationRequests("app")),
        new RateLimitRequest(RateLimitScope.PhoneNumberThroughput(phoneNumberId), 80, 80),
        new RateLimitRequest(RateLimitScope.RecipientPair(phoneNumberId, recipient), 1d / 6d, 45),
    ];

    private RedisRateLimiter CreateLimiter(RedisRateLimiterOptions options) => new(
        _connection,
        new InMemoryRateLimiter(TimeProvider.System),
        Options.Create(options),
        TimeProvider.System,
        NullLogger<RedisRateLimiter>.Instance);
}
