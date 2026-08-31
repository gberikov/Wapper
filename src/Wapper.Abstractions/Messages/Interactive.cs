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
