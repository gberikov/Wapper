using System.Text.Json.Serialization;

namespace Wapper.Internal;

/// <summary>
/// Wire shape of a messaging analytics read.
/// </summary>
/// <remarks>
/// Every analytics read is a field expansion on the WhatsApp Business Account node, so the
/// answer arrives wrapped in the name of the field that was asked for.
/// </remarks>
internal sealed class MessagingAnalyticsResponse
{
    [JsonPropertyName("analytics")]
    public MessagingAnalyticsPayload? Analytics { get; set; }
}

internal sealed class MessagingAnalyticsPayload
{
    [JsonPropertyName("phone_numbers")]
    public List<string>? PhoneNumbers { get; set; }

    [JsonPropertyName("country_codes")]
    public List<string>? CountryCodes { get; set; }

    [JsonPropertyName("granularity")]
    public string? Granularity { get; set; }

    [JsonPropertyName("data_points")]
    public List<MessagingDataPointPayload>? DataPoints { get; set; }
}

internal sealed class MessagingDataPointPayload
{
    /// <summary>Unix seconds, as a number this time rather than as a string.</summary>
    [JsonPropertyName("start")]
    public long Start { get; set; }

    [JsonPropertyName("end")]
    public long End { get; set; }

    [JsonPropertyName("sent")]
    public int Sent { get; set; }

    [JsonPropertyName("delivered")]
    public int Delivered { get; set; }
}

/// <summary>
/// Wire shape of a conversation analytics read.
/// </summary>
/// <remarks>
/// One level deeper than the messaging one: the field holds a <c>data</c> array whose entries
/// each hold the data points.
/// </remarks>
internal sealed class ConversationAnalyticsResponse
{
    [JsonPropertyName("conversation_analytics")]
    public AnalyticsDataWrapper<ConversationDataPointPayload>? ConversationAnalytics { get; set; }
}

internal sealed class ConversationDataPointPayload
{
    [JsonPropertyName("start")]
    public long Start { get; set; }

    [JsonPropertyName("end")]
    public long End { get; set; }

    [JsonPropertyName("conversation")]
    public int? Conversation { get; set; }

    [JsonPropertyName("cost")]
    public decimal? Cost { get; set; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("conversation_category")]
    public string? ConversationCategory { get; set; }

    [JsonPropertyName("conversation_type")]
    public string? ConversationType { get; set; }

    [JsonPropertyName("conversation_direction")]
    public string? ConversationDirection { get; set; }
}

/// <summary>Wire shape of a pricing analytics read. Nested like the conversation one.</summary>
internal sealed class PricingAnalyticsResponse
{
    [JsonPropertyName("pricing_analytics")]
    public AnalyticsDataWrapper<PricingDataPointPayload>? PricingAnalytics { get; set; }
}

internal sealed class PricingDataPointPayload
{
    [JsonPropertyName("start")]
    public long Start { get; set; }

    [JsonPropertyName("end")]
    public long End { get; set; }

    [JsonPropertyName("volume")]
    public int? Volume { get; set; }

    [JsonPropertyName("cost")]
    public decimal? Cost { get; set; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    /// <summary>Only present on data points the volume tiers actually applied to.</summary>
    [JsonPropertyName("tier")]
    public string? Tier { get; set; }

    [JsonPropertyName("pricing_category")]
    public string? PricingCategory { get; set; }

    [JsonPropertyName("pricing_type")]
    public string? PricingType { get; set; }
}

/// <summary>The <c>{ "data": [ { "data_points": [...] } ] }</c> both of those share.</summary>
internal sealed class AnalyticsDataWrapper<TDataPoint>
{
    [JsonPropertyName("data")]
    public List<AnalyticsDataSet<TDataPoint>>? Data { get; set; }
}

internal sealed class AnalyticsDataSet<TDataPoint>
{
    [JsonPropertyName("data_points")]
    public List<TDataPoint>? DataPoints { get; set; }
}

/// <summary>
/// Wire shape of a template analytics read.
/// </summary>
/// <remarks>
/// The odd one out: an edge of its own with ordinary query parameters, rather than a field
/// expansion on the account.
/// </remarks>
internal sealed class TemplateAnalyticsResponse
{
    [JsonPropertyName("data")]
    public List<TemplateAnalyticsPayload>? Data { get; set; }
}

internal sealed class TemplateAnalyticsPayload
{
    [JsonPropertyName("granularity")]
    public string? Granularity { get; set; }

    [JsonPropertyName("product_type")]
    public string? ProductType { get; set; }

    [JsonPropertyName("waba_timezone")]
    public string? WabaTimezone { get; set; }

    [JsonPropertyName("data_points")]
    public List<TemplateDataPointPayload>? DataPoints { get; set; }
}

internal sealed class TemplateDataPointPayload
{
    [JsonPropertyName("template_id")]
    public string? TemplateId { get; set; }

    [JsonPropertyName("start")]
    public long Start { get; set; }

    [JsonPropertyName("end")]
    public long End { get; set; }

    [JsonPropertyName("sent")]
    public int? Sent { get; set; }

    [JsonPropertyName("delivered")]
    public int? Delivered { get; set; }

    [JsonPropertyName("read")]
    public int? Read { get; set; }

    /// <summary>An array of typed objects, not a count.</summary>
    [JsonPropertyName("clicked")]
    public List<TemplateClickPayload>? Clicked { get; set; }

    /// <summary>An array of typed figures, not an amount.</summary>
    [JsonPropertyName("cost")]
    public List<TemplateCostPayload>? Cost { get; set; }
}

internal sealed class TemplateClickPayload
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("button_content")]
    public string? ButtonContent { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

internal sealed class TemplateCostPayload
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("value")]
    public decimal Value { get; set; }
}
