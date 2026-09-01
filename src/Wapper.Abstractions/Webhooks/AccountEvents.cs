using Wapper.PhoneNumbers;

namespace Wapper.Webhooks;

/// <summary>What happened to a WhatsApp Business Account.</summary>
/// <remarks>
/// Meta keeps adding to this list, and a good half of it is only meaningful to a Solution
/// Partner — the ad account, pricing tier, business verification and partner app events all
/// arrive as <see cref="Unknown"/> with <see cref="AccountUpdated.RawEvent"/> and
/// <see cref="AccountUpdated.Json"/> to read them from.
/// </remarks>
public enum AccountUpdateEvent
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>The account was verified.</summary>
    VerifiedAccount,

    /// <summary>
    /// The account broke Meta's policies or terms. <see cref="AccountUpdated.ViolationType"/>
    /// says which.
    /// </summary>
    AccountViolation,

    /// <summary>
    /// Something has been taken away over a violation.
    /// <see cref="AccountUpdated.Restrictions"/> says what, and until when.
    /// </summary>
    AccountRestriction,

    /// <summary>
    /// The account is being disabled, or has been reinstated.
    /// <see cref="AccountUpdated.BanState"/> says which.
    /// </summary>
    DisabledUpdate,

    /// <summary>The account was deleted. Nothing on it will work again.</summary>
    AccountDeleted,

    /// <summary>The account was offboarded after a device change or a re-registration.</summary>
    AccountOffboarded,

    /// <summary>The account came back after being offboarded.</summary>
    AccountReconnected,

    /// <summary>
    /// A number's quality rating or messaging limit moved.
    /// </summary>
    /// <remarks>
    /// The same news the <c>phone_number_quality_update</c> field carries, which Meta also
    /// sends here. Handling one of the two is enough.
    /// </remarks>
    PhoneNumberQualityUpdate,

    /// <summary>The account was shared with a Solution Partner.</summary>
    PartnerAdded,

    /// <summary>The account was unshared from a Solution Partner.</summary>
    PartnerRemoved,
}

/// <summary>Something an account may no longer do, and until when.</summary>
public sealed record AccountRestriction
{
    /// <summary>
    /// What was restricted, as Meta spells it — for example
    /// <c>RESTRICTED_BIZ_INITIATED_MESSAGING</c> or <c>RESTRICTED_ADD_PHONE_NUMBER_ACTION</c>.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>When the restriction lifts, when Meta said.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>What Meta suggests doing about it.</summary>
    public string? Remediation { get; init; }
}

/// <summary>
/// Something changed about the WhatsApp Business Account itself.
/// </summary>
/// <remarks>
/// <para>
/// The webhook that says an account is being disabled, restricted or deleted, and the one
/// place any of that is reported. Every other failure mode surfaces as sends starting to fail
/// for reasons that read like a bug.
/// </para>
/// <para>
/// Account-level, so <see cref="WhatsAppEvent.PhoneNumberId"/> is empty; match on
/// <see cref="WhatsAppEvent.BusinessAccountId"/>. The events that concern one number name it
/// in <see cref="PhoneNumber"/>.
/// </para>
/// </remarks>
public sealed record AccountUpdated : WhatsAppEvent
{
    /// <summary>What happened.</summary>
    public AccountUpdateEvent Event { get; init; }

    /// <summary>The raw event string, in case Meta sent one this library does not know.</summary>
    public string? RawEvent { get; init; }

    /// <summary>
    /// The number the event is about, when it is about one.
    /// </summary>
    /// <remarks>
    /// Meta sends <c>phone_number</c> both as an object and as a bare string, and the string
    /// is the display number on a direct delivery but the phone number id on a partner one.
    /// It arrives here exactly as sent rather than being guessed at.
    /// </remarks>
    public string? PhoneNumber { get; init; }

    /// <summary>How recipients have been receiving that number's messages, when Meta said.</summary>
    public PhoneNumberQuality QualityRating { get; init; }

    /// <summary>The number's messaging limit, when Meta said.</summary>
    public MessagingLimitTier CurrentLimit { get; init; }

    /// <summary>
    /// Where a disablement stands: <c>SCHEDULE_FOR_DISABLE</c>, <c>DISABLE</c> or
    /// <c>REINSTATE</c>.
    /// </summary>
    public string? BanState { get; init; }

    /// <summary>When the account is scheduled to be disabled, as Meta wrote it.</summary>
    public string? BanDate { get; init; }

    /// <summary>Which policy was broken, for example <c>ADULT</c> or <c>SCAM</c>.</summary>
    public string? ViolationType { get; init; }

    /// <summary>What the account may no longer do.</summary>
    public IReadOnlyList<AccountRestriction> Restrictions { get; init; } = [];

    /// <summary>
    /// The <c>value</c> object, exactly as it arrived.
    /// </summary>
    /// <remarks>
    /// This field carries a different sub-object for each of its twenty-odd events and Meta
    /// adds to them faster than they are documented, so the body comes along rather than
    /// leaving an application to wait for a release.
    /// </remarks>
    public string Json { get; init; } = string.Empty;
}
