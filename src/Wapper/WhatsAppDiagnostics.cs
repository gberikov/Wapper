using System.Diagnostics;
using Wapper.Internal;

namespace Wapper;

/// <summary>
/// The <see cref="ActivitySource"/> every Cloud API call is traced on.
/// </summary>
/// <remarks>
/// <para>
/// One span per logical call, spanning the retries and the waits rather than one per HTTP
/// attempt — that is what a caller experiences, and it is the number worth alerting on. The
/// attempts underneath show up separately if the host also instruments <c>HttpClient</c>.
/// </para>
/// <para>
/// Nothing is emitted until something listens, so leaving this alone costs a null check per
/// call. Subscribe with the name:
/// </para>
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(tracing => tracing.AddSource(WhatsAppDiagnostics.ActivitySourceName));
/// </code>
/// </remarks>
public static class WhatsAppDiagnostics
{
    /// <summary>Name to subscribe to. Stable across releases.</summary>
    public const string ActivitySourceName = "Wapper";

    internal static readonly ActivitySource Source = new(
        ActivitySourceName,
        typeof(WhatsAppDiagnostics).Assembly.GetName().Version?.ToString());

    /// <summary>
    /// Opens the span for one call, or returns <see langword="null"/> when nobody is
    /// listening.
    /// </summary>
    /// <remarks>
    /// The recipient is deliberately not a tag. It is the customer's phone number, and a
    /// trace backend is not a place to put one; the business's own number identifies which
    /// of your numbers a slow call belongs to, which is what an operator actually needs.
    /// </remarks>
    internal static Activity? StartCall(GraphRequest request)
    {
        var activity = Source.StartActivity(
            request.Operation ?? $"whatsapp {request.Method.Method}",
            ActivityKind.Client);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("http.request.method", request.Method.Method);
        activity.SetTag("wapper.tenant", request.Tenant.Length == 0 ? "(default)" : request.Tenant);
        activity.SetTag("wapper.phone_number_id", request.Credentials.PhoneNumberId);

        return activity;
    }

    /// <summary>Notes that the call was rejected and is about to be tried again.</summary>
    internal static void RecordRetry(Activity? activity, int attempts, int code)
    {
        if (activity is null)
        {
            return;
        }

        // A span that took ninety seconds is otherwise indistinguishable from a slow Meta.
        activity.SetTag("wapper.attempts", attempts);
        activity.SetTag("wapper.last_error_code", code);
    }

    /// <summary>Marks the span failed, and says what the Cloud API objected to.</summary>
    internal static void RecordFailure(Activity? activity, Exception exception)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);

        switch (exception)
        {
            case WhatsAppApiException api:
                activity.SetTag("wapper.error_code", api.Code);
                activity.SetTag("http.response.status_code", (int)api.StatusCode);
                break;

            case WhatsAppRateLimitedException limited:
                activity.SetTag("wapper.error_code", limited.Error?.Code);
                activity.SetTag("wapper.budget", limited.Scope.Budget.ToString());
                break;

            default:
                activity.SetTag("error.type", exception.GetType().Name);
                break;
        }
    }
}
