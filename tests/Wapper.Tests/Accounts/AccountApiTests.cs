using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Wapper.Accounts;
using Wapper.Internal;
using Wapper.PhoneNumbers;
using Wapper.RateLimiting;
using Wapper.Tests.Fakes;

namespace Wapper.Tests.Accounts;

/// <summary>
/// Subscribing the app to an account's webhooks. Easy to forget and impossible to debug: the
/// endpoint can be configured, reachable and correctly signed and still receive nothing.
/// </summary>
public class AccountApiTests
{
    private const string Ok = """{"success":true}""";

    private static readonly WhatsAppCredentials Credentials = new()
    {
        AccessToken = "token-abc",
        PhoneNumberId = "106540352242922",
        WhatsAppBusinessAccountId = "102290129340398",
    };

    [Fact]
    public async Task Subscribing_posts_to_the_account_with_nothing_to_say()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, Ok);

        await CreateAccount(handler).SubscribeAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://graph.facebook.com/v26.0/102290129340398/subscribed_apps",
            request.RequestUri!.AbsoluteUri);
        // Meta subscribes whichever app the token belongs to; there is nothing to send.
        Assert.Null(request.Content);
    }

    [Fact]
    public async Task Unsubscribing_deletes_the_same_edge()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, Ok);

        await CreateAccount(handler).UnsubscribeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Delete, Assert.Single(handler.Requests).Method);
    }

    [Fact]
    public async Task The_subscribed_apps_are_read_out_of_the_wrapper_Meta_puts_them_in()
    {
        const string Body = """
            {"data":[{"whatsapp_business_api_data":{"id":"app-1","name":"Orders","link":"https://x"}}]}
            """;
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, Body);

        var apps = await CreateAccount(handler)
            .GetSubscribedAppsAsync(TestContext.Current.CancellationToken);

        var app = Assert.Single(apps);
        Assert.Equal("app-1", app.Id);
        Assert.Equal("Orders", app.Name);
        Assert.Equal("https://x", app.Link);
    }

    [Fact]
    public async Task Subscribing_without_a_business_account_id_says_which_setting_is_missing()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, Ok);
        var account = CreateAccount(handler, Credentials with { WhatsAppBusinessAccountId = null });

        var exception = await Assert.ThrowsAsync<WhatsAppConfigurationException>(() =>
            account.SubscribeAsync(TestContext.Current.CancellationToken));

        Assert.Contains("WhatsAppBusinessAccountId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Account_calls_spend_the_account_allowance()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, Ok);
        var time = new FakeTimeProvider();
        var limiter = new RecordingRateLimiter(new InMemoryRateLimiter(time));
        var account = new WhatsAppAccountApi(Graph(handler, Credentials, time, limiter), WhatsAppTenant.Default);

        await account.SubscribeAsync(TestContext.Current.CancellationToken);

        Assert.Contains(
            limiter.Requested,
            r => r.Scope.Budget == RateLimitBudget.BusinessAccountRequests);
    }

    [Fact]
    public void The_account_group_is_resolvable_on_its_own()
    {
        var services = new ServiceCollection();
        services.AddWhatsApp(options =>
        {
            options.AccessToken = "token";
            options.PhoneNumberId = "111";
        });

        Assert.NotNull(services.BuildServiceProvider().GetRequiredService<IWhatsAppAccountApi>());
    }

    private static IWhatsAppAccountApi CreateAccount(
        StubHttpMessageHandler handler,
        WhatsAppCredentials? credentials = null)
    {
        var time = new FakeTimeProvider();

        return new WhatsAppAccountApi(
            Graph(handler, credentials ?? Credentials, time, new InMemoryRateLimiter(time)),
            WhatsAppTenant.Default);
    }

    internal static GraphApiClient Graph(
        StubHttpMessageHandler handler,
        WhatsAppCredentials credentials,
        FakeTimeProvider time,
        IWhatsAppRateLimiter limiter) =>
        new(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            new StubCredentialsProvider(credentials),
            limiter,
            new StaticOptionsMonitor<WhatsAppOptions>(new WhatsAppOptions()),
            time);
}

/// <summary>
/// The key Meta encrypts a Flow endpoint's traffic with. A Flow that has an endpoint will not
/// run until one is uploaded.
/// </summary>
public class BusinessEncryptionTests
{
    private const string PemKey = """
        -----BEGIN PUBLIC KEY-----
        MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8A
        -----END PUBLIC KEY-----
        """;

    private static readonly WhatsAppCredentials Credentials = new()
    {
        AccessToken = "token-abc",
        PhoneNumberId = "106540352242922",
    };

    [Fact]
    public async Task Uploading_the_key_sends_it_as_form_data()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"success":true}""");

        await CreateApi(handler).SetEncryptionKeyAsync(
            PemKey,
            cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://graph.facebook.com/v26.0/106540352242922/whatsapp_business_encryption",
            request.RequestUri!.AbsoluteUri);
        Assert.Equal(
            "application/x-www-form-urlencoded",
            request.Content!.Headers.ContentType!.MediaType);

        // PEM is newlines and all, so the encoding has to survive them.
        var body = Assert.Single(handler.Bodies)!;
        Assert.StartsWith("business_public_key=", body, StringComparison.Ordinal);
        Assert.Equal(PemKey, Uri.UnescapeDataString(body["business_public_key=".Length..]));
    }

    [Fact]
    public async Task Reading_the_key_unwraps_the_one_element_array()
    {
        const string Body = """
            {"data":[{"business_public_key":"KEY","business_public_key_signature_status":"VALID"}]}
            """;
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, Body);

        var key = await CreateApi(handler)
            .GetEncryptionKeyAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("KEY", key!.PublicKey);
        Assert.Equal("VALID", key.SignatureStatus);
    }

    [Fact]
    public async Task A_number_with_no_key_reads_back_as_nothing_rather_than_failing()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"data":[]}""");

        Assert.Null(await CreateApi(handler)
            .GetEncryptionKeyAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    private static IPhoneNumbersApi CreateApi(StubHttpMessageHandler handler)
    {
        var time = new FakeTimeProvider();

        return new PhoneNumbersApi(
            AccountApiTests.Graph(handler, Credentials, time, new InMemoryRateLimiter(time)),
            WhatsAppTenant.Default);
    }
}
