namespace Wapper.RateLimiting;

/// <summary>
/// How hard the client paces itself. The defaults are the allowances Meta documents for a
/// new business phone number, so an application that changes nothing is already correct.
/// </summary>
public sealed class WhatsAppRateLimitOptions
{
    /// <summary>Whether the client paces and retries at all. On by default.</summary>
    /// <remarks>
    /// Turning this off means every rejection reaches the caller as an exception. Only
    /// sensible when something in front of the client already does the pacing.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Messages a second for one business phone number. Meta allows 80 by default and
    /// raises it to 1000 automatically once the number qualifies.
    /// </summary>
    /// <remarks>
    /// Read the current value from <c>GET /{phone-number-id}?fields=throughput</c>. A number
    /// upgraded to 1000 left at 80 here simply sends slower than it could.
    /// </remarks>
    public int MessagesPerSecond { get; set; } = 80;

    /// <summary>
    /// The shortest gap between two messages to the same recipient. Meta allows one every
    /// six seconds.
    /// </summary>
    public TimeSpan PairInterval { get; set; } = TimeSpan.FromSeconds(6);

    /// <summary>
    /// How many messages may go to one recipient back to back before the interval applies.
    /// Meta allows a burst of 45 and borrows it back from the following minutes.
    /// </summary>
    public int PairBurst { get; set; } = 45;

    /// <summary>
    /// Management requests an hour for one WhatsApp Business Account. Meta allows 200, and
    /// 5000 once the account has a registered phone number.
    /// </summary>
    public int BusinessAccountRequestsPerHour { get; set; } = 200;

    /// <summary>
    /// The longest a call will wait for a permit before giving up with
    /// <see cref="WhatsAppRateLimitedException"/>.
    /// </summary>
    /// <remarks>
    /// Waiting is asynchronous and never blocks a thread, but a request handler that waits
    /// minutes is its own kind of outage, so there is a ceiling.
    /// </remarks>
    public TimeSpan MaxWait { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many times a rejected call is retried before the exception reaches the caller.
    /// </summary>
    /// <remarks>
    /// Retries are spaced by Meta's documented <c>4^X</c> seconds, so four retries spread
    /// over roughly 85 seconds.
    /// </remarks>
    public int MaxRetries { get; set; } = 4;

    /// <summary>
    /// The usage percentage, reported in the <c>X-App-Usage</c> and
    /// <c>X-Business-Use-Case-Usage</c> headers, at which the client starts holding calls
    /// back on its own.
    /// </summary>
    /// <remarks>
    /// Meta throttles at 100. Lowering this to, say, 90 buys margin at the cost of sending
    /// more slowly near the limit.
    /// </remarks>
    public int UsagePercentThreshold { get; set; } = 100;
}
