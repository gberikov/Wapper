using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Wapper.Internal;

/// <summary>Builds the request bodies the Graph API takes.</summary>
internal static class GraphContent
{
    /// <summary>
    /// A JSON body, serialized once however many attempts it takes.
    /// </summary>
    /// <remarks>
    /// A retry needs a fresh <see cref="HttpContent"/> — the first one has already been read
    /// to the wire and cannot be rewound — but it does not need fresh bytes. Serializing out
    /// here rather than inside the factory does the work once, and gives the request a
    /// <c>Content-Length</c> instead of chunking a body that is always small enough to count.
    /// </remarks>
    public static Func<HttpContent> Json<TPayload>(TPayload payload, JsonTypeInfo<TPayload> typeInfo)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, typeInfo);

        return () => new ByteArrayContent(bytes)
        {
            Headers =
            {
                ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" },
            },
        };
    }
}
