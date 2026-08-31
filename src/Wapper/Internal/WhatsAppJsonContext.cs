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
[JsonSerializable(typeof(TemplateDefinitionPayload))]
[JsonSerializable(typeof(TemplateListResponse))]
[JsonSerializable(typeof(TemplateCreatedResponse))]
[JsonSerializable(typeof(PhoneNumberPayload))]
[JsonSerializable(typeof(PhoneNumberListResponse))]
[JsonSerializable(typeof(TwoStepPinPayload))]
[JsonSerializable(typeof(SendMessageResponse))]
[JsonSerializable(typeof(MediaIdResponse))]
[JsonSerializable(typeof(MediaInfoResponse))]
[JsonSerializable(typeof(SuccessResponse))]
[JsonSerializable(typeof(Dictionary<string, List<BusinessUseCaseUsage>>), TypeInfoPropertyName = "DictionaryStringListBusinessUseCaseUsage")]
internal sealed partial class WhatsAppJsonContext : JsonSerializerContext;
