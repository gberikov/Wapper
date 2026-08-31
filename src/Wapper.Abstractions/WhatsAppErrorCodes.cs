namespace Wapper;

/// <summary>
/// Cloud API error codes.
/// </summary>
/// <remarks>
/// Not an exhaustive list of Meta's codes. It holds the ones this library branches on, and
/// the ones a caller most often branches on itself — because <see cref="WhatsAppError.Code"/>
/// is the only field Meta says is stable, so an application that reacts to failures at all
/// ends up writing these numbers down somewhere.
/// </remarks>
public static class WhatsAppErrorCodes
{
    // Platform-level. These come from Graph rather than from WhatsApp, and are about the
    // token or the app rather than about the message.

    /// <summary>Temporary downtime or an overloaded service. Retry with backoff.</summary>
    public const int TemporarilyUnavailable = 2;

    /// <summary>The app reached its platform-wide call limit for the rolling hour.</summary>
    /// <remarks>
    /// Keep calling and the block lasts longer: rejected calls count too. The budget is
    /// <c>200 × daily active users</c>, which Meta does not disclose, so this one can only
    /// be handled after the fact.
    /// </remarks>
    public const int ApplicationRequestLimitReached = 4;

    /// <summary>The app is missing a permission the call needs. Never retried.</summary>
    public const int PermissionDenied = 10;

    /// <summary>A parameter was missing or malformed.</summary>
    /// <remarks>
    /// Meta's catch-all for a bad request, and it rarely says which parameter it objected
    /// to. This library checks the ones it can before sending, so a <c>100</c> that still
    /// gets through is worth reading the request body for.
    /// </remarks>
    public const int InvalidParameter = 100;

    /// <summary>The access token has expired, been revoked, or was never valid.</summary>
    /// <remarks>
    /// Never retried: the same token fails the same way. Refresh it in your
    /// <see cref="IWhatsAppCredentialsProvider"/>, which is where token lifetime belongs.
    /// </remarks>
    public const int InvalidAccessToken = 190;

    /// <summary>The token does not grant access to this WhatsApp Business Account.</summary>
    public const int PermissionError = 200;

    /// <summary>The app reached its hourly request limit for one WhatsApp Business Account.</summary>
    /// <remarks>200 requests an hour, or 5000 once the account has a registered phone number.</remarks>
    public const int BusinessAccountRateLimitReached = 80007;

    // Messaging.

    /// <summary>Something went wrong that Meta did not name. Often transient.</summary>
    public const int GenericError = 131000;

    /// <summary>Service temporarily unavailable. Retry with backoff.</summary>
    public const int ServiceUnavailable = 131016;

    /// <summary>The sender and the recipient are the same number.</summary>
    public const int SenderAndRecipientMatch = 131021;

    /// <summary>
    /// The message could not be delivered — the number is not on WhatsApp, or the handset
    /// has been unreachable for too long.
    /// </summary>
    /// <remarks>Retrying does not help; nothing about the recipient changes on a retry.</remarks>
    public const int MessageUndeliverable = 131026;

    /// <summary>The business account is locked, usually pending a policy review.</summary>
    public const int AccountLocked = 131031;

    /// <summary>Cloud API message throughput for the business phone number is exhausted.</summary>
    /// <remarks>
    /// 80 messages a second by default, 1000 after an automatic upgrade. The message never
    /// entered the pipeline, so it has to be sent again.
    /// </remarks>
    public const int MessageThroughputReached = 130429;

    /// <summary>The business is not eligible to send, usually a billing or verification problem.</summary>
    public const int BusinessEligibilityPaymentIssue = 131042;

    /// <summary>The certificate presented at registration did not match the number.</summary>
    public const int RegistrationCertificateMismatch = 131045;

    /// <summary>
    /// The 24-hour customer service window has closed, so only a template may be sent.
    /// </summary>
    /// <remarks>
    /// Never retried: the window does not reopen on its own. Send an approved template, or
    /// wait for the customer to write first.
    /// </remarks>
    public const int ReEngagementRequired = 131047;

    /// <summary>Sending is restricted because earlier messages were flagged as spam.</summary>
    /// <remarks>Retrying does not help. The template or the message quality has to change.</remarks>
    public const int SpamRateLimitReached = 131048;

    /// <summary>Held back to protect ecosystem engagement: the per-user marketing limit.</summary>
    /// <remarks>Wait at least 24 hours. Retrying sooner just burns delivery metrics.</remarks>
    public const int PerUserMarketingLimitReached = 131049;

    /// <summary>The user opted out of marketing messages. Never retry.</summary>
    public const int UserOptedOut = 131050;

    /// <summary>The message type is not supported for this recipient or this account.</summary>
    public const int UnsupportedMessageType = 131051;

    /// <summary>Meta could not download the media from the link that was given.</summary>
    public const int MediaDownloadFailed = 131052;

    /// <summary>Meta could not accept the uploaded media.</summary>
    public const int MediaUploadFailed = 131053;

    /// <summary>Too many messages from this sender to this one recipient.</summary>
    /// <remarks>
    /// One message per six seconds per recipient, with a burst allowance of 45 that is
    /// borrowed from the following minutes. Other recipients are unaffected, so a queue
    /// must not stall on this.
    /// </remarks>
    public const int PairRateLimitReached = 131056;

    /// <summary>The account is in maintenance mode, including during a throughput upgrade.</summary>
    public const int MaintenanceMode = 131057;

    // Templates.

    /// <summary>The number of parameters sent does not match the number the template declares.</summary>
    public const int TemplateParameterCountMismatch = 132000;

    /// <summary>No approved template with that name and language exists.</summary>
    public const int TemplateDoesNotExist = 132001;

    /// <summary>The template filled in with these values is longer than WhatsApp allows.</summary>
    public const int TemplateTextTooLong = 132005;

    /// <summary>A parameter broke the template's formatting rules, such as a newline or a tab.</summary>
    public const int TemplateFormatCharacterPolicyViolated = 132007;

    /// <summary>A parameter did not match the format the template declares for it.</summary>
    public const int TemplateParameterFormatMismatch = 132012;

    /// <summary>The template is paused after repeated negative feedback and cannot be sent.</summary>
    public const int TemplatePaused = 132015;

    /// <summary>The template is disabled for good and cannot be sent.</summary>
    public const int TemplateDisabled = 132016;

    /// <summary>The Flow is blocked because its endpoint is unhealthy.</summary>
    public const int FlowBlocked = 132068;

    /// <summary>The Flow is throttled because its endpoint is struggling: ten sends an hour.</summary>
    public const int FlowThrottled = 132069;

    // Registration.

    /// <summary>Server temporarily unavailable. Retry with backoff.</summary>
    public const int ServerUnavailable = 133004;

    /// <summary>The two-step verification PIN was wrong.</summary>
    public const int TwoStepPinMismatch = 133005;

    /// <summary>The number has to be verified with a code before it can be registered.</summary>
    public const int PhoneNumberNotVerified = 133006;

    /// <summary>Too many wrong PIN guesses. The number is locked out for a while.</summary>
    public const int TooManyPinGuesses = 133008;

    /// <summary>PIN guesses arrived too quickly. Wait before trying again.</summary>
    public const int PinGuessedTooFast = 133009;

    /// <summary>The number is not registered on the Cloud API.</summary>
    public const int PhoneNumberNotRegistered = 133010;

    /// <summary>Registration or deregistration attempted too many times for this number.</summary>
    /// <remarks>
    /// Ten attempts per 72 hours, after which the number is blocked for the rest of the
    /// window. Never retry this one.
    /// </remarks>
    public const int RegistrationLimitReached = 133016;
}
