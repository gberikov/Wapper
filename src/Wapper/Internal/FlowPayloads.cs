using System.Text.Json.Serialization;

namespace Wapper.Internal;

/// <summary>Wire shape of a Flow.</summary>
internal sealed class FlowPayload
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("categories")]
    public List<string>? Categories { get; set; }

    [JsonPropertyName("validation_errors")]
    public List<FlowValidationErrorPayload>? ValidationErrors { get; set; }

    [JsonPropertyName("json_version")]
    public string? JsonVersion { get; set; }

    [JsonPropertyName("data_api_version")]
    public string? DataApiVersion { get; set; }

    [JsonPropertyName("endpoint_uri")]
    public string? EndpointUri { get; set; }

    [JsonPropertyName("preview")]
    public FlowPreviewPayload? Preview { get; set; }

    [JsonPropertyName("health_status")]
    public FlowHealthPayload? HealthStatus { get; set; }
}

/// <summary>Wire shape of a problem with a Flow's JSON.</summary>
internal sealed class FlowValidationErrorPayload
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_type")]
    public string? ErrorType { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("line_start")]
    public int? LineStart { get; set; }

    [JsonPropertyName("line_end")]
    public int? LineEnd { get; set; }

    [JsonPropertyName("column_start")]
    public int? ColumnStart { get; set; }

    [JsonPropertyName("column_end")]
    public int? ColumnEnd { get; set; }

    [JsonPropertyName("pointers")]
    public List<FlowErrorPointerPayload>? Pointers { get; set; }
}

internal sealed class FlowErrorPointerPayload
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

internal sealed class FlowPreviewPayload
{
    [JsonPropertyName("preview_url")]
    public string? PreviewUrl { get; set; }

    /// <summary>ISO 8601 with a colonless offset, like the phone number's onboarding time.</summary>
    [JsonPropertyName("expires_at")]
    public string? ExpiresAt { get; set; }
}

internal sealed class FlowHealthPayload
{
    [JsonPropertyName("can_send_message")]
    public string? CanSendMessage { get; set; }

    [JsonPropertyName("entities")]
    public List<FlowHealthEntityPayload>? Entities { get; set; }
}

internal sealed class FlowHealthEntityPayload
{
    [JsonPropertyName("entity_type")]
    public string? EntityType { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("can_send_message")]
    public string? CanSendMessage { get; set; }

    [JsonPropertyName("errors")]
    public List<FlowHealthErrorPayload>? Errors { get; set; }

    [JsonPropertyName("additional_info")]
    public List<string>? AdditionalInfo { get; set; }
}

internal sealed class FlowHealthErrorPayload
{
    [JsonPropertyName("error_code")]
    public int ErrorCode { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }

    [JsonPropertyName("possible_solution")]
    public string? PossibleSolution { get; set; }
}

/// <summary>Wire shape of a page of Flows.</summary>
internal sealed class FlowListResponse
{
    [JsonPropertyName("data")]
    public List<FlowPayload>? Data { get; set; }

    [JsonPropertyName("paging")]
    public GraphPagingPayload? Paging { get; set; }
}

/// <summary>Wire shape of a Flow being created or edited.</summary>
internal sealed class FlowDefinitionPayload
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("categories")]
    public List<string>? Categories { get; set; }

    /// <summary>A string holding JSON, rather than nested JSON.</summary>
    [JsonPropertyName("flow_json")]
    public string? FlowJson { get; set; }

    [JsonPropertyName("publish")]
    public bool? Publish { get; set; }

    [JsonPropertyName("clone_flow_id")]
    public string? CloneFlowId { get; set; }

    [JsonPropertyName("endpoint_uri")]
    public string? EndpointUri { get; set; }

    [JsonPropertyName("application_id")]
    public string? ApplicationId { get; set; }
}

/// <summary>
/// Wire shape of a created Flow, or of an uploaded Flow JSON.
/// </summary>
/// <remarks>
/// <c>success</c> and <c>validation_errors</c> arrive together: the Flow is stored whether or
/// not its JSON is valid.
/// </remarks>
internal sealed class FlowWriteResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("success")]
    public bool? Success { get; set; }

    [JsonPropertyName("validation_errors")]
    public List<FlowValidationErrorPayload>? ValidationErrors { get; set; }
}

/// <summary>Wire shape of a preview read, which is a Flow with one field on it.</summary>
internal sealed class FlowPreviewResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("preview")]
    public FlowPreviewPayload? Preview { get; set; }
}

/// <summary>Wire shape of a Flow's assets.</summary>
internal sealed class FlowAssetListResponse
{
    [JsonPropertyName("data")]
    public List<FlowAssetPayload>? Data { get; set; }
}

internal sealed class FlowAssetPayload
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("asset_type")]
    public string? AssetType { get; set; }

    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; set; }
}
