using Wapper.Media;

namespace Wapper.Messages;

/// <summary>
/// The banner above an interactive message. Optional, and a list message only accepts the
/// text form.
/// </summary>
public sealed record InteractiveHeader
{
    private InteractiveHeader(string type, string? text, MediaSource? media)
    {
        Type = type;
        Text = text;
        Media = media;
    }

    /// <summary>Which of the forms this is: <c>text</c>, <c>image</c>, <c>video</c> or <c>document</c>.</summary>
    public string Type { get; }

    /// <summary>The heading, for a text header.</summary>
    public string? Text { get; }

    /// <summary>The media, for the other three.</summary>
    public MediaSource? Media { get; }

    /// <summary>A line of text. The only header a list message accepts.</summary>
    public static InteractiveHeader FromText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new InteractiveHeader("text", text, null);
    }

    /// <summary>An image.</summary>
    public static InteractiveHeader FromImage(MediaSource media) => new("image", null, media);

    /// <summary>A video.</summary>
    public static InteractiveHeader FromVideo(MediaSource media) => new("video", null, media);

    /// <summary>A document. WhatsApp shows its first page.</summary>
    public static InteractiveHeader FromDocument(MediaSource media) => new("document", null, media);
}

/// <summary>One of the buttons under a reply-button message.</summary>
public sealed record ReplyButton
{
    /// <summary>
    /// What comes back on the webhook when the button is tapped. Up to 256 characters, and
    /// the only thing that identifies which button was pressed, so make it meaningful.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>The label. WhatsApp allows 20 characters and does not wrap.</summary>
    public required string Title { get; init; }
}

/// <summary>
/// A message with up to three tappable reply buttons.
/// </summary>
/// <remarks>
/// Use a list message when there are more than three choices; WhatsApp rejects a fourth
/// button rather than dropping it.
/// </remarks>
public sealed record ButtonMessage
{
    /// <summary>The text above the buttons.</summary>
    public required string Body { get; init; }

    /// <summary>Up to three buttons.</summary>
    public required IReadOnlyList<ReplyButton> Buttons { get; init; }

    /// <summary>Optional banner above the body.</summary>
    public InteractiveHeader? Header { get; init; }

    /// <summary>Optional small print below the buttons.</summary>
    public string? Footer { get; init; }
}

/// <summary>One choice inside a list message.</summary>
public sealed record ListRow
{
    /// <summary>What comes back on the webhook when the row is chosen. Up to 200 characters.</summary>
    public required string Id { get; init; }

    /// <summary>The label. Up to 24 characters.</summary>
    public required string Title { get; init; }

    /// <summary>Optional second line. Up to 72 characters.</summary>
    public string? Description { get; init; }
}

/// <summary>A group of rows under a heading.</summary>
public sealed record ListSection
{
    /// <summary>
    /// The heading. Required once there is more than one section, and ignored when there is
    /// only one.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>The rows in this section.</summary>
    public required IReadOnlyList<ListRow> Rows { get; init; }
}

/// <summary>
/// A message that opens a list of choices.
/// </summary>
/// <remarks>
/// WhatsApp allows ten rows in total across all sections, and only a text header.
/// </remarks>
public sealed record ListMessage
{
    /// <summary>The text above the button that opens the list.</summary>
    public required string Body { get; init; }

    /// <summary>The label on the button that opens the list. Up to 20 characters.</summary>
    public required string ButtonText { get; init; }

    /// <summary>The sections, holding ten rows between them at most.</summary>
    public required IReadOnlyList<ListSection> Sections { get; init; }

    /// <summary>Optional heading. A list message only accepts a text header.</summary>
    public string? Header { get; init; }

    /// <summary>Optional small print.</summary>
    public string? Footer { get; init; }
}

/// <summary>What a Flow does when the customer opens it.</summary>
public enum FlowAction
{
    /// <summary>
    /// Opens straight onto a screen the message names. For a Flow with no endpoint, which is
    /// every Flow whose screens are all defined in its JSON.
    /// </summary>
    Navigate,

    /// <summary>
    /// Asks the Flow's endpoint what to show first. Only for a Flow that has one, and it has
    /// to be reachable when the customer taps — an unhealthy endpoint gets the Flow throttled.
    /// </summary>
    DataExchange,
}

/// <summary>
/// A message that opens a Flow: a form the customer fills in without leaving WhatsApp.
/// </summary>
/// <remarks>
/// The Flow has to be published first, unless <see cref="Draft"/> is set. What the customer
/// submits comes back on the webhook as a <c>FlowReply</c>, carrying
/// <see cref="FlowToken"/> — which is the only thing tying a submission to the person and the
/// thing they were doing, since the reply carries no other context of yours.
/// </remarks>
public sealed record FlowMessage
{
    /// <summary>Identifier of the Flow. Set this or <see cref="FlowName"/>, not both.</summary>
    public string? FlowId { get; init; }

    /// <summary>Name of the Flow, as an alternative to its id.</summary>
    public string? FlowName { get; init; }

    /// <summary>
    /// Your own token for this send, echoed back untouched when the form is submitted.
    /// </summary>
    /// <remarks>
    /// Generate one per send and store what it means. Reusing a token across customers makes
    /// the replies impossible to tell apart.
    /// </remarks>
    public required string FlowToken { get; init; }

    /// <summary>The label on the button that opens the Flow.</summary>
    public required string ButtonText { get; init; }

    /// <summary>The text above the button.</summary>
    public required string Body { get; init; }

    /// <summary>Optional banner above the body.</summary>
    public InteractiveHeader? Header { get; init; }

    /// <summary>Optional small print below the button.</summary>
    public string? Footer { get; init; }

    /// <summary>Whether the first screen comes from the Flow itself or from its endpoint.</summary>
    public FlowAction Action { get; init; } = FlowAction.Navigate;

    /// <summary>
    /// Which screen to open on. Required for <see cref="FlowAction.Navigate"/>.
    /// </summary>
    public string? Screen { get; init; }

    /// <summary>
    /// Values to hand the first screen, as a JSON object.
    /// </summary>
    /// <remarks>
    /// The shape is the Flow's own, so it is passed through as written rather than modelled
    /// here. It is parsed before sending, so a malformed document fails here rather than as a
    /// bare <c>100</c> from Meta.
    /// </remarks>
    public string? DataJson { get; init; }

    /// <summary>
    /// Whether to send the draft rather than the published Flow, for testing.
    /// </summary>
    /// <remarks>
    /// The customer sees a warning that this is a draft, so it is not something to leave on.
    /// </remarks>
    public bool Draft { get; init; }
}

/// <summary>
/// A message with a single button that opens a link.
/// </summary>
/// <remarks>
/// Worth preferring over a bare URL in a text message: the link is rendered as a button and
/// is not subject to the link preview rules.
/// </remarks>
public sealed record CallToActionMessage
{
    /// <summary>The text above the button.</summary>
    public required string Body { get; init; }

    /// <summary>The label on the button.</summary>
    public required string ButtonText { get; init; }

    /// <summary>Where the button leads.</summary>
    public required Uri Url { get; init; }

    /// <summary>Optional banner above the body.</summary>
    public InteractiveHeader? Header { get; init; }

    /// <summary>Optional small print below the button.</summary>
    public string? Footer { get; init; }
}
