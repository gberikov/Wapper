using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Wapper.Raw;

/// <summary>Which of Meta's budgets a raw call spends, so it is paced like the rest.</summary>
public enum RawCallKind
{
    /// <summary>
    /// Not governed by a budget this library can pace ahead of time. The safe default, and
    /// what media calls use: a rejection still holds the application back afterwards.
    /// </summary>
    Unpaced,

    /// <summary>
    /// Sending a message. Spends the throughput of the business phone number, and the pair
    /// allowance of the conversation when <see cref="RawRequest.Recipient"/> is set.
    /// </summary>
    Message,

    /// <summary>
    /// A management call — anything addressed to the WhatsApp Business Account. Spends its
    /// hourly allowance: 200 requests, or 5000 once a number is registered.
    /// </summary>
    Management,
}

/// <summary>
/// One call to an endpoint this library does not model.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Path"/> is everything after the API version, and may carry a query string:
/// <c>{waba_id}/product_catalogs</c>, <c>{phone_number_id}/whatsapp_business_profile?fields=about</c>.
/// Three placeholders are filled in from the tenant's credentials, so the same path works for
/// every tenant of a multi-tenant host:
/// </para>
/// <list type="bullet">
/// <item><description><c>{phone_number_id}</c> — the business phone number.</description></item>
/// <item><description><c>{waba_id}</c> — the WhatsApp Business Account.</description></item>
/// <item><description><c>{app_id}</c> — the Meta app.</description></item>
/// </list>
/// <para>
/// Anything else you interpolate is yours to escape. An id that came from a customer, a
/// webhook or a database is data: <see cref="Uri.EscapeDataString(string)"/> it, or the
/// endpoint you address is no longer the one you named.
/// </para>
/// </remarks>
public sealed record RawRequest
{
    /// <summary>HTTP method.</summary>
    public required HttpMethod Method { get; init; }

    /// <summary>Path below the API version, with placeholders and an optional query string.</summary>
    public required string Path { get; init; }

    /// <summary>The request body, as JSON you wrote out yourself.</summary>
    public string? Body { get; init; }

    /// <summary>Which budget the call spends. Defaults to <see cref="RawCallKind.Unpaced"/>.</summary>
    public RawCallKind Kind { get; init; } = RawCallKind.Unpaced;

    /// <summary>
    /// The recipient, for a message. Only read when <see cref="Kind"/> is
    /// <see cref="RawCallKind.Message"/>, and needed for the per-conversation pair allowance.
    /// </summary>
    public string? Recipient { get; init; }

    /// <summary>
    /// Whether the call may be sent again after a retryable rejection.
    /// </summary>
    /// <remarks>
    /// Leave it on for a read. Turn it off for anything that spends an allowance of its own
    /// or is not safe to repeat — Meta offers no idempotency key to settle it.
    /// </remarks>
    public bool Retryable { get; init; } = true;

    /// <summary>
    /// A short, stable name for the span this call produces when tracing is on, such as
    /// <c>catalogs.list</c>. Keep ids out of it, or the traces cannot be aggregated.
    /// </summary>
    public string? Operation { get; init; }
}

/// <summary>
/// The way out, for an endpoint this library has no typed API for yet.
/// </summary>
/// <remarks>
/// <para>
/// Everything a typed call gets, this gets too: the tenant's credentials, the configured
/// Graph API version and base address, the four rate limit budgets, the retry policy, and
/// the same <see cref="WhatsAppApiException"/> carrying the parsed error object. Only the
/// request and the response shape are yours.
/// </para>
/// <para>
/// It exists so a missing endpoint is an inconvenience rather than a reason to hand-roll a
/// second <c>HttpClient</c> beside this one — which would pace against nothing and walk both
/// of them into Meta's limits. Prefer a typed API where one exists: this one checks nothing
/// before sending, so Meta's bare <c>100</c> is all you get back.
/// </para>
/// </remarks>
public interface IRawApi
{
    /// <summary>Sends the call and hands back the response body as JSON.</summary>
    /// <param name="request">What to send.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The result is self-contained — it does not have to be disposed, and it outlives the
    /// response it came from.
    /// </remarks>
    Task<JsonElement> SendAsync(RawRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sends the call and deserializes the response into a type of your own.</summary>
    /// <typeparam name="TResponse">What to read the body as.</typeparam>
    /// <param name="request">What to send.</param>
    /// <param name="responseTypeInfo">
    /// Source-generated metadata for <typeparamref name="TResponse"/>, from a
    /// <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> of your own. Asked
    /// for rather than inferred because these packages are trim- and AOT-compatible, and a
    /// reflection-based overload would quietly break both.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<TResponse> SendAsync<TResponse>(
        RawRequest request,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken = default);
}
