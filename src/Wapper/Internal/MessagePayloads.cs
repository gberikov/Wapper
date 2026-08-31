using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wapper.Internal;

/// <summary>
/// Wire shape of a send.
/// </summary>
/// <remarks>
/// One class with a nullable property per message type, rather than a hierarchy, because
/// that is exactly what the Cloud API expects: a <c>type</c> discriminator and the single
/// matching object. Nulls are dropped on write, so only the one that was set appears.
/// </remarks>
internal sealed class SendMessagePayload
{
    [JsonPropertyName("messaging_product")]
    public string MessagingProduct { get; set; } = "whatsapp";

    /// <summary>
    /// Omitted for a read receipt, which goes to the same endpoint but carries a status
    /// instead of a recipient.
    /// </summary>
    [JsonPropertyName("recipient_type")]
    public string? RecipientType { get; set; } = "individual";

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("message_id")]
    public string? MessageId { get; set; }

    [JsonPropertyName("typing_indicator")]
    public TypingIndicatorPayload? TypingIndicator { get; set; }

    /// <summary>
    /// Handed back untouched on every status this message produces. Meta caps it at 512
    /// characters and never looks inside it.
    /// </summary>
    [JsonPropertyName("biz_opaque_callback_data")]
    public string? CallbackData { get; set; }

    [JsonPropertyName("context")]
    public MessageContextPayload? Context { get; set; }

    [JsonPropertyName("text")]
    public TextPayload? Text { get; set; }

    [JsonPropertyName("image")]
    public MediaPayload? Image { get; set; }

    [JsonPropertyName("video")]
    public MediaPayload? Video { get; set; }

    [JsonPropertyName("audio")]
    public MediaPayload? Audio { get; set; }

    [JsonPropertyName("document")]
    public MediaPayload? Document { get; set; }

    [JsonPropertyName("sticker")]
    public MediaPayload? Sticker { get; set; }

    [JsonPropertyName("location")]
    public LocationPayload? Location { get; set; }

    [JsonPropertyName("contacts")]
    public List<ContactPayload>? Contacts { get; set; }

    [JsonPropertyName("reaction")]
    public ReactionPayload? Reaction { get; set; }

    [JsonPropertyName("interactive")]
    public InteractivePayload? Interactive { get; set; }

    [JsonPropertyName("template")]
    public TemplatePayload? Template { get; set; }
}

internal sealed class TypingIndicatorPayload
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";
}

internal sealed class MessageContextPayload
{
    [JsonPropertyName("message_id")]
    public string? MessageId { get; set; }
}

internal sealed class TextPayload
{
    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("preview_url")]
    public bool? PreviewUrl { get; set; }
}

internal sealed class MediaPayload
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }

    [JsonPropertyName("filename")]
    public string? FileName { get; set; }
}

internal sealed class LocationPayload
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }
}

internal sealed class ReactionPayload
{
    [JsonPropertyName("message_id")]
    public string? MessageId { get; set; }

    /// <summary>
    /// An empty string takes the reaction back. It has to be written even though it is
    /// empty, which is why it is not treated as absent.
    /// </summary>
    [JsonPropertyName("emoji")]
    public string Emoji { get; set; } = string.Empty;
}

internal sealed class ContactPayload
{
    [JsonPropertyName("name")]
    public ContactNamePayload? Name { get; set; }

    [JsonPropertyName("phones")]
    public List<ContactPhonePayload>? Phones { get; set; }

    [JsonPropertyName("emails")]
    public List<ContactEmailPayload>? Emails { get; set; }

    [JsonPropertyName("addresses")]
    public List<ContactAddressPayload>? Addresses { get; set; }

    [JsonPropertyName("urls")]
    public List<ContactUrlPayload>? Urls { get; set; }

    [JsonPropertyName("org")]
    public ContactOrgPayload? Org { get; set; }

    [JsonPropertyName("birthday")]
    public string? Birthday { get; set; }
}

internal sealed class ContactNamePayload
{
    [JsonPropertyName("formatted_name")]
    public string? FormattedName { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("middle_name")]
    public string? MiddleName { get; set; }

    [JsonPropertyName("prefix")]
    public string? Prefix { get; set; }

    [JsonPropertyName("suffix")]
    public string? Suffix { get; set; }
}

internal sealed class ContactPhonePayload
{
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("wa_id")]
    public string? WaId { get; set; }
}

internal sealed class ContactEmailPayload
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

internal sealed class ContactUrlPayload
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

internal sealed class ContactAddressPayload
{
    [JsonPropertyName("street")]
    public string? Street { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("zip")]
    public string? Zip { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

internal sealed class ContactOrgPayload
{
    [JsonPropertyName("company")]
    public string? Company { get; set; }

    [JsonPropertyName("department")]
    public string? Department { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

internal sealed class InteractivePayload
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("header")]
    public InteractiveHeaderPayload? Header { get; set; }

    [JsonPropertyName("body")]
    public InteractiveTextPayload? Body { get; set; }

    [JsonPropertyName("footer")]
    public InteractiveTextPayload? Footer { get; set; }

    [JsonPropertyName("action")]
    public InteractiveActionPayload? Action { get; set; }
}

internal sealed class InteractiveHeaderPayload
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("image")]
    public MediaPayload? Image { get; set; }

