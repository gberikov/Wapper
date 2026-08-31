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

        // The session has to declare the length up front, and a retry has to be able to send
        // the bytes again. Both are answered by reading the file into memory once — these are
        // pictures and template samples, not the hundred-megabyte documents the media
        // endpoint streams.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var bytes = buffer.ToArray();

        var session = await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    Path = $"{appId}/uploads?file_name={Uri.EscapeDataString(fileName)}" +
                           $"&file_length={bytes.Length}" +
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
                        var body = new ByteArrayContent(bytes);
                        body.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
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
}
