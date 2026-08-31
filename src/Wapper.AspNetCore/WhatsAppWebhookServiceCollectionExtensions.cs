using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wapper.AspNetCore;
using Wapper.Webhooks;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers webhook handling.</summary>
public static class WhatsAppWebhookServiceCollectionExtensions
{
    /// <summary>
    /// Registers the pieces <c>MapWhatsAppWebhook</c> needs, and one handler.
    /// </summary>
    /// <typeparam name="THandler">The handler.</typeparam>
    /// <typeparam name="TEvent">
    /// The event it handles. Use a concrete type such as <see cref="TextMessage"/>, or
    /// <see cref="IncomingMessage"/> or <see cref="WhatsAppEvent"/> to see everything of that
    /// shape.
    /// </typeparam>
    /// <param name="services">The container.</param>
    /// <param name="lifetime">
    /// How long the handler lives. Scoped by default, so it may inject scoped services such
    /// as a database context.
    /// </param>
    /// <remarks>
    /// Several handlers may be registered for the same event; all of them run, in the order
    /// they were registered.
    /// </remarks>
    public static IServiceCollection AddWhatsAppWebhookHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler,
        TEvent>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where THandler : class, IWhatsAppEventHandler<TEvent>
        where TEvent : WhatsAppEvent
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddWhatsAppWebhooks();

        // Enumerable rather than replacing: more than one handler per event is the point.
        services.TryAddEnumerable(
            new ServiceDescriptor(typeof(IWhatsAppEventHandler<TEvent>), typeof(THandler), lifetime));

        return services;
    }

    /// <summary>
    /// Registers the pieces <c>MapWhatsAppWebhook</c> needs, without a handler.
    /// </summary>
    /// <remarks>
    /// Only needed on its own when every handler is registered by hand.
    /// </remarks>
    public static IServiceCollection AddWhatsAppWebhooks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<WhatsAppWebhookDispatcher>();

        return services;
    }
}
