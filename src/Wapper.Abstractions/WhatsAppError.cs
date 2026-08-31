namespace Wapper;

/// <summary>
/// The error object the Cloud API returns. Meta's guidance is explicit: branch on
/// <see cref="Code"/>, never on the HTTP status code and never on
/// <see cref="Subcode"/>.
/// </summary>
public sealed record WhatsAppError
{
    /// <summary>The error code. This is the only field worth branching on.</summary>
    public required int Code { get; init; }

    /// <summary>Error type, for example <c>OAuthException</c>.</summary>
    public string? Type { get; init; }

    /// <summary>Human-readable message, for example <c>(#130429) Rate limit hit</c>.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// A short heading, such as <c>Healthy ecosystem</c>.
    /// </summary>
    /// <remarks>
    /// Only errors reported on the webhook carry one, and on a delivery failure it is
    /// sometimes all Meta says.
    /// </remarks>
    public string? Title { get; init; }

    /// <summary>
    /// The <c>error_data.details</c> string, which usually carries the part a human
    /// actually needs, such as <c>Cloud API message throughput has been reached.</c>
    /// </summary>
    public string? Details { get; init; }

    /// <summary>Meta's request identifier. Quote it when opening a support ticket.</summary>
    public string? TraceId { get; init; }

    /// <summary>
    /// Meta's own hint that the failure is temporary and the request may be retried.
    /// Present on codes such as <c>1</c>, <c>2</c> and <c>4</c>.
    /// </summary>
    public bool IsTransient { get; init; }

    /// <summary>
    /// The <c>error_subcode</c>. Deprecated by Meta and no longer returned since Graph
    /// API v16.0; kept only so nothing is silently dropped when it does appear.
    /// </summary>
    public int? Subcode { get; init; }

    /// <inheritdoc />
    public override string ToString() =>
        Details is null ? $"{Code}: {Message}" : $"{Code}: {Message} ({Details})";
}
