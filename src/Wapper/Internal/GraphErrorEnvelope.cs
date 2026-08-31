using System.Text.Json.Serialization;

namespace Wapper.Internal;

/// <summary>Wire shape of a Graph API error response.</summary>
internal sealed class GraphErrorEnvelope
{
    [JsonPropertyName("error")]
    public GraphError? Error { get; set; }
}

/// <summary>Wire shape of the <c>error</c> object itself.</summary>
internal sealed class GraphError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    // Not fb_trace_id, which is what a snake-case naming policy would produce.
    [JsonPropertyName("fbtrace_id")]
    public string? FbTraceId { get; set; }

    [JsonPropertyName("is_transient")]
    public bool IsTransient { get; set; }

    [JsonPropertyName("error_subcode")]
    public int? ErrorSubcode { get; set; }

    [JsonPropertyName("error_data")]
    public GraphErrorData? ErrorData { get; set; }
}

/// <summary>Wire shape of <c>error.error_data</c>.</summary>
internal sealed class GraphErrorData
{
    [JsonPropertyName("messaging_product")]
    public string? MessagingProduct { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }
}
