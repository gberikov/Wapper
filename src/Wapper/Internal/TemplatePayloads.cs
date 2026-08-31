using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wapper.Internal;

/// <summary>Wire shape of a template, as the management API sends and takes it.</summary>
internal sealed class TemplateDefinitionPayload
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("sub_category")]
    public string? SubCategory { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("parameter_format")]
    public string? ParameterFormat { get; set; }

    [JsonPropertyName("allow_category_change")]
    public bool? AllowCategoryChange { get; set; }

    /// <summary>Seconds. Meta takes and returns the time-to-live as a plain number.</summary>
    [JsonPropertyName("message_send_ttl_seconds")]
    public int? MessageSendTtlSeconds { get; set; }

    /// <summary>Comes back on a read. An object, not the bare string the webhook sends.</summary>
    [JsonPropertyName("quality_score")]
    public TemplateQualityScorePayload? QualityScore { get; set; }

    [JsonPropertyName("rejected_reason")]
    public string? RejectedReason { get; set; }

    [JsonPropertyName("previous_category")]
    public string? PreviousCategory { get; set; }

    [JsonPropertyName("components")]
    public List<TemplateComponentDefinitionPayload>? Components { get; set; }
}

internal sealed class TemplateQualityScorePayload
{
    [JsonPropertyName("score")]
    public string? Score { get; set; }
}

internal sealed class TemplateComponentDefinitionPayload
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("example")]
    public TemplateExamplePayload? Example { get; set; }

    /// <summary>Authentication body only: Meta appends its own "do not share" line.</summary>
    [JsonPropertyName("add_security_recommendation")]
    public bool? AddSecurityRecommendation { get; set; }

    /// <summary>Authentication footer only, and it replaces the footer text.</summary>
    [JsonPropertyName("code_expiration_minutes")]
    public int? CodeExpirationMinutes { get; set; }

    [JsonPropertyName("buttons")]
    public List<TemplateButtonDefinitionPayload>? Buttons { get; set; }
}

/// <summary>
/// Wire shape of the sample values Meta reviews a template against.
/// </summary>
/// <remarks>
/// Four differently shaped fields for the same idea, because the header and the body each
/// have a positional and a named form, and the positional body one is a list of lists.
/// </remarks>
internal sealed class TemplateExamplePayload
{
    [JsonPropertyName("header_text")]
    public List<string>? HeaderText { get; set; }

    [JsonPropertyName("header_text_named_params")]
    public List<TemplateNamedExamplePayload>? HeaderTextNamedParams { get; set; }

    [JsonPropertyName("header_handle")]
    public List<string>? HeaderHandle { get; set; }

    [JsonPropertyName("body_text")]
    public List<List<string>>? BodyText { get; set; }

    [JsonPropertyName("body_text_named_params")]
    public List<TemplateNamedExamplePayload>? BodyTextNamedParams { get; set; }
}

internal sealed class TemplateNamedExamplePayload
{
    [JsonPropertyName("param_name")]
    public string? ParamName { get; set; }

    [JsonPropertyName("example")]
    public string? Example { get; set; }
}

internal sealed class TemplateButtonDefinitionPayload
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("example")]
    public TemplateButtonExamplePayload? Example { get; set; }

    // One-time-passcode buttons, on authentication templates.

    [JsonPropertyName("otp_type")]
    public string? OtpType { get; set; }

    [JsonPropertyName("autofill_text")]
    public string? AutofillText { get; set; }

    /// <summary>Superseded by <see cref="SupportedApps"/>, and still sent by older accounts.</summary>
    [JsonPropertyName("package_name")]
    public string? PackageName { get; set; }

    /// <inheritdoc cref="PackageName" />
    [JsonPropertyName("signature_hash")]
    public string? SignatureHash { get; set; }

    [JsonPropertyName("supported_apps")]
    public List<TemplateApplicationPayload>? SupportedApps { get; set; }

    [JsonPropertyName("zero_tap_terms_accepted")]
    public bool? ZeroTapTermsAccepted { get; set; }
}

internal sealed class TemplateApplicationPayload
{
    [JsonPropertyName("package_name")]
    public string? PackageName { get; set; }

    [JsonPropertyName("signature_hash")]
    public string? SignatureHash { get; set; }
}

/// <summary>
/// The sample value of a button, which Meta sends as a list for a URL button and as a bare
/// string for a copy-code one.
/// </summary>
/// <remarks>
/// Two shapes under one field name. A converter is the only way to accept both without
/// reflection, which the trimming and AOT analysers would refuse.
/// </remarks>
[JsonConverter(typeof(TemplateButtonExampleConverter))]
internal sealed class TemplateButtonExamplePayload
{
    /// <summary>The samples, for a URL button.</summary>
    public List<string>? Values { get; set; }

    /// <summary>The sample, for a copy-code button.</summary>
    public string? Value { get; set; }

    /// <summary>The first sample, whichever shape it arrived in.</summary>
    public string? First => Value ?? (Values is { Count: > 0 } values ? values[0] : null);
}

/// <summary>Reads and writes <see cref="TemplateButtonExamplePayload"/> in either shape.</summary>
internal sealed class TemplateButtonExampleConverter : JsonConverter<TemplateButtonExamplePayload>
{
    public override TemplateButtonExamplePayload? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return new TemplateButtonExamplePayload { Value = reader.GetString() };

            case JsonTokenType.StartArray:
                var values = new List<string>();

                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        values.Add(reader.GetString()!);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                return new TemplateButtonExamplePayload { Values = values };

            default:
                // Meta adding a third shape must not fail the whole listing.
                reader.Skip();
                return null;
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        TemplateButtonExamplePayload value,
        JsonSerializerOptions options)
    {
        if (value.Values is { } values)
        {
            writer.WriteStartArray();

            foreach (var item in values)
            {
                writer.WriteStringValue(item);
            }

            writer.WriteEndArray();
            return;
        }

        writer.WriteStringValue(value.Value);
    }
}

/// <summary>Wire shape of a page of templates.</summary>
internal sealed class TemplateListResponse
{
    [JsonPropertyName("data")]
    public List<TemplateDefinitionPayload>? Data { get; set; }

    [JsonPropertyName("paging")]
    public GraphPagingPayload? Paging { get; set; }
}

/// <summary>Wire shape of the response to submitting a template.</summary>
internal sealed class TemplateCreatedResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }
}
