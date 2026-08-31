using System.Net.Http.Json;
using Microsoft.Extensions.Time.Testing;
using Wapper.Internal;
using Wapper.RateLimiting;
using Wapper.Tests.Fakes;

namespace Wapper.Tests.RateLimiting;

/// <summary>
/// The retry loop end to end: a rejection from the Cloud API, a backoff on the fake clock,
/// and another attempt.
/// </summary>
public class RetryTests
{
    private const string Ok = """{"error":{"code":0}}""";

    private static readonly WhatsAppCredentials Credentials = new()
    {
        AccessToken = "token-abc",
        PhoneNumberId = "106540352242922",
        WhatsAppBusinessAccountId = "waba-1",
    };

    private static string Rejection(int code) =>
        $$$"""{"error":{"message":"rejected","type":"OAuthException","code":{{{code}}}}}""";

    [Fact]
    public async Task A_throughput_rejection_is_retried_and_then_succeeds()
    {
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.BadRequest, Rejection(WhatsAppErrorCodes.MessageThroughputReached)),
            (HttpStatusCode.OK, Ok));
        var time = new FakeTimeProvider();
        var client = CreateClient(handler, time);
        var started = time.GetUtcNow();

        await Clock.RunAsync(time, SendAsync(client));

        Assert.Equal(2, handler.Requests.Count);
        // The first backoff Meta documents is 4^0 seconds.
        Assert.True(time.GetUtcNow() - started >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Retries_run_out_and_the_caller_is_told_which_budget_it_was()
    {
        var handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.BadRequest,
            Rejection(WhatsAppErrorCodes.PairRateLimitReached));
        var time = new FakeTimeProvider();
        var client = CreateClient(handler, time, options => options.RateLimits.MaxRetries = 2);

        var exception = await Assert.ThrowsAsync<WhatsAppRateLimitedException>(
            () => Clock.RunAsync(time, SendAsync(client)));

        // The first attempt plus two retries.
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(RateLimitBudget.RecipientPair, exception.Scope.Budget);
        Assert.Equal(WhatsAppErrorCodes.PairRateLimitReached, exception.Error!.Code);
        Assert.True(exception.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public async Task Every_configured_retry_is_actually_spent()
    {
        // The defaults are four retries spread over Meta's documented 1, 4, 16 and 64
        // seconds. The last of those is longer than the default 30-second MaxWait, and MaxWait
        // is what the limiter refuses on — so a client that applied it to its own backoff
        // would quietly stop after three, and report the wait rather than the rejection.
        var handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.BadRequest,
            Rejection(WhatsAppErrorCodes.MessageThroughputReached));
        var time = new FakeTimeProvider();
        var client = CreateClient(handler, time);

        var exception = await Assert.ThrowsAsync<WhatsAppRateLimitedException>(
            () => Clock.RunAsync(time, SendAsync(client)));

        // The first attempt plus the four retries the options ask for.
        Assert.Equal(5, handler.Requests.Count);

        // And the caller is told what the Cloud API actually said, not what this client
        // decided about its own patience.
        Assert.Equal(WhatsAppErrorCodes.MessageThroughputReached, exception.Error!.Code);
        Assert.IsType<WhatsAppApiException>(exception.InnerException);
    }

    [Fact]
    public async Task A_timeout_is_reported_as_a_timeout_rather_than_as_a_cancellation()
    {
        // The per-tenant timeout is enforced with a token, so it surfaces as a cancellation
        // that looks exactly like the caller's own. A request handler would log a client
        // disconnect for what is really an unreachable Cloud API.
        var time = new FakeTimeProvider();
        var client = new GraphApiClient(
            new HttpClient(new HangingHttpMessageHandler()) { Timeout = Timeout.InfiniteTimeSpan },
            new StubCredentialsProvider(Credentials),
            new InMemoryRateLimiter(time),
            new StaticOptionsMonitor<WhatsAppOptions>(
                new WhatsAppOptions { Timeout = TimeSpan.FromMilliseconds(50) }),
            time);

        var exception = await Assert.ThrowsAsync<WhatsAppException>(() => SendAsync(client));

        Assert.IsNotType<TaskCanceledException>(exception);
        Assert.Contains("timeout", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_caller_s_own_cancellation_is_still_a_cancellation()
    {
        var time = new FakeTimeProvider();
        var client = new GraphApiClient(
            new HttpClient(new HangingHttpMessageHandler()) { Timeout = Timeout.InfiniteTimeSpan },
            new StubCredentialsProvider(Credentials),
            new InMemoryRateLimiter(time),
            new StaticOptionsMonitor<WhatsAppOptions>(new WhatsAppOptions()),
            time);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(
            NewRequest() with { Kind = GraphCallKind.Message, Recipient = "79000000001" },
            WhatsAppJsonContext.Default.GraphErrorEnvelope,
            cancellation.Token));
    }

    [Fact]
    public async Task A_rejection_retrying_cannot_clear_is_not_retried()
    {
        var handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.BadRequest,
            Rejection(WhatsAppErrorCodes.UserOptedOut));
        var time = new FakeTimeProvider();
        var client = CreateClient(handler, time);

        var exception = await Assert.ThrowsAsync<WhatsAppApiException>(
            () => Clock.RunAsync(time, SendAsync(client)));

        Assert.Single(handler.Requests);
        Assert.Equal(WhatsAppErrorCodes.UserOptedOut, exception.Code);
        Assert.IsNotType<WhatsAppRateLimitedException>(exception);
    }

    [Fact]
    public async Task Every_attempt_gets_a_freshly_built_body()
    {
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.BadRequest, Rejection(WhatsAppErrorCodes.MessageThroughputReached)),
            (HttpStatusCode.OK, Ok));
        var time = new FakeTimeProvider();
        var client = CreateClient(handler, time);

        await Clock.RunAsync(time, client.SendAsync(
            NewRequest() with
            {
                Method = HttpMethod.Post,
                Content = () => JsonContent.Create(
                    new GraphErrorEnvelope { Error = new GraphError { Code = 7 } },
                    WhatsAppJsonContext.Default.GraphErrorEnvelope),
            },
            WhatsAppJsonContext.Default.GraphErrorEnvelope,
            TestContext.Current.CancellationToken));

        // The body of the first attempt has already gone to the wire and cannot be rewound,
        // so a retry that reused it would send nothing at all.
        Assert.Equal(2, handler.Bodies.Count);
        Assert.All(handler.Bodies, body =>
            Assert.Contains("\"code\":7", body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_usage_header_over_the_threshold_holds_the_next_call_back()
    {
        var handler = StubHttpMessageHandler.ReturningWithHeader(
            HttpStatusCode.OK,
            Ok,
            GraphUsageHeaders.AppUsageHeader,
            """{"call_count":100,"total_time":25,"total_cputime":25}""");
        var time = new FakeTimeProvider();
        // Long enough to sit out the hold rather than be refused on the spot.
        var client = CreateClient(
            handler,
            time,
            options => options.RateLimits.MaxWait = TimeSpan.FromMinutes(5));

        await SendAsync(client);

        var started = time.GetUtcNow();
        await Clock.RunAsync(time, SendAsync(client));

        // Meta throttles at 100 percent. Seeing it on a successful response is the only
        // chance to slow down before the wall rather than after it.
        Assert.True(time.GetUtcNow() - started >= TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task A_hold_longer_than_the_caller_will_wait_fails_fast_instead_of_parking_it()
    {
        var handler = StubHttpMessageHandler.ReturningWithHeader(
            HttpStatusCode.OK,
            Ok,
            GraphUsageHeaders.AppUsageHeader,
            """{"call_count":100,"total_time":25,"total_cputime":25}""");
        var time = new FakeTimeProvider();
        var client = CreateClient(
            handler,
            time,
            options => options.RateLimits.MaxWait = TimeSpan.FromSeconds(30));

        await SendAsync(client);

        // The hold is a minute and the caller will wait thirty seconds. Parking a request
        // handler past its own patience is worse than telling it now.
        var exception = await Assert.ThrowsAsync<WhatsAppRateLimitedException>(
            () => Clock.RunAsync(time, SendAsync(client)));

        Assert.Equal(RateLimitBudget.ApplicationRequests, exception.Scope.Budget);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_usage_header_below_the_threshold_changes_nothing()
    {
        var handler = StubHttpMessageHandler.ReturningWithHeader(
            HttpStatusCode.OK,
            Ok,
            GraphUsageHeaders.AppUsageHeader,
            """{"call_count":28,"total_time":25,"total_cputime":25}""");
        var time = new FakeTimeProvider();
        var client = CreateClient(handler, time);

        await SendAsync(client);

        var started = time.GetUtcNow();
        await SendAsync(client);

        Assert.Equal(started, time.GetUtcNow());
    }

    [Fact]
    public async Task Turning_the_limiter_off_leaves_every_rejection_to_the_caller()
    {
        var handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.BadRequest,
            Rejection(WhatsAppErrorCodes.MessageThroughputReached));
        var time = new FakeTimeProvider();
        var client = CreateClient(handler, time, options => options.RateLimits.Enabled = false);

        await Assert.ThrowsAsync<WhatsAppApiException>(() => SendAsync(client));

        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(GraphCallKind.Message, RateLimitBudget.PhoneNumberThroughput, true)]
    [InlineData(GraphCallKind.Message, RateLimitBudget.RecipientPair, true)]
    [InlineData(GraphCallKind.Message, RateLimitBudget.BusinessAccountRequests, false)]
    [InlineData(GraphCallKind.Management, RateLimitBudget.BusinessAccountRequests, true)]
    [InlineData(GraphCallKind.Management, RateLimitBudget.PhoneNumberThroughput, false)]
    [InlineData(GraphCallKind.Other, RateLimitBudget.PhoneNumberThroughput, false)]
    internal void A_call_spends_only_the_budgets_that_apply_to_it(
        GraphCallKind kind,
        RateLimitBudget budget,
        bool expected)
    {
        var budgets = GraphApiClient.BuildBudgets(
            NewRequest() with { Kind = kind, Recipient = "79000000001" },
            new WhatsAppRateLimitOptions());

        Assert.Equal(expected, budgets.Any(b => b.Scope.Budget == budget));
    }

    [Fact]
    public void Every_call_carries_the_application_budget()
    {
        // It is never paced, but it is the only way a platform-level block can stop the
        // tenant instead of only the call that discovered it.
        foreach (var kind in Enum.GetValues<GraphCallKind>())
        {
            var budgets = GraphApiClient.BuildBudgets(
                NewRequest() with { Kind = kind },
                new WhatsAppRateLimitOptions());

            Assert.Contains(budgets, b => b.Scope.Budget == RateLimitBudget.ApplicationRequests);
        }
    }

    [Fact]
    public void A_message_without_a_known_recipient_skips_the_pair_budget()
    {
        var budgets = GraphApiClient.BuildBudgets(
            NewRequest() with { Kind = GraphCallKind.Message, Recipient = null },
            new WhatsAppRateLimitOptions());

        Assert.DoesNotContain(budgets, b => b.Scope.Budget == RateLimitBudget.RecipientPair);
    }

    [Fact]
    public void The_business_account_budget_falls_back_to_the_phone_number()
    {
        // A tenant that only ever sends messages has no reason to configure the account id,
        // but a management call still has to be counted against something stable.
        var budgets = GraphApiClient.BuildBudgets(
            NewRequest() with
            {
                Kind = GraphCallKind.Management,
                Credentials = Credentials with { WhatsAppBusinessAccountId = null },
            },
            new WhatsAppRateLimitOptions());

        Assert.Contains(
            budgets,
            b => b.Scope.Budget == RateLimitBudget.BusinessAccountRequests
                 && b.Scope.Key == Credentials.PhoneNumberId);
    }

    private static GraphRequest NewRequest() => new()
    {
        Tenant = WhatsAppTenant.Default,
        Credentials = Credentials,
        Method = HttpMethod.Get,
        Path = "whatever",
    };

    private static Task SendAsync(GraphApiClient client) => client.SendAsync(
        NewRequest() with { Kind = GraphCallKind.Message, Recipient = "79000000001" },
        WhatsAppJsonContext.Default.GraphErrorEnvelope,
        TestContext.Current.CancellationToken);

    private static GraphApiClient CreateClient(
        StubHttpMessageHandler handler,
        FakeTimeProvider time,
        Action<WhatsAppOptions>? configure = null)
    {
        var options = new WhatsAppOptions();
        configure?.Invoke(options);

        return new GraphApiClient(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            new StubCredentialsProvider(Credentials),
            new InMemoryRateLimiter(time),
            new StaticOptionsMonitor<WhatsAppOptions>(options),
            time);
    }
}
