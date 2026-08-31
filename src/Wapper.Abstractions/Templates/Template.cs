using Wapper.Webhooks;

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

    /// <summary>
    /// Hands a one-time passcode to the customer. Only on an authentication template, and
    /// only one per template.
    /// </summary>
    OneTimePassword,
}

/// <summary>How an authentication template's button delivers the passcode.</summary>
public enum OneTimePasswordDelivery
{
    /// <summary>A delivery method this library does not know about yet.</summary>
    Unknown,

    /// <summary>
    /// The customer copies the code and pastes it themselves. Works everywhere, and needs
    /// nothing from your app.
    /// </summary>
    CopyCode,

    /// <summary>
    /// One tap fills the code into your Android app. Falls back to copy-code on iOS and on
    /// handsets that cannot autofill.
    /// </summary>
    OneTap,

    /// <summary>
    /// The code reaches your Android app without the customer doing anything at all. Meta
    /// requires its terms to have been accepted before a template using it is approved.
    /// </summary>
    ZeroTap,
}

/// <summary>An Android app a one-time passcode may be delivered into.</summary>
/// <param name="PackageName">Its package name, for example <c>com.example.myapp</c>.</param>
/// <param name="SignatureHash">
/// The 11-character hash of the signing certificate. Meta matches on it so a passcode cannot
/// be autofilled into an impostor app.
/// </param>
public readonly record struct TemplateApplication(string PackageName, string SignatureHash);

/// <summary>What an authentication template's one-time-passcode button does.</summary>
public sealed record TemplateOneTimePassword
{
    /// <summary>How the code reaches the customer.</summary>
    public OneTimePasswordDelivery Delivery { get; init; }

    /// <summary>The raw <c>otp_type</c>, for a delivery method not known here.</summary>
    public string? RawDelivery { get; init; }

    /// <summary>
    /// The label shown while the code is being filled in. One-tap and zero-tap only.
    /// </summary>
    public string? AutofillText { get; init; }

    /// <summary>The apps the code may be delivered into. One-tap and zero-tap only.</summary>
    public IReadOnlyList<TemplateApplication> SupportedApps { get; init; } = [];

    /// <summary>Whether Meta's zero-tap terms have been accepted. Required for zero-tap.</summary>
    public bool? ZeroTapTermsAccepted { get; init; }
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
    /// <remarks>
    /// Internal rather than private so the mapping layer can build a header out of whatever
    /// Meta actually sent. The public factories validate, which is right for a template being
    /// written; a template being read has to survive a missing field rather than throw and
    /// take the whole listing with it.
    /// </remarks>
    internal TemplateHeader(TemplateHeaderFormat format)
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
    /// <remarks>
    /// Empty on an authentication template: Meta writes the body itself, in every language it
    /// supports, and rejects one that brings its own.
    /// </remarks>
    public required string Text { get; init; }

    /// <summary>Sample values for the placeholders, one per placeholder.</summary>
    public IReadOnlyList<TemplateParameterExample> Examples { get; init; } = [];

    /// <summary>
    /// Whether Meta should append "For your security, do not share this code." to the body.
    /// </summary>
    /// <remarks>Authentication templates only.</remarks>
    public bool? AddSecurityRecommendation { get; init; }
}

/// <summary>One of a template's buttons.</summary>
public sealed record TemplateButton
{
    /// <inheritdoc cref="TemplateHeader(TemplateHeaderFormat)" path="/remarks" />
    internal TemplateButton(TemplateButtonKind kind)
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

    /// <summary>How the passcode is delivered, for a one-time-passcode button.</summary>
    public TemplateOneTimePassword? OneTimePassword { get; init; }

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
    /// A button that copies a one-time passcode to the clipboard, for an authentication
    /// template.
    /// </summary>
    /// <param name="text">
    /// The label. Meta supplies a translated default when it is left unset, which is usually
    /// better than translating it yourself.
    /// </param>
    public static TemplateButton CopyOneTimePassword(string? text = null) =>
        new(TemplateButtonKind.OneTimePassword)
        {
            Text = text,
            OneTimePassword = new TemplateOneTimePassword
            {
                Delivery = OneTimePasswordDelivery.CopyCode,
            },
        };

