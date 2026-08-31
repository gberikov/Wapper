using Wapper.Media;

namespace Wapper.Messages;

/// <summary>
/// A message built from a template Meta has approved.
/// </summary>
/// <remarks>
/// The only kind of message a business may send outside the 24-hour customer service
/// window, and therefore the one that counts against the messaging tier.
/// </remarks>
public sealed record TemplateMessage
{
    /// <summary>Name of the approved template.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Language of the template, as a locale such as <c>en_US</c>. A template approved in one
    /// language cannot be sent in another.
    /// </summary>
    public required string Language { get; init; }

    /// <summary>The values filled into the template's placeholders.</summary>
    public IReadOnlyList<TemplateComponent> Components { get; init; } = [];
}

/// <summary>Which part of a template a set of values belongs to.</summary>
public enum TemplateComponentType
{
    /// <summary>The banner at the top.</summary>
    Header,

    /// <summary>The main text.</summary>
    Body,

    /// <summary>One of the buttons.</summary>
    Button,
}

/// <summary>The values for one part of a template.</summary>
public sealed record TemplateComponent
{
    /// <summary>Which part of the template.</summary>
    public required TemplateComponentType Type { get; init; }

    /// <summary>The values, in the order the placeholders appear.</summary>
    public IReadOnlyList<TemplateParameter> Parameters { get; init; } = [];

    /// <summary>
    /// For a button, which kind it is: <c>quick_reply</c>, <c>url</c> or <c>copy_code</c>.
    /// </summary>
    public string? SubType { get; init; }

    /// <summary>
    /// For a button, its position, counting from zero. Required, and Meta matches on it
    /// rather than on the label.
    /// </summary>
    public int? Index { get; init; }

    /// <summary>The header of a template, filled with the given values.</summary>
    public static TemplateComponent Header(params TemplateParameter[] parameters) =>
        new() { Type = TemplateComponentType.Header, Parameters = parameters };

    /// <summary>The body of a template, filled with the given values.</summary>
    public static TemplateComponent Body(params TemplateParameter[] parameters) =>
        new() { Type = TemplateComponentType.Body, Parameters = parameters };

    /// <summary>A quick-reply button, carrying the payload that comes back on the webhook.</summary>
    public static TemplateComponent QuickReplyButton(int index, string payload) => new()
    {
        Type = TemplateComponentType.Button,
        SubType = "quick_reply",
        Index = index,
        Parameters = [TemplateParameter.FromPayload(payload)],
    };

    /// <summary>A URL button, carrying the part appended to the template's base link.</summary>
    public static TemplateComponent UrlButton(int index, string suffix) => new()
    {
        Type = TemplateComponentType.Button,
        SubType = "url",
        Index = index,
        Parameters = [TemplateParameter.FromText(suffix)],
    };
}

/// <summary>One value filled into a template placeholder.</summary>
public sealed record TemplateParameter
{
    private TemplateParameter(string type)
    {
        Type = type;
    }

    /// <summary>Which kind of value this is.</summary>
    public string Type { get; }

    /// <summary>
    /// The placeholder this fills, for a template that uses named parameters rather than
    /// numbered ones.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>The text, for a text parameter.</summary>
    public string? Text { get; init; }

    /// <summary>The media, for an image, video or document parameter.</summary>
    public MediaSource? Media { get; init; }

    /// <summary>The amount, for a currency parameter.</summary>
    public TemplateCurrency? Currency { get; init; }

    /// <summary>The moment, for a date and time parameter.</summary>
    public string? DateTimeText { get; init; }

    /// <summary>What comes back on the webhook, for a quick-reply button.</summary>
    public string? PayloadValue { get; init; }

    /// <summary>A piece of text.</summary>
    public static TemplateParameter FromText(string text, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TemplateParameter("text") { Text = text, Name = name };
    }

    /// <summary>An image, for a template whose header is an image.</summary>
    public static TemplateParameter FromImage(MediaSource media, string? name = null) =>
        new("image") { Media = media, Name = name };

    /// <summary>A video, for a template whose header is a video.</summary>
    public static TemplateParameter FromVideo(MediaSource media, string? name = null) =>
        new("video") { Media = media, Name = name };

    /// <summary>A document, for a template whose header is a document.</summary>
    public static TemplateParameter FromDocument(MediaSource media, string? name = null) =>
        new("document") { Media = media, Name = name };

    /// <summary>
    /// An amount of money, which WhatsApp formats for the recipient's locale.
    /// </summary>
    public static TemplateParameter FromMoney(TemplateCurrency currency, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new TemplateParameter("currency") { Currency = currency, Name = name };
    }

    /// <summary>
    /// A moment in time.
    /// </summary>
    /// <remarks>
    /// WhatsApp only ever shows the text given here. It does not localise it, whatever the
    /// structured fields in the older documentation imply, so format it for the recipient
    /// before sending.
    /// </remarks>
    public static TemplateParameter FromDateTime(string formatted, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatted);
        return new TemplateParameter("date_time") { DateTimeText = formatted, Name = name };
    }

    /// <summary>The payload a quick-reply button sends back.</summary>
    public static TemplateParameter FromPayload(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new TemplateParameter("payload") { PayloadValue = payload };
    }
}

/// <summary>An amount of money for a template placeholder.</summary>
public sealed record TemplateCurrency
{
    /// <summary>What to show if WhatsApp cannot format the amount itself.</summary>
    public required string FallbackValue { get; init; }

    /// <summary>Three-letter currency code, such as <c>USD</c>.</summary>
    public required string Code { get; init; }

    /// <summary>
    /// The amount multiplied by 1000. Meta takes it as an integer to avoid rounding, so
    /// 12.34 is sent as 12340.
    /// </summary>
    public required long AmountInThousandths { get; init; }

    /// <summary>An amount, converted to the thousandths Meta expects.</summary>
    public static TemplateCurrency FromDecimal(decimal amount, string code, string fallback) => new()
    {
        FallbackValue = fallback,
        Code = code,
        AmountInThousandths = (long)Math.Round(amount * 1000m, MidpointRounding.AwayFromZero),
    };
}
