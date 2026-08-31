using System.Text.Json.Serialization;

namespace Wapper.Internal;

/// <summary>Wire shape of the apps subscribed to an account's webhooks.</summary>
internal sealed class SubscribedAppListResponse
{
    [JsonPropertyName("data")]
    public List<SubscribedAppPayload>? Data { get; set; }
}

internal sealed class SubscribedAppPayload
{
    /// <summary>The app is one field deeper than the array it arrives in.</summary>
    [JsonPropertyName("whatsapp_business_api_data")]
    public SubscribedAppDataPayload? Data { get; set; }
}

internal sealed class SubscribedAppDataPayload
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }
}

/// <summary>Wire shape of a phone number's business encryption key.</summary>
/// <remarks>Wrapped in a one-element array, like the business profile.</remarks>
internal sealed class BusinessEncryptionResponse
{
    [JsonPropertyName("data")]
    public List<BusinessEncryptionPayload>? Data { get; set; }
}

internal sealed class BusinessEncryptionPayload
{
    [JsonPropertyName("business_public_key")]
    public string? BusinessPublicKey { get; set; }

    [JsonPropertyName("business_public_key_signature_status")]
    public string? SignatureStatus { get; set; }
}
