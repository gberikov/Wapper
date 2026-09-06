using System.Net.Http.Headers;

namespace Wapper.Internal;

/// <summary>
/// Meta's Resumable Upload API, which hands back a <em>handle</em> rather than a media id.
/// </summary>
/// <remarks>
/// <para>
/// Two calls: one to open a session against the Meta app, one to send the bytes. Nothing
/// about this endpoint looks like the rest of the Graph API — it wants the token under the
/// <c>OAuth</c> scheme instead of <c>Bearer</c>, it takes the body as raw bytes, and it
/// answers with a single-letter field.
/// </para>
/// <para>
/// A handle is what a business profile picture and a template's header sample are set with.
/// It is not interchangeable with a media id from the media endpoint, and it is never sent
/// to a customer.
/// </para>
/// </remarks>
internal static class ResumableUpload
{
    /// <summary>
    /// The most a stream that cannot be rewound is buffered before the upload is refused.
    /// </summary>
    /// <remarks>
    /// The session has to declare the length up front, and a retry has to send the bytes
    /// again, so a forward-only stream is read into memory first. The ceiling is the largest
    /// file the Cloud API accepts for any media at all — a document — rather than a guess at
    /// what a sample or a profile picture may be, so it guards the process and nothing else;
    /// Meta still has the final word on the size. A seekable stream is never buffered.
    /// </remarks>
    internal const long MaxBufferedBytes = 100 * 1024 * 1024;

    /// <summary>Puts a file through the resumable upload and returns the handle it becomes.</summary>
    /// <remarks>
    /// The file name is a label for the session. Meta records it and shows it nowhere, so it
    /// only has to be something. The operation names the two spans this produces.
    /// </remarks>
    public static async Task<string> UploadAsync(
        GraphApiClient client,
        string tenant,
        WhatsAppCredentials credentials,
        Stream content,
        string mimeType,
        string fileName,
        string operation,
        CancellationToken cancellationToken)
    {
        var appId = GraphApiClient.RequireApp(credentials);

        if (content.CanSeek)
        {
            return await UploadSeekableAsync(
                    client, tenant, credentials, appId, content, mimeType, fileName, operation, cancellationToken)
                .ConfigureAwait(false);
        }

        // Read once into memory, and from then on treated like any seekable stream: the
        // buffer is the one copy there is, and a retry rewinds it rather than copying it.
        using var buffer = await BufferAsync(content, cancellationToken).ConfigureAwait(false);

        return await UploadSeekableAsync(
                client, tenant, credentials, appId, buffer, mimeType, fileName, operation, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<string> UploadSeekableAsync(
        GraphApiClient client,
        string tenant,
        WhatsAppCredentials credentials,
        string appId,
        Stream content,
        string mimeType,
        string fileName,
        string operation,
        CancellationToken cancellationToken)
    {
        // What is left of the stream from where the caller positioned it, and where to wind
        // it back to for a retry. The stream stays the caller's: it is neither closed nor
        // copied, only read from here to its end as many times as the upload takes.
        var origin = content.Position;
        var length = content.Length - origin;

        var session = await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    Path = $"{appId}/uploads?file_name={Uri.EscapeDataString(fileName)}" +
                           $"&file_length={length}" +
                           $"&file_type={Uri.EscapeDataString(mimeType)}",
                    Operation = operation,
                },
                WhatsAppJsonContext.Default.UploadSessionResponse,
                cancellationToken)
            .ConfigureAwait(false);

        var sessionId = session.Id ?? throw new WhatsAppException(
            "Meta opened an upload session without returning its id, so there is nowhere to " +
            "send the file.");

        var uploaded = await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    // Already carries its own "upload:" prefix, and came from Meta rather than
                    // from a caller, so it goes into the path as it is.
                    Path = sessionId,
                    Operation = operation,
                    Content = () =>
                    {
                        // A retry starts over from where the caller left the stream. Without
                        // the rewind the second attempt would send an empty file, and Meta
                        // would accept it.
                        content.Position = origin;

                        var body = new StreamContent(new NonClosingStream(content));
                        body.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
                        body.Headers.ContentLength = length;
                        return body;
                    },
                    Configure = request =>
                    {
                        // Bearer is refused here. This endpoint predates the convention the
                        // rest of the Graph API follows.
                        request.Headers.Authorization =
                            new AuthenticationHeaderValue("OAuth", credentials.AccessToken);
                        // Where to resume from. Always the start: the whole file goes up in
                        // one call.
                        request.Headers.TryAddWithoutValidation("file_offset", "0");
                    },
                },
                WhatsAppJsonContext.Default.UploadedFileResponse,
                cancellationToken)
            .ConfigureAwait(false);

        return uploaded.Handle ?? throw new WhatsAppException(
            "Meta accepted the file but returned no handle, so there is nothing to refer to " +
            "it by.");
    }

    /// <summary>
    /// Reads a stream that cannot be rewound into memory, refusing before the ceiling rather
    /// than after the process has run out of it.
    /// </summary>
    private static async Task<MemoryStream> BufferAsync(Stream content, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();

        try
        {
            var chunk = new byte[81920];

            while (true)
            {
                var read = await content.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                if (buffer.Length + read > MaxBufferedBytes)
                {
                    throw new ArgumentException(
                        $"The stream cannot be rewound, so it is read into memory before it is " +
                        $"uploaded, and it is longer than the {MaxBufferedBytes} bytes this " +
                        "client will buffer. Hand in a seekable stream — a file, or a " +
                        "MemoryStream — to upload something this large.",
                        nameof(content));
                }

                buffer.Write(chunk, 0, read);
            }
        }
        catch
        {
            await buffer.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        buffer.Position = 0;
        return buffer;
    }
}