    [JsonPropertyName("video")]
    public MediaPayload? Video { get; set; }

    [JsonPropertyName("document")]
    public MediaPayload? Document { get; set; }
}

internal sealed class InteractiveTextPayload
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

internal sealed class InteractiveActionPayload
{
    [JsonPropertyName("buttons")]
    public List<InteractiveButtonPayload>? Buttons { get; set; }

    [JsonPropertyName("button")]
    public string? Button { get; set; }

    [JsonPropertyName("sections")]
    public List<InteractiveSectionPayload>? Sections { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("parameters")]
    public InteractiveParametersPayload? Parameters { get; set; }
}

internal sealed class InteractiveButtonPayload
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "reply";

    [JsonPropertyName("reply")]
    public InteractiveReplyPayload? Reply { get; set; }
}

internal sealed class InteractiveReplyPayload
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

internal sealed class InteractiveSectionPayload
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("rows")]
    public List<InteractiveRowPayload>? Rows { get; set; }
}

internal sealed class InteractiveRowPayload
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// The parameters of an interactive action.
/// </summary>
/// <remarks>
/// One class covering both shapes that use this field — the call-to-action button and the
/// Flow — for the same reason <see cref="SendMessagePayload"/> is one class: nulls are
/// dropped on write, so only the fields the action in hand actually set appear.
/// </remarks>
internal sealed class InteractiveParametersPayload
{
    // A call-to-action button.

    [JsonPropertyName("display_text")]
    public string? DisplayText { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    // A Flow.

    /// <summary>Always "3". Meta rejects anything else, and takes it as a string.</summary>
    [JsonPropertyName("flow_message_version")]
    public string? FlowMessageVersion { get; set; }

    [JsonPropertyName("flow_token")]
    public string? FlowToken { get; set; }

    [JsonPropertyName("flow_id")]
    public string? FlowId { get; set; }

    [JsonPropertyName("flow_name")]
    public string? FlowName { get; set; }

    /// <summary>The label on the button that opens the Flow.</summary>
    [JsonPropertyName("flow_cta")]
    public string? FlowCallToAction { get; set; }

    [JsonPropertyName("flow_action")]
    public string? FlowAction { get; set; }

    /// <summary>Only ever "draft". Left out entirely for a published Flow.</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("flow_action_payload")]
    public FlowActionPayload? FlowActionPayload { get; set; }
}

internal sealed class FlowActionPayload
{
    [JsonPropertyName("screen")]
    public string? Screen { get; set; }

    /// <summary>
    /// Whatever the Flow's first screen expects. Its shape belongs to the Flow, so it is
    /// carried through as the caller wrote it rather than modelled here.
    /// </summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }
}

internal sealed class TemplatePayload
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("language")]
    public TemplateLanguagePayload? Language { get; set; }

    [JsonPropertyName("components")]
    public List<TemplateComponentPayload>? Components { get; set; }
}

internal sealed class TemplateLanguagePayload
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }
}

internal sealed class TemplateComponentPayload
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("sub_type")]
    public string? SubType { get; set; }

    [JsonPropertyName("index")]
    public string? Index { get; set; }

    [JsonPropertyName("parameters")]
    public List<TemplateParameterPayload>? Parameters { get; set; }
}

internal sealed class TemplateParameterPayload
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("parameter_name")]
    public string? ParameterName { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("payload")]
    public string? Payload { get; set; }

    [JsonPropertyName("image")]
    public MediaPayload? Image { get; set; }

    [JsonPropertyName("video")]
    public MediaPayload? Video { get; set; }

    [JsonPropertyName("document")]
    public MediaPayload? Document { get; set; }

    [JsonPropertyName("currency")]
    public TemplateCurrencyPayload? Currency { get; set; }

    [JsonPropertyName("date_time")]
    public TemplateDateTimePayload? DateTime { get; set; }
}

internal sealed class TemplateCurrencyPayload
{
    [JsonPropertyName("fallback_value")]
    public string? FallbackValue { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("amount_1000")]
    public long Amount1000 { get; set; }
}

internal sealed class TemplateDateTimePayload
{
    [JsonPropertyName("fallback_value")]
    public string? FallbackValue { get; set; }
}

/// <summary>Wire shape of a send response.</summary>
internal sealed class SendMessageResponse
{
    [JsonPropertyName("messaging_product")]
    public string? MessagingProduct { get; set; }

    [JsonPropertyName("contacts")]
    public List<SendMessageContact>? Contacts { get; set; }

    [JsonPropertyName("messages")]
    public List<SendMessageResult>? Messages { get; set; }

    [JsonPropertyName("success")]
    public bool? Success { get; set; }
}

internal sealed class SendMessageContact
{
    [JsonPropertyName("input")]
    public string? Input { get; set; }

    [JsonPropertyName("wa_id")]
    public string? WaId { get; set; }
}

internal sealed class SendMessageResult
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("message_status")]
    public string? MessageStatus { get; set; }
}
