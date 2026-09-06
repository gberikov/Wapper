using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wapper.AspNetCore;
using Wapper.Webhooks;

namespace Wapper.Tests.Webhooks;

/// <summary>
/// A signed delivery the parser refuses is acknowledged, because Meta would otherwise repeat
/// it for a week and be refused every time. Acknowledging is also how it is lost for good —
/// unless the application was given the chance to keep the body first.
/// </summary>
public class WebhookUnparsedDeliveryTests : IAsyncLifetime
{
    private const string AppSecret = "an-app-secret";
    private const string Nonsense = """{"object":"whatsapp_business_account"}""";

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private Keeper _keeper = null!;

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
            options.WebhookVerifyToken = "a-verify-token";
        });

        _keeper = new Keeper();
        builder.Services.AddSingleton<IWhatsAppUnparsedWebhookHandler>(_keeper);
        builder.Services.AddWhatsAppWebhooks();

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
    public async Task The_body_is_handed_over_and_the_delivery_acknowledged_once_it_is_kept()
    {
        var response = await PostAsync(Nonsense, Sign(Nonsense));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var kept = Assert.Single(_keeper.Kept);
        Assert.Equal(Nonsense, kept.Body);
        Assert.Equal(WhatsAppTenant.Default, kept.Tenant);
        Assert.Contains("no entries", kept.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_keeper_that_fails_fails_the_delivery_so_Meta_repeats_it()
    {
        _keeper.Fail = true;

        var response = await PostAsync(Nonsense, Sign(Nonsense));

        // The store is down. Acknowledging now would lose the delivery for good; Meta's
        // retry is the one there is.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task A_delivery_with_a_bad_signature_never_reaches_the_keeper()
    {
        var response = await PostAsync(Nonsense, Sign(Nonsense, "someone-elses"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_keeper.Kept);
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

    private static string Sign(string body, string secret = AppSecret) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private sealed class Keeper : IWhatsAppUnparsedWebhookHandler
    {
        public List<(string Body, string Tenant, string Error)> Kept { get; } = [];

        public bool Fail { get; set; }

        public Task HandleAsync(WhatsAppUnparsedWebhook delivery, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new IOException("The inbox is unavailable.");
            }

            // Copied: the buffer belongs to the request.
            Kept.Add((Encoding.UTF8.GetString(delivery.Body.Span), delivery.Tenant, delivery.Error.Message));
            return Task.CompletedTask;
        }
    }
}
