using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Wapper.Internal;
using Wapper.RateLimiting;
using Wapper.Tests.Fakes;

namespace Wapper.Tests;

/// <summary>
/// What goes out on the wire and how the ways it can fail are told apart: the HTTP version
/// the client negotiated, and the timeout that has to look the same whether it fired before
/// the headers or while the body was still arriving.
/// </summary>
public class TransportTests
{
    private static readonly WhatsAppCredentials Credentials = new()
    {
        AccessToken = "token-abc",
        PhoneNumberId = "106540352242922",
    };

    [Fact]
    public async Task A_request_carries_the_version_the_client_was_configured_for()
    {
        // HttpClient.SendAsync(HttpRequestMessage) ignores DefaultRequestVersion; only the
        // GetAsync family applies it. Left alone, every call goes out as HTTP/1.1 however the
        // client was set up, and the multiplexing the registration asks for never happens.
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"error":{"code":0}}""");
        var client = CreateClient(handler, http =>
        {
            http.DefaultRequestVersion = HttpVersion.Version20;
            http.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        });

        await SendAsync(client);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpVersion.Version20, request.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, request.VersionPolicy);
    }

    [Fact]
    public async Task A_retry_carries_the_version_too()
    {
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.InternalServerError,
             """{"error":{"code":131000,"message":"Something went wrong","is_transient":true}}"""),
            (HttpStatusCode.OK, """{"error":{"code":0}}"""));
        var time = new FakeTimeProvider();
        var client = CreateClient(
            handler,
            http => http.DefaultRequestVersion = HttpVersion.Version20,
            time);

        await Clock.RunAsync(time, SendAsync(client));

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpVersion.Version20, request.Version));
    }

    [Fact]
    public async Task A_download_carries_the_version_and_the_consumer_s_policy()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "bytes", "image/png");
        var client = CreateClient(handler, http =>
        {
            http.DefaultRequestVersion = HttpVersion.Version11;
            http.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        });

        using var response = await client.FetchAsync(
            NewRequest(),
            new Uri("https://lookaside.fbsbx.com/whatsapp_business/attachments/?mid=1"),
            TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpVersion.Version11, request.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionExact, request.VersionPolicy);
    }

    [Fact]
    public async Task A_body_that_never_finishes_arriving_is_reported_as_a_timeout()
    {
        // The headers said 200 and the body stalled. That is the same unreachable Cloud API
        // as a stall before the headers, and it must not surface as the caller's own
        // cancellation — a request handler would log a client disconnect for it.
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream()),
        });
        var client = CreateClient(
            handler,
            configure: options => options.Timeout = TimeSpan.FromMilliseconds(80));

        var exception = await Assert.ThrowsAsync<WhatsAppException>(() => SendAsync(client));

        Assert.IsNotType<TaskCanceledException>(exception);
        Assert.Contains("timeout", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("read", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
    }

    [Fact]
    public async Task A_caller_that_gives_up_while_the_body_arrives_still_sees_its_own_cancellation()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream()),
        });
        var client = CreateClient(handler);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(
            NewRequest(),
            WhatsAppJsonContext.Default.GraphErrorEnvelope,
            cancellation.Token));
    }

    [Fact]
    public async Task A_body_cut_off_by_the_connection_is_reported_and_not_retried()
    {
        var attempts = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new BrokenStream()),
            };
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<WhatsAppException>(() => SendAsync(client));

        Assert.IsType<IOException>(exception.InnerException);
        Assert.Contains("could not be read", exception.Message, StringComparison.Ordinal);
        // The call may have taken effect. Sending a message again on that evidence could
        // deliver it twice.
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task A_body_that_is_not_the_documented_response_is_told_apart_from_a_transport_failure()
    {
        var client = CreateClient(StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"error": "not-an-object"}"""));

        var exception = await Assert.ThrowsAsync<WhatsAppException>(() => SendAsync(client));

        Assert.IsType<JsonException>(exception.InnerException);
        Assert.Contains("could not read", exception.Message, StringComparison.Ordinal);
    }

    private static GraphRequest NewRequest() => new()
    {
        Tenant = WhatsAppTenant.Default,
        Credentials = Credentials,
        Method = HttpMethod.Get,
        Path = $"{Credentials.PhoneNumberId}/messages",
        Kind = GraphCallKind.Message,
        Recipient = "79000000001",
    };

    private static Task SendAsync(GraphApiClient client) =>
        client.SendAsync(
            NewRequest(),
            WhatsAppJsonContext.Default.GraphErrorEnvelope,
            TestContext.Current.CancellationToken);

    private static GraphApiClient CreateClient(
        HttpMessageHandler handler,
        Action<HttpClient>? configureHttp = null,
        FakeTimeProvider? time = null,
        Action<WhatsAppOptions>? configure = null)
    {
        var options = new WhatsAppOptions();
        configure?.Invoke(options);

        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        configureHttp?.Invoke(http);

        time ??= new FakeTimeProvider();

        return new GraphApiClient(
            http,
            new StubCredentialsProvider(Credentials),
            new InMemoryRateLimiter(time),
            new StaticOptionsMonitor<WhatsAppOptions>(options),
            time);
    }
}
