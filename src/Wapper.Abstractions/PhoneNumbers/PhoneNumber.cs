namespace Wapper.PhoneNumbers;

/// <summary>Where a business phone number stands with the platform.</summary>
/// <remarks>
/// Only <see cref="Connected"/> can send and receive. Everything else is a reason messages
/// are failing that no amount of retrying will fix.
/// </remarks>
public enum PhoneNumberStatus
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>Registered and working.</summary>
    Connected,

    /// <summary>Added, but registration has not finished.</summary>
    Pending,

    /// <summary>Under review after negative feedback. Still sending, for now.</summary>
    Flagged,

    /// <summary>Sending is blocked until the messaging limit resets.</summary>
    RateLimited,

    /// <summary>Sending is blocked by a policy violation.</summary>
    Restricted,

    /// <summary>Deregistered, or disconnected by Meta.</summary>
    Disconnected,

    /// <summary>Moved to another WhatsApp Business Account.</summary>
    Migrated,

    /// <summary>Banned from the platform. Appealable, not fixable through the API.</summary>
    Banned,

    /// <summary>Removed from the account.</summary>
    Deleted,

    /// <summary>Ownership has not been proven with a verification code.</summary>
    Unverified,
}

/// <summary>How recipients are receiving messages from a number over the last seven days.</summary>
public enum PhoneNumberQuality
{
    /// <summary>Not rated yet, or the platform did not say.</summary>
    Unknown,

    /// <summary>High quality. Little to no negative feedback.</summary>
    Green,

    /// <summary>Medium quality. Some negative feedback; the messaging limit is at risk.</summary>
    Yellow,

    /// <summary>Low quality. The number is close to being flagged or rate limited.</summary>
    Red,
}

/// <summary>Whether ownership of the number has been proven with a code.</summary>
public enum CodeVerificationStatus
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>Verified. Requesting another code returns error 136024.</summary>
    Verified,

    /// <summary>Not verified. A code has to be requested and submitted before registering.</summary>
    NotVerified,

    /// <summary>The verification has lapsed and has to be done again.</summary>
    Expired,
}

/// <summary>Where a display name stands with review.</summary>
public enum DisplayNameStatus
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>No name has been submitted.</summary>
    None,

    /// <summary>Approved, and shown at the top of the chat.</summary>
    Approved,

    /// <summary>Usable straight away; this one does not need reviewing.</summary>
    AvailableWithoutReview,

    /// <summary>Turned down. Edit it in WhatsApp Manager and submit again.</summary>
    Declined,

    /// <summary>The certificate behind the name has expired.</summary>
    Expired,

    /// <summary>Under review.</summary>
    PendingReview,
}

/// <summary>How many messages a second a number is allowed to send.</summary>
/// <remarks>
/// This is the budget the client paces sends against. A number Meta has moved to
/// <see cref="High"/> may send far more than the conservative default assumes — see
/// <c>WhatsAppRateLimitOptions.MessagesPerSecond</c>.
/// </remarks>
public enum ThroughputLevel
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>80 messages a second, the level every number starts at.</summary>
    Standard,

    /// <summary>1000 messages a second, which Meta grants automatically as volume grows.</summary>
    High,

    /// <summary>The number is not registered, so it has no throughput at all.</summary>
    NotApplicable,
}

/// <summary>
/// How many unique customers a number may open a conversation with in a rolling 24 hours.
/// </summary>
/// <remarks>
/// Separate from throughput: this caps how many people are messaged in a day, not how fast.
/// Replying to a customer within their 24-hour window does not count against it.
/// </remarks>
public enum MessagingLimitTier
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>No limit set yet, because the number has never sent a message.</summary>
    NotSet,

    /// <summary>50 customers a day.</summary>
    Tier50,

    /// <summary>250 customers a day.</summary>
    Tier250,

    /// <summary>1000 customers a day.</summary>
    Tier1K,

    /// <summary>2000 customers a day.</summary>
    Tier2K,

    /// <summary>10 000 customers a day.</summary>
    Tier10K,

    /// <summary>100 000 customers a day.</summary>
    Tier100K,

    /// <summary>No cap.</summary>
    Unlimited,
}

