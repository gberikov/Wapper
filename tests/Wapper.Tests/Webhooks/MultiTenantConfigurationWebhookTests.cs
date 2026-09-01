using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wapper.AspNetCore;
using Wapper.Webhooks;

namespace Wapper.Tests.Webhooks;

/// <summary>
/// The same endpoint, for tenants that exist only in configuration.
/// </summary>
/// <remarks>
/// <c>AddWhatsApp()</c> binds a tenant the first time it is asked for and never enumerates
/// them, which is what lets a tenant be added to a config map without a restart — so nothing
/// has recorded these names and the resolver has to read them itself.
/// </remarks>
public class MultiTenantConfigurationWebhookTests : IAsyncLifetime
{
    private const string AcmeSecret = "acme-app-secret";
    private const string GlobexSecret = "globex-app-secret";

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WhatsApp:Tenants:acme:AccessToken"] = "acme-token",
            ["WhatsApp:Tenants:acme:PhoneNumberId"] = "106540352242922",
            ["WhatsApp:Tenants:acme:AppSecret"] = AcmeSecret,
            ["WhatsApp:Tenants:globex:AccessToken"] = "globex-token",
            ["WhatsApp:Tenants:globex:PhoneNumberId"] = "115540352242911",
            ["WhatsApp:Tenants:globex:AppSecret"] = GlobexSecret,
        });

        // No tenant named anywhere in code. The names live in configuration and nothing has
        // enumerated them.
        builder.Services.AddWhatsApp();

        // No handler here: this test is about which secret the delivery is checked against,
        // not about what happens after.
        builder.Services.AddWhatsAppWebhooks();

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
    [InlineData("106540352242922", AcmeSecret, HttpStatusCode.OK)]
    [InlineData("115540352242911", GlobexSecret, HttpStatusCode.OK)]
    // The right number, the other tenant's secret.
    [InlineData("106540352242922", GlobexSecret, HttpStatusCode.Forbidden)]
    // A number neither tenant has.
    [InlineData("999999999999999", AcmeSecret, HttpStatusCode.Forbidden)]
    public async Task A_tenant_declared_only_in_configuration_is_still_resolved(
        string number,
        string secret,
        HttpStatusCode expected)
    {
        var body = """
            {"object":"whatsapp_business_account","entry":[{"id":"W","changes":[{"field":"messages",
             "value":{"messaging_product":"whatsapp",
              "metadata":{"phone_number_id":"NUMBER"},
              "messages":[{"from":"79000000001","id":"wamid.A","timestamp":"1755000000",
                           "type":"text","text":{"body":"hello"}}]}}]}]}
            """
            .Replace("NUMBER", number, StringComparison.Ordinal);

        var request = new HttpRequestMessage(HttpMethod.Post, "/whatsapp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        request.Headers.TryAddWithoutValidation(
            WhatsAppWebhookSignature.HeaderName,
            "sha256=" + Convert.ToHexString(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret),
                Encoding.UTF8.GetBytes(body))).ToLowerInvariant());

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }
}
