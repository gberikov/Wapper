using System.Net;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Options;
using Wapper.Internal;
using Wapper.RateLimiting;

namespace Wapper.Benchmarks;

/// <summary>
/// What the resumable upload costs in memory for a seekable stream, which is sent in place,
/// and for one that cannot be rewound, which is read into memory once first.
/// </summary>
/// <remarks>
/// The allocated bytes column is the one to read: a seekable file of any size should cost a
/// few kilobytes of request plumbing, and a forward-only stream about its own length — never
/// twice it.
/// </remarks>
[MemoryDiagnoser]
[ShortRunJob]
public class UploadBufferingBenchmarks
{
    private static readonly WhatsAppCredentials Credentials = new()
    {
        AccessToken = "token",
        PhoneNumberId = "106540352242922",
        AppId = "1234567890",
    };

    private GraphApiClient _client = null!;
    private byte[] _file = [];

    /// <summary>File size in bytes.</summary>
    [Params(64 * 1024, 4 * 1024 * 1024)]
    public int Bytes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _file = new byte[Bytes];
        Random.Shared.NextBytes(_file);

        _client = new GraphApiClient(
            new HttpClient(new DrainingHandler()) { Timeout = Timeout.InfiniteTimeSpan },
            new FixedCredentials(),
            new InMemoryRateLimiter(TimeProvider.System),
            new FixedOptions(),
            TimeProvider.System);
    }

    [Benchmark(Baseline = true)]
    public Task<string> Seekable() =>
        ResumableUpload.UploadAsync(
            _client, WhatsAppTenant.Default, Credentials, new MemoryStream(_file), "image/png", "f", "bench", default);

    [Benchmark]
    public Task<string> ForwardOnly() =>
        ResumableUpload.UploadAsync(
            _client, WhatsAppTenant.Default, Credentials, new ForwardOnlyStream(_file), "image/png", "f", "bench", default);

    /// <summary>Reads the body to its end and answers like the upload endpoint.</summary>
    private sealed class DrainingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                await using var body = await request.Content.ReadAsStreamAsync(cancellationToken);
                await body.CopyToAsync(Stream.Null, cancellationToken);
            }

            var reply = request.RequestUri!.AbsolutePath.Contains("/uploads", StringComparison.Ordinal)
                ? """{"id":"upload:MTphdHRhY2htZW50"}"""
                : """{"h":"4:aW1hZ2U="}""";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(reply, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class ForwardOnlyStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

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

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _inner.Read(buffer);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FixedCredentials : IWhatsAppCredentialsProvider
    {
        public ValueTask<WhatsAppCredentials> GetCredentialsAsync(string tenant, CancellationToken cancellationToken = default) =>
            new(Credentials);
    }

    private sealed class FixedOptions : IOptionsMonitor<WhatsAppOptions>
    {
        public WhatsAppOptions CurrentValue { get; } = new();

        public WhatsAppOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<WhatsAppOptions, string?> listener) => null;
    }
}