/// <summary>Which WhatsApp platform a number is hosted on.</summary>
public enum PhoneNumberPlatform
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>Cloud API, hosted by Meta. What this library talks to.</summary>
    CloudApi,

    /// <summary>The retired On-Premises API.</summary>
    OnPremise,

    /// <summary>Not hosted on either, which is what an unregistered number reports.</summary>
    NotApplicable,
}

/// <summary>Whether a number sends real messages or only test ones.</summary>
public enum PhoneNumberAccountMode
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>Sending to real customers.</summary>
    Live,

    /// <summary>Test mode. Messages only reach numbers added to the allow list.</summary>
    Sandbox,
}

/// <summary>
/// The public key Meta encrypts a Flow endpoint's traffic with.
/// </summary>
/// <remarks>
/// Only Flows with an endpoint need one, and they will not run without it.
/// </remarks>
public sealed record BusinessEncryptionKey
{
    /// <summary>The key, in PEM form.</summary>
    public string? PublicKey { get; init; }

    /// <summary>
    /// Whether Meta could verify it: <c>VALID</c>, or <c>MISMATCH</c> when the key does not
    /// match the signature it was uploaded with.
    /// </summary>
    public string? SignatureStatus { get; init; }
}

/// <summary>A business phone number on a WhatsApp Business Account.</summary>
/// <remarks>
/// Meta only returns a handful of these fields by default; the client asks for the rest
/// explicitly, so everything here is populated on every read.
/// </remarks>
public sealed record PhoneNumber
{
    /// <summary>Identifier. This is what messages are sent from, not the number itself.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The number as customers see it, spaced and punctuated by Meta.</summary>
    public string? DisplayPhoneNumber { get; init; }

    /// <summary>The approved display name.</summary>
    public string? VerifiedName { get; init; }

    /// <summary>Whether the number can send and receive.</summary>
    public PhoneNumberStatus Status { get; init; }

    /// <summary>The raw status string, in case Meta sent one this library does not know.</summary>
    public string? RawStatus { get; init; }

    /// <summary>How recipients have been receiving its messages.</summary>
    public PhoneNumberQuality Quality { get; init; }

    /// <summary>The raw quality rating.</summary>
    public string? RawQuality { get; init; }

    /// <summary>Whether ownership has been proven with a code.</summary>
    public CodeVerificationStatus CodeVerification { get; init; }

    /// <summary>The raw code verification status.</summary>
    public string? RawCodeVerification { get; init; }

    /// <summary>Where the current display name stands with review.</summary>
    public DisplayNameStatus NameStatus { get; init; }

    /// <summary>The raw name status.</summary>
    public string? RawNameStatus { get; init; }

    /// <summary>Where a requested change of display name stands. Only set while one is pending.</summary>
    public DisplayNameStatus NewNameStatus { get; init; }

    /// <summary>The raw new-name status.</summary>
    public string? RawNewNameStatus { get; init; }

    /// <summary>How fast this number may send.</summary>
    public ThroughputLevel Throughput { get; init; }

    /// <summary>The raw throughput level.</summary>
    public string? RawThroughput { get; init; }

    /// <summary>How many customers a day it may start a conversation with.</summary>
    public MessagingLimitTier MessagingLimit { get; init; }

    /// <summary>The raw messaging limit tier.</summary>
    public string? RawMessagingLimit { get; init; }

    /// <summary>Which platform hosts it.</summary>
    public PhoneNumberPlatform Platform { get; init; }

    /// <summary>The raw platform type.</summary>
    public string? RawPlatform { get; init; }

    /// <summary>Whether it sends to real customers or only to test numbers.</summary>
    public PhoneNumberAccountMode AccountMode { get; init; }

    /// <summary>The raw account mode.</summary>
    public string? RawAccountMode { get; init; }

    /// <summary>Whether it carries the blue checkmark.</summary>
    public bool IsOfficialBusinessAccount { get; init; }

    /// <summary>
    /// Whether two-step verification is on. It is required to register, and the PIN is
    /// needed to change the PIN or to delete the number.
    /// </summary>
    public bool IsTwoStepPinEnabled { get; init; }

    /// <summary>When the number was last onboarded, when Meta reported it.</summary>
    public DateTimeOffset? LastOnboardedAt { get; init; }
}
