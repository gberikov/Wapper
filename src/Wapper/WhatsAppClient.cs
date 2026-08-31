using System.Collections.Concurrent;
using Wapper.Internal;
using Wapper.Media;

namespace Wapper;

/// <summary>One tenant's view of the Cloud API.</summary>
internal sealed class WhatsAppTenantClient(GraphApiClient client, string tenant) : IWhatsAppTenantClient
{
    public string Tenant { get; } = tenant;

    public IMediaApi Media { get; } = new MediaApi(client, tenant);
}

/// <summary>
/// The default tenant, and the factory for the others.
/// </summary>
/// <remarks>
/// Registered as a singleton, so the per-tenant clients are cached rather than rebuilt on
/// every call. They hold no state beyond their tenant name — the credentials are resolved
/// per request — so sharing them is safe.
/// </remarks>
internal sealed class WhatsAppClient(GraphApiClient client) : IWhatsAppClient
{
    private readonly ConcurrentDictionary<string, IWhatsAppTenantClient> _tenants = new(StringComparer.Ordinal);
    private readonly IWhatsAppTenantClient _default = new WhatsAppTenantClient(client, WhatsAppTenant.Default);

    public string Tenant => _default.Tenant;

    public IMediaApi Media => _default.Media;

    public IWhatsAppTenantClient For(string tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        return tenant.Length == 0
            ? _default
            : _tenants.GetOrAdd(tenant, static (name, graph) => new WhatsAppTenantClient(graph, name), client);
    }
}
