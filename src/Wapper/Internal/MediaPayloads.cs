using System.Text.Json.Serialization;

namespace Wapper.Internal;

/// <summary>Wire shape of an upload response.</summary>
internal sealed class MediaIdResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

/// <summary>Wire shape of a media lookup response.</summary>
internal sealed class MediaInfoResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("file_size")]
    public long FileSize { get; set; }
}

/// <summary>Wire shape of the responses that only report whether the call worked.</summary>
internal sealed class SuccessResponse
{
    /// <summary>
    /// Nullable so a body without the field can be told apart from an explicit refusal: a
    /// missing field must not read as <c>false</c>.
    /// </summary>
    [JsonPropertyName("success")]
    public bool? Success { get; set; }
}
