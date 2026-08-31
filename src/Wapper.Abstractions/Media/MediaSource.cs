namespace Wapper.Media;

/// <summary>
/// Where the media of an outgoing message comes from: something already uploaded, or a
/// public URL Meta fetches itself.
/// </summary>
/// <remarks>
/// Uploading first is the reliable choice. A link is fetched by Meta at send time, so a slow
/// or private host fails the send, and the result is cached for ten minutes — a link whose
/// content changed within that window sends the old file.
/// </remarks>
public readonly record struct MediaSource
{
    private MediaSource(string? id, Uri? link)
    {
        Id = id;
        Link = link;
    }

    /// <summary>Identifier of media already uploaded, or <see langword="null"/> for a link.</summary>
    public string? Id { get; }

    /// <summary>Public URL Meta fetches, or <see langword="null"/> for uploaded media.</summary>
    public Uri? Link { get; }

    /// <summary>Media already uploaded through the media endpoint.</summary>
    /// <remarks>
    /// Uploaded media lives 30 days. An id that arrived on a webhook expires after 7.
    /// </remarks>
    public static MediaSource FromId(string mediaId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);
        return new MediaSource(mediaId, null);
    }

    /// <summary>Media Meta downloads from a public URL when the message is sent.</summary>
    public static MediaSource FromLink(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (!link.IsAbsoluteUri)
        {
            throw new ArgumentException("The media link must be an absolute URL.", nameof(link));
        }

        return new MediaSource(null, link);
    }

    /// <inheritdoc cref="FromLink(Uri)" />
    public static MediaSource FromLink(string link) => FromLink(new Uri(link, UriKind.Absolute));

    /// <inheritdoc />
    public override string ToString() => Id ?? Link?.ToString() ?? string.Empty;
}
