namespace Wapper.Tests.Fakes;

/// <summary>
/// A stream that cannot be wound back, like a network or compression stream.
/// </summary>
/// <remarks>
/// The interesting case for every upload in this library: a body that has already gone to
/// the wire cannot be sent a second time, so the call must not be retried.
/// </remarks>
internal sealed class ForwardOnlyStream(byte[] bytes) : Stream
{
    private readonly MemoryStream _inner = new(bytes);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    // Completed synchronously on purpose. The base class would run Read on the thread pool,
    // and a test winding a fake clock forward while that happens can outrun the timer the
    // code under test has not yet registered.
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        new(_inner.Read(buffer.Span));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromResult(_inner.Read(buffer, offset, count));

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
