using System.Collections.Concurrent;
using Wapper.Accounts;
using Wapper.BusinessProfiles;
using Wapper.Analytics;
using Wapper.Flows;
using Wapper.Internal;
using Wapper.Media;
using Wapper.Messages;
using Wapper.PhoneNumbers;
using Wapper.Templates;

namespace Wapper;

/// <summary>One tenant's view of the Cloud API.</summary>
internal sealed class WhatsAppTenantClient(GraphApiClient client, string tenant) : IWhatsAppTenantClient
{
    public string Tenant { get; } = tenant;

    public IMessagesApi Messages { get; } = new MessagesApi(client, tenant);

    public IMediaApi Media { get; } = new MediaApi(client, tenant);

    public ITemplatesApi Templates { get; } = new TemplatesApi(client, tenant);

    public IPhoneNumbersApi PhoneNumbers { get; } = new PhoneNumbersApi(client, tenant);

    public IWhatsAppAccountApi Account { get; } = new WhatsAppAccountApi(client, tenant);

    public IBusinessProfileApi BusinessProfile { get; } = new BusinessProfileApi(client, tenant);

    public IFlowsApi Flows { get; } = new FlowsApi(client, tenant);

    public IAnalyticsApi Analytics { get; } = new AnalyticsApi(client, tenant);
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

    public IMessagesApi Messages => _default.Messages;

    public IMediaApi Media => _default.Media;

    public ITemplatesApi Templates => _default.Templates;

    public IPhoneNumbersApi PhoneNumbers => _default.PhoneNumbers;

    public IWhatsAppAccountApi Account => _default.Account;

    public IBusinessProfileApi BusinessProfile => _default.BusinessProfile;

    public IFlowsApi Flows => _default.Flows;

    public IAnalyticsApi Analytics => _default.Analytics;

    public IWhatsAppTenantClient For(string tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        return tenant.Length == 0
            ? _default
            : _tenants.GetOrAdd(tenant, static (name, graph) => new WhatsAppTenantClient(graph, name), client);
    }
}
