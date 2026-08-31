namespace Wapper.RateLimiting;

/// <summary>
/// Decides what to do with a rejected call: retry it or not, how long to wait, and which
/// budget to hold back while waiting.
/// </summary>
internal static class ThrottlePolicy
{
    /// <summary>
    /// Meta publishes exactly one backoff formula — <c>4^X</c> seconds, X starting at zero —
    /// given for the pair rate limit. It is the sanest default for the others too.
    /// </summary>
    public static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Pow(4, Math.Clamp(attempt, 0, 5)));

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
