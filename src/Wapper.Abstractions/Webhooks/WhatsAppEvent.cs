using Wapper.Messages;

namespace Wapper.Webhooks;

/// <summary>
/// Something the Cloud API told us about, on the webhook.
/// </summary>
/// <remarks>
/// These types are produced by <c>WhatsAppWebhookParser</c> and consumed by handlers; an
/// application never builds one except in a test, which is why the fields carry defaults
/// rather than being required.
/// </remarks>
public abstract record WhatsAppEvent
{
    /// <summary>The business phone number the event arrived on.</summary>
    /// <remarks>
    /// The only thing in the payload that identifies the account, so this is what a
    /// multi-tenant host matches on.
    /// </remarks>
    public string PhoneNumberId { get; init; } = string.Empty;

    /// <summary>The business number in display form, as customers see it.</summary>
    public string? DisplayPhoneNumber { get; init; }

    /// <summary>When it happened, as reported by WhatsApp.</summary>
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>A message a customer sent.</summary>
public abstract record IncomingMessage : WhatsAppEvent
{
    /// <summary>Identifier of the message. Quote it to reply, to react, or to mark it read.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Who sent it, as a WhatsApp id.</summary>
    public string From { get; init; } = string.Empty;

    /// <summary>The sender's profile name, when WhatsApp shared it.</summary>
    public string? ProfileName { get; init; }

    /// <summary>The message this one replies to, when the customer quoted something.</summary>
    public string? ReplyToMessageId { get; init; }

    /// <summary>Whether the customer forwarded this from somewhere else.</summary>
    public bool IsForwarded { get; init; }
}

/// <summary>A text message.</summary>
public sealed record TextMessage : IncomingMessage
{
    /// <summary>What the customer wrote.</summary>
    public string Text { get; init; } = string.Empty;
}

/// <summary>Which kind of media arrived.</summary>
public enum IncomingMediaKind
{
    /// <summary>A picture.</summary>
    Image,

    /// <summary>Audio, which for a voice note has <see cref="MediaMessage.IsVoice"/> set.</summary>
    Audio,

    /// <summary>A video.</summary>
    Video,

    /// <summary>A file.</summary>
    Document,

    /// <summary>A sticker.</summary>
    Sticker,
}

/// <summary>
/// A message carrying a file.
/// </summary>
/// <remarks>
/// Only the id arrives, not the bytes. Fetch them with <c>Media.DownloadAsync</c>, and do it
/// promptly: an id from a webhook expires after seven days.
/// </remarks>
public sealed record MediaMessage : IncomingMessage
{
    /// <summary>Which kind of media it is.</summary>
    public IncomingMediaKind Kind { get; init; }

    /// <summary>Identifier to download it with.</summary>
    public string MediaId { get; init; } = string.Empty;

    /// <summary>Media type, for example <c>image/jpeg</c>.</summary>
    public string? MimeType { get; init; }

    /// <summary>Checksum WhatsApp computed for the file.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Text the customer sent with the file.</summary>
    public string? Caption { get; init; }

    /// <summary>Name of the file, for a document.</summary>
    public string? FileName { get; init; }

    /// <summary>Whether an audio message is a voice note rather than an audio file.</summary>
    public bool IsVoice { get; init; }

    /// <summary>Whether a sticker is animated.</summary>
    public bool IsAnimated { get; init; }
}

/// <summary>A location a customer shared.</summary>
public sealed record LocationMessage : IncomingMessage
{
    /// <summary>Where they are, or where they pointed.</summary>
    public Location Location { get; init; } = new() { Latitude = 0, Longitude = 0 };
}

/// <summary>One or more contact cards a customer shared.</summary>
public sealed record ContactsMessage : IncomingMessage
{
    /// <summary>The cards.</summary>
    public IReadOnlyList<Contact> Contacts { get; init; } = [];
}

/// <summary>A customer reacted to a message.</summary>
public sealed record ReactionMessage : IncomingMessage
{
    /// <summary>The message they reacted to.</summary>
    public string MessageId { get; init; } = string.Empty;

    /// <summary>
    /// The emoji, or empty when the reaction was taken back.
    /// </summary>
    public string Emoji { get; init; } = string.Empty;

    /// <summary>Whether the reaction was removed rather than added.</summary>
    public bool IsRemoved => Emoji.Length == 0;
}

/// <summary>Which kind of interactive control the customer used.</summary>
public enum InteractiveReplyKind
{
    /// <summary>One of the reply buttons under a message.</summary>
    Button,

