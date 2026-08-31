using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wapper.AspNetCore;
using Wapper.Webhooks;

namespace Wapper.Tests.Webhooks;

/// <summary>
/// The endpoint end to end, through a real ASP.NET Core pipeline: signature, parsing and
/// dispatch to handlers resolved from the container.
/// </summary>
public class WebhookEndpointTests : IAsyncLifetime
{
    private const string AppSecret = "an-app-secret";
    private const string VerifyToken = "a-verify-token";

    private const string Delivery = """
        {"object":"whatsapp_business_account","entry":[{"id":"W","changes":[{"field":"messages",
         "value":{"messaging_product":"whatsapp",
          "metadata":{"display_phone_number":"15550001111","phone_number_id":"106540352242922"},
          "messages":[{"from":"79000000001","id":"wamid.A","timestamp":"1755000000",
                       "type":"text","text":{"body":"hello"}}]}}]}]}
        """;

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private Recorder _recorder = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddWhatsApp(options =>
        {
            options.AccessToken = "token";
            options.PhoneNumberId = "106540352242922";
            options.AppSecret = AppSecret;
            options.WebhookVerifyToken = VerifyToken;
        });

        _recorder = new Recorder();
        builder.Services.AddSingleton(_recorder);
        builder.Services.AddWhatsAppWebhookHandler<TextHandler, TextMessage>(ServiceLifetime.Singleton);
        builder.Services.AddWhatsAppWebhookHandler<CatchAllHandler, WhatsAppEvent>(ServiceLifetime.Singleton);

        _app = builder.Build();
        _app.MapWhatsAppWebhook("/whatsapp");

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task The_subscription_handshake_echoes_the_challenge()
    {
        var response = await _client.GetAsync(
            $"/whatsapp?hub.mode=subscribe&hub.verify_token={VerifyToken}&hub.challenge=1158201444",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Meta wants the challenge back as bare text; a JSON string fails the subscription.
        Assert.Equal(
            "1158201444",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_handshake_is_refused_when_the_token_does_not_match()
    {
        var response = await _client.GetAsync(
            "/whatsapp?hub.mode=subscribe&hub.verify_token=guessed&hub.challenge=1158201444",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_signed_delivery_reaches_the_handler()
    {
        var response = await PostAsync(Delivery, Sign(Delivery));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var message = Assert.IsType<TextMessage>(Assert.Single(_recorder.Handled));
        Assert.Equal("hello", message.Text);
        Assert.Equal("79000000001", message.From);
    }

    [Fact]
    public async Task A_handler_registered_for_the_base_type_sees_it_too()
    {
        await PostAsync(Delivery, Sign(Delivery));

        // What a logger or an auditor wants: everything, without naming each type.
        Assert.Single(_recorder.SeenByCatchAll);
    }

    [Fact]
    public async Task An_unsigned_delivery_is_refused_and_never_reaches_a_handler()
    {
        var response = await PostAsync(Delivery, signature: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_recorder.Handled);
    }

    [Fact]
    public async Task A_delivery_signed_with_the_wrong_secret_is_refused()
    {
        // The endpoint is public. Without this check anyone who learns the URL can feed the
        // application whatever they like.
        var response = await PostAsync(Delivery, Sign(Delivery, "someone-elses"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_recorder.Handled);
    }

    [Fact]
    public async Task A_body_altered_after_signing_is_refused()
    {
        var signature = Sign(Delivery);
        var tampered = Delivery.Replace("hello", "hellO", StringComparison.Ordinal);

        var response = await PostAsync(tampered, signature);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_recorder.Handled);
    }

    [Fact]
    public async Task A_signed_delivery_that_cannot_be_parsed_is_still_acknowledged()
    {
        const string Nonsense = """{"object":"whatsapp_business_account"}""";

        var response = await PostAsync(Nonsense, Sign(Nonsense));

        // It really did come from Meta, so answering with an error would only have it
        // redelivered for the next seven days.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_recorder.Handled);
    }

    private async Task<HttpResponseMessage> PostAsync(string body, string? signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/whatsapp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (signature is not null)
        {
            request.Headers.TryAddWithoutValidation(WhatsAppWebhookSignature.HeaderName, signature);
        }

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string Sign(string body, string secret = AppSecret) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private sealed class Recorder
    {
        public List<WhatsAppEvent> Handled { get; } = [];

        public List<WhatsAppEvent> SeenByCatchAll { get; } = [];
    }

    private sealed class TextHandler(Recorder recorder) : IWhatsAppEventHandler<TextMessage>
    {
        public Task HandleAsync(TextMessage notification, CancellationToken cancellationToken = default)
        {
            recorder.Handled.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class CatchAllHandler(Recorder recorder) : IWhatsAppEventHandler<WhatsAppEvent>
    {
        public Task HandleAsync(WhatsAppEvent notification, CancellationToken cancellationToken = default)
        {
            recorder.SeenByCatchAll.Add(notification);
            return Task.CompletedTask;
        }
    }
}
