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
    /// <summary>
    /// The business phone number the event arrived on.
    /// </summary>
    /// <remarks>
    /// Empty on events that belong to the account rather than to a number — a template
    /// moving through review, for instance. Match on
    /// <see cref="BusinessAccountId"/> for those.
    /// </remarks>
    public string PhoneNumberId { get; init; } = string.Empty;

    /// <summary>
    /// The WhatsApp Business Account the event belongs to.
    /// </summary>
    /// <remarks>
    /// Present on every delivery, and the only identifier account-level events carry.
    /// </remarks>
    public string BusinessAccountId { get; init; } = string.Empty;

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

    /// <summary>
    /// The ad or post the customer came from, when they arrived through one.
    /// </summary>
    /// <remarks>
    /// Present on the first message of a conversation started from a Click-to-WhatsApp ad or
    /// a Facebook page post. This is the only place the attribution appears, and the
    /// conversation it opens is free of charge — so an application that reports on ad spend
    /// has to read it here or not at all.
    /// </remarks>
    public MessageReferral? Referral { get; init; }

    /// <summary>
    /// The catalogue item the customer was looking at when they wrote, when they quoted one.
    /// </summary>
    public ReferredProduct? ReferredProduct { get; init; }
}

/// <summary>Where a customer came from, when they arrived through an ad or a post.</summary>
public sealed record MessageReferral
{
    /// <summary>The ad or post itself.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>What it was: <c>ad</c> or <c>post</c>.</summary>
    public string? SourceType { get; init; }

    /// <summary>Identifier of the ad or post, which is what ties this to a campaign.</summary>
    public string? SourceId { get; init; }

    /// <summary>Its headline.</summary>
    public string? Headline { get; init; }

    /// <summary>Its body text.</summary>
    public string? Body { get; init; }

    /// <summary>What the ad showed: <c>image</c> or <c>video</c>.</summary>
    public string? MediaType { get; init; }

    /// <summary>The image, for an image ad.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>The video, for a video ad.</summary>
    public string? VideoUrl { get; init; }

    /// <summary>Its thumbnail.</summary>
    public string? ThumbnailUrl { get; init; }

    /// <summary>
    /// The click identifier, for matching this conversation against Meta's ad reporting.
    /// </summary>
    public string? ClickId { get; init; }
}

/// <summary>A catalogue item a customer quoted.</summary>
/// <param name="CatalogId">Which catalogue.</param>
/// <param name="ProductRetailerId">The item's identifier within it.</param>
public readonly record struct ReferredProduct(string? CatalogId, string? ProductRetailerId);

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
/// A customer filled in a Flow and submitted it.
/// </summary>
/// <remarks>
/// The other half of sending a Flow, and the only place its answers arrive. WhatsApp shows
/// the customer a summary in the chat; what the screens actually collected is in
/// <see cref="ResponseJson"/>, whose shape is the Flow's own and therefore yours to parse.
/// </remarks>
public sealed record FlowReply : IncomingMessage
{
    /// <summary>What WhatsApp showed in the chat once the form was submitted.</summary>
    public string? Body { get; init; }

    /// <summary>The name Meta echoes back. In practice always <c>flow</c>.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// The answers, as the JSON document the Flow's screens produced.
    /// </summary>
    /// <remarks>
    /// Carries the <c>flow_token</c> the Flow was sent with, which is how a submission is
    /// matched to the customer and the thing they were doing.
    /// </remarks>
    public string ResponseJson { get; init; } = string.Empty;
}

/// <summary>An order a customer placed from a catalogue.</summary>
public sealed record OrderMessage : IncomingMessage
{
    /// <summary>The catalogue it came from.</summary>
    public string? CatalogId { get; init; }

    /// <summary>What the customer wrote alongside it.</summary>
    public string? Text { get; init; }

    /// <summary>What they ordered.</summary>
    public IReadOnlyList<OrderProduct> Products { get; init; } = [];
}

/// <summary>One line of an order.</summary>
public sealed record OrderProduct
{
    /// <summary>The item's identifier within the catalogue.</summary>
    public string? ProductRetailerId { get; init; }

    /// <summary>How many.</summary>
    public int Quantity { get; init; }

    /// <summary>The price each, as the catalogue had it when the order was placed.</summary>
    public decimal ItemPrice { get; init; }

    /// <summary>Its currency.</summary>
    public string? Currency { get; init; }
}

/// <summary>
/// A customer opened the chat for the first time and has not written anything yet.
/// </summary>
/// <remarks>
/// The cue for a welcome message. It only arrives for accounts where Meta has the feature
/// switched on, so it is a nicety to handle rather than something to depend on.
/// </remarks>
public sealed record WelcomeRequest : IncomingMessage;

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

    /// <summary>Which rate it was charged at, such as <c>marketing</c> or <c>service</c>.</summary>
    public string? PricingType { get; init; }

    /// <summary>
    /// Which pricing model applied, such as <c>PMP</c> for per-message pricing.
    /// </summary>
    public string? PricingModel { get; init; }

    /// <summary>
    /// When the conversation's customer service window closes.
    /// </summary>
    /// <remarks>
    /// Only sent on the status that opens a conversation. Until then a free-form reply is
    /// allowed; afterwards only a template is, and sending anything else fails with
    /// <see cref="WhatsAppErrorCodes.ReEngagementRequired"/>.
    /// </remarks>
    public DateTimeOffset? ConversationExpiresAt { get; init; }

    /// <summary>
    /// Whatever was attached to the send as <c>callbackData</c>, echoed back untouched.
    /// </summary>
    /// <remarks>
    /// The way to match a status against your own records without keeping a table of message
    /// ids: put the order number, or the row id, on the send and read it back here.
    /// </remarks>
    public string? CallbackData { get; init; }

    /// <summary>Why it failed, when it did.</summary>
    public IReadOnlyList<WhatsAppError> Errors { get; init; } = [];
}

/// <summary>
/// A change this library could not turn into a typed event.
/// </summary>
/// <remarks>
/// <para>
/// Usually a webhook field this library has no event for. Meta has more than twenty and adds
/// to them; this library types the ones it can act on, and anything else would otherwise be
/// dropped without trace — an account being offboarded, a template's components being
/// rewritten. It arrives here instead, with the body it came in, so an application can notice
/// and decide.
/// </para>
/// <para>
/// Occasionally a field this library does know — <c>messages</c>, say — shaped in a way it
/// could not read. That lands here too, under the same <see cref="Field"/>, rather than being
/// dropped: a handler for this event is the one place to find out that something is being
/// discarded.
/// </para>
/// </remarks>
public sealed record UnknownEvent : WhatsAppEvent
{
    /// <summary>The <c>field</c> of the change, for example <c>account_update</c>.</summary>
    public string Field { get; init; } = string.Empty;

    /// <summary>The <c>value</c> object, exactly as it arrived.</summary>
    public string Json { get; init; } = string.Empty;
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
