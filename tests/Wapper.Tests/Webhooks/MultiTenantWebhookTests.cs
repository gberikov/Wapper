using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wapper.AspNetCore;
using Wapper.Webhooks;

namespace Wapper.Tests.Webhooks;

/// <summary>
/// One endpoint for tenants on different Meta apps, and therefore on different app secrets.
/// The tenant is resolved from the delivery, and the signature is checked against that
/// tenant's secret rather than against whichever one the endpoint was mapped with.
/// </summary>
public class MultiTenantWebhookTests : IAsyncLifetime
{
    private const string AcmeSecret = "acme-app-secret";
    private const string GlobexSecret = "globex-app-secret";

    private const string AcmeNumber = "106540352242922";
    private const string GlobexNumber = "115540352242911";

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private Recorder _recorder = null!;

    /// <summary>A delivery for one number.</summary>
    private static string Delivery(string phoneNumberId, string text = "hello") => $$$"""
        {"object":"whatsapp_business_account","entry":[{"id":"W","changes":[{"field":"messages",
         "value":{"messaging_product":"whatsapp",
          "metadata":{"display_phone_number":"15550001111","phone_number_id":"{{{phoneNumberId}}}"},
          "messages":[{"from":"79000000001","id":"wamid.A","timestamp":"1755000000",
                       "type":"text","text":{"body":"{{{text}}}"}}]}}]}]}
        """;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        // Two customers onboarded through two Meta apps: same host, same endpoint, different
        // app secrets. This is the shape one endpoint could not serve at all before.
        builder.Services.AddWhatsApp("acme", options =>
        {
            options.AccessToken = "acme-token";
            options.PhoneNumberId = AcmeNumber;
            options.WhatsAppBusinessAccountId = "acme-waba";
            options.AppSecret = AcmeSecret;
        });

        builder.Services.AddWhatsApp("globex", options =>
        {
            options.AccessToken = "globex-token";
            options.PhoneNumberId = GlobexNumber;
            options.WhatsAppBusinessAccountId = "globex-waba";
            options.AppSecret = GlobexSecret;
        });

        _recorder = new Recorder();
        builder.Services.AddSingleton(_recorder);
        builder.Services.AddWhatsAppWebhookHandler<TextHandler, TextMessage>(ServiceLifetime.Singleton);

        _app = builder.Build();
        _app.MapWhatsAppWebhookForTenants("/whatsapp");

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData(AcmeNumber, AcmeSecret)]
    [InlineData(GlobexNumber, GlobexSecret)]
    public async Task A_delivery_is_verified_against_the_secret_of_the_tenant_it_names(
        string number,
        string secret)
    {
        var body = Delivery(number);

        var response = await PostAsync(body, Sign(body, secret));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello", Assert.IsType<TextMessage>(Assert.Single(_recorder.Handled)).Text);
    }

    [Fact]
    public async Task One_tenant_s_secret_does_not_verify_another_tenant_s_delivery()
    {
        // The whole point of resolving. Signing acme's number with globex's secret is what an
        // endpoint that checked against a single tenant would have accepted for one of them
        // and refused for the other.
        var body = Delivery(AcmeNumber);

        var response = await PostAsync(body, Sign(body, GlobexSecret));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_recorder.Handled);
    }

    [Fact]
    public async Task A_delivery_for_a_number_this_host_does_not_serve_is_refused()
    {
        // Correctly signed by somebody, for a number nobody here has configured. There is no
        // secret to check it against, so there is nothing to do but refuse and say so.
        var body = Delivery("999999999999999");

        var response = await PostAsync(body, Sign(body, AcmeSecret));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_recorder.Handled);
    }

    [Fact]
    public async Task A_delivery_covering_two_tenants_on_different_apps_is_refused()
    {
        // Meta signs a delivery once, with one app's secret, so a delivery spanning two apps
        // is not one it could have sent. Verifying it against the first tenant's secret would
        // let events for the second in on a signature that says nothing about them.
        const string Body = $$$"""
            {"object":"whatsapp_business_account","entry":[
              {"id":"acme-waba","changes":[{"field":"messages",
               "value":{"messaging_product":"whatsapp",
                "metadata":{"phone_number_id":"{{{AcmeNumber}}}"},
                "messages":[{"from":"79000000001","id":"wamid.A","timestamp":"1755000000",
                             "type":"text","text":{"body":"mine"}}]}}]},
              {"id":"globex-waba","changes":[{"field":"messages",
               "value":{"messaging_product":"whatsapp",
                "metadata":{"phone_number_id":"{{{GlobexNumber}}}"},
                "messages":[{"from":"79000000002","id":"wamid.B","timestamp":"1755000001",
                             "type":"text","text":{"body":"theirs"}}]}}]}]}
            """;

        var response = await PostAsync(Body, Sign(Body, AcmeSecret));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // Not even the half that was signed correctly.
        Assert.Empty(_recorder.Handled);
    }

    [Fact]
    public async Task An_account_level_delivery_resolves_by_the_account_on_the_entry()
    {
        // A template verdict names no phone number at all — only the account on the entry.
        // Without that fallback every account-level field would be unroutable.
        const string Body = """
            {"object":"whatsapp_business_account","entry":[{"id":"globex-waba","changes":[
              {"field":"message_template_status_update",
               "value":{"event":"APPROVED","message_template_id":1,
                        "message_template_name":"receipt","message_template_language":"en_US"}}]}]}
            """;

        var response = await PostAsync(Body, Sign(Body, GlobexSecret));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_body_that_names_nothing_is_refused_before_anything_is_parsed()
    {
        const string Body = """{"object":"whatsapp_business_account","entry":[]}""";

        var response = await PostAsync(Body, Sign(Body, AcmeSecret));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_forged_phone_number_only_ever_costs_the_forger_a_refusal()
    {
        // The routing field is read out of a body nobody has verified. All naming another
        // tenant buys is having the signature checked against that tenant's secret, which it
        // does not match either.
        var body = Delivery(GlobexNumber, "forged");

        var response = await PostAsync(body, Sign(body, "not-a-secret-of-ours"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_recorder.Handled);
    }

    private Task<HttpResponseMessage> PostAsync(string body, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/whatsapp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (signature is not null)
        {
            request.Headers.TryAddWithoutValidation(WhatsAppWebhookSignature.HeaderName, signature);
        }

        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string Sign(string body, string secret) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private sealed class Recorder
    {
        public List<WhatsAppEvent> Handled { get; } = [];
    }

    private sealed class TextHandler(Recorder recorder) : IWhatsAppEventHandler<TextMessage>
    {
        public Task HandleAsync(TextMessage notification, CancellationToken cancellationToken = default)
        {
            recorder.Handled.Add(notification);
            return Task.CompletedTask;
        }
    }
}
