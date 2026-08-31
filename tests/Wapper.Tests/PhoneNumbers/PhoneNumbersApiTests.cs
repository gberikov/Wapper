using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Wapper.Internal;
using Wapper.PhoneNumbers;
using Wapper.RateLimiting;
using Wapper.Tests.Fakes;

namespace Wapper.Tests.PhoneNumbers;

public class PhoneNumbersApiTests
{
    private const string Ok = """{"success":true}""";

    /// <summary>Everything Meta returns for a registered, connected number.</summary>
    private const string Number = """
        {
          "id": "106540352242922",
          "display_phone_number": "+1 631-555-5555",
          "verified_name": "Jasper's Market",
          "status": "CONNECTED",
          "quality_rating": "GREEN",
          "code_verification_status": "VERIFIED",
          "name_status": "APPROVED",
          "new_name_status": "PENDING_REVIEW",
          "throughput": {"level": "HIGH"},
          "messaging_limit_tier": "TIER_100K",
          "platform_type": "CLOUD_API",
          "account_mode": "LIVE",
          "is_official_business_account": true,
          "is_pin_enabled": true,
          "last_onboarded_time": "2023-08-22T19:05:53+0000"
        }
        """;

    private static readonly WhatsAppCredentials Credentials = new()
    {
        AccessToken = "token-abc",
        PhoneNumberId = "106540352242922",
        WhatsAppBusinessAccountId = "102290129340398",
    };

