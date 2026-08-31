using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Wapper.Internal;
using Wapper.RateLimiting;
using Wapper.Raw;
using Wapper.Tests.Fakes;

namespace Wapper.Tests.Raw;

/// <summary>
/// The way out for an endpoint this library does not model. It has to be worth using instead
/// of a second HttpClient beside this one, which would pace against nothing.
/// </summary>
public class RawApiTests
{
    private static readonly WhatsAppCredentials Credentials = new()
    {
        AccessToken = "token-abc",
        PhoneNumberId = "106540352242922",
        WhatsAppBusinessAccountId = "102290129340398",
        AppId = "1234567890",
    };

    [Fact]
    public async Task A_raw_call_goes_to_the_versioned_path_with_the_tenants_token()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"data":[{"id":"cat-1"}]}""");
        var raw = CreateApi(handler);

        var response = await raw.SendAsync(
            new RawRequest { Method = HttpMethod.Get, Path = "{waba_id}/product_catalogs" },
            TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://graph.facebook.com/v26.0/102290129340398/product_catalogs",
            request.RequestUri!.AbsoluteUri);
        Assert.Equal("token-abc", request.Headers.Authorization!.Parameter);

        // The body comes back as JSON, self-contained: no disposal, and it outlives the
        // response it was read from.
        Assert.Equal("cat-1", response.GetProperty("data")[0].GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("{phone_number_id}/messages", "106540352242922/messages")]
    [InlineData("{waba_id}/flows", "102290129340398/flows")]
    [InlineData("{app_id}/uploads", "1234567890/uploads")]
    public async Task The_placeholders_are_filled_in_from_the_tenants_credentials(string path, string expected)
    {
        // The same path string then works for every tenant of a multi-tenant host, which is
        // the only way a raw call is usable in one.
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var raw = CreateApi(handler);

        await raw.SendAsync(
            new RawRequest { Method = HttpMethod.Get, Path = path },
            TestContext.Current.CancellationToken);

        Assert.Equal(
            $"https://graph.facebook.com/v26.0/{expected}",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task A_placeholder_the_tenant_has_not_configured_names_the_setting()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var raw = CreateApi(handler, Credentials with { WhatsAppBusinessAccountId = null });

        var exception = await Assert.ThrowsAsync<WhatsAppConfigurationException>(() =>
            raw.SendAsync(
                new RawRequest { Method = HttpMethod.Get, Path = "{waba_id}/product_catalogs" },
                TestContext.Current.CancellationToken));

        Assert.Contains("WhatsAppBusinessAccountId", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_placeholder_the_path_does_not_use_is_never_demanded()
    {
        // A tenant that only sends messages has no account id, and a raw call that needs none
        // should not be the thing that makes it configure one.
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var raw = CreateApi(handler, Credentials with { WhatsAppBusinessAccountId = null, AppId = null });

        await raw.SendAsync(
            new RawRequest { Method = HttpMethod.Get, Path = "{phone_number_id}/whatsapp_business_profile" },
            TestContext.Current.CancellationToken);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_body_goes_up_as_the_json_it_was_written_as()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"success":true}""");
        var raw = CreateApi(handler);

        await raw.SendAsync(
            new RawRequest
            {
                Method = HttpMethod.Post,
                Path = "{waba_id}/something",
                Body = """{"name":"a catalogue"}""",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal("""{"name":"a catalogue"}""", Assert.Single(handler.Bodies));
        Assert.Equal(
            "application/json",
            Assert.Single(handler.Requests).Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task A_raw_call_is_paced_against_the_budget_it_says_it_spends()
    {
        // The whole reason this exists rather than a second HttpClient: an endpoint the
        // library does not model still spends Meta's allowances.
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var time = new FakeTimeProvider();
        var limiter = new RecordingRateLimiter(new InMemoryRateLimiter(time));
        var raw = CreateApi(handler, Credentials, time, limiter);

        await raw.SendAsync(
            new RawRequest
            {
                Method = HttpMethod.Post,
                Path = "{phone_number_id}/messages",
                Kind = RawCallKind.Message,
                Recipient = "79000000001",
            },
            TestContext.Current.CancellationToken);

        Assert.Contains(limiter.Requested, r => r.Scope.Budget == RateLimitBudget.PhoneNumberThroughput);
        Assert.Contains(limiter.Requested, r => r.Scope.Budget == RateLimitBudget.RecipientPair);
    }

    [Fact]
    public async Task A_management_call_spends_the_hourly_account_allowance()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var time = new FakeTimeProvider();
        var limiter = new RecordingRateLimiter(new InMemoryRateLimiter(time));
        var raw = CreateApi(handler, Credentials, time, limiter);

        await raw.SendAsync(
            new RawRequest
            {
                Method = HttpMethod.Get,
                Path = "{waba_id}/product_catalogs",
                Kind = RawCallKind.Management,
            },
            TestContext.Current.CancellationToken);

        Assert.Contains(
            limiter.Requested,
            r => r.Scope.Budget == RateLimitBudget.BusinessAccountRequests
                 && r.Scope.Key == "102290129340398");
    }

    [Fact]
    public async Task A_rejection_arrives_as_the_same_typed_exception_a_modelled_call_would_raise()
    {
        var handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.BadRequest,
            """{"error":{"message":"(#100) Unsupported","type":"OAuthException","code":100}}""");
        var raw = CreateApi(handler);

        var exception = await Assert.ThrowsAsync<WhatsAppApiException>(() =>
            raw.SendAsync(
                new RawRequest { Method = HttpMethod.Get, Path = "{waba_id}/nonsense" },
                TestContext.Current.CancellationToken));

        Assert.Equal(WhatsAppErrorCodes.InvalidParameter, exception.Code);
    }

    [Fact]
    public async Task A_raw_call_can_be_read_into_a_type_of_the_callers_own()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"success":true}""");
        var raw = CreateApi(handler);

        // Source-generated metadata, asked for rather than inferred, because a
        // reflection-based overload would break trimming and AOT for everyone.
        var response = await raw.SendAsync(
            new RawRequest { Method = HttpMethod.Post, Path = "{waba_id}/something" },
            WhatsAppJsonContext.Default.SuccessResponse,
            TestContext.Current.CancellationToken);

        Assert.True(response.Success);
    }

    [Fact]
    public async Task A_path_that_climbs_out_from_under_the_api_version_is_refused()
    {
        // The escape hatch does not escape the Graph API. An id interpolated into the path
        // without escaping would otherwise address a different endpoint entirely.
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var raw = CreateApi(handler);

        await Assert.ThrowsAsync<WhatsAppException>(() =>
            raw.SendAsync(
                new RawRequest { Method = HttpMethod.Get, Path = "../../oauth/access_token" },
                TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_raw_call_is_traced_under_the_name_it_was_given()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var raw = CreateApi(handler);

        using var recorder = new ActivityRecorder();

        await raw.SendAsync(
            new RawRequest
            {
                Method = HttpMethod.Get,
                Path = "{waba_id}/product_catalogs",
                Operation = "catalogs.list",
            },
            TestContext.Current.CancellationToken);

        var activity = Assert.Single(recorder.Activities);
        Assert.Equal("catalogs.list", activity.OperationName);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
    }

    private static IRawApi CreateApi(
        StubHttpMessageHandler handler,
        WhatsAppCredentials? credentials = null,
        FakeTimeProvider? time = null,
        IWhatsAppRateLimiter? limiter = null)
    {
        time ??= new FakeTimeProvider();

        return new RawApi(
            new GraphApiClient(
                new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
                new StubCredentialsProvider(credentials ?? Credentials),
                limiter ?? new InMemoryRateLimiter(time),
                new StaticOptionsMonitor<WhatsAppOptions>(new WhatsAppOptions()),
                time),
            WhatsAppTenant.Default);
    }
}
