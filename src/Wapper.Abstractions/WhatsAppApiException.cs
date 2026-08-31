using System.Net;

namespace Wapper;

/// <summary>
/// The Cloud API rejected the request and returned an error object.
/// </summary>
/// <remarks>
/// Branch on <see cref="WhatsAppError.Code"/>. The HTTP status code is recorded on
/// <see cref="StatusCode"/> for diagnostics only: Meta documents it as unstable, and in
/// practice most WhatsApp failures arrive as <c>400 Bad Request</c> regardless of cause.
/// </remarks>
public class WhatsAppApiException : WhatsAppException
{
    /// <summary>Creates the exception from a parsed error object.</summary>
    public WhatsAppApiException(WhatsAppError error, HttpStatusCode statusCode, TimeSpan? retryAfter = null)
        : base(error.ToString())
    {
        Error = error;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    /// <summary>The error object returned by the Cloud API.</summary>
    public WhatsAppError Error { get; }

    /// <summary>The HTTP status the error arrived with. Diagnostic only.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The <c>Retry-After</c> the response carried, when it carried one.
    /// </summary>
    /// <remarks>
    /// The Cloud API does not document this header and normally does not send it, so this is
    /// usually <see langword="null"/>. It is read opportunistically because a gateway or a
    /// proxy in front of Meta may add one, and never relied upon: the backoff falls back to
    /// Meta's documented <c>4^X</c> seconds.
    /// </remarks>
    public TimeSpan? RetryAfter { get; }

    /// <summary>Shorthand for <see cref="WhatsAppError.Code"/>.</summary>
    public int Code => Error.Code;
}
