using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wapper.Internal;

/// <summary>Wire shape of a webhook delivery.</summary>
internal sealed class WebhookPayload
{
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("entry")]
    public List<WebhookEntry>? Entry { get; set; }
}

internal sealed class WebhookEntry
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("changes")]
    public List<WebhookChange>? Changes { get; set; }
}

internal sealed class WebhookChange
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    /// <summary>
    /// Left as raw JSON rather than bound to <see cref="WebhookValue"/> here.
    /// </summary>
    /// <remarks>
    /// Two reasons. A field this library has no typed event for still has to be reported with
    /// the body it arrived in, and there is nothing to bind it to. And the fields that are
    /// typed are bound one at a time, so a delivery on a field nobody handles costs nothing
    /// to skip.
    /// </remarks>
    [JsonPropertyName("value")]
    public JsonElement Value { get; set; }
}

internal sealed class WebhookValue
{
    [JsonPropertyName("messaging_product")]
    public string? MessagingProduct { get; set; }

    [JsonPropertyName("metadata")]
    public WebhookMetadata? Metadata { get; set; }

    [JsonPropertyName("contacts")]
    public List<WebhookContact>? Contacts { get; set; }

    [JsonPropertyName("messages")]
    public List<WebhookMessage>? Messages { get; set; }

    [JsonPropertyName("statuses")]
    public List<WebhookStatus>? Statuses { get; set; }

    [JsonPropertyName("errors")]
    public List<GraphError>? Errors { get; set; }

    // Template events. These arrive on their own `field` values and carry no metadata at
    // all, so they identify themselves by the WhatsApp Business Account on the entry.

    [JsonPropertyName("event")]
    public string? Event { get; set; }

    /// <summary>Sent as a number, not a string, unlike every other id in the payload.</summary>
    [JsonPropertyName("message_template_id")]
    public long? MessageTemplateId { get; set; }

    [JsonPropertyName("message_template_name")]
    public string? MessageTemplateName { get; set; }

    [JsonPropertyName("message_template_language")]
    public string? MessageTemplateLanguage { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("other_info")]
    public WebhookOtherInfo? OtherInfo { get; set; }

    /// <summary>
    /// Where the review's own words about a rejection live, rather than in
    /// <see cref="OtherInfo"/>.
    /// </summary>
    [JsonPropertyName("rejection_info")]
    public WebhookRejectionInfo? RejectionInfo { get; set; }

    [JsonPropertyName("previous_quality_score")]
    public string? PreviousQualityScore { get; set; }

    [JsonPropertyName("new_quality_score")]
    public string? NewQualityScore { get; set; }

    // Phone number events. Account-level like the template ones, and identified by the number
    // in display form: there is no phone number id anywhere in the payload.

    [JsonPropertyName("display_phone_number")]
    public string? DisplayPhoneNumber { get; set; }

    /// <summary>
    /// Superseded by <see cref="MaxDailyConversationsPerBusiness"/>, which Meta introduced as
    /// its replacement.
    /// </summary>
    [JsonPropertyName("current_limit")]
    public string? CurrentLimit { get; set; }

    [JsonPropertyName("old_limit")]
    public string? OldLimit { get; set; }

    [JsonPropertyName("max_daily_conversations_per_business")]
    public string? MaxDailyConversationsPerBusiness { get; set; }

    [JsonPropertyName("decision")]
    public string? Decision { get; set; }

    [JsonPropertyName("requested_verified_name")]
    public string? RequestedVerifiedName { get; set; }

    [JsonPropertyName("rejection_reason")]
    public string? RejectionReason { get; set; }

    // Flow events. Account-level too, and sharing the `event` and `message` fields above.

    [JsonPropertyName("flow_id")]
    public string? FlowId { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("old_status")]
    public string? OldStatus { get; set; }

    [JsonPropertyName("new_status")]
    public string? NewStatus { get; set; }

    [JsonPropertyName("alert_state")]
    public string? AlertState { get; set; }

    [JsonPropertyName("threshold")]
    public double? Threshold { get; set; }

    [JsonPropertyName("requests_count")]
    public int? RequestsCount { get; set; }

    [JsonPropertyName("error_rate")]
    public double? ErrorRate { get; set; }

    [JsonPropertyName("p50_latency")]
    public int? P50Latency { get; set; }

    [JsonPropertyName("p90_latency")]
    public int? P90Latency { get; set; }

