using Wapper.RateLimiting;

namespace Wapper;

/// <summary>
/// Configuration of one tenant. Bind it from configuration, or set it in code, or both —
/// values set in code win over the bound ones.
/// </summary>
public sealed class WhatsAppOptions
{
    /// <summary>Configuration section these options are bound from by convention.</summary>
    public const string SectionName = "WhatsApp";

    /// <summary>Access token presented as a bearer token.</summary>
    /// <remarks>
    /// Leave empty when a custom <see cref="IWhatsAppCredentialsProvider"/> supplies
    /// credentials instead, which is the normal arrangement for a multi-tenant host where
    /// tokens live in a database and are refreshed.
    /// </remarks>
    public string? AccessToken { get; set; }

    /// <summary>Identifier of the business phone number that sends and receives messages.</summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>Identifier of the WhatsApp Business Account, needed by management endpoints.</summary>
    public string? WhatsAppBusinessAccountId { get; set; }

    /// <summary>
    /// Graph API version used in request paths. Defaults to the newest version at the time
    /// of release.
    /// </summary>
    /// <remarks>
    /// Meta publishes a new version every few months and retires each one about two years
    /// later, so this is deliberately configurable: a consumer can move forward, or stay
    /// put, without waiting for a new package.
    /// </remarks>
    public string GraphApiVersion { get; set; } = "v26.0";

    /// <summary>Root address of the Graph API.</summary>
    public Uri BaseAddress { get; set; } = new("https://graph.facebook.com/");

    /// <summary>How long a single HTTP call may take. Defaults to 100 seconds.</summary>
    /// <remarks>This does not include time spent waiting for a rate limit token.</remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>How hard the client paces itself against Meta's limits.</summary>
    public WhatsAppRateLimitOptions RateLimits { get; set; } = new();

    /// <summary>
    /// The app secret, used to check that a webhook delivery really came from Meta.
    /// </summary>
    /// <remarks>
    /// Required to receive webhooks. The endpoint is public, so without it anyone who learns
    /// the URL can post whatever they like into the application.
    /// </remarks>
    public string? AppSecret { get; set; }

    /// <summary>
    /// The token Meta echoes back when the webhook subscription is first verified.
    /// </summary>
    /// <remarks>Chosen when the webhook is configured in the Meta app dashboard.</remarks>
    public string? WebhookVerifyToken { get; set; }
}
