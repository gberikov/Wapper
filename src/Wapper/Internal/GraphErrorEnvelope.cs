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

    // Only ever sent on the errors attached to a webhook, never on a response to a call, and
    // there it is the whole of what Meta says: "Healthy ecosystem" with no message beside it.
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    // Not fb_trace_id, which is what a snake-case naming policy would produce.
    [JsonPropertyName("fbtrace_id")]
    public string? FbTraceId { get; set; }

    [JsonPropertyName("is_transient")]
    public bool IsTransient { get; set; }

    [JsonPropertyName("error_subcode")]
    public int? ErrorSubcode { get; set; }

    [JsonPropertyName("error_data")]
    public GraphErrorData? ErrorData { get; set; }

    // The `errors` array of a Flow alert reuses this field name for a different shape: no
    // code, no message, and a count and a rate instead. Both forms are read here rather than
    // being told apart by which webhook they arrived on.

    [JsonPropertyName("error_type")]
    public string? ErrorType { get; set; }

    [JsonPropertyName("error_count")]
    public int? ErrorCount { get; set; }

    [JsonPropertyName("error_rate")]
    public double? ErrorRate { get; set; }
}

/// <summary>Wire shape of <c>error.error_data</c>.</summary>
internal sealed class GraphErrorData
{
    [JsonPropertyName("messaging_product")]
    public string? MessagingProduct { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }
}

/// <summary>Turns the wire error into the one callers see.</summary>
internal static class GraphErrorExtensions
{
    public static WhatsAppError ToError(this GraphError error) => new()
    {
        Code = error.Code,
        Type = error.Type,
        Title = error.Title,
        Message = error.Message,
        Details = error.ErrorData?.Details,
        TraceId = error.FbTraceId,
        IsTransient = error.IsTransient,
        Subcode = error.ErrorSubcode,
    };
}
