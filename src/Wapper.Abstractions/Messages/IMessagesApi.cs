using Wapper.Media;

namespace Wapper.Messages;

/// <summary>
/// Sending messages, and acknowledging the ones that arrive.
/// </summary>
/// <remarks>
/// <para>
/// Every send takes an optional <c>replyToMessageId</c>. Passing the id of a message the
/// customer sent shows the reply quoted beneath it, which is worth doing whenever the
/// conversation has more than one thread running.
/// </para>
/// <para>
/// Outside the 24-hour customer service window only a template message is allowed. Anything
/// else is rejected, whatever it contains.
/// </para>
/// </remarks>
public interface IMessagesApi
{
    /// <summary>Sends a text message.</summary>
    /// <param name="to">Recipient, in international format without a leading plus.</param>
    /// <param name="text">The message. Up to 4096 characters.</param>
    /// <param name="previewUrl">
    /// Whether WhatsApp should render a preview of the first link in the text. Off by
    /// default, because a preview is fetched from the link at send time and slows delivery.
    /// </param>
    /// <param name="replyToMessageId">A message to quote.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    Task<SentMessage> SendTextAsync(
        string to,
        string text,
        bool previewUrl = false,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends an image.</summary>
    Task<SentMessage> SendImageAsync(
        string to,
        MediaSource media,
        string? caption = null,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a video.</summary>
    Task<SentMessage> SendVideoAsync(
        string to,
        MediaSource media,
        string? caption = null,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends audio.</summary>
    /// <remarks>WhatsApp shows audio without a caption, so there is nowhere to put one.</remarks>
    Task<SentMessage> SendAudioAsync(
        string to,
        MediaSource media,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a document.</summary>
    /// <param name="to">Recipient, in international format without a leading plus.</param>
    /// <param name="media">The file.</param>
    /// <param name="caption">Text shown with the document.</param>
    /// <param name="fileName">
    /// The name the recipient sees and saves it under. Worth setting: without it WhatsApp
    /// falls back to the name recorded at upload time, which is rarely meaningful.
    /// </param>
    /// <param name="replyToMessageId">A message to quote.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    Task<SentMessage> SendDocumentAsync(
        string to,
        MediaSource media,
        string? caption = null,
        string? fileName = null,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a sticker.</summary>
    /// <remarks>webp only: 100 KB static, 500 KB animated.</remarks>
    Task<SentMessage> SendStickerAsync(
        string to,
        MediaSource media,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a location.</summary>
    Task<SentMessage> SendLocationAsync(
        string to,
        Location location,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends one or more contact cards.</summary>
    Task<SentMessage> SendContactsAsync(
        string to,
        IEnumerable<Contact> contacts,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reacts to a message with an emoji.</summary>
    /// <param name="to">The other party in the conversation.</param>
    /// <param name="messageId">The message being reacted to.</param>
    /// <param name="emoji">A single emoji.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <remarks>
    /// A second reaction to the same message replaces the first; there is only ever one
    /// reaction per message per sender.
    /// </remarks>
    Task<SentMessage> SendReactionAsync(
        string to,
        string messageId,
        string emoji,
        CancellationToken cancellationToken = default);

    /// <summary>Takes back a reaction.</summary>
    Task<SentMessage> RemoveReactionAsync(
        string to,
        string messageId,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a message with up to three reply buttons.</summary>
    Task<SentMessage> SendButtonsAsync(
        string to,
        ButtonMessage message,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a message that opens a list of choices.</summary>
    Task<SentMessage> SendListAsync(
        string to,
        ListMessage message,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a message with a single button that opens a link.</summary>
    Task<SentMessage> SendCallToActionAsync(
        string to,
        CallToActionMessage message,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a message built from an approved template.</summary>
    /// <remarks>The only kind of message allowed outside the 24-hour customer service window.</remarks>
    Task<SentMessage> SendTemplateAsync(
        string to,
        TemplateMessage template,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a received message as read, putting the blue ticks on the customer's screen.
    /// </summary>
    /// <param name="messageId">The message that was read.</param>
    /// <param name="showTyping">
    /// Whether to show a typing indicator as well. It runs for up to 25 seconds, or until
    /// the next message is sent, so only set it when a reply really is coming.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task MarkAsReadAsync(
        string messageId,
        bool showTyping = false,
        CancellationToken cancellationToken = default);
}