    // A Flow alert's own `errors` array lands in Errors above. It shares the field name with
    // the message-level errors and nothing else, which is why GraphError carries both shapes.

    // Marketing preference changes. Shaped like a messages delivery — metadata and contacts
    // — with this array in place of the messages.

    [JsonPropertyName("user_preferences")]
    public List<WebhookUserPreference>? UserPreferences { get; set; }

    // The same change also arrives flat, with the fields of one preference on `value` itself
    // and no array at all. Both forms are live, so both are read.

    [JsonPropertyName("wa_id")]
    public string? WaId { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>The preference itself, <c>stop</c> or <c>resume</c>, on the flat form.</summary>
    [JsonPropertyName("value")]
    public string? PreferenceValue { get; set; }

    [JsonPropertyName("timestamp")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? Timestamp { get; set; }

    // Account events. Account-level like the template ones, and carrying a different
    // sub-object for each of the twenty-odd values `event` can take.

    /// <summary>
    /// Sent both as an object and as a bare string, so it stays raw until it is read.
    /// </summary>
    [JsonPropertyName("phone_number")]
    public JsonElement PhoneNumber { get; set; }

    [JsonPropertyName("ban_info")]
    public WebhookBanInfo? BanInfo { get; set; }

    [JsonPropertyName("violation_info")]
    public WebhookViolationInfo? ViolationInfo { get; set; }

    [JsonPropertyName("restriction_info")]
    public List<WebhookRestriction>? RestrictionInfo { get; set; }
}

internal sealed class WebhookBanInfo
{
    [JsonPropertyName("waba_ban_state")]
    public string? WabaBanState { get; set; }

    [JsonPropertyName("waba_ban_date")]
    public string? WabaBanDate { get; set; }
}

internal sealed class WebhookViolationInfo
{
    [JsonPropertyName("violation_type")]
    public string? ViolationType { get; set; }
}

internal sealed class WebhookRestriction
{
    [JsonPropertyName("restriction_type")]
    public string? RestrictionType { get; set; }

    /// <summary>Unix seconds, sent as a number.</summary>
    [JsonPropertyName("expiration")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? Expiration { get; set; }

    [JsonPropertyName("remediation")]
    public string? Remediation { get; set; }
}

internal sealed class WebhookRejectionInfo
{
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("recommendation")]
    public string? Recommendation { get; set; }
}

internal sealed class WebhookUserPreference
{
    [JsonPropertyName("wa_id")]
    public string? WaId { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    /// <summary>Only ever <c>marketing_messages</c> so far.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary><c>stop</c> or <c>resume</c>.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>
    /// Unix seconds, and — unlike every timestamp on a message — sent as a number. Read
    /// either way, in case Meta lines it up with the others one day.
    /// </summary>
    [JsonPropertyName("timestamp")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? Timestamp { get; set; }
}

internal sealed class WebhookOtherInfo
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal sealed class WebhookMetadata
{
    [JsonPropertyName("display_phone_number")]
    public string? DisplayPhoneNumber { get; set; }

    [JsonPropertyName("phone_number_id")]
    public string? PhoneNumberId { get; set; }
}

internal sealed class WebhookContact
{
    [JsonPropertyName("wa_id")]
    public string? WaId { get; set; }

    [JsonPropertyName("profile")]
    public WebhookProfile? Profile { get; set; }
}

internal sealed class WebhookProfile
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class WebhookMessage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    /// <summary>Unix seconds, sent as a string.</summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("context")]
    public WebhookContext? Context { get; set; }

    [JsonPropertyName("text")]
    public WebhookText? Text { get; set; }

    [JsonPropertyName("image")]
    public WebhookMedia? Image { get; set; }

    [JsonPropertyName("audio")]
    public WebhookMedia? Audio { get; set; }

    [JsonPropertyName("video")]
    public WebhookMedia? Video { get; set; }

    [JsonPropertyName("document")]
    public WebhookMedia? Document { get; set; }

    [JsonPropertyName("sticker")]
    public WebhookMedia? Sticker { get; set; }

    [JsonPropertyName("location")]
    public LocationPayload? Location { get; set; }

    [JsonPropertyName("contacts")]
    public List<ContactPayload>? Contacts { get; set; }

    [JsonPropertyName("reaction")]
    public ReactionPayload? Reaction { get; set; }

    [JsonPropertyName("interactive")]
    public WebhookInteractive? Interactive { get; set; }

    [JsonPropertyName("button")]
    public WebhookButton? Button { get; set; }

    [JsonPropertyName("system")]
    public WebhookSystem? System { get; set; }

    [JsonPropertyName("order")]
    public WebhookOrder? Order { get; set; }

    /// <summary>Where the customer came from, on the first message of a conversation.</summary>
    [JsonPropertyName("referral")]
    public WebhookReferral? Referral { get; set; }

    [JsonPropertyName("errors")]
    public List<GraphError>? Errors { get; set; }
}

internal sealed class WebhookContext
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("forwarded")]
    public bool Forwarded { get; set; }

    [JsonPropertyName("frequently_forwarded")]
    public bool FrequentlyForwarded { get; set; }

    [JsonPropertyName("referred_product")]
    public WebhookReferredProduct? ReferredProduct { get; set; }
}

internal sealed class WebhookReferredProduct
{
    [JsonPropertyName("catalog_id")]
    public string? CatalogId { get; set; }

