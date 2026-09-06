using System.Text.Json.Serialization;

namespace Wapper.Internal;

/// <summary>
/// Source-generated serialization for every type that crosses the wire. Nothing in this
/// library may use a reflection-based <c>System.Text.Json</c> overload: the packages are
/// marked trim- and AOT-compatible, and reflection would break both.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GraphErrorEnvelope))]
[JsonSerializable(typeof(AppUsage))]
[JsonSerializable(typeof(SendMessagePayload))]
[JsonSerializable(typeof(WebhookPayload))]
// Bound one change at a time rather than reached through WebhookPayload, so a delivery on a
// field this library has no event for is never walked at all.
[JsonSerializable(typeof(WebhookValue))]
// Bound one item at a time from the raw arrays on WebhookValue, so one item shaped in a way
// this library cannot read costs that item alone.
[JsonSerializable(typeof(WebhookMessage))]
[JsonSerializable(typeof(WebhookStatus))]
[JsonSerializable(typeof(TemplateDefinitionPayload))]
// Written back out on its own when a template carries a component this library does not
// know, so the component's body survives the read.
[JsonSerializable(typeof(TemplateComponentDefinitionPayload))]
[JsonSerializable(typeof(TemplateListResponse))]
[JsonSerializable(typeof(TemplateCreatedResponse))]
[JsonSerializable(typeof(PhoneNumberPayload))]
[JsonSerializable(typeof(PhoneNumberListResponse))]
[JsonSerializable(typeof(TwoStepPinPayload))]
[JsonSerializable(typeof(RegisterPayload))]
[JsonSerializable(typeof(BusinessProfilePayload))]
[JsonSerializable(typeof(BusinessProfileResponse))]
[JsonSerializable(typeof(UploadSessionResponse))]
[JsonSerializable(typeof(UploadedFileResponse))]
[JsonSerializable(typeof(FlowPayload))]
[JsonSerializable(typeof(FlowListResponse))]
[JsonSerializable(typeof(FlowDefinitionPayload))]
[JsonSerializable(typeof(FlowWriteResponse))]
[JsonSerializable(typeof(FlowPreviewResponse))]
[JsonSerializable(typeof(FlowAssetListResponse))]
[JsonSerializable(typeof(MessagingAnalyticsResponse))]
[JsonSerializable(typeof(ConversationAnalyticsResponse))]
[JsonSerializable(typeof(PricingAnalyticsResponse))]
[JsonSerializable(typeof(TemplateAnalyticsResponse))]
[JsonSerializable(typeof(SendMessageResponse))]
[JsonSerializable(typeof(MediaIdResponse))]
[JsonSerializable(typeof(MediaInfoResponse))]
[JsonSerializable(typeof(SubscribedAppListResponse))]
[JsonSerializable(typeof(BusinessEncryptionResponse))]
[JsonSerializable(typeof(SuccessResponse))]
[JsonSerializable(typeof(Dictionary<string, List<BusinessUseCaseUsage>>), TypeInfoPropertyName = "DictionaryStringListBusinessUseCaseUsage")]
// What a raw call reads its response as when the caller brought no type of its own. The
// converter clones, so the element outlives the response it was read from.
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
internal sealed partial class WhatsAppJsonContext : JsonSerializerContext;
