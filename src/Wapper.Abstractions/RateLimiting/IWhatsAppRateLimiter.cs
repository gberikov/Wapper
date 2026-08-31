namespace Wapper.RateLimiting;

/// <summary>
/// Paces outgoing calls so the Cloud API is not asked for more than it allows.
/// </summary>
/// <remarks>
/// <para>
/// The bundled implementation keeps its counters in memory, which is correct for a single
/// process. Meta counts per phone number on its side, so several instances of the same
/// application each pacing themselves locally will together exceed the limit. Register the
/// Redis-backed limiter to share the counters.
/// </para>
/// <para>
/// Implementations must be safe for concurrent use.
/// </para>
/// </remarks>
public interface IWhatsAppRateLimiter
{
    /// <summary>
    /// Waits until the call may proceed under every one of <paramref name="requests"/>.
    /// </summary>
    /// <param name="requests">The budgets this call spends, and their allowances.</param>
    /// <param name="maxWait">
    /// How long the caller is willing to wait. Exceeding it raises rather than blocking
    /// forever, so a request thread cannot be parked indefinitely.
    /// </param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <exception cref="WhatsAppRateLimitedException">
    /// The call would have to wait longer than <paramref name="maxWait"/>.
    /// </exception>
    ValueTask WaitAsync(
        IReadOnlyList<RateLimitRequest> requests,
        TimeSpan maxWait,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Holds back a scope for a while, after the Cloud API said the budget is spent.
    /// </summary>
    /// <remarks>
    /// This is the half that keeps the client honest when its own estimate was wrong: the
    /// configured throughput may be stale, or another process may share the same phone
    /// number. Meta is explicit that continuing to call while limited lengthens the block,
    /// because rejected calls are counted too.
    /// </remarks>
    /// <param name="scope">The budget to hold back.</param>
    /// <param name="duration">How long to hold it back.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    ValueTask PenaliseAsync(
        RateLimitScope scope,
        TimeSpan duration,
        CancellationToken cancellationToken = default);
}
