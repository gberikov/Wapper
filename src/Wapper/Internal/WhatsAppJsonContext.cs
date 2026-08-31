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
internal sealed partial class WhatsAppJsonContext : JsonSerializerContext;
