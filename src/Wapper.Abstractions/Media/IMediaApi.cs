namespace Wapper.Media;

/// <summary>Uploading, locating, downloading and deleting media.</summary>
public interface IMediaApi
{
    /// <summary>Uploads a file and returns the id to send it with.</summary>
    /// <param name="content">The bytes. Read once, from the current position.</param>
    /// <param name="mimeType">
    /// Media type, for example <c>image/jpeg</c>. Meta accepts a fixed list and rejects the
    /// rest, so this is not a free-form field.
    /// </param>
    /// <param name="fileName">
    /// Name to record. Shown to the recipient for documents, and ignored for everything else.
    /// </param>
    /// <param name="cancellationToken">Cancels the upload.</param>
    /// <returns>The media id. Valid for 30 days.</returns>
    /// <exception cref="ArgumentException">The file is larger than Meta accepts for its type.</exception>
    Task<string> UploadAsync(
        Stream content,
        string mimeType,
        string? fileName = null,
        CancellationToken cancellationToken = default);

    /// <summary>Looks up where a piece of media can be downloaded from.</summary>
    /// <remarks>The URL it returns is valid for five minutes.</remarks>
    Task<MediaInfo> GetAsync(string mediaId, CancellationToken cancellationToken = default);

    /// <summary>Downloads media by id, looking up its URL first.</summary>
    /// <remarks>
    /// <para>The result owns a network response and has to be disposed.</para>
    /// <para>
    /// <strong>Nothing here caps how much is read.</strong> An upload is checked against
    /// <see cref="MediaLimits"/> and refused before it is sent; a download is a stream, and a
    /// stream that is capped is not one. So the ceiling is the caller's: copy with a limit,
    /// or write to a bounded buffer. <see cref="MediaContent.FileSize"/> and the
    /// <c>Content-Length</c> it comes from are what the server said, not a promise — read
    /// them as a hint for sizing, never as the amount that will arrive.
    /// </para>
    /// </remarks>
    Task<MediaContent> DownloadAsync(string mediaId, CancellationToken cancellationToken = default);

    /// <summary>Downloads media whose location is already known.</summary>
    /// <remarks>
    /// Use this when the URL came from a recent <see cref="GetAsync"/>; it is only good for
    /// five minutes. The result owns a network response and has to be disposed, and the
    /// ceiling on how much is read is the caller's — see
    /// <see cref="DownloadAsync(string, CancellationToken)"/>.
    /// </remarks>
    Task<MediaContent> DownloadAsync(MediaInfo media, CancellationToken cancellationToken = default);

    /// <summary>Deletes uploaded media.</summary>
    Task<bool> DeleteAsync(string mediaId, CancellationToken cancellationToken = default);
}
