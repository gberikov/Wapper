using System.Net;
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

    /// <summary>Registers the default tenant, configured in code.</summary>
    /// <returns>
    /// The <see cref="IHttpClientBuilder"/> of the underlying client, so handlers and
    /// policies can be chained onto it.
    /// </returns>
    public static IHttpClientBuilder AddWhatsApp(
        this IServiceCollection services,
        Action<WhatsAppOptions>? configure = null) =>
        services.AddWhatsApp(WhatsAppTenant.Default, configure);

    /// <summary>
    /// Registers every tenant the configuration section describes.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">
    /// The <c>WhatsApp</c> section. What it sets directly configures the default tenant; a
    /// <see cref="WhatsAppOptions.TenantsSectionName"/> child registers one named tenant per
    /// entry, keyed by the name <c>IWhatsAppClient.For(tenant)</c> takes.
    /// </param>
    /// <param name="configure">
    /// Runs for every tenant registered here, after the configuration is bound, so it wins
    /// over both. For settings that differ per tenant, put them in that tenant's section.
    /// </param>
    /// <remarks>
    /// <para>
    /// An application with one business phone number writes its settings in the section and
    /// stops; there is nothing else to do and no tenant to name. One with several adds a
    /// <c>Tenants</c> entry each, and every entry inherits what is set alongside it — so the
    /// app secret, the Graph API version and the rate limits are written once and only the
    /// credentials are repeated.
    /// </para>
    /// <para>
    /// The default tenant is registered either way. In a multi-tenant host that is what the
    /// webhook endpoint reads its <see cref="WhatsAppOptions.AppSecret"/> and
    /// <see cref="WhatsAppOptions.WebhookVerifyToken"/> from, and it deliberately has no
    /// credentials of its own: sending through it then fails saying so, rather than sending
    /// as whichever tenant happened to be first.
    /// </para>
    /// </remarks>
    public static IHttpClientBuilder AddWhatsApp(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<WhatsAppOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        Register(services, WhatsAppTenant.Default, shared: null, own: configuration, configure);

        foreach (var tenant in configuration.GetSection(WhatsAppOptions.TenantsSectionName).GetChildren())
        {
            // Bound twice: the shared section first, then the tenant's own over the top of
            // it. Without that a version or a limit set for everybody would silently apply to
            // the default tenant alone, which is the one nobody sends through.
            Register(services, tenant.Key, shared: configuration, own: tenant, configure);
        }

        return services.AddWhatsAppCore();
    }

    /// <summary>Registers a named tenant, configured in code.</summary>
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

        Register(services, tenant, shared: null, own: null, configure);

        return services.AddWhatsAppCore();
    }

    /// <summary>
    /// Registers one named tenant from one configuration section.
    /// </summary>
    /// <remarks>
    /// The section is the tenant's own, and nothing is inherited from around it. Use the
    /// overload without a name to read a whole <c>WhatsApp</c> section, tenants and all.
    /// </remarks>
    public static IHttpClientBuilder AddWhatsApp(
        this IServiceCollection services,
        string tenant,
        IConfiguration configuration,
        Action<WhatsAppOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(configuration);

        Register(services, tenant, shared: null, own: configuration, configure);

        return services.AddWhatsAppCore();
    }

    /// <summary>
    /// Binds one tenant's options: the shared section, then its own, then the code.
    /// </summary>
    /// <remarks>
    /// Order is the whole point. Configuration binding leaves alone what a source does not
    /// set, so each layer overrides only what it mentions.
    /// </remarks>
    private static void Register(
        IServiceCollection services,
        string tenant,
        IConfiguration? shared,
        IConfiguration? own,
        Action<WhatsAppOptions>? configure)
    {
        var options = services.AddOptions<WhatsAppOptions>(tenant);

        if (shared is not null)
        {
            options.Bind(shared);
        }

        if (own is not null)
        {
            options.Bind(own);
        }

        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
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
        services.TryAddSingleton(static provider => provider.GetRequiredService<IWhatsAppClient>().Account);
        services.TryAddSingleton(static provider => provider.GetRequiredService<IWhatsAppClient>().BusinessProfile);
        services.TryAddSingleton(static provider => provider.GetRequiredService<IWhatsAppClient>().Flows);
        services.TryAddSingleton(static provider => provider.GetRequiredService<IWhatsAppClient>().Analytics);
        services.TryAddSingleton(static provider => provider.GetRequiredService<IWhatsAppClient>().Raw);

        return services
            .AddHttpClient<GraphApiClient>(HttpClientName)
            .ConfigureHttpClient(static client =>
            {
                // Every tenant sets its own timeout, enforced per call. A timeout on the
                // shared client would cap them all at whichever tenant was registered first.
                client.Timeout = Timeout.InfiniteTimeSpan;

                // Graph speaks HTTP/2, and a number sending a thousand messages a second is
                // better off multiplexing them over a few connections than opening one per
                // request. Negotiated, not demanded: a proxy that only speaks 1.1 still works.
                client.DefaultRequestVersion = HttpVersion.Version20;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            // IWhatsAppClient is a singleton and holds this client for the life of the
            // process, so the factory never gets to hand it a rebuilt handler. Rotating the
            // connections inside the handler instead is what keeps a DNS change to
            // graph.facebook.com from being ignored until the next deployment.
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            })
            // Says out loud what is already true: the handler is never rotated, because
            // nothing ever asks the factory for a second one.
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
    }
}
