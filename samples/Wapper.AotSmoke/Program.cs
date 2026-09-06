using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Wapper;
using Wapper.AotSmoke;
using Wapper.AspNetCore;
using Wapper.Webhooks;

// The smallest application that exercises everything the packages claim to do under Native
// AOT: the container, source-generated serialization, the webhook endpoint with its
// signature check, parsing and dispatch, and the client sending through the paced transport.
// The Cloud API is stood in for by a handler that answers in-process; nothing here reaches
// the network beyond the loopback interface.
//
// Exit code 0 means every check passed. Anything else names the check that did not.

const string AppSecret = "smoke-app-secret";

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();

var cloudApi = new FakeCloudApi();
var seen = new Seen();

builder.Services.AddSingleton(seen);
builder.Services
    .AddWhatsApp(options =>
    {
        options.AccessToken = "smoke-token";
        options.PhoneNumberId = "106540352242922";
        options.WhatsAppBusinessAccountId = "102290129340398";
        options.AppSecret = AppSecret;
        options.WebhookVerifyToken = "smoke-verify";
    })
    .ConfigurePrimaryHttpMessageHandler(() => cloudApi);

builder.Services.AddWhatsAppWebhookHandler<Recorder<TextMessage>, TextMessage>(ServiceLifetime.Singleton);
builder.Services.AddWhatsAppWebhookHandler<Recorder<UnknownMessage>, UnknownMessage>(ServiceLifetime.Singleton);
builder.Services.AddWhatsAppWebhookHandler<Recorder<UnknownEvent>, UnknownEvent>(ServiceLifetime.Singleton);
builder.Services.AddWhatsAppWebhookHandler<Recorder<TemplateCategoryChanged>, TemplateCategoryChanged>(ServiceLifetime.Singleton);
builder.Services.AddWhatsAppWebhookHandler<Recorder<WhatsAppEvent>, WhatsAppEvent>(ServiceLifetime.Singleton);

var app = builder.Build();
app.MapWhatsAppWebhook("/whatsapp");

await app.StartAsync();

