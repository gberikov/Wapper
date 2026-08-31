using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wapper.Webhooks;

namespace Wapper.AspNetCore;

/// <summary>
/// Hands each event to the handlers registered for it.
/// </summary>
/// <remarks>
/// The dispatch is a plain switch over the closed set of event types rather than reflection
/// over open generics, so it survives trimming and works under Native AOT.
/// </remarks>
internal sealed class WhatsAppWebhookDispatcher(ILogger<WhatsAppWebhookDispatcher> logger)
{
    public async Task DispatchAsync(
        IServiceProvider services,
        WhatsAppEvent notification,
        CancellationToken cancellationToken)
    {
        switch (notification)
        {
            case TextMessage message:
                await InvokeAsync(services, message, cancellationToken).ConfigureAwait(false);
                break;
            case MediaMessage message:
                await InvokeAsync(services, message, cancellationToken).ConfigureAwait(false);
                break;
            case LocationMessage message:
                await InvokeAsync(services, message, cancellationToken).ConfigureAwait(false);
                break;
            case ContactsMessage message:
                await InvokeAsync(services, message, cancellationToken).ConfigureAwait(false);
                break;
            case ReactionMessage message:
                await InvokeAsync(services, message, cancellationToken).ConfigureAwait(false);
                break;
            case InteractiveReply message:
                await InvokeAsync(services, message, cancellationToken).ConfigureAwait(false);
                break;
            case TemplateButtonReply message:
                await InvokeAsync(services, message, cancellationToken).ConfigureAwait(false);
                break;
            case SystemMessage message:
                await InvokeAsync(services, message, cancellationToken).ConfigureAwait(false);
                break;
            case UnsupportedMessage message:
                await InvokeAsync(services, message, cancellationToken).ConfigureAwait(false);
                break;
            case MessageStatusChanged status:
                await InvokeAsync(services, status, cancellationToken).ConfigureAwait(false);
                break;
            case WebhookError error:
                await InvokeAsync(services, error, cancellationToken).ConfigureAwait(false);
                break;
            case TemplateStatusChanged template:
                await InvokeAsync(services, template, cancellationToken).ConfigureAwait(false);
                break;
            case TemplateQualityChanged quality:
                await InvokeAsync(services, quality, cancellationToken).ConfigureAwait(false);
                break;
            case PhoneNumberQualityChanged quality:
                await InvokeAsync(services, quality, cancellationToken).ConfigureAwait(false);
                break;
            case PhoneNumberNameChanged name:
                await InvokeAsync(services, name, cancellationToken).ConfigureAwait(false);
                break;
            default:
                logger.LogWarning(
                    "No dispatch is registered for webhook event type {EventType}.",
                    notification.GetType().Name);
                break;
        }

        // A handler registered for a base type sees everything of that shape, which is what
        // a logger or an auditor wants. The concrete handlers run first.
        if (notification is IncomingMessage incoming)
        {
            await InvokeAsync(services, incoming, cancellationToken).ConfigureAwait(false);
        }

        await InvokeAsync(services, notification, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InvokeAsync<TEvent>(
        IServiceProvider services,
        TEvent notification,
        CancellationToken cancellationToken)
        where TEvent : WhatsAppEvent
    {
        foreach (var handler in services.GetServices<IWhatsAppEventHandler<TEvent>>())
        {
            await handler.HandleAsync(notification, cancellationToken).ConfigureAwait(false);
        }
    }
}
