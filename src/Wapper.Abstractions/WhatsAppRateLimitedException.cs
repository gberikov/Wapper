using Wapper.RateLimiting;

namespace Wapper;

/// <summary>
/// A call was held back by a rate limit — either by this client before it was sent, or by
/// the Cloud API after every retry was used up.
/// </summary>
public sealed class WhatsAppRateLimitedException : WhatsAppException
{
    /// <summary>Raised by the client, before the call went anywhere.</summary>
    public WhatsAppRateLimitedException(RateLimitScope scope, TimeSpan retryAfter, TimeSpan maxWait)
        : base($"Sending would exceed the {scope.Budget} rate limit for '{scope.RedactedKey}'. The " +
               $"call would have to wait {retryAfter.TotalSeconds:0.##}s, which is longer than the " +
               $"configured maximum of {maxWait.TotalSeconds:0.##}s.")
    {
        Scope = scope;
        RetryAfter = retryAfter;
    }

    /// <summary>Raised after the Cloud API reported the limit and the retries ran out.</summary>
    public WhatsAppRateLimitedException(
        RateLimitScope scope,
        TimeSpan retryAfter,
        WhatsAppApiException apiException)
        : base($"The Cloud API rejected the call against the {scope.Budget} rate limit for " +
               $"'{scope.RedactedKey}' and it did not succeed within the configured number of " +
               $"retries. Try again in about {retryAfter.TotalSeconds:0.##}s. {apiException.Message}",
               apiException)
    {
        Scope = scope;
        RetryAfter = retryAfter;
        Error = apiException.Error;
    }

    /// <summary>Which budget held the call back.</summary>
    public RateLimitScope Scope { get; }

    /// <summary>
    /// How long to wait before trying again. An estimate: the Cloud API does not send a
    /// <c>Retry-After</c> header, so this comes from the
    /// <c>X-Business-Use-Case-Usage</c> header when it is present, and from Meta's
    /// documented <c>4^X</c> second backoff otherwise.
    /// </summary>
    public TimeSpan RetryAfter { get; }

    /// <summary>
    /// The error the Cloud API returned, or <see langword="null"/> when this client held
    /// the call back before sending it.
    /// </summary>
    public WhatsAppError? Error { get; }
}
