using Wapper.Webhooks;

namespace Wapper.Media;

/// <summary>
/// Which kind of attachment a media type is, the way the Cloud API sorts them.
/// </summary>
/// <remarks>
/// The mapping is not quite the obvious one — <c>image/webp</c> is a sticker rather than an
/// image, and a sticker is a different message type with a different size limit — so it is
/// held here rather than rewritten by everything that sends a file.
/// </remarks>
public static class MediaKinds
{
    /// <summary>
    /// Which kind of attachment the given media type is.
    /// </summary>
    /// <param name="mimeType">
    /// The media type, with or without parameters: <c>text/plain; charset=utf-8</c> is read
    /// as <c>text/plain</c>.
    /// </param>
    /// <returns>
    /// The kind, or <see langword="null"/> when nothing the Cloud API accepts has that type.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <see cref="IncomingMediaKind"/> is the same enum a webhook delivers, on purpose: what
    /// arrives and what is sent are the same five kinds, and a second enum saying the same
    /// thing would only have to be mapped onto this one.
    /// </para>
    /// <para>
    /// Nothing in the media type distinguishes a static sticker from an animated one, and
    /// Meta allows them different sizes — 100 KB and 500 KB. See
    /// <see cref="MediaLimits.StickerBytes"/> for which of the two this library checks
    /// against.
    /// </para>
    /// </remarks>
    public static IncomingMediaKind? For(string mimeType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        // Anything after a semicolon is a parameter, as in "text/plain; charset=utf-8".
        var separator = mimeType.IndexOf(';');
        var type = (separator < 0 ? mimeType : mimeType[..separator]).Trim();

        if (type.Equals("image/webp", StringComparison.OrdinalIgnoreCase))
        {
            return IncomingMediaKind.Sticker;
        }

        if (type.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return IncomingMediaKind.Audio;
        }

        if (type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return IncomingMediaKind.Image;
        }

        if (type.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return IncomingMediaKind.Video;
        }

        return type.StartsWith("application/", StringComparison.OrdinalIgnoreCase)
               || type.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            ? IncomingMediaKind.Document
            : null;
    }
}
