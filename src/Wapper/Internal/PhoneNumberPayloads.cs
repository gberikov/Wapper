using System.Text.Json.Serialization;

namespace Wapper.Internal;

/// <summary>Wire shape of a business phone number.</summary>
internal sealed class PhoneNumberPayload
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("display_phone_number")]
    public string? DisplayPhoneNumber { get; set; }

    [JsonPropertyName("verified_name")]
    public string? VerifiedName { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("quality_rating")]
    public string? QualityRating { get; set; }

    [JsonPropertyName("code_verification_status")]
    public string? CodeVerificationStatus { get; set; }

    [JsonPropertyName("name_status")]
    public string? NameStatus { get; set; }

    [JsonPropertyName("new_name_status")]
    public string? NewNameStatus { get; set; }

    /// <summary>An object with one field, rather than the plain string every sibling is.</summary>
    [JsonPropertyName("throughput")]
    public PhoneNumberThroughputPayload? Throughput { get; set; }

    [JsonPropertyName("messaging_limit_tier")]
    public string? MessagingLimitTier { get; set; }

    [JsonPropertyName("platform_type")]
    public string? PlatformType { get; set; }

    [JsonPropertyName("account_mode")]
    public string? AccountMode { get; set; }

    [JsonPropertyName("is_official_business_account")]
    public bool? IsOfficialBusinessAccount { get; set; }

    [JsonPropertyName("is_pin_enabled")]
    public bool? IsPinEnabled { get; set; }

    /// <summary>ISO 8601, unlike the Unix seconds every timestamp on the webhook uses.</summary>
    [JsonPropertyName("last_onboarded_time")]
    public string? LastOnboardedTime { get; set; }
}

internal sealed class PhoneNumberThroughputPayload
{
    [JsonPropertyName("level")]
    public string? Level { get; set; }
}

/// <summary>Wire shape of a page of phone numbers.</summary>
internal sealed class PhoneNumberListResponse
{
    [JsonPropertyName("data")]
    public List<PhoneNumberPayload>? Data { get; set; }

    [JsonPropertyName("paging")]
    public GraphPagingPayload? Paging { get; set; }
}

/// <summary>Wire shape of a two-step verification PIN change.</summary>
internal sealed class TwoStepPinPayload
{
    [JsonPropertyName("pin")]
    public string? Pin { get; set; }
}

/// <summary>Wire shape of a registration.</summary>
internal sealed class RegisterPayload
{
    [JsonPropertyName("messaging_product")]
    public string MessagingProduct { get; set; } = "whatsapp";

    [JsonPropertyName("pin")]
    public string? Pin { get; set; }

    /// <summary>Left out entirely when the number is not to use local storage.</summary>
    [JsonPropertyName("data_localization_region")]
    public string? DataLocalizationRegion { get; set; }
}
