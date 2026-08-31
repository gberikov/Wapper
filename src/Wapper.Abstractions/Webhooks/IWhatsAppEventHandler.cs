namespace Wapper.Webhooks;

/// <summary>
/// Handles one kind of webhook event.
/// </summary>
/// <typeparam name="TEvent">
/// The event to handle. Register a handler for a concrete type such as
/// <see cref="TextMessage"/>, or for <see cref="IncomingMessage"/> or
/// <see cref="WhatsAppEvent"/> to see everything of that shape.
/// </typeparam>
/// <remarks>
/// <para>
/// Handlers are resolved from the container for each delivery, so a scoped handler gets a
/// scope of its own and may inject scoped services.
/// </para>
/// <para>
/// Meta expects the webhook to answer quickly — a median under 250 ms, with the endpoint
/// able to take three times the outgoing message volume — and retries anything that fails
/// for up to seven days. Long work belongs on a queue, not in a handler.
/// </para>
/// </remarks>
public interface IWhatsAppEventHandler<in TEvent>
    where TEvent : WhatsAppEvent
{
    /// <summary>Handles one event.</summary>
    Task HandleAsync(TEvent notification, CancellationToken cancellationToken = default);
}
