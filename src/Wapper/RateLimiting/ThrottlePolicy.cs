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
    /// <remarks>
    /// What each code means lives in <see cref="WhatsAppErrorExtensions.Classify"/>, which
    /// callers read too. Deliberately not a second table beside it: two tables of Meta's
    /// error codes would drift apart, and drift silently.
    /// </remarks>
    public static bool ShouldRetry(WhatsAppError error, out RateLimitBudget? budget)
    {
        var failure = error.Classify();

        budget = failure.Budget;

        return failure.CanRetry;
    }
}
