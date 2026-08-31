namespace Wapper.Templates;

/// <summary>How a template is categorised. Meta prices and polices each category differently.</summary>
public enum TemplateCategory
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>One-time passcodes and verification codes.</summary>
    Authentication,

    /// <summary>Promotions, offers, announcements. The category subject to per-user limits.</summary>
    Marketing,

    /// <summary>Order updates, appointment reminders, anything tied to a transaction.</summary>
    Utility,
}

/// <summary>Where a template stands with Meta's review and quality systems.</summary>
public enum TemplateStatus
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>Under review. Meta says this can take up to 24 hours.</summary>
    Pending,

    /// <summary>Reviewed and usable. The only status a template message can be sent with.</summary>
    Approved,

    /// <summary>Turned down by review. Editable without limit, and re-reviewed on edit.</summary>
    Rejected,

    /// <summary>Held back after repeated negative feedback. Cannot be sent while paused.</summary>
    Paused,

    /// <summary>Switched off for good after repeated negative feedback. Cannot be deleted.</summary>
    Disabled,

    /// <summary>An appeal against a rejection is being considered.</summary>
    InAppeal,

    /// <summary>Deleted, but still being delivered to recipients it had already been sent to.</summary>
    PendingDeletion,

    /// <summary>Gone.</summary>
    Deleted,

    /// <summary>Put aside after twelve months of disuse. Deleted 28 days later unless restored.</summary>
    Archived,

    /// <summary>The account has as many templates as it is allowed.</summary>
    LimitExceeded,
}

/// <summary>How the placeholders in a template are written.</summary>
public enum TemplateParameterFormat
{
    /// <summary>
    /// Numbered, as <c>{{1}}</c> and <c>{{2}}</c>. Values are matched by order, so inserting a
    /// placeholder renumbers everything after it.
    /// </summary>
    Positional,

    /// <summary>
    /// Named, as <c>{{order_number}}</c>. Values are matched by name and may be sent in any
    /// order, which survives edits far better.
    /// </summary>
    Named,
}

/// <summary>What a template's header shows.</summary>
public enum TemplateHeaderFormat
{
    /// <summary>A line of text, which may hold one placeholder.</summary>
    Text,

    /// <summary>A picture.</summary>
    Image,

    /// <summary>A video.</summary>
    Video,

    /// <summary>A file, shown as its first page.</summary>
    Document,

    /// <summary>A map, whose point is supplied when the template is sent.</summary>
    Location,
}

/// <summary>What one of a template's buttons does.</summary>
public enum TemplateButtonKind
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>Sends its label back as a message. At most ten per template.</summary>
    QuickReply,

    /// <summary>Opens a link. At most two per template, and one may hold a placeholder.</summary>
    Url,

    /// <summary>Dials a number. At most one per template.</summary>
    PhoneNumber,

    /// <summary>Copies a string to the clipboard. At most one per template.</summary>
    CopyCode,

    /// <summary>Places a WhatsApp call to the business.</summary>
    VoiceCall,
}

/// <summary>
/// A sample value for one placeholder, required by Meta so a human reviewer can see what the
/// template will actually look like.
/// </summary>
/// <param name="Value">The sample.</param>
/// <param name="Name">
/// Which placeholder it fills, for a template using named parameters. Left unset for
/// positional ones, where order is what matters.
/// </param>
public readonly record struct TemplateParameterExample(string Value, string? Name = null);

/// <summary>The banner at the top of a template.</summary>
public sealed record TemplateHeader
{
    private TemplateHeader(TemplateHeaderFormat format)
    {
        Format = format;
    }

    /// <summary>What the header shows.</summary>
    public TemplateHeaderFormat Format { get; }

    /// <summary>The heading, for a text header. At most 60 characters, and one placeholder.</summary>
    public string? Text { get; init; }

    /// <summary>Sample values for the placeholder in <see cref="Text"/>.</summary>
    public IReadOnlyList<TemplateParameterExample> Examples { get; init; } = [];

    /// <summary>
    /// Handle of the sample media, for an image, video or document header.
    /// </summary>
    /// <remarks>
    /// Not a media id. Header samples go through the Resumable Upload API, which returns a
    /// handle of its own, and the sample is reviewed along with the template.
    /// </remarks>
    public string? MediaHandle { get; init; }

    /// <summary>A line of text, optionally holding one placeholder.</summary>
    public static TemplateHeader FromText(string text, params TemplateParameterExample[] examples)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new TemplateHeader(TemplateHeaderFormat.Text) { Text = text, Examples = examples };
    }

    /// <summary>An image, shown from the sample uploaded at review time.</summary>
    public static TemplateHeader FromImage(string mediaHandle) =>
        FromMedia(TemplateHeaderFormat.Image, mediaHandle);

    /// <summary>A video.</summary>
    public static TemplateHeader FromVideo(string mediaHandle) =>
        FromMedia(TemplateHeaderFormat.Video, mediaHandle);

    /// <summary>A document.</summary>
    public static TemplateHeader FromDocument(string mediaHandle) =>
        FromMedia(TemplateHeaderFormat.Document, mediaHandle);

    /// <summary>
    /// A map. The point itself is supplied when the template is sent, not now.
    /// </summary>
    /// <remarks>Only allowed on utility and marketing templates.</remarks>
    public static TemplateHeader FromLocation() => new(TemplateHeaderFormat.Location);

    private static TemplateHeader FromMedia(TemplateHeaderFormat format, string mediaHandle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaHandle);
        return new TemplateHeader(format) { MediaHandle = mediaHandle };
    }
}

