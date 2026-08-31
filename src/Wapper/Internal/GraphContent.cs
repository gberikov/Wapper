using System.Net.Http.Headers;
using System.Text;
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
    public static Func<HttpContent> Json<TPayload>(TPayload payload, JsonTypeInfo<TPayload> typeInfo) =>
        Json(JsonSerializer.SerializeToUtf8Bytes(payload, typeInfo));

    /// <summary>
    /// A JSON body the caller wrote out itself, for the endpoints this library does not model.
    /// </summary>
    public static Func<HttpContent> Json(string json) => Json(Encoding.UTF8.GetBytes(json));

    private static Func<HttpContent> Json(byte[] bytes) =>
        () => new ByteArrayContent(bytes)
        {
            Headers =
            {
                ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" },
            },
        };

    /// <summary>
    /// A form-encoded body, for the handful of endpoints that take one instead of JSON.
    /// </summary>
    public static Func<HttpContent> Form(params (string Name, string Value)[] fields)
    {
        var encoded = string.Join(
            '&',
            fields.Select(field =>
                $"{Uri.EscapeDataString(field.Name)}={Uri.EscapeDataString(field.Value)}"));

        var bytes = Encoding.UTF8.GetBytes(encoded);

        return () => new ByteArrayContent(bytes)
        {
            Headers =
            {
                ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded"),
            },
        };
    }
}
