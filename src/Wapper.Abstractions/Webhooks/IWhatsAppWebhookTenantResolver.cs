namespace Wapper.Webhooks;

/// <summary>
/// What a webhook delivery says about where it came from, read before its signature has been
/// checked.
/// </summary>
/// <remarks>
/// Both fields come out of a body nobody has verified yet, so neither is evidence of
/// anything. They are only ever used to choose which app secret to check the signature
/// against: a forged one picks a tenant whose secret does not match, and the delivery is
/// refused. Nothing read here reaches a handler.
/// </remarks>
/// <param name="PhoneNumberId">
/// The business phone number the change is about, from <c>metadata.phone_number_id</c>.
/// Absent on the account-level fields — a template verdict, an account update — which name no
/// number at all.
/// </param>
/// <param name="BusinessAccountId">
/// The WhatsApp Business Account, from the <c>id</c> of the entry. Present on every delivery.
/// </param>
public readonly record struct WhatsAppWebhookOrigin(string? PhoneNumberId, string BusinessAccountId);

/// <summary>
/// Says which tenant a webhook delivery belongs to, so its signature can be checked against
/// that tenant's app secret.
/// </summary>
/// <remarks>
/// <para>
/// What lets one endpoint serve every tenant. The default implementation matches the origin
/// against the phone numbers and business accounts in <c>WhatsAppOptions</c>, which is right
/// for the handful of tenants an application configures; a host whose tenants live in a
/// database registers its own, the same way it replaces
/// <see cref="IWhatsAppCredentialsProvider"/>.
/// </para>
/// <para>
/// Called once per delivery, before anything in it is trusted, so an implementation that
/// talks to a store is expected to cache.
/// </para>
/// </remarks>
public interface IWhatsAppWebhookTenantResolver
{
    /// <summary>Resolves the tenant a delivery belongs to.</summary>
    /// <param name="origin">What the unverified body says about where it came from.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// The tenant name, or <see langword="null"/> when nothing matches — which is a delivery
    /// for a number this application does not serve, and is refused.
    /// </returns>
    ValueTask<string?> ResolveAsync(
        WhatsAppWebhookOrigin origin,
        CancellationToken cancellationToken = default);
}
