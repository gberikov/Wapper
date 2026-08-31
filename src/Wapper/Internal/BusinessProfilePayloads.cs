using System.Text.Json.Serialization;

namespace Wapper.Internal;

/// <summary>Wire shape of a business profile.</summary>
internal sealed class BusinessProfilePayload
{
    /// <summary>Required on the way up, and echoed back on the way down.</summary>
    [JsonPropertyName("messaging_product")]
    public string? MessagingProduct { get; set; }

    [JsonPropertyName("about")]
    public string? About { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("vertical")]
    public string? Vertical { get; set; }

    [JsonPropertyName("websites")]
    public List<string>? Websites { get; set; }

    /// <summary>Comes back on a read.</summary>
    [JsonPropertyName("profile_picture_url")]
    public string? ProfilePictureUrl { get; set; }

    /// <summary>Goes up on a write. Never comes back.</summary>
    [JsonPropertyName("profile_picture_handle")]
    public string? ProfilePictureHandle { get; set; }
}

/// <summary>
/// Wire shape of a business profile read.
/// </summary>
/// <remarks>
/// The profile is wrapped in a one-element array, even though a phone number has exactly one.
/// </remarks>
internal sealed class BusinessProfileResponse
{
    [JsonPropertyName("data")]
    public List<BusinessProfilePayload>? Data { get; set; }
}

/// <summary>Wire shape of a started resumable upload session.</summary>
internal sealed class UploadSessionResponse
{
    /// <summary>Already prefixed with <c>upload:</c>, and used as a path exactly as it is.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

/// <summary>Wire shape of a finished resumable upload.</summary>
internal sealed class UploadedFileResponse
{
    /// <summary>One letter, because this endpoint is not a WhatsApp one.</summary>
    [JsonPropertyName("h")]
    public string? Handle { get; set; }
}