    /// <summary>A row from a list.</summary>
    List,
}

/// <summary>A customer tapped a reply button or picked a row from a list.</summary>
public sealed record InteractiveReply : IncomingMessage
{
    /// <summary>Which control it was.</summary>
    public InteractiveReplyKind Kind { get; init; }

    /// <summary>The id that was set when the message was sent. This is what to branch on.</summary>
    public string ReplyId { get; init; } = string.Empty;

    /// <summary>The label the customer saw.</summary>
    public string? Title { get; init; }

    /// <summary>The second line of a list row.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// A customer tapped a quick-reply button on a template message.
/// </summary>
/// <remarks>
/// Not the same as <see cref="InteractiveReply"/>: a template button arrives as its own
/// message type and carries the payload set when the template was sent, not a button id.
/// </remarks>
public sealed record TemplateButtonReply : IncomingMessage
{
    /// <summary>The payload the template attached to the button.</summary>
    public string Payload { get; init; } = string.Empty;

    /// <summary>The label the customer saw.</summary>
    public string? Text { get; init; }
}

/// <summary>
/// A notice from WhatsApp itself rather than from a person, such as a customer changing
/// their number.
/// </summary>
public sealed record SystemMessage : IncomingMessage
{
    /// <summary>What happened, in words.</summary>
    public string? Body { get; init; }

    /// <summary>The kind of notice, for example <c>user_changed_number</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>The customer's new WhatsApp id, when they changed number.</summary>
    public string? NewWhatsAppId { get; init; }
}

/// <summary>
/// A message this library has no typed form for.
/// </summary>
/// <remarks>
/// Meta adds message types without warning. They arrive here rather than being dropped, so
/// an application can notice and decide what to do.
/// </remarks>
public sealed record UnsupportedMessage : IncomingMessage
{
    /// <summary>The <c>type</c> WhatsApp used.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>The error WhatsApp attached, when it said the message was unsupported.</summary>
    public WhatsAppError? Error { get; init; }
}

/// <summary>How far along an outgoing message is.</summary>
public enum MessageDeliveryStatus
{
    /// <summary>Something arrived that this library does not recognise.</summary>
    Unknown,

    /// <summary>Handed to WhatsApp.</summary>
    Sent,

    /// <summary>On the recipient's device.</summary>
    Delivered,

    /// <summary>Opened by the recipient.</summary>
    Read,

    /// <summary>Given up on. <see cref="MessageStatusChanged.Errors"/> says why.</summary>
    Failed,

    /// <summary>Played, for a voice note.</summary>
    Played,

    /// <summary>Deleted by the sender.</summary>
    Deleted,
}

/// <summary>
/// An outgoing message moved along.
/// </summary>
/// <remarks>
/// The other half of sending. A send call only reports that Meta accepted the message;
/// whether it was delivered, and why it was not, only ever arrives here.
/// </remarks>
public sealed record MessageStatusChanged : WhatsAppEvent
{
    /// <summary>The message, matching the id the send returned.</summary>
    public string MessageId { get; init; } = string.Empty;

    /// <summary>Who it was for.</summary>
    public string RecipientId { get; init; } = string.Empty;

    /// <summary>How far it got.</summary>
    public MessageDeliveryStatus Status { get; init; }

    /// <summary>The raw status string, in case Meta sent one this library does not know.</summary>
    public string? RawStatus { get; init; }

    /// <summary>Identifier of the conversation it belongs to, for billing.</summary>
    public string? ConversationId { get; init; }

    /// <summary>How the conversation was categorised: marketing, utility, service, authentication.</summary>
    public string? ConversationCategory { get; init; }

    /// <summary>Whether Meta charged for it.</summary>
    public bool? Billable { get; init; }

    /// <summary>Why it failed, when it did.</summary>
    public IReadOnlyList<WhatsAppError> Errors { get; init; } = [];
}

/// <summary>
/// An error the Cloud API reported out of band.
/// </summary>
/// <remarks>
/// Errors arrive both in the response to a call and, separately, here. A send that was
/// accepted can still fail later, and this is the only place that says so.
/// </remarks>
public sealed record WebhookError : WhatsAppEvent
{
    /// <summary>What went wrong.</summary>
    public WhatsAppError Error { get; init; } = new() { Code = 0 };
}
