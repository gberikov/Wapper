using Microsoft.Extensions.Time.Testing;
using Wapper.Internal;
using Wapper.RateLimiting;
using Wapper.Templates;
using Wapper.Tests.Fakes;

namespace Wapper.Tests.Templates;

/// <summary>
/// The resumable upload behind header samples and profile pictures. What matters is what it
/// does with the caller's stream: how many copies it makes, whether it closes it, and what a
/// retry sends.
/// </summary>
public class ResumableUploadTests
{
    private const string Session = """{"id":"upload:MTphdHRhY2htZW50"}""";
    private const string Handle = """{"h":"4:aW1hZ2U="}""";
    private const string Transient = """{"error":{"code":131000,"message":"Something went wrong","is_transient":true}}""";

    private static readonly WhatsAppCredentials Credentials = new()
    {
        AccessToken = "token-abc",
        PhoneNumberId = "106540352242922",
        AppId = "1234567890",
    };

    [Fact]
    public async Task A_seekable_stream_is_sent_from_where_it_stands_and_left_open()
    {
        var handler = StubHttpMessageHandler.Sequence((HttpStatusCode.OK, Session), (HttpStatusCode.OK, Handle));
        var (templates, _) = Create(handler);

        var file = new MemoryStream([9, 9, 1, 2, 3]);
        file.Position = 2;

        await templates.UploadHeaderSampleAsync(file, "image/png", TestContext.Current.CancellationToken);

        // The length declared to Meta is what is left of the stream, not the whole of it.
        Assert.Contains("file_length=3", handler.Requests[0].RequestUri!.Query, StringComparison.Ordinal);
        Assert.Equal("", handler.Bodies[1]);
        Assert.Equal(3, handler.Requests[1].Content!.Headers.ContentLength);

        // The stream belongs to the caller.
        Assert.True(file.CanRead);
    }

    [Fact]
    public async Task A_retry_sends_the_file_again_rather_than_an_empty_body()
    {
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.OK, Session),
            (HttpStatusCode.InternalServerError, Transient),
            (HttpStatusCode.OK, Handle));
        var (templates, time) = Create(handler);

        var handle = await Clock.RunAsync(
            time,
            templates.UploadHeaderSampleAsync(new MemoryStream([1, 2, 3]), "image/png", TestContext.Current.CancellationToken));

        Assert.Equal("4:aW1hZ2U=", handle);
        Assert.Equal(3, handler.Requests.Count);
        // The first attempt read the stream to its end. Without a rewind the second would
        // upload nothing, and Meta would accept it.
        Assert.Equal("", handler.Bodies[1]);
        Assert.Equal("", handler.Bodies[2]);
    }

    [Fact]
    public async Task A_stream_that_cannot_be_rewound_is_buffered_once_and_can_still_be_retried()
    {
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.OK, Session),
            (HttpStatusCode.InternalServerError, Transient),
            (HttpStatusCode.OK, Handle));
        var (templates, time) = Create(handler);

        await Clock.RunAsync(
            time,
            templates.UploadHeaderSampleAsync(
                new ForwardOnlyStream([1, 2, 3]),
                "image/png",
                TestContext.Current.CancellationToken));

        Assert.Contains("file_length=3", handler.Requests[0].RequestUri!.Query, StringComparison.Ordinal);
        Assert.Equal("", handler.Bodies[1]);
        Assert.Equal("", handler.Bodies[2]);
    }

    [Fact]
    public async Task A_stream_that_cannot_be_rewound_is_refused_before_it_fills_the_memory()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, Session);
        var (templates, _) = Create(handler);

        // Claims to be endless. The upload has to stop reading at the ceiling rather than
        // find out what the process can hold.
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            templates.UploadHeaderSampleAsync(
                new EndlessStream(),
                "application/pdf",
                TestContext.Current.CancellationToken));

        Assert.Equal("content", exception.ParamName);
        Assert.Empty(handler.Requests);
    }

    private static (ITemplatesApi Templates, FakeTimeProvider Time) Create(StubHttpMessageHandler handler)
    {
        var time = new FakeTimeProvider();

        var templates = new TemplatesApi(
            new GraphApiClient(
                new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
                new StubCredentialsProvider(Credentials),
                new InMemoryRateLimiter(time),
                new StaticOptionsMonitor<WhatsAppOptions>(new WhatsAppOptions()),
                time),
            WhatsAppTenant.Default);

        return (templates, time);
    }

    /// <summary>Reads zeros forever, and cannot be asked how long it is.</summary>
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => count;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
