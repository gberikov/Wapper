namespace Wapper;

/// <summary>
/// Tenant identifiers. A tenant is one set of WhatsApp credentials: an access token,
/// a business phone number and the account it belongs to.
/// </summary>
public static class WhatsAppTenant
{
    /// <summary>
    /// The tenant used when a caller does not name one. Matches the name that
    /// <c>Microsoft.Extensions.Options</c> gives to unnamed options, so a single-number
    /// application never has to think about tenants at all.
    /// </summary>
    public const string Default = "";
}
