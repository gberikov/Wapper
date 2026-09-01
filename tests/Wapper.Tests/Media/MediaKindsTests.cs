using Wapper.Media;
using Wapper.Webhooks;

namespace Wapper.Tests.Media;

public class MediaKindsTests
{
    [Theory]
    [InlineData("image/jpeg", IncomingMediaKind.Image)]
    [InlineData("image/png", IncomingMediaKind.Image)]
    // The one nobody guesses. A webp is a sticker, which is its own message type with its own
    // size limit, not a picture.
    [InlineData("image/webp", IncomingMediaKind.Sticker)]
    [InlineData("audio/ogg", IncomingMediaKind.Audio)]
    [InlineData("video/mp4", IncomingMediaKind.Video)]
    [InlineData("application/pdf", IncomingMediaKind.Document)]
    [InlineData("text/plain; charset=utf-8", IncomingMediaKind.Document)]
    public void A_media_type_says_which_kind_of_attachment_it_is(string mimeType, IncomingMediaKind expected) =>
        Assert.Equal(expected, MediaKinds.For(mimeType));

    [Fact]
    public void Nothing_the_api_accepts_has_this_type() =>
        Assert.Null(MediaKinds.For("model/gltf+json"));

    [Theory]
    [InlineData("image/webp", MediaLimits.StickerBytes)]
    [InlineData("image/png", MediaLimits.ImageBytes)]
    [InlineData("audio/ogg", MediaLimits.AudioBytes)]
    [InlineData("video/mp4", MediaLimits.VideoBytes)]
    [InlineData("application/pdf", MediaLimits.DocumentBytes)]
    public void The_limit_follows_from_the_kind(string mimeType, long expected) =>
        Assert.Equal(expected, MediaLimits.For(mimeType));
}
