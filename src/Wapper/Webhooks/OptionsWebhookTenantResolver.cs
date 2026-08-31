using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Wapper.Internal;

namespace Wapper.Webhooks;

/// <summary>
/// Matches a delivery against the phone numbers and business accounts in
/// <see cref="WhatsAppOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// Walks the configured tenants, which is right for the handful an application writes down
/// and wrong for a host with thousands — that one registers an
/// <see cref="IWhatsAppWebhookTenantResolver"/> backed by its own store, the same way it
/// replaces <see cref="IWhatsAppCredentialsProvider"/>.
/// </para>
/// <para>
/// The configuration is optional and read for the tenants nothing enumerated: with
/// <c>AddWhatsApp()</c> a tenant is bound the first time it is asked for, so the names under
/// <c>WhatsApp:Tenants</c> were never part of a registration.
/// </para>
/// </remarks>
internal sealed class OptionsWebhookTenantResolver(
    IOptionsMonitor<WhatsAppOptions> options,
    WhatsAppTenantNames tenants,
    IConfiguration? configuration)
    : IWhatsAppWebhookTenantResolver
{
    public ValueTask<string?> ResolveAsync(
        WhatsAppWebhookOrigin origin,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Resolve(origin));

    private string? Resolve(WhatsAppWebhookOrigin origin)
    {
        // The number first, because it names exactly one tenant.
        if (origin.PhoneNumberId is { Length: > 0 } number)
        {
            foreach (var tenant in Candidates())
            {
                if (string.Equals(options.Get(tenant).PhoneNumberId, number, StringComparison.Ordinal))
                {
                    return tenant;
                }
            }
        }

        // An account-level delivery names no number, so there is only the account to go on.
        // Several tenants usually share one, and any of them will do: what is being resolved
        // is which app secret the delivery was signed with, and tenants on one account are on
        // one app.
        if (origin.BusinessAccountId is { Length: > 0 } account)
        {
            foreach (var tenant in Candidates())
            {
                if (string.Equals(
                    options.Get(tenant).WhatsAppBusinessAccountId,
                    account,
                    StringComparison.Ordinal))
                {
                    return tenant;
                }
            }
        }

        return null;
    }

    /// <summary>Every tenant name worth asking the options about.</summary>
    private IEnumerable<string> Candidates()
    {
        foreach (var tenant in tenants.Registered)
        {
            yield return tenant;
        }

        var section = configuration?
            .GetSection(WhatsAppOptions.SectionName)
            .GetSection(WhatsAppOptions.TenantsSectionName);

        foreach (var tenant in section?.GetChildren() ?? [])
        {
            if (!tenants.Contains(tenant.Key))
            {
                yield return tenant.Key;
            }
        }
    }
}