    [JsonPropertyName("product_retailer_id")]
    public string? ProductRetailerId { get; set; }
}

internal sealed class WebhookReferral
{
    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; set; }

    [JsonPropertyName("source_type")]
    public string? SourceType { get; set; }

    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    [JsonPropertyName("headline")]
    public string? Headline { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("video_url")]
    public string? VideoUrl { get; set; }

    [JsonPropertyName("thumbnail_url")]
    public string? ThumbnailUrl { get; set; }

    /// <summary>The click identifier, spelled the way Meta's ad reporting spells it.</summary>
    [JsonPropertyName("ctwa_clid")]
    public string? ClickId { get; set; }
}

internal sealed class WebhookOrder
{
    [JsonPropertyName("catalog_id")]
    public string? CatalogId { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("product_items")]
    public List<WebhookOrderItem>? ProductItems { get; set; }
}

internal sealed class WebhookOrderItem
{
    [JsonPropertyName("product_retailer_id")]
    public string? ProductRetailerId { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("item_price")]
    public decimal ItemPrice { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
}

internal sealed class WebhookText
{
    [JsonPropertyName("body")]
    public string? Body { get; set; }
}

internal sealed class WebhookMedia
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }

    [JsonPropertyName("filename")]
    public string? FileName { get; set; }

    [JsonPropertyName("voice")]
    public bool Voice { get; set; }

    [JsonPropertyName("animated")]
    public bool Animated { get; set; }
}

internal sealed class WebhookInteractive
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("button_reply")]
    public WebhookInteractiveReply? ButtonReply { get; set; }

    [JsonPropertyName("list_reply")]
    public WebhookInteractiveReply? ListReply { get; set; }

    /// <summary>A submitted Flow. Meta's name for it is "native flow message reply".</summary>
    [JsonPropertyName("nfm_reply")]
    public WebhookFlowReply? FlowReply { get; set; }
}

internal sealed class WebhookFlowReply
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    /// <summary>A string holding JSON, rather than nested JSON.</summary>
    [JsonPropertyName("response_json")]
    public string? ResponseJson { get; set; }
}

internal sealed class WebhookInteractiveReply
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal sealed class WebhookButton
{
    [JsonPropertyName("payload")]
    public string? Payload { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

internal sealed class WebhookSystem
{
    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("wa_id")]
    public string? WaId { get; set; }
}

internal sealed class WebhookStatus
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("recipient_id")]
    public string? RecipientId { get; set; }

    [JsonPropertyName("conversation")]
    public WebhookConversation? Conversation { get; set; }

    [JsonPropertyName("pricing")]
    public WebhookPricing? Pricing { get; set; }

    /// <summary>Echoed back from the send, untouched.</summary>
    [JsonPropertyName("biz_opaque_callback_data")]
    public string? CallbackData { get; set; }

    [JsonPropertyName("errors")]
    public List<GraphError>? Errors { get; set; }
}

internal sealed class WebhookConversation
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("origin")]
    public WebhookConversationOrigin? Origin { get; set; }

    /// <summary>Unix seconds as a string. Only on the status that opens a conversation.</summary>
    [JsonPropertyName("expiration_timestamp")]
    public string? ExpirationTimestamp { get; set; }
}

internal sealed class WebhookConversationOrigin
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

internal sealed class WebhookPricing
{
    [JsonPropertyName("billable")]
    public bool? Billable { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary><c>PMP</c> since Meta moved from per-conversation to per-message pricing.</summary>
    [JsonPropertyName("pricing_model")]
    public string? PricingModel { get; set; }
}
