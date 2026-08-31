using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Wapper;
using Wapper.Internal;
using Wapper.RateLimiting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the WhatsApp Cloud API client.</summary>
public static class WhatsAppServiceCollectionExtensions
{
    /// <summary>Name of the <see cref="HttpClient"/> the client sends through.</summary>
    public const string HttpClientName = "Wapper";

    /// <summary>Registers the default tenant.</summary>
    /// <returns>
    /// The <see cref="IHttpClientBuilder"/> of the underlying client, so handlers and
    /// policies can be chained onto it.
    /// </returns>
    public static IHttpClientBuilder AddWhatsApp(
        this IServiceCollection services,
        Action<WhatsAppOptions>? configure = null) =>
        services.AddWhatsApp(WhatsAppTenant.Default, configure);

    /// <summary>Registers the default tenant, bound from a configuration section.</summary>
    public static IHttpClientBuilder AddWhatsApp(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<WhatsAppOptions>? configure = null) =>
        services.AddWhatsApp(WhatsAppTenant.Default, configuration, configure);

    /// <summary>Registers a named tenant.</summary>
    /// <param name="services">The container.</param>
    /// <param name="tenant">
    /// Tenant name, passed later to <c>IWhatsAppClient.For(tenant)</c>. Use
    /// <see cref="WhatsAppTenant.Default"/> for an application with a single phone number.
    /// </param>
    /// <param name="configure">Configures the tenant's options.</param>
    public static IHttpClientBuilder AddWhatsApp(
        this IServiceCollection services,
        string tenant,
        Action<WhatsAppOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tenant);

        var options = services.AddOptions<WhatsAppOptions>(tenant);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();

        return services.AddWhatsAppCore();
    }

    /// <summary>Registers a named tenant, bound from a configuration section.</summary>
    public static IHttpClientBuilder AddWhatsApp(
        this IServiceCollection services,
        string tenant,
        IConfiguration configuration,
        Action<WhatsAppOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = services.AddOptions<WhatsAppOptions>(tenant).Bind(configuration);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();

        return services.AddWhatsAppCore();
    }

    private static IHttpClientBuilder AddWhatsAppCore(this IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<WhatsAppOptions>, WhatsAppOptionsValidator>());

        // Replaced by the host when credentials come from somewhere other than configuration.
        services.TryAddSingleton<IWhatsAppCredentialsProvider, OptionsCredentialsProvider>();

        // Replaced by the Redis package when the application runs in more than one instance.
        services.TryAddSingleton<IWhatsAppRateLimiter, InMemoryRateLimiter>();

        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<IWhatsAppClient, WhatsAppClient>();

        // Resolvable on their own, for code that only ever touches one resource group and
        // should not have to know the facade exists.
        services.TryAddSingleton(static provider => provider.GetRequiredService<IWhatsAppClient>().Messages);
        services.TryAddSingleton(static provider => provider.GetRequiredService<IWhatsAppClient>().Media);
        services.TryAddSingleton(static provider => provider.GetRequiredService<IWhatsAppClient>().Templates);
        services.TryAddSingleton(static provider => provider.GetRequiredService<IWhatsAppClient>().PhoneNumbers);
        services.TryAddSingleton(static provider => provider.GetRequiredService<IWhatsAppClient>().BusinessProfile);

        return services
            .AddHttpClient<GraphApiClient>(HttpClientName)
            // Every tenant sets its own timeout, enforced per call. A timeout on the shared
            // client would cap them all at whichever tenant was registered first.
            .ConfigureHttpClient(static client => client.Timeout = Timeout.InfiniteTimeSpan);
    }
}
