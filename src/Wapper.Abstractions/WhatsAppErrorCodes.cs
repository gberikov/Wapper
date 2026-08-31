namespace Wapper;

/// <summary>
/// The Cloud API error codes this library branches on. Not an exhaustive list of
/// Meta's codes — only the ones that change what the client does next.
/// </summary>
public static class WhatsAppErrorCodes
{
    /// <summary>The app reached its platform-wide call limit for the rolling hour.</summary>
    /// <remarks>
    /// Keep calling and the block lasts longer: rejected calls count too. The budget is
    /// <c>200 × daily active users</c>, which Meta does not disclose, so this one can only
    /// be handled after the fact.
    /// </remarks>
    public const int ApplicationRequestLimitReached = 4;

    /// <summary>Temporary downtime or an overloaded service. Retry with backoff.</summary>
    public const int TemporarilyUnavailable = 2;

    /// <summary>The app reached its hourly request limit for one WhatsApp Business Account.</summary>
    /// <remarks>200 requests an hour, or 5000 once the account has a registered phone number.</remarks>
    public const int BusinessAccountRateLimitReached = 80007;

    /// <summary>Cloud API message throughput for the business phone number is exhausted.</summary>
    /// <remarks>
    /// 80 messages a second by default, 1000 after an automatic upgrade. The message never
    /// entered the pipeline, so it has to be sent again.
    /// </remarks>
    public const int MessageThroughputReached = 130429;

    /// <summary>Too many messages from this sender to this one recipient.</summary>
    /// <remarks>
    /// One message per six seconds per recipient, with a burst allowance of 45 that is
    /// borrowed from the following minutes. Other recipients are unaffected, so a queue
    /// must not stall on this.
    /// </remarks>
    public const int PairRateLimitReached = 131056;

    /// <summary>Sending is restricted because earlier messages were flagged as spam.</summary>
    /// <remarks>Retrying does not help. The template or the message quality has to change.</remarks>
    public const int SpamRateLimitReached = 131048;

    /// <summary>Registration or deregistration attempted too many times for this number.</summary>
    /// <remarks>
    /// Ten attempts per 72 hours, after which the number is blocked for the rest of the
    /// window. Never retry this one.
    /// </remarks>
    public const int RegistrationLimitReached = 133016;

    /// <summary>The account is in maintenance mode, including during a throughput upgrade.</summary>
    public const int MaintenanceMode = 131057;

    /// <summary>Held back to protect ecosystem engagement: the per-user marketing limit.</summary>
    /// <remarks>Wait at least 24 hours. Retrying sooner just burns delivery metrics.</remarks>
    public const int PerUserMarketingLimitReached = 131049;

    /// <summary>The user opted out of marketing messages. Never retry.</summary>
    public const int UserOptedOut = 131050;

    /// <summary>Service temporarily unavailable. Retry with backoff.</summary>
    public const int ServiceUnavailable = 131016;

    /// <summary>Server temporarily unavailable. Retry with backoff.</summary>
    public const int ServerUnavailable = 133004;
}
