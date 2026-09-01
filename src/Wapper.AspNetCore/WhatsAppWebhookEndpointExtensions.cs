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
    /// Which tenant's app secret and verify token to check against. Every tenant on one Meta
    /// app shares an app secret, so one endpoint serves all of them; tenants on separate apps
    /// have separate secrets, and want either an endpoint each or
    /// <see cref="MapWhatsAppWebhookForTenants"/>.
    /// </param>
    /// <returns>The mapped endpoints, so authorization and metadata can be chained on.</returns>
    public static IEndpointConventionBuilder MapWhatsAppWebhook(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/whatsapp",
        string tenant = WhatsAppTenant.Default)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return Map(endpoints, pattern, tenant);
    }

    /// <summary>
    /// Maps one webhook endpoint for every tenant, checking each delivery against the app
    /// secret of the tenant it turns out to be for.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">Where to map it, for example <c>/whatsapp</c>.</param>
    /// <returns>The mapped endpoints, so authorization and metadata can be chained on.</returns>
    /// <remarks>
    /// <para>
    /// For a host whose tenants are on more than one Meta app, and therefore have more than
    /// one app secret. <see cref="MapWhatsAppWebhook"/> checks every delivery against one
    /// tenant's secret, which is correct and enough when they share an app.
    /// </para>
    /// <para>
    /// Which tenant a delivery is for comes from
    /// <see cref="IWhatsAppWebhookTenantResolver"/>, whose default implementation matches the
    /// numbers and accounts in configuration. A host keeping its tenants in a database
    /// registers its own.
    /// </para>
    /// <para>
    /// The subscription handshake is checked against the default tenant's
    /// <see cref="WhatsAppOptions.WebhookVerifyToken"/>: a <c>GET</c> names no number, so
    /// there is nothing to resolve by. The verify token is a value you choose rather than one
    /// Meta issues, so one shared across tenants costs nothing.
    /// </para>
    /// </remarks>
    public static IEndpointConventionBuilder MapWhatsAppWebhookForTenants(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/whatsapp")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return Map(endpoints, pattern, tenant: null);
    }

    /// <summary>
    /// Maps the two routes. A <paramref name="tenant"/> of <see langword="null"/> is the
    /// mode that resolves one per delivery.
    /// </summary>
    private static IEndpointConventionBuilder Map(
        IEndpointRouteBuilder endpoints,
        string pattern,
        string? tenant)
    {
        var group = endpoints.MapGroup(pattern);

        // Mapped as RequestDelegate rather than as a minimal-API lambda on purpose: the
        // lambda overloads bind parameters by reflection, which the trimming and AOT
        // analysers rightly refuse in a library that claims to be compatible with both.
        group.MapGet("/", (RequestDelegate)(context =>
            Verify(context, tenant ?? WhatsAppTenant.Default).ExecuteAsync(context)));

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

    private static async Task<IResult> ReceiveAsync(HttpContext context, string? tenant)
    {
        var logger = Logger(context);

        // Read before the tenant is known, because in the resolving mode the body is the only
        // thing that says which tenant it is. Bounded, so an unverified body still cannot
        // cost more than a megabyte.
        var body = await ReadBodyAsync(context).ConfigureAwait(false);
        if (body is null)
        {
            logger.LogWarning("A webhook delivery was larger than {MaxBytes} bytes.", MaxBodyBytes);
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var secret = tenant is null
            ? await ResolveSecretAsync(context, body.WrittenMemory, logger).ConfigureAwait(false)
            : Secret(context, tenant, logger);

        if (secret.Result is { } refusal)
        {
            return refusal;
        }

        var signature = context.Request.Headers[WhatsAppWebhookSignature.HeaderName].ToString();

        // Checked against the bytes as they arrived. Re-serializing a parsed model would
        // change whitespace or ordering and produce a different digest.
        if (!WhatsAppWebhookSignature.IsValid(body.WrittenSpan, signature, secret.AppSecret!))
        {
            logger.LogWarning(
                "A webhook delivery for tenant '{Tenant}' failed signature verification and " +
                "was rejected.",
                secret.Tenant);

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
    /// The app secret to check a delivery against, or the answer to send instead.
    /// </summary>
    private readonly record struct WebhookSecret(string Tenant, string? AppSecret, IResult? Result);

    /// <summary>Reads one named tenant's app secret.</summary>
    private static WebhookSecret Secret(HttpContext context, string tenant, ILogger logger)
    {
        var options = context.RequestServices
            .GetRequiredService<IOptionsMonitor<WhatsAppOptions>>()
            .Get(tenant);

        if (!string.IsNullOrEmpty(options.AppSecret))
        {
            return new WebhookSecret(tenant, options.AppSecret, null);
        }

        // Refusing is the only safe answer. Accepting unsigned deliveries would let anyone
        // who learns the URL feed events into the application.
        logger.LogError(
            "A webhook delivery arrived for tenant '{Tenant}' but no AppSecret is configured, " +
            "so it cannot be verified.",
            tenant);

        return new WebhookSecret(
            tenant,
            null,
            Results.StatusCode(StatusCodes.Status500InternalServerError));
    }

    /// <summary>
    /// Works out whose app secret a delivery should be checked against, from the delivery
    /// itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The body is read before it is verified, which is the only order available: the
    /// signature cannot be checked without a secret, and nothing but the body says which
    /// secret. What makes it safe is what the reading is allowed to do — it picks a secret,
    /// and the signature still has to verify against it, so a forged identifier can only ever
    /// cause a refusal. The scan itself reads two property names out of a size-capped buffer
    /// and builds no object graph.
    /// </para>
    /// <para>
    /// One delivery can name several numbers. Meta signs it once, with one app's secret, so
    /// the tenants it names have to agree on one — if they do not, this is not a delivery Meta
    /// could have sent, and it is refused rather than verified against whichever came first.
    /// </para>
    /// </remarks>
    private static async Task<WebhookSecret> ResolveSecretAsync(
        HttpContext context,
        ReadOnlyMemory<byte> body,
        ILogger logger)
    {
        var origins = WhatsAppWebhookParser.ReadOrigins(body.Span);

        if (origins.Count == 0)
        {
            logger.LogWarning(
                "A webhook delivery named no business account or phone number, so there is no " +
                "tenant to verify it against, and it was rejected.");

            return Refused();
        }

        var resolver = context.RequestServices.GetRequiredService<IWhatsAppWebhookTenantResolver>();

        WebhookSecret resolved = default;

        foreach (var origin in origins)
        {
            var tenant = await resolver
                .ResolveAsync(origin, context.RequestAborted)
                .ConfigureAwait(false);

            if (tenant is null)
            {
                logger.LogWarning(
                    "A webhook delivery for phone number {PhoneNumberId} on account " +
                    "{BusinessAccountId} matched no configured tenant and was rejected.",
                    origin.PhoneNumberId ?? "(none)",
                    origin.BusinessAccountId);

                return Refused();
            }

            var secret = Secret(context, tenant, logger);
            if (secret.Result is not null)
            {
                return secret;
            }

            if (resolved.AppSecret is null)
            {
                resolved = secret;
                continue;
            }

            if (!string.Equals(resolved.AppSecret, secret.AppSecret, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "A webhook delivery covered tenants '{First}' and '{Second}', which are on " +
                    "different Meta apps and so have different app secrets. One signature " +
                    "cannot be right for both, so it was rejected.",
                    resolved.Tenant,
                    secret.Tenant);

                return Refused();
            }
        }

        return resolved;

        static WebhookSecret Refused() =>
            new(string.Empty, null, Results.StatusCode(StatusCodes.Status403Forbidden));
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
