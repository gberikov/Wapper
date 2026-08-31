using Wapper.RateLimiting;

namespace Wapper.Tests.Fakes;

/// <summary>Records which budgets were asked for, then defers to a real limiter.</summary>
internal sealed class RecordingRateLimiter(IWhatsAppRateLimiter inner) : IWhatsAppRateLimiter
{
    public List<RateLimitRequest> Requested { get; } = [];

    public List<(RateLimitScope Scope, TimeSpan Duration)> Penalties { get; } = [];

    public ValueTask WaitAsync(
        IReadOnlyList<RateLimitRequest> requests,
        TimeSpan maxWait,
        CancellationToken cancellationToken = default)
    {
        Requested.AddRange(requests);
        return inner.WaitAsync(requests, maxWait, cancellationToken);
    }

    public ValueTask PenaliseAsync(
        RateLimitScope scope,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        Penalties.Add((scope, duration));
        return inner.PenaliseAsync(scope, duration, cancellationToken);
    }
}
