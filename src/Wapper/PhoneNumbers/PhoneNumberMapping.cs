using System.Globalization;
using Wapper.Internal;

namespace Wapper.PhoneNumbers;

/// <summary>Turns the wire shape of a phone number into the model, and back.</summary>
internal static class PhoneNumberMapping
{
    internal static PhoneNumber ToPhoneNumber(this PhoneNumberPayload payload) => new()
    {
        Id = payload.Id ?? string.Empty,
        DisplayPhoneNumber = payload.DisplayPhoneNumber,
        VerifiedName = payload.VerifiedName,
        Status = ParseStatus(payload.Status),
        Quality = ParseQuality(payload.QualityRating),
        CodeVerification = ParseVerification(payload.CodeVerificationStatus),
        NameStatus = ParseNameStatus(payload.NameStatus),
        NewNameStatus = ParseNameStatus(payload.NewNameStatus),
        Throughput = ParseThroughput(payload.Throughput?.Level),
        MessagingLimit = ParseTier(payload.MessagingLimitTier),
        Platform = ParsePlatform(payload.PlatformType),
        AccountMode = ParseAccountMode(payload.AccountMode),
        IsOfficialBusinessAccount = payload.IsOfficialBusinessAccount ?? false,
        IsTwoStepPinEnabled = payload.IsPinEnabled ?? false,
        LastOnboardedAt = ParseTimestamp(payload.LastOnboardedTime),
    };

    internal static PhoneNumberStatus ParseStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "CONNECTED" => PhoneNumberStatus.Connected,
        "PENDING" => PhoneNumberStatus.Pending,
        "FLAGGED" => PhoneNumberStatus.Flagged,
        "RATE_LIMITED" => PhoneNumberStatus.RateLimited,
        "RESTRICTED" => PhoneNumberStatus.Restricted,
        "DISCONNECTED" => PhoneNumberStatus.Disconnected,
        "MIGRATED" => PhoneNumberStatus.Migrated,
        "BANNED" => PhoneNumberStatus.Banned,
        "DELETED" => PhoneNumberStatus.Deleted,
        "UNVERIFIED" => PhoneNumberStatus.Unverified,
        _ => PhoneNumberStatus.Unknown,
    };

    internal static PhoneNumberQuality ParseQuality(string? quality) => quality?.ToUpperInvariant() switch
    {
        "GREEN" => PhoneNumberQuality.Green,
        "YELLOW" => PhoneNumberQuality.Yellow,
        "RED" => PhoneNumberQuality.Red,
        // "NA" is what a number that has not sent enough messages to be rated reports.
        _ => PhoneNumberQuality.Unknown,
    };

    internal static CodeVerificationStatus ParseVerification(string? status) => status?.ToUpperInvariant() switch
    {
        "VERIFIED" => CodeVerificationStatus.Verified,
        "NOT_VERIFIED" => CodeVerificationStatus.NotVerified,
        "EXPIRED" => CodeVerificationStatus.Expired,
        _ => CodeVerificationStatus.Unknown,
    };

    internal static DisplayNameStatus ParseNameStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "NONE" => DisplayNameStatus.None,
        "APPROVED" => DisplayNameStatus.Approved,
        "AVAILABLE_WITHOUT_REVIEW" => DisplayNameStatus.AvailableWithoutReview,
        "DECLINED" => DisplayNameStatus.Declined,
        "EXPIRED" => DisplayNameStatus.Expired,
        "PENDING_REVIEW" => DisplayNameStatus.PendingReview,
        _ => DisplayNameStatus.Unknown,
    };

    internal static ThroughputLevel ParseThroughput(string? level) => level?.ToUpperInvariant() switch
    {
        "STANDARD" => ThroughputLevel.Standard,
        "HIGH" => ThroughputLevel.High,
        "NOT_APPLICABLE" => ThroughputLevel.NotApplicable,
        _ => ThroughputLevel.Unknown,
    };

    internal static MessagingLimitTier ParseTier(string? tier) => tier?.ToUpperInvariant() switch
    {
        "TIER_NOT_SET" => MessagingLimitTier.NotSet,
        "TIER_50" => MessagingLimitTier.Tier50,
        "TIER_250" => MessagingLimitTier.Tier250,
        "TIER_1K" => MessagingLimitTier.Tier1K,
        "TIER_2K" => MessagingLimitTier.Tier2K,
        "TIER_10K" => MessagingLimitTier.Tier10K,
        "TIER_100K" => MessagingLimitTier.Tier100K,
        "TIER_UNLIMITED" => MessagingLimitTier.Unlimited,
        _ => MessagingLimitTier.Unknown,
    };

    internal static PhoneNumberPlatform ParsePlatform(string? platform) => platform?.ToUpperInvariant() switch
    {
        "CLOUD_API" => PhoneNumberPlatform.CloudApi,
        "ON_PREMISE" => PhoneNumberPlatform.OnPremise,
        "NOT_APPLICABLE" => PhoneNumberPlatform.NotApplicable,
        _ => PhoneNumberPlatform.Unknown,
    };

    internal static PhoneNumberAccountMode ParseAccountMode(string? mode) => mode?.ToUpperInvariant() switch
    {
        "LIVE" => PhoneNumberAccountMode.Live,
        "SANDBOX" => PhoneNumberAccountMode.Sandbox,
        _ => PhoneNumberAccountMode.Unknown,
    };

    /// <summary>
    /// Reads the onboarding time, which arrives as ISO 8601 rather than as the Unix seconds
    /// every timestamp on the webhook uses.
    /// </summary>
    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
}