/// <summary>The main text of a template. The only component Meta requires.</summary>
public sealed record TemplateBody
{
    /// <summary>The text, at most 1024 characters, holding any number of placeholders.</summary>
    public required string Text { get; init; }

    /// <summary>Sample values for the placeholders, one per placeholder.</summary>
    public IReadOnlyList<TemplateParameterExample> Examples { get; init; } = [];
}

/// <summary>One of a template's buttons.</summary>
public sealed record TemplateButton
{
    private TemplateButton(TemplateButtonKind kind)
    {
        Kind = kind;
    }

    /// <summary>What the button does.</summary>
    public TemplateButtonKind Kind { get; init; }

    /// <summary>The label, at most 25 characters. Absent on a copy-code button.</summary>
    public string? Text { get; init; }

    /// <summary>Where a URL button leads. May hold one placeholder, appended at the end.</summary>
    public string? Url { get; init; }

    /// <summary>Sample value for the placeholder in <see cref="Url"/>.</summary>
    public string? UrlExample { get; init; }

    /// <summary>What a phone-number button dials.</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>Sample string a copy-code button puts on the clipboard. At most 20 characters.</summary>
    public string? CopyCodeExample { get; init; }

    /// <summary>The raw type string, for a button kind this library does not know.</summary>
    public string? RawKind { get; init; }

    /// <summary>A button that sends its own label back as a message.</summary>
    public static TemplateButton QuickReply(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new TemplateButton(TemplateButtonKind.QuickReply) { Text = text };
    }

    /// <summary>
    /// A button that opens a link.
    /// </summary>
    /// <param name="text">The label.</param>
    /// <param name="url">
    /// The address. One placeholder is allowed, and only at the end of the string.
    /// </param>
    /// <param name="example">
    /// Sample value for that placeholder. Required whenever the URL has one.
    /// </param>
    public static TemplateButton Link(string text, string url, string? example = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        return new TemplateButton(TemplateButtonKind.Url)
        {
            Text = text,
            Url = url,
            UrlExample = example,
        };
    }

    /// <summary>A button that dials a number.</summary>
    public static TemplateButton Call(string text, string phoneNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        return new TemplateButton(TemplateButtonKind.PhoneNumber)
        {
            Text = text,
            PhoneNumber = phoneNumber,
        };
    }

    /// <summary>A button that copies a code to the clipboard.</summary>
    public static TemplateButton CopyCode(string example)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(example);
        return new TemplateButton(TemplateButtonKind.CopyCode) { CopyCodeExample = example };
    }

    /// <summary>A button that places a WhatsApp call to the business.</summary>
    public static TemplateButton VoiceCall(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new TemplateButton(TemplateButtonKind.VoiceCall) { Text = text };
    }

    /// <summary>
    /// A button of a kind this library has no typed form for.
    /// </summary>
    /// <remarks>
    /// Meta adds button types without warning. Reading a template that has one keeps it here
    /// rather than dropping it, though it cannot be sent back in an edit.
    /// </remarks>
    public static TemplateButton FromUnknown(string? rawKind, string? text) =>
        new(TemplateButtonKind.Unknown) { RawKind = rawKind, Text = text };
}

/// <summary>
/// A template, as Meta holds it.
/// </summary>
/// <remarks>
/// Meta models a template as a list of components, but allows at most one header, one body,
/// one footer and one buttons block, so those are separate properties here. A malformed
/// combination is then unrepresentable rather than rejected at review time.
/// </remarks>
public sealed record Template
{
    /// <summary>Identifier, once the template exists. Unset on one being drafted.</summary>
    public string? Id { get; init; }

    /// <summary>
    /// The name. Lowercase letters, digits and underscores only, at most 512 characters.
    /// </summary>
    /// <remarks>
    /// Not unique on its own: the same name in a different language is a different template,
    /// and each one counts separately against the account's limit.
    /// </remarks>
    public required string Name { get; init; }

    /// <summary>Locale of the template, such as <c>en_US</c>. Meta translates nothing.</summary>
    public required string Language { get; init; }

    /// <summary>Which category it was filed under.</summary>
    public required TemplateCategory Category { get; init; }

    /// <summary>Where it stands with review. Only <see cref="TemplateStatus.Approved"/> can be sent.</summary>
    public TemplateStatus Status { get; init; }

    /// <summary>The raw status string, in case Meta sent one this library does not know.</summary>
    public string? RawStatus { get; init; }

    /// <summary>Meta's finer classification, such as <c>CUSTOM</c>.</summary>
    public string? SubCategory { get; init; }

    /// <summary>Whether the placeholders are numbered or named.</summary>
    public TemplateParameterFormat ParameterFormat { get; init; } = TemplateParameterFormat.Positional;

    /// <summary>The banner, if there is one.</summary>
    public TemplateHeader? Header { get; init; }

    /// <summary>The main text.</summary>
    public required TemplateBody Body { get; init; }

    /// <summary>The small print below the body. At most 60 characters, and no placeholders.</summary>
    public string? Footer { get; init; }

    /// <summary>The buttons, at most ten between them.</summary>
    public IReadOnlyList<TemplateButton> Buttons { get; init; } = [];

    /// <summary>
    /// How long Meta keeps trying to deliver a message built from this template.
    /// </summary>
    public TimeSpan? TimeToLive { get; init; }
}
