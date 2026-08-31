namespace Wapper;

/// <summary>
/// Resolves the credentials of a tenant. The default implementation reads them from
/// configuration; a multi-tenant host replaces it with one backed by its own store,
/// which is also where token refresh belongs.
/// </summary>
/// <remarks>
/// Called on every request, so an implementation that talks to a database or a secret
/// store is expected to cache.
/// </remarks>
public interface IWhatsAppCredentialsProvider
{
    /// <summary>Resolves the credentials of <paramref name="tenant"/>.</summary>
    /// <param name="tenant">Tenant name, or <see cref="WhatsAppTenant.Default"/>.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <exception cref="WhatsAppConfigurationException">The tenant is unknown or misconfigured.</exception>
    ValueTask<WhatsAppCredentials> GetCredentialsAsync(string tenant, CancellationToken cancellationToken = default);
}
