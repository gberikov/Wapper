using Microsoft.Extensions.Options;

namespace Wapper;

/// <summary>
/// Reads credentials straight out of <see cref="WhatsAppOptions"/>, which is what an
/// application with a fixed set of phone numbers wants. Replace it in the container to
/// resolve tenants from a database instead.
/// </summary>
internal sealed class OptionsCredentialsProvider(IOptionsMonitor<WhatsAppOptions> options)
    : IWhatsAppCredentialsProvider
{
    public ValueTask<WhatsAppCredentials> GetCredentialsAsync(
        string tenant,
        CancellationToken cancellationToken = default)
    {
        var tenantOptions = options.Get(tenant);
        var described = string.IsNullOrEmpty(tenant) ? "the default tenant" : $"tenant '{tenant}'";

        if (string.IsNullOrWhiteSpace(tenantOptions.AccessToken))
        {
            throw new WhatsAppConfigurationException(
                $"No access token is configured for {described}. Set WhatsApp:AccessToken, or " +
                $"register an {nameof(IWhatsAppCredentialsProvider)} that resolves credentials " +
                "from your own store.");
        }

        if (string.IsNullOrWhiteSpace(tenantOptions.PhoneNumberId))
        {
            throw new WhatsAppConfigurationException(
                $"No phone number id is configured for {described}. Set WhatsApp:PhoneNumberId, or " +
                $"register an {nameof(IWhatsAppCredentialsProvider)} that resolves credentials " +
                "from your own store.");
        }

        return ValueTask.FromResult(new WhatsAppCredentials
        {
            AccessToken = tenantOptions.AccessToken,
            PhoneNumberId = tenantOptions.PhoneNumberId,
            WhatsAppBusinessAccountId = tenantOptions.WhatsAppBusinessAccountId,
            AppId = tenantOptions.AppId,
        });
    }
}
