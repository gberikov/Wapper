using System.Text;

namespace Wapper;

/// <summary>
/// The credentials one tenant sends to the Cloud API.
/// </summary>
/// <remarks>
/// <see cref="object.ToString"/> deliberately leaves the access token out. A record prints
/// every property by default, so logging one — or an object holding one — would put a
/// working token into the log.
/// </remarks>
public sealed record WhatsAppCredentials
{
    /// <summary>System user or business access token presented as a bearer token.</summary>
    public required string AccessToken { get; init; }

    /// <summary>Identifier of the business phone number that sends and receives messages.</summary>
    public required string PhoneNumberId { get; init; }

    /// <summary>
    /// Identifier of the WhatsApp Business Account the phone number belongs to. Only the
    /// management endpoints (templates, subscribed apps, phone numbers) need it, so it is
    /// optional for an application that merely sends messages.
    /// </summary>
    public string? WhatsAppBusinessAccountId { get; init; }

    /// <summary>
    /// Identifier of the Meta app the token belongs to. It addresses the resumable upload
    /// endpoint — which is how a business profile picture is set — and keys the app-level
    /// rate limit, so several tenants sharing one app share one budget. Optional: without it
    /// the app budget falls back to being keyed per tenant.
    /// </summary>
    public string? AppId { get; init; }

    /// <summary>Prints everything except the access token.</summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("AccessToken = ***, PhoneNumberId = ").Append(PhoneNumberId);

        if (WhatsAppBusinessAccountId is { } account)
        {
            builder.Append(", WhatsAppBusinessAccountId = ").Append(account);
        }

        if (AppId is { } app)
        {
            builder.Append(", AppId = ").Append(app);
        }

        return true;
    }
}
