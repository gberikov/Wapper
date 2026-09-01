using Wapper.Templates;

namespace Wapper.Webhooks;

/// <summary>Why a template's status changed.</summary>
public enum TemplateStatusChangeReason
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>No reason was given, which is what an approval looks like.</summary>
    None,

    /// <summary>The template asks for information WhatsApp does not allow to be requested.</summary>
    AbusiveContent,

    /// <summary>The name is already taken in this language.</summary>
    InvalidFormat,

    /// <summary>Held back or switched off after repeated negative feedback from recipients.</summary>
    ScamOrLowQuality,
}

/// <summary>
/// A template moved through review, or was paused or disabled afterwards.
/// </summary>
/// <remarks>
/// The other half of creating a template. Submission only ever returns
/// <see cref="TemplateStatus.Pending"/>; whether it was approved, and why it was not, arrives
/// here up to 24 hours later.
/// </remarks>
public sealed record TemplateStatusChanged : WhatsAppEvent
{
    /// <summary>Identifier of the template.</summary>
    public string TemplateId { get; init; } = string.Empty;

    /// <summary>Its name.</summary>
    public string TemplateName { get; init; } = string.Empty;

    /// <summary>Its locale. A name alone does not identify a template.</summary>
    public string TemplateLanguage { get; init; } = string.Empty;

    /// <summary>Where it now stands.</summary>
    public TemplateStatus Status { get; init; }

    /// <summary>The raw event string, in case Meta sent one this library does not know.</summary>
    public string? RawEvent { get; init; }

    /// <summary>Why, when Meta said.</summary>
    public TemplateStatusChangeReason Reason { get; init; }

    /// <summary>The raw reason string.</summary>
    public string? RawReason { get; init; }

    /// <summary>
    /// Anything else Meta attached: the title of a rejected component, or — on a rejection —
    /// the review's own sentence about what is wrong with the template.
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// What Meta suggests changing, on a rejection.
    /// </summary>
    /// <remarks>
    /// The actionable half of a rejection. <see cref="Reason"/> only says
    /// <see cref="TemplateStatusChangeReason.InvalidFormat"/>; this says "Separate parameters
    /// with descriptive text."
    /// </remarks>
    public string? Recommendation { get; init; }
}

/// <summary>How well recipients are receiving a template.</summary>
public enum TemplateQuality
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>Not enough feedback yet to say.</summary>
    Pending,

    /// <summary>Little to no negative feedback.</summary>
    Green,

    /// <summary>Some negative feedback or low read rates. At risk of being paused.</summary>
    Yellow,

    /// <summary>Enough negative feedback that pausing or disabling is close.</summary>
    Red,
}

/// <summary>
/// A template's quality rating moved.
/// </summary>
/// <remarks>
/// The warning before <see cref="TemplateStatus.Paused"/>. A template dropping to
/// <see cref="TemplateQuality.Red"/> is worth acting on, because the next step is Meta
/// stopping it.
/// </remarks>
public sealed record TemplateQualityChanged : WhatsAppEvent
{
    /// <summary>Identifier of the template.</summary>
    public string TemplateId { get; init; } = string.Empty;

    /// <summary>Its name.</summary>
    public string TemplateName { get; init; } = string.Empty;

    /// <summary>Its locale.</summary>
    public string TemplateLanguage { get; init; } = string.Empty;

    /// <summary>What the rating was.</summary>
    public TemplateQuality Previous { get; init; }

    /// <summary>The raw previous rating, in case Meta sent one this library does not know.</summary>
    public string? RawPrevious { get; init; }

    /// <summary>What it is now.</summary>
    public TemplateQuality Current { get; init; }

    /// <summary>The raw current rating.</summary>
    public string? RawCurrent { get; init; }
}
