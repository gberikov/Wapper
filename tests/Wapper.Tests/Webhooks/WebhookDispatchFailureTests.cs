using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wapper.AspNetCore;
using Wapper.Webhooks;

namespace Wapper.Tests.Webhooks;

/// <summary>
/// What one throwing handler costs the rest of the delivery. Meta packs many events into one
/// POST, and its only retry is to send the whole POST again.
/// </summary>
public class WebhookDispatchFailureTests : IAsyncLifetime
{
    private const string AppSecret = "an-app-secret";

    /// <summary>Two messages in one delivery. The first one is the one that blows up.</summary>
    private const string Delivery = """
        {"object":"whatsapp_business_account","entry":[{"id":"W","changes":[{"field":"messages",
         "value":{"messaging_product":"whatsapp",
          "metadata":{"display_phone_number":"15550001111","phone_number_id":"106540352242922"},
          "messages":[{"from":"79000000001","id":"wamid.A","timestamp":"1755000000",
                       "type":"text","text":{"body":"poison"}},
                      {"from":"79000000002","id":"wamid.B","timestamp":"1755000001",
                       "type":"text","text":{"body":"fine"}}]}}]}]}
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
        });

        _recorder = new Recorder();
        builder.Services.AddSingleton(_recorder);
        builder.Services.AddWhatsAppWebhookHandler<PoisonHandler, TextMessage>(ServiceLifetime.Singleton);

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
    public async Task One_throwing_handler_does_not_swallow_the_rest_of_the_delivery()
    {
        var response = await PostAsync(Delivery, Sign(Delivery));

        // Both were offered. Stopping at the first would silently cost you every event
        // behind it, and Meta would only ever redeliver the same poisonous one first.
        Assert.Equal(["poison", "fine"], _recorder.Seen);

        // And the delivery is still failed, because Meta redelivering it is the only retry
        // there is. Answering 200 would lose the message for good.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PostAsync(string body, string signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/whatsapp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(WhatsAppWebhookSignature.HeaderName, signature);

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string Sign(string body) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(AppSecret),
            Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private sealed class Recorder
    {
        public List<string> Seen { get; } = [];
    }

    private sealed class PoisonHandler(Recorder recorder) : IWhatsAppEventHandler<TextMessage>
    {
        public Task HandleAsync(TextMessage notification, CancellationToken cancellationToken = default)
        {
            recorder.Seen.Add(notification.Text);

            return notification.Text == "poison"
                ? throw new InvalidOperationException("the database is down")
                : Task.CompletedTask;
        }
    }
}
