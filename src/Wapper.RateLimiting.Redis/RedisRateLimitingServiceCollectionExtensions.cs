using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using Wapper.RateLimiting;
using Wapper.RateLimiting.Redis;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Shares the rate limiter's counters through Redis.</summary>
public static class RedisRateLimitingServiceCollectionExtensions
{
    /// <summary>
    /// Shares the budgets through an <see cref="IConnectionMultiplexer"/> already in the
    /// container.
    /// </summary>
    /// <remarks>
    /// Call after <c>AddWhatsApp</c>: this replaces the per-process limiter it registered.
    /// </remarks>
    public static IServiceCollection AddWhatsAppRedisRateLimiting(
        this IServiceCollection services,
        Action<RedisRateLimiterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);

        // The per-process limiter stays registered as the fallback, so losing Redis degrades
        // to local pacing rather than stopping every send.
        services.AddSingleton<InMemoryRateLimiter>();

        services.Replace(ServiceDescriptor.Singleton<IWhatsAppRateLimiter>(provider =>
            ActivatorUtilities.CreateInstance<RedisRateLimiter>(
                provider,
                provider.GetRequiredService<InMemoryRateLimiter>())));

        return services;
    }

    /// <summary>
    /// Shares the budgets through a connection of this library's own.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">
    /// A StackExchange.Redis configuration string, for example <c>localhost:6379</c>.
    /// </param>
    /// <param name="configure">Configures the limiter.</param>
    /// <remarks>
    /// Only registers a connection if the container has none. An application already talking
    /// to Redis should register its own multiplexer and use the other overload, so the two do
    /// not open separate connections.
    /// </remarks>
    public static IServiceCollection AddWhatsAppRedisRateLimiting(
        this IServiceCollection services,
        string configuration,
        Action<RedisRateLimiterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        services.TryAddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(configuration));

        return services.AddWhatsAppRedisRateLimiting(configure);
    }
}
