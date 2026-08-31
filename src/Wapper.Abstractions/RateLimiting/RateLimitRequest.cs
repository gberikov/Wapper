namespace Wapper.RateLimiting;

/// <summary>
/// One budget a call has to fit inside, together with the allowance that applies to it.
/// </summary>
/// <param name="Scope">Which budget, and what it is keyed on.</param>
/// <param name="PermitsPerSecond">
/// Sustained rate. <see cref="double.PositiveInfinity"/> for a budget that cannot be paced
/// ahead of time and is only ever held back after the Cloud API rejects a call.
/// </param>
/// <param name="Burst">
/// How many permits may be spent at once before the sustained rate takes over. Meta allows
/// a burst on the pair limit and borrows it back from the following minutes.
/// </param>
/// <remarks>
/// The allowance travels with the request rather than living inside the limiter, because it
/// is configured per tenant while the limiter is shared by all of them.
/// </remarks>
public readonly record struct RateLimitRequest(
    RateLimitScope Scope,
    double PermitsPerSecond,
    double Burst)
{
    /// <summary>
    /// A budget with no rate of its own, which therefore only ever waits while it is being
    /// held back after a rejection.
    /// </summary>
    public static RateLimitRequest Unpaced(RateLimitScope scope) =>
        new(scope, double.PositiveInfinity, double.PositiveInfinity);
}
