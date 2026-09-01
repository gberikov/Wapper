using Wapper.Webhooks;

namespace Wapper.Media;

/// <summary>
/// The upload sizes Meta documents, checked before a file is sent rather than after.
/// </summary>
/// <remarks>
/// Pushing 100 MB up the wire to be told it was 84 MB too large is a slow way to find out,
/// and the error that comes back does not say which limit was passed.
/// </remarks>
public static class MediaLimits
{
    /// <summary>aac, amr, mpeg, mp4 and ogg audio.</summary>
    public const long AudioBytes = 16 * 1024 * 1024;

    /// <summary>pdf and the Office formats.</summary>
    public const long DocumentBytes = 100 * 1024 * 1024;

    /// <summary>jpeg and png.</summary>
    public const long ImageBytes = 5 * 1024 * 1024;

    /// <summary>
    /// webp stickers. Meta allows 100 KB for a static sticker and 500 KB for an animated
    /// one, and nothing in the media type says which this is, so the larger applies and the
    /// server has the final word.
    /// </summary>
    public const long StickerBytes = 500 * 1024;

    /// <summary>3gpp and mp4 video.</summary>
    public const long VideoBytes = 16 * 1024 * 1024;

    /// <summary>The largest upload Meta accepts for the given media type.</summary>
    /// <returns>The limit in bytes, or <see langword="null"/> when the type is unknown.</returns>
    /// <remarks>
    /// Which limit applies follows from which kind of attachment the type is, so it is asked
    /// of <see cref="MediaKinds.For"/> rather than worked out a second time here.
    /// </remarks>
    public static long? For(string mimeType) => MediaKinds.For(mimeType) switch
    {
        IncomingMediaKind.Sticker => StickerBytes,
        IncomingMediaKind.Audio => AudioBytes,
        IncomingMediaKind.Image => ImageBytes,
        IncomingMediaKind.Video => VideoBytes,
        IncomingMediaKind.Document => DocumentBytes,
        _ => null,
    };
}
