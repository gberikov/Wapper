using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Wapper.Internal;
using Wapper.PhoneNumbers;
using Wapper.RateLimiting;
using Wapper.Tests.Fakes;

namespace Wapper.Tests.PhoneNumbers;

/// <summary>
/// Getting a number onto the Cloud API: request a code, verify it, register. The three calls
/// are addressed to the phone number node and every one of them has a way to waste an
/// allowance that does not refill quickly.
/// </summary>
public class RegistrationTests
{
    private const string Ok = """{"success":true}""";

    private static readonly WhatsAppCredentials Credentials = new()
    {
        AccessToken = "token-abc",
        PhoneNumberId = "106540352242922",
        WhatsAppBusinessAccountId = "102290129340398",
    };

    [Fact]
    public async Task Requesting_a_code_puts_the_method_and_language_in_the_query()
    {
        var (numbers, handler) = Create(Ok);

        await numbers.RequestVerificationCodeAsync(
            VerificationCodeMethod.Voice,
            cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        // Meta documents this one as query parameters on a POST with no body at all.
        Assert.Equal(
            "https://graph.facebook.com/v26.0/106540352242922/request_code" +
            "?code_method=VOICE&language=en_US",
            request.RequestUri!.AbsoluteUri);
        Assert.Null(Assert.Single(handler.Bodies));
    }

    [Fact]
    public async Task Requesting_a_code_is_never_retried()
    {
        // Transient by Meta's own reckoning, which is exactly what the retry loop acts on.
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.InternalServerError,
             """{"error":{"code":131000,"message":"Something went wrong","is_transient":true}}"""),
            (HttpStatusCode.OK, Ok));

        await Assert.ThrowsAsync<WhatsAppApiException>(() =>
            CreateWith(handler, Credentials).RequestVerificationCodeAsync(
                VerificationCodeMethod.Sms,
                cancellationToken: TestContext.Current.CancellationToken));

        // A second attempt would send a second code and silently invalidate the first.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_code_copied_out_of_the_message_keeps_its_hyphen_out_of_the_request()
    {
        var (numbers, handler) = Create(Ok);

        await numbers.VerifyAsync("123-830", cancellationToken: TestContext.Current.CancellationToken);

        // The SMS spells it "123-830"; the endpoint takes "123830" and treats the hyphenated
        // form as a wrong code rather than a malformed one.
        Assert.Equal(
            "https://graph.facebook.com/v26.0/106540352242922/verify_code?code=123830",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12a830")]
    public async Task A_code_that_is_not_digits_never_reaches_Meta(string code)
    {
        var (numbers, handler) = Create(Ok);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            numbers.VerifyAsync(code, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("code", exception.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Registering_sends_the_messaging_product_and_the_pin()
    {
        var (numbers, handler) = Create(Ok);

        await numbers.RegisterAsync("150954", cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://graph.facebook.com/v26.0/106540352242922/register",
            request.RequestUri!.AbsoluteUri);

        var body = JsonDocument.Parse(Assert.Single(handler.Bodies)!).RootElement;
        Assert.Equal("whatsapp", body.GetProperty("messaging_product").GetString());
        Assert.Equal("150954", body.GetProperty("pin").GetString());
        // Absent rather than null: sending the field at all is what turns local storage on.
        Assert.False(body.TryGetProperty("data_localization_region", out _));
    }

    [Fact]
    public async Task Registering_with_local_storage_names_the_region_in_capitals()
    {
        var (numbers, handler) = Create(Ok);

        await numbers.RegisterAsync(
            "150954",
            "de",
            cancellationToken: TestContext.Current.CancellationToken);

        var body = JsonDocument.Parse(Assert.Single(handler.Bodies)!).RootElement;
        Assert.Equal("DE", body.GetProperty("data_localization_region").GetString());
    }

    [Theory]
    [InlineData("Germany")]
    [InlineData("DEU")]
    [InlineData("D")]
    public async Task A_region_that_is_not_a_country_code_never_reaches_Meta(string region)
    {
        var (numbers, handler) = Create(Ok);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            numbers.RegisterAsync(
                "150954",
                region,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("dataLocalizationRegion", exception.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Registering_with_a_bad_pin_never_reaches_Meta()
    {
        var (numbers, handler) = Create(Ok);

        // A wasted call here is not merely wasted: it is one of ten in 72 hours.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            numbers.RegisterAsync("1234", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Registering_is_never_retried()
    {
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.InternalServerError,
             """{"error":{"code":131000,"message":"Something went wrong","is_transient":true}}"""),
            (HttpStatusCode.OK, Ok));

        await Assert.ThrowsAsync<WhatsAppApiException>(() =>
            CreateWith(handler, Credentials).RegisterAsync(
                "150954",
                cancellationToken: TestContext.Current.CancellationToken));

        // Ten attempts per number per 72 hours, and Meta counts the ones that failed.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Deregistering_posts_to_the_number_with_no_body()
    {
        var (numbers, handler) = Create(Ok);

        await numbers.DeregisterAsync(cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://graph.facebook.com/v26.0/106540352242922/deregister",
            request.RequestUri!.AbsoluteUri);
        Assert.Null(Assert.Single(handler.Bodies));
    }

    [Fact]
    public async Task Registration_can_address_a_number_other_than_the_tenants_own()
    {
        var (numbers, handler) = Create(Ok);

        await numbers.RegisterAsync(
            "150954",
            phoneNumberId: "999888777",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            "https://graph.facebook.com/v26.0/999888777/register",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task A_blocked_number_is_not_retried_either()
    {
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.BadRequest,
             """{"error":{"code":133016,"message":"Too many attempts","is_transient":true}}"""),
            (HttpStatusCode.OK, Ok));

        // 133016 means the number is locked out for 72 hours whatever the client does, and
        // Meta still marks it transient.
        var exception = await Assert.ThrowsAsync<WhatsAppApiException>(() =>
            CreateWith(handler, Credentials).DeregisterAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(WhatsAppErrorCodes.RegistrationLimitReached, exception.Error.Code);
        Assert.Single(handler.Requests);
    }

    private static (IPhoneNumbersApi Numbers, StubHttpMessageHandler Handler) Create(string response)
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, response);
        return (CreateWith(handler, Credentials), handler);
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
