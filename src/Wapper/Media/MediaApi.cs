using System.Net.Http.Headers;
using Wapper.Internal;

namespace Wapper.Media;

/// <summary>Uploading, locating, downloading and deleting media for one tenant.</summary>
internal sealed class MediaApi(GraphApiClient client, string tenant) : IMediaApi
{
    public async Task<string> UploadAsync(
        Stream content,
        string mimeType,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        GuardSize(content, mimeType);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        // A retry has to send the file again, which is only possible if the stream can be
        // wound back. When it cannot, one attempt is all there is: a second would upload
        // nothing at all.
        var rewindable = content.CanSeek;
        var origin = rewindable ? content.Position : 0L;

        var response = await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    Path = $"{credentials.PhoneNumberId}/media",
                    Retryable = rewindable,
                    Content = () => BuildUpload(content, mimeType, fileName, rewindable, origin),
                },
                WhatsAppJsonContext.Default.MediaIdResponse,
                cancellationToken)
            .ConfigureAwait(false);

        return response.Id ?? throw new WhatsAppException(
            "The Cloud API accepted the upload but returned no media id.");
    }

    public async Task<MediaInfo> GetAsync(
        string mediaId,
        CancellationToken cancellationToken = default)
    {
        // An id from a webhook, or from a caller's own store, goes straight into the path.
        var id = GraphApiClient.PathSegment(mediaId);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        var response = await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Get,
                    // Scoping the lookup to the phone number is what stops one tenant reading
                    // another tenant's media on a shared business account.
                    Path = $"{id}?phone_number_id={Uri.EscapeDataString(credentials.PhoneNumberId)}",
                },
                WhatsAppJsonContext.Default.MediaInfoResponse,
                cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(response.Url))
        {
            throw new WhatsAppException($"The Cloud API returned no download URL for media '{mediaId}'.");
        }

        return new MediaInfo
        {
            Id = response.Id ?? mediaId,
            Url = new Uri(response.Url, UriKind.Absolute),
            MimeType = response.MimeType,
            FileSize = response.FileSize,
            Sha256 = response.Sha256,
        };
    }

    public async Task<MediaContent> DownloadAsync(
        string mediaId,
        CancellationToken cancellationToken = default)
    {
        var media = await GetAsync(mediaId, cancellationToken).ConfigureAwait(false);
        return await DownloadAsync(media, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MediaContent> DownloadAsync(
        MediaInfo media,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(media);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        var response = await client.FetchAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Get,
                    Path = media.Url.AbsoluteUri,
                },
                media.Url,
                cancellationToken)
            .ConfigureAwait(false);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        return new MediaContent(
            new ResponseOwningStream(stream, response),
            response.Content.Headers.ContentType?.MediaType ?? media.MimeType,
            response.Content.Headers.ContentLength ?? (media.FileSize > 0 ? media.FileSize : null));
    }

    public async Task<bool> DeleteAsync(
        string mediaId,
        CancellationToken cancellationToken = default)
    {
        var id = GraphApiClient.PathSegment(mediaId);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        var response = await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Delete,
                    Path = $"{id}?phone_number_id={Uri.EscapeDataString(credentials.PhoneNumberId)}",
                },
                WhatsAppJsonContext.Default.SuccessResponse,
                cancellationToken)
            .ConfigureAwait(false);

        return response.Success;
    }

    private static void GuardSize(Stream content, string mimeType)
    {
        if (!content.CanSeek)
        {
            return;
        }

        var limit = MediaLimits.For(mimeType);
        var length = content.Length - content.Position;

        if (limit is { } maximum && length > maximum)
        {
            throw new ArgumentException(
                $"The file is {length} bytes, and the Cloud API accepts at most {maximum} for " +
                $"'{mimeType}'. Sending it would fail after the whole file had been uploaded.",
                nameof(content));
        }
    }

    private static MultipartFormDataContent BuildUpload(
        Stream content,
        string mimeType,
        string? fileName,
        bool rewindable,
        long origin)
    {
        if (rewindable)
        {
            content.Position = origin;
        }

        // The stream belongs to the caller. Handing it to StreamContent directly would see it
        // closed when the request message is disposed, which both breaks the retry and takes
        // away a stream the caller may still be using.
        var file = new StreamContent(new NonClosingStream(content));
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);

        return new MultipartFormDataContent
        {
            { new StringContent("whatsapp"), "messaging_product" },
            { new StringContent(mimeType), "type" },
            { file, "file", fileName ?? "file" },
        };
    }
}