try
{
    var address = app.Services.GetRequiredService<IServer>().Features
        .Get<IServerAddressesFeature>()!.Addresses.First();
    using var http = new HttpClient { BaseAddress = new Uri(address) };

    // 1. The subscription handshake.
    var challenge = await http.GetStringAsync(
        "/whatsapp?hub.mode=subscribe&hub.verify_token=smoke-verify&hub.challenge=4242");
    Check(challenge == "4242", "handshake echoes the challenge");

    // 2. A signed delivery carrying a text, a message of a type nobody has seen, and one
    //    the parser cannot read, followed by a typed account-level field.
    const string Delivery = """
        {"object":"whatsapp_business_account","entry":[{"id":"102290129340398","time":1746169200,"changes":[
          {"field":"messages","value":{"messaging_product":"whatsapp",
            "metadata":{"display_phone_number":"15550001111","phone_number_id":"106540352242922"},
            "messages":[
              {"from":"79000000001","id":"wamid.TEXT","timestamp":"1755000000","type":"text","text":{"body":"hello"}},
              {"from":"79000000001","id":"wamid.NEW","timestamp":"1755000000","type":"hologram","hologram":{"x":1}},
              {"from":"79000000001","id":"wamid.BAD","timestamp":"1755000000","type":"text","text":{"body":42}}]}},
          {"field":"template_category_update","value":{"message_template_id":278077987957091,
            "message_template_name":"welcome","message_template_language":"en_US",
            "previous_category":"UTILITY","new_category":"MARKETING"}}]}]}
        """;

    var accepted = await PostAsync(http, Delivery, Sign(Delivery, AppSecret));
    Check(accepted == HttpStatusCode.OK, $"signed delivery is accepted ({accepted})");
    Check(seen.Of<TextMessage>().SingleOrDefault()?.Text == "hello", "text message reached its handler");
    Check(seen.Of<UnknownMessage>().SingleOrDefault()?.Type == "hologram", "unknown message type reached its handler");
    Check(seen.Of<UnknownEvent>().SingleOrDefault()?.Json.Contains("wamid.BAD", StringComparison.Ordinal) == true,
        "unreadable item reported on its own");
    Check(seen.Of<TemplateCategoryChanged>().SingleOrDefault()?.PreviousCategory == Wapper.Templates.TemplateCategory.Utility,
        "template category change is typed");
    Check(seen.Everything.Count == 4, $"catch-all handler saw every event ({seen.Everything.Count})");

    var forged = await PostAsync(http, Delivery, Sign(Delivery, "somebody-else"));
    Check(forged == HttpStatusCode.Forbidden, $"forged signature is refused ({forged})");

    // 3. Sending through the client: credentials from options, the limiter, the transport
    //    and the source-generated response.
    var client = app.Services.GetRequiredService<IWhatsAppClient>();
    var sent = await client.Messages.SendTextAsync("79000000001", "hi there");
    Check(sent.Id == "wamid.SMOKE", $"send returns the id the API gave ({sent.Id})");
    Check(cloudApi.LastBody?.Contains("\"hi there\"", StringComparison.Ordinal) == true, "send serialized the body");
    Check(cloudApi.LastRequest?.Headers.Authorization?.Parameter == "smoke-token", "send presented the token");

    // 4. The typed error path and its classification.
    cloudApi.NextStatus = HttpStatusCode.BadRequest;
    cloudApi.NextBody = """{"error":{"code":131026,"message":"Message undeliverable","type":"OAuthException"}}""";
    try
    {
        await client.Messages.SendTextAsync("79000000002", "unreachable");
        Check(false, "an API error throws");
    }
    catch (WhatsAppApiException exception)
    {
        Check(exception.Code == 131026, "the error code is parsed");
        Check(exception.Error.Classify().Kind == WhatsAppFailureKind.RecipientUnreachable, "the error is classified");
    }

    Console.WriteLine("Wapper AOT smoke: all checks passed.");
    return 0;
}
catch (SmokeFailure failure)
{
    Console.Error.WriteLine($"Wapper AOT smoke: FAILED — {failure.Message}");
    return 1;
}
finally
{
    await app.StopAsync();
}

static void Check(bool condition, string what)
{
    if (!condition)
    {
        throw new SmokeFailure(what);
    }

    Console.WriteLine($"  ok  {what}");
}

static async Task<HttpStatusCode> PostAsync(HttpClient http, string body, string signature)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/whatsapp")
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
    request.Headers.TryAddWithoutValidation(WhatsAppWebhookSignature.HeaderName, signature);

    using var response = await http.SendAsync(request);
    return response.StatusCode;
}

static string Sign(string body, string secret) =>
    "sha256=" + Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(secret),
        Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

namespace Wapper.AotSmoke
{
    internal sealed class SmokeFailure(string what) : Exception(what);

    /// <summary>Stands in for graph.facebook.com.</summary>
    internal sealed class FakeCloudApi : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        public HttpStatusCode NextStatus { get; set; } = HttpStatusCode.OK;

        public string NextBody { get; set; } =
            """{"messaging_product":"whatsapp","contacts":[{"input":"79000000001","wa_id":"79000000001"}],"messages":[{"id":"wamid.SMOKE"}]}""";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(NextStatus)
            {
                Content = new StringContent(NextBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    internal sealed class Seen
    {
        public List<WhatsAppEvent> Everything { get; } = [];

        public List<WhatsAppEvent> Specific { get; } = [];

        public IEnumerable<TEvent> Of<TEvent>()
            where TEvent : WhatsAppEvent =>
            Specific.OfType<TEvent>();
    }

    internal sealed class Recorder<TEvent>(Seen seen) : IWhatsAppEventHandler<TEvent>
        where TEvent : WhatsAppEvent
    {
        public Task HandleAsync(TEvent notification, CancellationToken cancellationToken = default)
        {
            if (typeof(TEvent) == typeof(WhatsAppEvent))
            {
                seen.Everything.Add(notification);
            }
            else
            {
                seen.Specific.Add(notification);
            }

            return Task.CompletedTask;
        }
    }
}
