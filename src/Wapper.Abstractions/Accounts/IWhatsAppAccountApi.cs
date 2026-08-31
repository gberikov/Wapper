namespace Wapper.Accounts;

/// <summary>A Meta app receiving a WhatsApp Business Account's webhooks.</summary>
public sealed record SubscribedApp
{
    /// <summary>Identifier of the app.</summary>
    public string? Id { get; init; }

    /// <summary>Its name, as it appears in the app dashboard.</summary>
    public string? Name { get; init; }

    /// <summary>Where to find it.</summary>
    public string? Link { get; init; }
}

/// <summary>
/// The WhatsApp Business Account itself, rather than one of the things it holds.
/// </summary>
/// <remarks>
/// <para>
/// Every call here needs <see cref="WhatsAppCredentials.WhatsAppBusinessAccountId"/> and
/// spends the account's hourly management allowance, which the client paces for you.
/// </para>
/// <para>
/// Subscribing is the step that is easy to forget and impossible to debug: a webhook endpoint
/// can be configured, reachable and correctly signed, and still receive nothing at all until
/// the app is subscribed to the account. A host onboarding tenants through Embedded Signup
/// has to do this for each one.
/// </para>
/// </remarks>
public interface IWhatsAppAccountApi
{
    /// <summary>Lists the apps receiving this account's webhooks.</summary>
    Task<IReadOnlyList<SubscribedApp>> GetSubscribedAppsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the app the access token belongs to, so its webhooks start arriving.
    /// </summary>
    /// <remarks>
    /// There is nothing to pass: Meta subscribes whichever app the token belongs to. Calling
    /// it again when already subscribed is harmless.
    /// </remarks>
    Task SubscribeAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops this account's webhooks reaching the app.</summary>
    Task UnsubscribeAsync(CancellationToken cancellationToken = default);
}
