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

    /// <summary>
    /// Child section holding one entry per named tenant, keyed by tenant name.
    /// </summary>
    /// <remarks>
    /// Each entry inherits everything set alongside it in <see cref="SectionName"/> and
    /// overrides what it sets itself, so settings shared by every tenant are written once.
    /// </remarks>
    public const string TenantsSectionName = "Tenants";

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
    /// Identifier of the Meta app, needed to upload a file to Meta — which is the only way to
    /// set a business profile picture.
    /// </summary>
    public string? AppId { get; set; }

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

    /// <summary>
    /// The hosts a media download may present the access token to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A media URL is not a Graph API address: Meta hands back a host of its own choosing,
    /// and the download only works with the bearer token attached. That makes the URL a place
    /// a token can be sent, so it is checked against this list rather than trusted — matched
    /// whole, or as a suffix on a label boundary, so <c>evilfbcdn.net</c> does not pass for
    /// <c>fbcdn.net</c>. <see cref="BaseAddress"/>'s own host is always allowed.
    /// </para>
    /// <para>
    /// The defaults are the hosts Meta serves media from today. Add to them if Meta starts
    /// using another one; the entries configuration supplies are added to these rather than
    /// replacing them.
    /// </para>
    /// </remarks>
    public IList<string> MediaDownloadHosts { get; set; } =
        ["lookaside.fbsbx.com", "whatsapp.net", "fbcdn.net", "facebook.com"];

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