    /// <summary>
    /// A button that fills a one-time passcode straight into your Android app.
    /// </summary>
    /// <param name="apps">
    /// The apps the code may be delivered into. Meta matches the signing certificate, so a
    /// wrong hash means the code silently never arrives.
    /// </param>
    /// <param name="text">The label. Meta supplies a translated default when unset.</param>
    /// <param name="autofillText">The label shown while the code is being filled in.</param>
    /// <param name="zeroTap">
    /// Whether the code should reach the app without the customer tapping anything. Setting
    /// it also accepts Meta's zero-tap terms, which it will not approve the template without.
    /// </param>
    /// <remarks>
    /// Falls back to copying on iOS and on any handset that cannot autofill, so this is
    /// always at least as good as <see cref="CopyOneTimePassword"/>.
    /// </remarks>
    public static TemplateButton AutofillOneTimePassword(
        IReadOnlyList<TemplateApplication> apps,
        string? text = null,
        string? autofillText = null,
        bool zeroTap = false)
    {
        ArgumentNullException.ThrowIfNull(apps);

        if (apps.Count == 0)
        {
            throw new ArgumentException(
                "An autofilled passcode is delivered into an app, so at least one has to be " +
                "named. Use CopyOneTimePassword for a button that only copies the code.",
                nameof(apps));
        }

        return new TemplateButton(TemplateButtonKind.OneTimePassword)
        {
            Text = text,
            OneTimePassword = new TemplateOneTimePassword
            {
                Delivery = zeroTap ? OneTimePasswordDelivery.ZeroTap : OneTimePasswordDelivery.OneTap,
                AutofillText = autofillText,
                SupportedApps = apps,
                ZeroTapTermsAccepted = zeroTap ? true : null,
            },
        };
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

    /// <summary>
    /// How long the passcode stays valid, in minutes, written into the footer by Meta.
    /// </summary>
    /// <remarks>
    /// Authentication templates only, and it replaces <see cref="Footer"/> rather than
    /// joining it: Meta writes the sentence itself so it is translated everywhere.
    /// </remarks>
    public int? CodeExpirationMinutes { get; init; }

    /// <summary>The buttons, at most ten between them.</summary>
    public IReadOnlyList<TemplateButton> Buttons { get; init; } = [];

    /// <summary>
    /// How long Meta keeps trying to deliver a message built from this template.
    /// </summary>
    public TimeSpan? TimeToLive { get; init; }

    /// <summary>How recipients have been receiving it, when Meta reported it. Read only.</summary>
    public TemplateQuality QualityScore { get; init; }

    /// <summary>Why review turned it down, when it did. Read only.</summary>
    public string? RejectedReason { get; init; }

    /// <summary>
    /// The category it sat in before Meta moved it, when Meta moved it. Read only.
    /// </summary>
    public TemplateCategory PreviousCategory { get; init; }

    /// <summary>
    /// An authentication template: a one-time passcode and the button that hands it over.
    /// </summary>
    /// <param name="name">The template name.</param>
    /// <param name="language">Its locale.</param>
    /// <param name="button">
    /// How the passcode is delivered — <see cref="TemplateButton.CopyOneTimePassword"/> or
    /// <see cref="TemplateButton.AutofillOneTimePassword"/>.
    /// </param>
    /// <param name="codeExpirationMinutes">
    /// How long the code stays valid. Meta writes it into the footer, translated.
    /// </param>
    /// <param name="addSecurityRecommendation">
    /// Whether Meta should append its "do not share this code" line to the body.
    /// </param>
    /// <remarks>
    /// Authentication templates carry no text of their own: Meta writes the body and the
    /// footer in every language it supports, which is the whole point of the category. All
    /// this builds is the shape, so the rest of the library does not have to special-case it.
    /// </remarks>
    public static Template Authentication(
        string name,
        string language,
        TemplateButton button,
        int? codeExpirationMinutes = null,
        bool addSecurityRecommendation = true)
    {
        ArgumentNullException.ThrowIfNull(button);

        if (button.Kind != TemplateButtonKind.OneTimePassword)
        {
            throw new ArgumentException(
                "An authentication template carries a one-time-passcode button. Build one with " +
                $"{nameof(TemplateButton)}.{nameof(TemplateButton.CopyOneTimePassword)} or " +
                $"{nameof(TemplateButton.AutofillOneTimePassword)}.",
                nameof(button));
        }

        return new Template
        {
            Name = name,
            Language = language,
            Category = TemplateCategory.Authentication,
            Body = new TemplateBody
            {
                Text = string.Empty,
                AddSecurityRecommendation = addSecurityRecommendation,
            },
            CodeExpirationMinutes = codeExpirationMinutes,
            Buttons = [button],
        };
    }
}
