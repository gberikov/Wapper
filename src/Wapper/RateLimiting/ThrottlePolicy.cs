namespace Wapper.RateLimiting;

/// <summary>
/// Decides what to do with a rejected call: retry it or not, how long to wait, and which
/// budget to hold back while waiting.
/// </summary>
internal static class ThrottlePolicy
{
    /// <summary>
    /// How long to wait before the next attempt.
    /// </summary>
    /// <param name="attempt">Which attempt has just failed, counting from zero.</param>
    /// <param name="retryAfter">
    /// What the response's <c>Retry-After</c> header said, when it carried one.
    /// </param>
    /// <remarks>
    /// Meta publishes exactly one backoff formula — <c>4^X</c> seconds, X starting at zero —
    /// given for the pair rate limit, and it is the sanest default for the others too. The
    /// Cloud API sends no <c>Retry-After</c>, so the header is read opportunistically for the
    /// sake of whatever sits in front of it, and only ever lengthens the wait: a proxy asking
    /// for one second does not override Meta's own documented sixteen.
    /// </remarks>
    public static TimeSpan Backoff(int attempt, TimeSpan? retryAfter = null)
    {
        var documented = TimeSpan.FromSeconds(Math.Pow(4, Math.Clamp(attempt, 0, 5)));

        return retryAfter is { } hinted && hinted > documented ? hinted : documented;
    }

    /// <summary>
    /// Whether the call is worth sending again, and which budget it exhausted.
    /// </summary>
    /// <param name="error">The error the Cloud API returned.</param>
    /// <param name="budget">
    /// The budget to hold back while backing off, or <see langword="null"/> when the failure
    /// was a transient server problem rather than a rate limit.
    /// </param>
    public static bool ShouldRetry(WhatsAppError error, out RateLimitBudget? budget)
    {
        switch (error.Code)
        {
            case WhatsAppErrorCodes.MessageThroughputReached:
                budget = RateLimitBudget.PhoneNumberThroughput;
                return true;

            // Only this conversation is affected. Holding back the phone number as well
            // would stall every other recipient for no reason.
            case WhatsAppErrorCodes.PairRateLimitReached:
                budget = RateLimitBudget.RecipientPair;
                return true;

            case WhatsAppErrorCodes.BusinessAccountRateLimitReached:
                budget = RateLimitBudget.BusinessAccountRequests;
                return true;

            // Meta is explicit that calls made while blocked are still counted and push the
            // recovery further out, so this one has to stop the application, not just slow it.
            case WhatsAppErrorCodes.ApplicationRequestLimitReached:
                budget = RateLimitBudget.ApplicationRequests;
                return true;

            case WhatsAppErrorCodes.TemporarilyUnavailable:
            case WhatsAppErrorCodes.ServiceUnavailable:
            case WhatsAppErrorCodes.ServerUnavailable:
            // Raised while a phone number is being upgraded to higher throughput, which Meta
            // documents as lasting up to a minute.
            case WhatsAppErrorCodes.MaintenanceMode:
                budget = null;
                return true;

            // Retrying these is worse than failing. Spam and template-classification limits
            // lift only when the content changes; the per-user marketing limit needs a day
            // and burns delivery metrics if hammered; a blocked registration is blocked for
            // 72 hours whatever the client does.
            case WhatsAppErrorCodes.SpamRateLimitReached:
            case WhatsAppErrorCodes.PerUserMarketingLimitReached:
            case WhatsAppErrorCodes.UserOptedOut:
            case WhatsAppErrorCodes.RegistrationLimitReached:

            // Nothing about the token, the permission or the request changes on a retry, and
            // Meta marks some of these transient anyway — which is why they are named here
            // rather than left to is_transient below.
            case WhatsAppErrorCodes.PermissionDenied:
            case WhatsAppErrorCodes.InvalidParameter:
            case WhatsAppErrorCodes.InvalidAccessToken:
            case WhatsAppErrorCodes.PermissionError:
            case WhatsAppErrorCodes.SenderAndRecipientMatch:
            case WhatsAppErrorCodes.MessageUndeliverable:
            case WhatsAppErrorCodes.AccountLocked:
            case WhatsAppErrorCodes.BusinessEligibilityPaymentIssue:
            case WhatsAppErrorCodes.RegistrationCertificateMismatch:

            // The 24-hour window does not reopen on a retry. Only a template will get through.
            case WhatsAppErrorCodes.ReEngagementRequired:
            case WhatsAppErrorCodes.UnsupportedMessageType:

            // A template that does not fit its parameters will not fit them a second later,
            // and a paused or disabled one needs a human.
            case WhatsAppErrorCodes.TemplateParameterCountMismatch:
            case WhatsAppErrorCodes.TemplateDoesNotExist:
            case WhatsAppErrorCodes.TemplateTextTooLong:
            case WhatsAppErrorCodes.TemplateFormatCharacterPolicyViolated:
            case WhatsAppErrorCodes.TemplateParameterFormatMismatch:
            case WhatsAppErrorCodes.TemplatePaused:
            case WhatsAppErrorCodes.TemplateDisabled:

            // A Flow is blocked or throttled by Meta's monitoring for at least an hour, and
            // the retries here are spread over seconds.
            case WhatsAppErrorCodes.FlowBlocked:
            case WhatsAppErrorCodes.FlowThrottled:

            // Every one of these means the PIN, the code or the number is wrong, and three
            // of them get stricter the more they are tried.
            case WhatsAppErrorCodes.TwoStepPinMismatch:
            case WhatsAppErrorCodes.PhoneNumberNotVerified:
            case WhatsAppErrorCodes.TooManyPinGuesses:
            case WhatsAppErrorCodes.PinGuessedTooFast:
            case WhatsAppErrorCodes.PhoneNumberNotRegistered:
                budget = null;
                return false;

            default:
                budget = null;

                // Meta sets is_transient on the failures it considers worth repeating. Trust
                // it for anything not named above.
                return error.IsTransient;
        }
    }
}
