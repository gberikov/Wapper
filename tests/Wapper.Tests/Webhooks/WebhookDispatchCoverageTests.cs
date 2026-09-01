using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wapper.AspNetCore;
using Wapper.Tests.Fakes;
using Wapper.Webhooks;

namespace Wapper.Tests.Webhooks;

/// <summary>
/// The dispatch is a switch over a closed set of event types, so that it survives trimming.
/// A type added to the set and forgotten here reaches handlers registered for a base type and
/// no others — which looks exactly like a handler that is never called.
/// </summary>
public class WebhookDispatchCoverageTests
{
    public static TheoryData<Type> EventTypes()
    {
        var data = new TheoryData<Type>();

        foreach (var type in typeof(WhatsAppEvent).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true })
            .Where(typeof(WhatsAppEvent).IsAssignableFrom)
            .OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EventTypes))]
    public async Task Every_event_type_has_a_dispatch_of_its_own(Type eventType)
    {
        var logger = new RecordingLogger<WhatsAppWebhookDispatcher>();
        var services = new ServiceCollection().BuildServiceProvider();
        var notification = (WhatsAppEvent)Activator.CreateInstance(eventType)!;

        await new WhatsAppWebhookDispatcher(logger)
            .DispatchAsync(services, notification, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(logger.Lines, line => line.Level == LogLevel.Warning);
    }
}
