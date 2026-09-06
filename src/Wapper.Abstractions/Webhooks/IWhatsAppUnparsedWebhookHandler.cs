namespace Wapper.Webhooks;

/// <summary>
/// Takes custody of a signed delivery the parser could not read.
/// </summary>
/// <remarks>
/// <para>
/// The delivery came from Meta — its signature checked out — and something in it is new or
/// malformed enough that <c>WhatsAppWebhookParser</c> refused it whole. Answering Meta with
/// an error would have it redelivered for up to seven days and refused every time, so the
/// endpoint acknowledges it; but acknowledging is also how the delivery is lost for good,
/// unless something keeps the body. That something is this handler.
/// </para>
/// <para>
/// Store the body somewhere durable and return. Once a newer version of this library — or
/// your own reading of it — can parse it, replay it through <c>WhatsAppWebhookParser</c>.
/// Throw, and the delivery is failed so Meta sends it again: right when the store is down,
/// wrong as a way of retrying the parse, which will fail the same way every time.
/// </para>
/// <para>
/// Without a handler registered, the endpoint logs the failure — the error, never the body,
/// which carries messages and personal data — and acknowledges the delivery.
/// </para>
/// </remarks>
public interface IWhatsAppUnparsedWebhookHandler
{
    /// <summary>Keeps one delivery the parser could not read.</summary>
    /// <param name="delivery">The delivery, verified and unread.</param>
    /// <param name="cancellationToken">The request's own token.</param>
    Task HandleAsync(WhatsAppUnparsedWebhook delivery, CancellationToken cancellationToken = default);
}

/// <summary>A signed delivery the parser could not read.</summary>
public sealed class WhatsAppUnparsedWebhook
{
    /// <summary>
    /// The body, exactly as it arrived and as the signature was checked against it.
    /// </summary>
    /// <remarks>
    /// Valid for the duration of the handler call only: the buffer belongs to the request.
    /// Copy it to keep it.
    /// </remarks>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>The tenant whose app secret the signature verified against.</summary>
    public required string Tenant { get; init; }

    /// <summary>Why the parser refused it.</summary>
    public required WhatsAppException Error { get; init; }
}
