using Wapper.PhoneNumbers;

namespace Wapper.Webhooks;

/// <summary>What moved a number's messaging limit or throughput.</summary>
public enum PhoneNumberQualityEvent
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>The number is still being registered.</summary>
    Onboarding,

    /// <summary>Quality has dropped far enough that the limit will fall if it continues.</summary>
    Flagged,

    /// <summary>Quality has recovered. The number is eligible for an increase again.</summary>
    Unflagged,

    /// <summary>The messaging limit went up.</summary>
    Upgrade,

    /// <summary>The messaging limit went down.</summary>
    Downgrade,

    /// <summary>
    /// The number was moved to a higher throughput level, so it may now send considerably
    /// faster.
    /// </summary>
    ThroughputUpgrade,
}

/// <summary>
/// A number's messaging limit or throughput changed.
/// </summary>
/// <remarks>
/// <para>
/// Account-level, like the template events: it names the number in display form and carries
/// no phone number id, so match on <see cref="WhatsAppEvent.DisplayPhoneNumber"/>.
/// </para>
/// <para>
/// <see cref="PhoneNumberQualityEvent.Flagged"/> is the one to act on. It means quality has
/// dropped and the messaging limit will fall if nothing changes.
/// </para>
/// </remarks>
public sealed record PhoneNumberQualityChanged : WhatsAppEvent
{
    /// <summary>What happened.</summary>
    public PhoneNumberQualityEvent Event { get; init; }

    /// <summary>The raw event string, in case Meta sent one this library does not know.</summary>
    public string? RawEvent { get; init; }

    /// <summary>
    /// What the limit was. Only sent when the limit itself moved, so it stays
    /// <see cref="MessagingLimitTier.Unknown"/> on a throughput change.
    /// </summary>
    public MessagingLimitTier PreviousLimit { get; init; }

    /// <summary>What the limit is now.</summary>
    public MessagingLimitTier CurrentLimit { get; init; }
}

/// <summary>The outcome of reviewing a display name.</summary>
public enum DisplayNameDecision
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>Approved. The name now appears at the top of the chat.</summary>
    Approved,

    /// <summary>Put off. Nothing to do but wait for a later decision.</summary>
    Deferred,

    /// <summary>Still under review.</summary>
    Pending,

    /// <summary>Turned down. Edit the name in WhatsApp Manager and submit it again.</summary>
    Rejected,
}

/// <summary>Why a display name was turned down.</summary>
public enum DisplayNameRejectionReason
{
    /// <summary>No reason, which is what an approval carries.</summary>
    None,

    /// <summary>Turned down for a reason this library does not recognise.</summary>
    Unknown,

    /// <summary>The name contained a person's name or an employee identifier.</summary>
    PersonalName,

    /// <summary>The name referred to a business other than this one.</summary>
    UnrelatedBusiness,

    /// <summary>The formatting was not acceptable.</summary>
    UnacceptableFormat,

    /// <summary>The name did not match the business's own branding.</summary>
    InconsistentWithBranding,
}

/// <summary>
/// A display name was reviewed.
/// </summary>
/// <remarks>
/// Arrives when a new number's name is first reviewed, and again whenever an approved name is
/// edited. Account-level, and identified by the number in display form.
/// </remarks>
public sealed record PhoneNumberNameChanged : WhatsAppEvent
{
    /// <summary>The outcome.</summary>
    public DisplayNameDecision Decision { get; init; }

    /// <summary>The raw decision string.</summary>
    public string? RawDecision { get; init; }

    /// <summary>The name that was reviewed.</summary>
    public string? RequestedName { get; init; }

    /// <summary>Why it was turned down, when it was.</summary>
    public DisplayNameRejectionReason RejectionReason { get; init; }

    /// <summary>The raw rejection reason.</summary>
    public string? RawRejectionReason { get; init; }
}