    [Fact]
    public async Task Reading_a_number_asks_for_the_fields_Meta_leaves_out_by_default()
    {
        var (numbers, handler) = Create(Number);

        await numbers.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Without an explicit field list Graph answers with the name, the number and its
        // quality, and none of the fields anyone reads a phone number for.
        var query = Assert.Single(handler.Requests).RequestUri!.Query;
        Assert.Contains("status", query, StringComparison.Ordinal);
        Assert.Contains("throughput", query, StringComparison.Ordinal);
        Assert.Contains("messaging_limit_tier", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_a_number_defaults_to_the_tenants_own()
    {
        var (numbers, handler) = Create(Number);

        await numbers.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.StartsWith(
            "https://graph.facebook.com/v26.0/106540352242922?",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_another_number_addresses_that_one()
    {
        var (numbers, handler) = Create(Number);

        await numbers.GetAsync("999888777", TestContext.Current.CancellationToken);

        Assert.StartsWith(
            "https://graph.facebook.com/v26.0/999888777?",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_field_of_a_number_is_read()
    {
        var (numbers, _) = Create(Number);

        var number = await numbers.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("106540352242922", number.Id);
        Assert.Equal("+1 631-555-5555", number.DisplayPhoneNumber);
        Assert.Equal("Jasper's Market", number.VerifiedName);
        Assert.Equal(PhoneNumberStatus.Connected, number.Status);
        Assert.Equal(PhoneNumberQuality.Green, number.Quality);
        Assert.Equal(CodeVerificationStatus.Verified, number.CodeVerification);
        Assert.Equal(DisplayNameStatus.Approved, number.NameStatus);
        Assert.Equal(DisplayNameStatus.PendingReview, number.NewNameStatus);
        Assert.Equal(MessagingLimitTier.Tier100K, number.MessagingLimit);
        Assert.Equal(PhoneNumberPlatform.CloudApi, number.Platform);
        Assert.Equal(PhoneNumberAccountMode.Live, number.AccountMode);
        Assert.True(number.IsOfficialBusinessAccount);
        Assert.True(number.IsTwoStepPinEnabled);
        // ISO 8601 with a colonless offset, unlike the Unix seconds the webhook uses.
        Assert.Equal(
            new DateTimeOffset(2023, 8, 22, 19, 5, 53, TimeSpan.Zero),
            number.LastOnboardedAt);
    }

    [Fact]
    public async Task Throughput_is_read_from_the_one_field_that_is_an_object()
    {
        var (numbers, _) = Create(Number);

        var number = await numbers.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        // 1000 messages a second rather than the 80 a number starts at. Every sibling field
        // is a plain string; this one is an object with a single `level`.
        Assert.Equal(ThroughputLevel.High, number.Throughput);
    }

    [Fact]
    public async Task A_value_this_library_does_not_know_reads_as_unknown_rather_than_throwing()
    {
        var (numbers, _) = Create("""{"id":"1","status":"SOMETHING_NEW","quality_rating":"NA"}""");

        var number = await numbers.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(PhoneNumberStatus.Unknown, number.Status);
        // "NA" is what a number too new to be rated reports.
        Assert.Equal(PhoneNumberQuality.Unknown, number.Quality);
    }

    [Fact]
    public async Task Listing_reads_every_page()
    {
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.OK, """
                {"data":[{"id":"1"}],
                 "paging":{"cursors":{"after":"CURSOR"},"next":"https://graph.facebook.com/next"}}
                """),
            (HttpStatusCode.OK, """{"data":[{"id":"2"}],"paging":{"cursors":{"after":"CURSOR"}}}"""));

        var found = new List<PhoneNumber>();

        await foreach (var number in CreateWith(handler, Credentials)
            .ListAsync(TestContext.Current.CancellationToken))
        {
            found.Add(number);
        }

        Assert.Equal(["1", "2"], found.Select(n => n.Id));
        // The last page still carries a cursor. Following that rather than `next` would ask
        // for the same page for ever.
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("after=CURSOR", handler.Requests[1].RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Listing_without_a_business_account_id_says_which_setting_is_missing()
    {
        var (numbers, _) = Create(
            """{"data":[]}""",
            Credentials with { WhatsAppBusinessAccountId = null });

        var exception = await Assert.ThrowsAsync<WhatsAppConfigurationException>(async () =>
        {
            await foreach (var _ in numbers.ListAsync(TestContext.Current.CancellationToken))
            {
                // The enumeration throws before it yields anything.
            }
        });

        Assert.Contains("WhatsAppBusinessAccountId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_a_number_spends_the_account_allowance_not_the_send_throughput()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, Number);
        var time = new FakeTimeProvider();
        var limiter = new RecordingRateLimiter(new InMemoryRateLimiter(time));
        var numbers = new PhoneNumbersApi(
            new GraphApiClient(
                new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
                new StubCredentialsProvider(Credentials),
                limiter,
                new StaticOptionsMonitor<WhatsAppOptions>(new WhatsAppOptions()),
                time),
            WhatsAppTenant.Default);

        await numbers.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(
            limiter.Requested,
            r => r.Scope.Budget == RateLimitBudget.BusinessAccountRequests
                 && r.Scope.Key == "102290129340398");
        Assert.DoesNotContain(
            limiter.Requested,
            r => r.Scope.Budget == RateLimitBudget.PhoneNumberThroughput);
    }

    [Fact]
    public async Task Setting_a_pin_posts_it_to_the_number()
    {
        var (numbers, handler) = Create(Ok);

        await numbers.SetTwoStepPinAsync("150954", cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://graph.facebook.com/v26.0/106540352242922",
            request.RequestUri!.AbsoluteUri);

        var body = JsonDocument.Parse(Assert.Single(handler.Bodies)!).RootElement;
        Assert.Equal("150954", body.GetProperty("pin").GetString());
        // Nothing else goes up: this endpoint is the phone number node itself, and any other
        // field on it would be an edit nobody asked for.
        Assert.Single(body.EnumerateObject());
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public async Task A_pin_that_is_not_six_digits_never_reaches_Meta(string pin)
    {
        var (numbers, handler) = Create(Ok);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            numbers.SetTwoStepPinAsync(pin, cancellationToken: TestContext.Current.CancellationToken));

        // Meta answers a malformed PIN with a bare code 100 that says nothing about it.
        Assert.Equal("pin", exception.ParamName);
        Assert.Empty(handler.Requests);
    }

    private static (IPhoneNumbersApi Numbers, StubHttpMessageHandler Handler) Create(
        string response,
        WhatsAppCredentials? credentials = null)
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, response);
        return (CreateWith(handler, credentials ?? Credentials), handler);
    }

    private static IPhoneNumbersApi CreateWith(
        StubHttpMessageHandler handler,
        WhatsAppCredentials credentials)
    {
        var time = new FakeTimeProvider();

        return new PhoneNumbersApi(
            new GraphApiClient(
                new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
                new StubCredentialsProvider(credentials),
                new InMemoryRateLimiter(time),
                new StaticOptionsMonitor<WhatsAppOptions>(new WhatsAppOptions()),
                time),
            WhatsAppTenant.Default);
    }
}
