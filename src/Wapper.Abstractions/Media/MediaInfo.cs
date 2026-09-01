namespace Wapper.Media;

/// <summary>What the Cloud API knows about a piece of media.</summary>
public sealed record MediaInfo
{
    /// <summary>Identifier of the media.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Where to download it from.
    /// </summary>
    /// <remarks>
    /// Valid for five minutes, on a host of Meta's choosing rather than the Graph API, and
    /// it still requires the access token — fetching it without one returns 404.
    /// </remarks>
    public required Uri Url { get; init; }

    /// <summary>Media type, for example <c>image/jpeg</c>.</summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// Size in bytes, as the platform reported it.
    /// </summary>
    /// <remarks>
    /// Good for sizing a buffer and not for trusting: nothing checks the download against it,
    /// and a stream that runs longer than this is not an error anything here will raise.
    /// </remarks>
    public long FileSize { get; init; }

    /// <summary>Checksum Meta computed for the file.</summary>
    public string? Sha256 { get; init; }
}

/// <summary>Downloaded media, and what it turned out to be.</summary>
/// <remarks>
/// Owns the underlying network response. Dispose it, or the connection stays checked out of
/// the pool.
/// </remarks>
public sealed class MediaContent(Stream content, string? mimeType, long? fileSize)
    : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// The bytes. Read once, forward only.
    /// </summary>
    /// <remarks>
    /// Unbounded. Copy it with a ceiling of your own — nothing here stops a response that
    /// keeps going, and <see cref="FileSize"/> is not a promise that it will not.
    /// </remarks>
    public Stream Content { get; } = content;

    /// <summary>Media type reported by the server.</summary>
    public string? MimeType { get; } = mimeType;

    /// <summary>
    /// Length in bytes, when the server said.
    /// </summary>
    /// <remarks>
    /// The <c>Content-Length</c> header, falling back to what the lookup reported. Neither is
    /// checked against what actually arrives, so treat it as a hint for sizing a buffer
    /// rather than as the amount to expect.
    /// </remarks>
    public long? FileSize { get; } = fileSize;

    /// <inheritdoc />
    public void Dispose() => Content.Dispose();

    /// <summary>
    /// Releases the underlying response without blocking the thread on the last of the
    /// socket teardown, which is what <c>await using</c> gets you over <c>using</c>.
    /// </summary>
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
