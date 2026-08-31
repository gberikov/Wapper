using System.Net.Http.Json;
using Wapper.Internal;
using Wapper.Tests.Fakes;

namespace Wapper.Tests;

public class GraphApiClientTests
{
    private const string Tenant = WhatsAppTenant.Default;

    private static readonly WhatsAppCredentials Credentials = new()
    {
        AccessToken = "token-abc",
        PhoneNumberId = "106540352242922",
    };

    [Fact]
    public async Task Request_goes_to_the_versioned_path_with_a_bearer_token()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"error":{"code":0}}""");
        var client = CreateClient(handler);

        await client.SendAsync(
            Tenant,
            Credentials,
            HttpMethod.Post,
            $"{Credentials.PhoneNumberId}/messages",
            content: null,
            WhatsAppJsonContext.Default.GraphErrorEnvelope,
            TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://graph.facebook.com/v26.0/106540352242922/messages",
            request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("token-abc", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Documented_error_envelope_becomes_a_typed_exception()
    {
        // Taken verbatim from the Cloud API error code reference.
        const string Body = """
            {
              "error": {
                "message": "(#130429) Rate limit hit",
                "type": "OAuthException",
                "code": 130429,
                "error_data": {
                  "messaging_product": "whatsapp",
                  "details": "Cloud API message throughput has been reached."
                },
                "error_subcode": 2494055,
                "fbtrace_id": "Az8or2yhqkZfEZ-_4Qn_Bam"
              }
            }
            """;

        var client = CreateClient(StubHttpMessageHandler.Returning(HttpStatusCode.BadRequest, Body));

        var exception = await Assert.ThrowsAsync<WhatsAppApiException>(() => SendAsync(client));

        Assert.Equal(WhatsAppErrorCodes.MessageThroughputReached, exception.Code);
        Assert.Equal("OAuthException", exception.Error.Type);
        Assert.Equal("(#130429) Rate limit hit", exception.Error.Message);
        Assert.Equal("Cloud API message throughput has been reached.", exception.Error.Details);
        Assert.Equal("Az8or2yhqkZfEZ-_4Qn_Bam", exception.Error.TraceId);
        Assert.Equal(2494055, exception.Error.Subcode);
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task Transient_flag_survives_parsing()
    {
        const string Body = """
            {
              "error": {
                "message": "Application request limit reached",
                "type": "OAuthException",
                "code": 4,
                "is_transient": true,
                "fbtrace_id": "Ax0Q-eXaMpLe"
              }
            }
            """;

        var client = CreateClient(StubHttpMessageHandler.Returning(HttpStatusCode.TooManyRequests, Body));

        var exception = await Assert.ThrowsAsync<WhatsAppApiException>(() => SendAsync(client));

        Assert.Equal(WhatsAppErrorCodes.ApplicationRequestLimitReached, exception.Code);
        Assert.True(exception.Error.IsTransient);
    }

    [Theory]
    [InlineData("<html><body>502 Bad Gateway</body></html>", "text/html")]
    [InlineData("", "application/json")]
    [InlineData("{ this is not json", "application/json")]
    public async Task Unparseable_failure_body_still_produces_an_exception(string body, string mediaType)
    {
        // Gateways in front of the Graph API return HTML, and a dropped connection returns
        // nothing at all. Reporting the failure must not itself throw.
        var client = CreateClient(
            StubHttpMessageHandler.Returning(HttpStatusCode.BadGateway, body, mediaType));

        var exception = await Assert.ThrowsAsync<WhatsAppApiException>(() => SendAsync(client));

        Assert.Equal(0, exception.Code);
        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Contains("502", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_success_body_is_reported_rather_than_returned_as_null()
    {
        var client = CreateClient(StubHttpMessageHandler.Returning(HttpStatusCode.OK, "null"));

        var exception = await Assert.ThrowsAsync<WhatsAppException>(() => SendAsync(client));

        Assert.Contains("empty body", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_body_is_sent_as_json()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"error":{"code":0}}""");
        var client = CreateClient(handler);

        var content = JsonContent.Create(
            new GraphErrorEnvelope { Error = new GraphError { Code = 7 } },
            WhatsAppJsonContext.Default.GraphErrorEnvelope);

        await client.SendAsync(
            Tenant,
            Credentials,
            HttpMethod.Post,
            "whatever",
            content,
            WhatsAppJsonContext.Default.GraphErrorEnvelope,
            TestContext.Current.CancellationToken);

        Assert.Contains("\"code\":7", Assert.Single(handler.Bodies), StringComparison.Ordinal);
    }

    [Theory]
    // A base address without a trailing slash would otherwise lose its last segment, and a
    // leading slash on the relative part would drop the base path entirely.
    [InlineData("https://graph.facebook.com", "123/messages", "https://graph.facebook.com/v26.0/123/messages")]
    [InlineData("https://graph.facebook.com/", "/123/messages", "https://graph.facebook.com/v26.0/123/messages")]
    [InlineData("https://proxy.internal/graph/", "123/messages", "https://proxy.internal/graph/v26.0/123/messages")]
    [InlineData("https://proxy.internal/graph", "123/messages", "https://proxy.internal/graph/v26.0/123/messages")]
    public void Uri_is_built_from_the_base_address_and_the_api_version(
        string baseAddress,
        string path,
        string expected)
    {
        var options = new WhatsAppOptions { BaseAddress = new Uri(baseAddress) };

        Assert.Equal(expected, GraphApiClient.BuildUri(options, path).AbsoluteUri);
    }

    [Fact]
    public async Task Api_version_comes_from_the_options_of_the_tenant()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"error":{"code":0}}""");
        var client = CreateClient(handler, options => options.GraphApiVersion = "v23.0");

        await SendAsync(client);

        Assert.StartsWith(
            "https://graph.facebook.com/v23.0/",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri,
            StringComparison.Ordinal);
    }

    private static Task SendAsync(GraphApiClient client) => client.SendAsync(
        Tenant,
        Credentials,
        HttpMethod.Get,
        "whatever",
        content: null,
        WhatsAppJsonContext.Default.GraphErrorEnvelope,
        TestContext.Current.CancellationToken);

    private static GraphApiClient CreateClient(
        StubHttpMessageHandler handler,
        Action<WhatsAppOptions>? configure = null)
    {
        var options = new WhatsAppOptions();
        configure?.Invoke(options);

        return new GraphApiClient(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            new StubCredentialsProvider(Credentials),
            new StaticOptionsMonitor<WhatsAppOptions>(options));
    }
}
