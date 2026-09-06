using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Time.Testing;
using Wapper.RateLimiting;

namespace Wapper.Benchmarks;

/// <summary>
/// The in-memory limiter on the paths a send takes: granted at once, spread over many
/// scopes, queued behind a penalty and cancelled.
/// </summary>
/// <remarks>
/// The clock is a fake one wound forward by the benchmark itself, so what is measured is
/// the limiter's own work — reserving, re-pricing and claiming — and never a real wait.
/// Pacing correctness is the point of the limiter and is covered by its tests; this says
/// what that correctness costs per call.
/// </remarks>
[MemoryDiagnoser]
[ShortRunJob]
public class RateLimiterBenchmarks
{
    private static readonly TimeSpan Forever = TimeSpan.FromDays(1);

    private FakeTimeProvider _time = null!;
    private InMemoryRateLimiter _limiter = null!;
    private RateLimitRequest[][] _budgets = [];
    private int _next;

    /// <summary>Distinct recipients, and so distinct pair scopes, the calls rotate over.</summary>
    [Params(1, 1000)]
    public int Scopes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _time = new FakeTimeProvider();
        _limiter = new InMemoryRateLimiter(_time);
        // The allowances are generous on purpose: with the real pair limit of one message
        // every six seconds a hundred calls to one recipient would queue on a clock nobody
        // winds, and what is being measured is the limiter's bookkeeping, not the wait.
        _budgets = Enumerable.Range(0, Scopes).Select(i => new[]
        {
            RateLimitRequest.Unpaced(RateLimitScope.ApplicationRequests("app")),
            new RateLimitRequest(RateLimitScope.PhoneNumberThroughput("111"), 1000, 1000),
            new RateLimitRequest(RateLimitScope.RecipientPair("111", $"7900000{i:0000}"), 1000, 1000),
        }).ToArray();
    }

    [IterationSetup]
    public void Refill() => _time.Advance(TimeSpan.FromSeconds(10));

    /// <summary>One send with its three budgets, granted without waiting.</summary>
    [Benchmark(Baseline = true, OperationsPerInvoke = 100)]
    public async Task GrantedAtOnce()
    {
        for (var i = 0; i < 100; i++)
        {
            await _limiter.WaitAsync(_budgets[_next++ % Scopes], Forever);
        }
    }

    /// <summary>
    /// One hundred sends queued behind a two-second hold, then released together by the
    /// clock: the reserve, the re-pricing on wake-up and the claim.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 100)]
    public async Task QueuedBehindAPenalty()
    {
        await _limiter.PenaliseAsync(RateLimitScope.PhoneNumberThroughput("111"), TimeSpan.FromSeconds(2));

        var waits = new Task[100];
        for (var i = 0; i < waits.Length; i++)
        {
            waits[i] = _limiter.WaitAsync(_budgets[_next++ % Scopes], Forever).AsTask();
        }

        _time.Advance(TimeSpan.FromSeconds(3));
        await Task.WhenAll(waits);
    }

    /// <summary>A send that gives up while queued: reserve, then hand everything back.</summary>
    [Benchmark(OperationsPerInvoke = 100)]
    public async Task QueuedThenCancelled()
    {
        await _limiter.PenaliseAsync(RateLimitScope.PhoneNumberThroughput("111"), TimeSpan.FromSeconds(2));

        for (var i = 0; i < 100; i++)
        {
            using var cancellation = new CancellationTokenSource();
            var wait = _limiter.WaitAsync(_budgets[_next++ % Scopes], Forever, cancellation.Token).AsTask();
            cancellation.Cancel();

            try
            {
                await wait;
            }
            catch (OperationCanceledException)
            {
                // The point of the benchmark.
            }
        }

        _time.Advance(TimeSpan.FromSeconds(3));
    }
}
