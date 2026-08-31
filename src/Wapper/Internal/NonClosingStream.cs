namespace Wapper.Internal;

/// <summary>
/// Passes reads through to another stream but refuses to close it.
/// </summary>
/// <remarks>
/// <see cref="HttpRequestMessage"/> disposes its content, and <see cref="StreamContent"/>
/// disposes the stream it was given. For an upload that stream belongs to the caller, who
/// may still want it — and a retry has to rewind it, which a closed stream cannot do.
/// </remarks>
internal sealed class NonClosingStream(Stream inner) : Stream
{
    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override void Flush() => inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) =>
        inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => inner.Read(buffer);

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        inner.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

    protected override void Dispose(bool disposing)
    {
        // Deliberately does not touch the inner stream.
    }
}
