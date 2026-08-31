namespace Wapper;

/// <summary>
/// The credentials one tenant sends to the Cloud API.
/// </summary>
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
}
