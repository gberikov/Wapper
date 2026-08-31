using System.Buffers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wapper.Webhooks;

namespace Wapper.AspNetCore;

/// <summary>Maps the endpoint the Cloud API posts to.</summary>
public static class WhatsAppWebhookEndpointExtensions
{
    /// <summary>
    /// A webhook body is a handful of kilobytes. Reading an unbounded one from a public
    /// endpoint is an invitation, and the signature cannot be checked until the whole body
    /// has been read.
    /// </summary>
    private const int MaxBodyBytes = 1024 * 1024;

    /// <summary>
    /// Maps the webhook endpoint: the verification handshake on GET, and deliveries on POST.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">Where to map it, for example <c>/whatsapp</c>.</param>
    /// <param name="tenant">
    /// Which tenant's app secret and verify token to check against. Deliveries for several
    /// phone numbers can arrive on one endpoint, and every event carries its own
    /// <see cref="WhatsAppEvent.PhoneNumberId"/>, so a host serving many accounts either maps
    /// one endpoint per tenant or matches on that.
    /// </param>
    /// <returns>The mapped endpoints, so authorization and metadata can be chained on.</returns>
    public static IEndpointConventionBuilder MapWhatsAppWebhook(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/whatsapp",
        string tenant = WhatsAppTenant.Default)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(pattern);

        // Mapped as RequestDelegate rather than as a minimal-API lambda on purpose: the
        // lambda overloads bind parameters by reflection, which the trimming and AOT
        // analysers rightly refuse in a library that claims to be compatible with both.
        group.MapGet("/", (RequestDelegate)(context =>
            Verify(context, tenant).ExecuteAsync(context)));

        group.MapPost("/", (RequestDelegate)(async context =>
            await (await ReceiveAsync(context, tenant).ConfigureAwait(false))
                .ExecuteAsync(context)
                .ConfigureAwait(false)));

        return group;
    }

    private static IResult Verify(HttpContext context, string tenant)
    {
        var options = context.RequestServices
            .GetRequiredService<IOptionsMonitor<WhatsAppOptions>>()
            .Get(tenant);

        if (string.IsNullOrEmpty(options.WebhookVerifyToken))
        {
            Logger(context).LogError(
                "A webhook verification arrived for tenant '{Tenant}' but no WebhookVerifyToken " +
                "is configured, so the subscription cannot be confirmed.",
                tenant);

            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        var query = context.Request.Query;

        if (query["hub.mode"] != "subscribe"
            || !WhatsAppWebhookSignature.IsVerifyTokenValid(query["hub.verify_token"], options.WebhookVerifyToken))
        {
            // A plain status rather than Results.Forbid(): that one runs the authentication
            // stack, which a webhook endpoint has no reason to have configured.
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // Meta wants the challenge echoed back as bare text. Anything else, including a JSON
        // string, fails the subscription.
        return Results.Text(query["hub.challenge"].ToString());
    }

    private static async Task<IResult> ReceiveAsync(HttpContext context, string tenant)
    {
        var logger = Logger(context);
        var options = context.RequestServices
            .GetRequiredService<IOptionsMonitor<WhatsAppOptions>>()
            .Get(tenant);

        if (string.IsNullOrEmpty(options.AppSecret))
        {
            // Refusing is the only safe answer. Accepting unsigned deliveries would let anyone
            // who learns the URL feed events into the application.
            logger.LogError(
                "A webhook delivery arrived for tenant '{Tenant}' but no AppSecret is " +
                "configured, so it cannot be verified.",
                tenant);

            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        var body = await ReadBodyAsync(context).ConfigureAwait(false);
        if (body is null)
        {
            logger.LogWarning("A webhook delivery was larger than {MaxBytes} bytes.", MaxBodyBytes);
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var signature = context.Request.Headers[WhatsAppWebhookSignature.HeaderName].ToString();

        // Checked against the bytes as they arrived. Re-serializing a parsed model would
        // change whitespace or ordering and produce a different digest.
        if (!WhatsAppWebhookSignature.IsValid(body.WrittenSpan, signature, options.AppSecret))
        {
            logger.LogWarning("A webhook delivery failed signature verification and was rejected.");
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        IReadOnlyList<WhatsAppEvent> events;

        try
        {
            events = WhatsAppWebhookParser.Parse(body.WrittenSpan);
        }
        catch (WhatsAppException exception)
        {
            logger.LogError(exception, "A signed webhook delivery could not be parsed.");

            // Signed, so it did come from Meta; something in it is simply new or malformed.
            // Answering with an error would have Meta redeliver it for up to seven days.
            return Results.Ok();
        }

        return await DispatchAsync(context, events, logger).ConfigureAwait(false);
    }

    /// <summary>
    /// Hands every event to its handlers, and reports whether they all got through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One delivery carries many events. A handler that throws on the third must not stop the
    /// fourth from being seen, so each is dispatched on its own and a failure is logged and
    /// stepped over — otherwise one poisonous event silently costs you every event behind it.
    /// </para>
    /// <para>
    /// The delivery is still failed at the end, because Meta redelivering it is the only
    /// retry there is and swallowing the failure would lose the message for good. That does
    /// mean the events that did succeed are delivered again on the retry, so handlers have to
    /// be idempotent — which they have to be regardless, since Meta repeats deliveries of its
    /// own accord.
    /// </para>
    /// </remarks>
    private static async Task<IResult> DispatchAsync(
        HttpContext context,
        IReadOnlyList<WhatsAppEvent> events,
        ILogger logger)
    {
        var dispatcher = context.RequestServices.GetRequiredService<WhatsAppWebhookDispatcher>();
        var failed = 0;

        foreach (var notification in events)
        {
            try
            {
                await dispatcher
                    .DispatchAsync(context.RequestServices, notification, context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The caller hung up. Nothing left to answer.
                throw;
            }
            catch (Exception exception)
            {
                failed++;

                logger.LogError(
                    exception,
                    "A handler for webhook event {EventType} threw. The rest of the delivery is " +
                    "still being processed, and the delivery will be failed so Meta repeats it.",
                    notification.GetType().Name);
            }
        }

        return failed == 0
            ? Results.Ok()
            : Results.StatusCode(StatusCodes.Status500InternalServerError);
    }

    /// <summary>
    /// Reads the whole body, or gives up if it is too large.
    /// </summary>
    /// <returns>
    /// The bytes, or <see langword="null"/> when the limit was passed. Handed back as the
    /// writer rather than as an array: the signature check and the parser both take a span,
    /// so copying up to a megabyte out of it would buy nothing.
    /// </returns>
    private static async Task<ArrayBufferWriter<byte>?> ReadBodyAsync(HttpContext context)
    {
        var reader = context.Request.BodyReader;
        var buffer = new ArrayBufferWriter<byte>(4096);

        while (true)
        {
            var result = await reader.ReadAsync(context.RequestAborted).ConfigureAwait(false);
            var sequence = result.Buffer;

            foreach (var segment in sequence)
            {
                if (buffer.WrittenCount + segment.Length > MaxBodyBytes)
                {
                    reader.AdvanceTo(sequence.Start, sequence.End);
                    return null;
                }

                buffer.Write(segment.Span);
            }

            reader.AdvanceTo(sequence.End);

            if (result.IsCompleted)
            {
                return buffer;
            }
        }
    }

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(WhatsAppWebhookEndpointExtensions));
}
