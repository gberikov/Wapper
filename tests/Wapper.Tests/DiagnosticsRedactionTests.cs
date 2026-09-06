using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Wapper.Internal;
using Wapper.PhoneNumbers;
using Wapper.RateLimiting;
using Wapper.Tests.Fakes;

namespace Wapper.Tests;

/// <summary>
/// A verification code travels in the query string, and the query string is the one part of
/// a request that has no business in an exception message, a log line or a span: all three
/// end up in storage. The endpoint and the operation still have to be readable there.
/// </summary>
public class DiagnosticsRedactionTests
{
    private const string Code = "123830";

    private static readonly WhatsAppCredentials Credentials = new()
    {
        AccessToken = "token-abc",
        PhoneNumberId = "106540352242922",
    };

    [Fact]
    public async Task A_connection_failure_names_the_endpoint_and_not_the_code()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("Synthetic connection failure"));
        var (numbers, _, _) = Create(handler);

        using var recorder = new ActivityRecorder();

        var exception = await Assert.ThrowsAsync<WhatsAppException>(() =>
            numbers.VerifyAsync("123-830", cancellationToken: TestContext.Current.CancellationToken));

        Assert.DoesNotContain(Code, exception.Message, StringComparison.Ordinal);
        Assert.Contains("verify_code", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Synthetic connection failure", exception.Message, StringComparison.Ordinal);

        var activity = Assert.Single(recorder.Activities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.DoesNotContain(Code, activity.StatusDescription ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(activity.Tags, tag => tag.Value?.Contains(Code, StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task A_timeout_names_the_endpoint_and_not_the_code()
    {
        var (numbers, _, _) = Create(
            new HangingHttpMessageHandler(),
            options => options.Timeout = TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<WhatsAppException>(() =>
            numbers.VerifyAsync(Code, cancellationToken: TestContext.Current.CancellationToken));

        Assert.DoesNotContain(Code, exception.Message, StringComparison.Ordinal);
        Assert.Contains("verify_code", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_retry_is_logged_without_the_code()
    {
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.InternalServerError,
             """{"error":{"code":131000,"message":"Something went wrong","is_transient":true}}"""),
            (HttpStatusCode.OK, """{"success":true}"""));
        var (numbers, time, logger) = Create(handler);

        await Clock.RunAsync(
            time,
            numbers.VerifyAsync(Code, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(2, handler.Requests.Count);

        var retry = Assert.Single(logger.Lines, line => line.Level == LogLevel.Information);
        Assert.Contains("verify_code", retry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Code, retry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_path_that_escapes_the_api_version_is_reported_without_its_query()
    {
        // The guard names the path it refused. A raw call can put anything in the query, so
        // the query is not part of what it names.
        var exception = Assert.Throws<WhatsAppException>(() =>
            GraphApiClient.BuildUri(new WhatsAppOptions(), "../secret?token=abc"));

        Assert.DoesNotContain("token=abc", exception.Message, StringComparison.Ordinal);
    }

    private static (IPhoneNumbersApi Numbers, FakeTimeProvider Time, RecordingLogger<GraphApiClient> Logger) Create(
        HttpMessageHandler handler,
        Action<WhatsAppOptions>? configure = null)
    {
        var options = new WhatsAppOptions();
        configure?.Invoke(options);

        var time = new FakeTimeProvider();
        var logger = new RecordingLogger<GraphApiClient>();

        var numbers = new PhoneNumbersApi(
            new GraphApiClient(
                new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
                new StubCredentialsProvider(Credentials),
                new InMemoryRateLimiter(time),
                new StaticOptionsMonitor<WhatsAppOptions>(options),
                time,
                logger),
            WhatsAppTenant.Default);

        return (numbers, time, logger);
    }
}
