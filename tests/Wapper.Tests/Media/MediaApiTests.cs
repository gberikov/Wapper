using Microsoft.Extensions.Time.Testing;
using Wapper.Internal;
using Wapper.Media;
using Wapper.RateLimiting;
using Wapper.Tests.Fakes;

namespace Wapper.Tests.Media;

public class MediaApiTests
{
    private static readonly WhatsAppCredentials Credentials = new()
    {
        AccessToken = "token-abc",
        PhoneNumberId = "106540352242922",
    };

    [Fact]
    public async Task Upload_posts_the_file_to_the_phone_number()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"id":"media-1"}""");
        var media = CreateApi(handler);

        var id = await media.UploadAsync(
            new MemoryStream("hello"u8.ToArray()),
            "image/jpeg",
            "photo.jpg",
            TestContext.Current.CancellationToken);

        Assert.Equal("media-1", id);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://graph.facebook.com/v26.0/106540352242922/media",
            request.RequestUri!.AbsoluteUri);

        var body = Assert.Single(handler.Bodies)!;
        Assert.Contains("whatsapp", body, StringComparison.Ordinal);
        Assert.Contains("image/jpeg", body, StringComparison.Ordinal);
        Assert.Contains("photo.jpg", body, StringComparison.Ordinal);
        Assert.Contains("hello", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_oversized_file_is_refused_before_it_is_uploaded()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"id":"media-1"}""");
        var media = CreateApi(handler);

        // Meta accepts 5 MB of image. Discovering that after pushing the whole file up the
        // wire is a slow way to be told.
        var oversized = new MemoryStream(new byte[MediaLimits.ImageBytes + 1]);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await media.UploadAsync(oversized, "image/png", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(MediaLimits.ImageBytes.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_file_within_the_limit_of_its_own_type_is_uploaded()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"id":"media-1"}""");
        var media = CreateApi(handler);

        // Larger than the image limit, well inside the document one.
        var document = new MemoryStream(new byte[MediaLimits.ImageBytes + 1]);

        await media.UploadAsync(document, "application/pdf", "big.pdf", TestContext.Current.CancellationToken);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_retried_upload_sends_the_file_again_rather_than_an_empty_body()
    {
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.BadRequest,
                $$$"""{"error":{"message":"x","code":{{{WhatsAppErrorCodes.TemporarilyUnavailable}}}}}"""),
            (HttpStatusCode.OK, """{"id":"media-1"}"""));
        var time = new FakeTimeProvider();
        var media = CreateApi(handler, time);

        var id = await Clock.RunAsync(time, media.UploadAsync(
            new MemoryStream("hello"u8.ToArray()),
            "image/jpeg",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("media-1", id);
        Assert.Equal(2, handler.Bodies.Count);
        // The first attempt consumed the stream; without rewinding it the second would
        // upload nothing.
        Assert.All(handler.Bodies, body => Assert.Contains("hello", body!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_upload_from_a_stream_that_cannot_be_rewound_is_not_retried()
    {
        var handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.BadRequest,
            $$$"""{"error":{"message":"x","code":{{{WhatsAppErrorCodes.TemporarilyUnavailable}}}}}""");
        var time = new FakeTimeProvider();
        var media = CreateApi(handler, time);

        await Assert.ThrowsAsync<WhatsAppApiException>(() => Clock.RunAsync(
            time,
            media.UploadAsync(
                new ForwardOnlyStream("hello"u8.ToArray()),
                "image/jpeg",
                cancellationToken: TestContext.Current.CancellationToken)));

        // Sending it again would upload an empty file, which is worse than failing.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Lookup_scopes_the_media_to_the_phone_number()
    {
        const string Body = """
            {
              "messaging_product": "whatsapp",
              "url": "https://lookaside.fbsbx.com/whatsapp_business/attachments/?mid=1",
              "mime_type": "image/jpeg",
              "sha256": "abc123",
              "file_size": 3708174,
              "id": "media-1"
            }
            """;
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, Body);
        var media = CreateApi(handler);

        var info = await media.GetAsync("media-1", TestContext.Current.CancellationToken);

        Assert.Equal("media-1", info.Id);
        Assert.Equal("image/jpeg", info.MimeType);
        Assert.Equal(3708174, info.FileSize);
        Assert.Equal("abc123", info.Sha256);
        Assert.StartsWith("https://lookaside.fbsbx.com/", info.Url.AbsoluteUri, StringComparison.Ordinal);

        // Without the phone number, one tenant could read another tenant's media on a shared
        // business account.
        Assert.Contains(
            "phone_number_id=106540352242922",
            Assert.Single(handler.Requests).RequestUri!.Query,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Download_presents_the_token_to_the_host_Meta_names()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.Host == "graph.facebook.com"
                ? Json("""{"url":"https://lookaside.fbsbx.com/x?mid=1","mime_type":"image/jpeg","file_size":5,"id":"media-1"}""")
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent("hello"u8.ToArray()),
                });
        var media = CreateApi(handler);

        using var content = await media.DownloadAsync("media-1", TestContext.Current.CancellationToken);
        using var reader = new StreamReader(content.Content);

        Assert.Equal("hello", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));

        // The download host is not the Graph API, and it still refuses an unauthenticated
        // request with a 404.
        var download = handler.Requests[1];
        Assert.Equal("lookaside.fbsbx.com", download.RequestUri!.Host);
        Assert.Equal("token-abc", download.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task A_media_id_that_is_really_a_path_never_reaches_the_wire()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"success":true}""");
        var media = CreateApi(handler);

        // From a webhook, or from a caller's own store: an id is data, and this one would
        // otherwise delete every template on the account instead of a file.
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            media.DeleteAsync("../102290129340398/message_templates?name=order_confirmation&", TestContext.Current.CancellationToken));

        Assert.Equal("mediaId", exception.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Delete_reports_what_the_api_said()
    {
        var media = CreateApi(StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"success":true}"""));

        Assert.True(await media.DeleteAsync("media-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_upload_with_no_id_in_the_response_is_reported()
    {
        var media = CreateApi(StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"messaging_product":"whatsapp"}"""));

        var exception = await Assert.ThrowsAsync<WhatsAppException>(async () =>
            await media.UploadAsync(
                new MemoryStream([1, 2, 3]),
                "image/png",
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("no media id", exception.Message, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static IMediaApi CreateApi(StubHttpMessageHandler handler, FakeTimeProvider? time = null)
    {
        time ??= new FakeTimeProvider();

        return new MediaApi(
            new GraphApiClient(
                new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
                new StubCredentialsProvider(Credentials),
                new InMemoryRateLimiter(time),
                new StaticOptionsMonitor<WhatsAppOptions>(new WhatsAppOptions()),
                time),
            WhatsAppTenant.Default);
    }
}
