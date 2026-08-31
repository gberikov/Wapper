using Wapper.Flows;

namespace Wapper.Webhooks;

/// <summary>What Meta's monitoring is complaining about.</summary>
public enum FlowAlertKind
{
    /// <summary>An alert this library does not know about yet.</summary>
    Unknown,

    /// <summary>Customers' devices are failing to render or submit the Flow.</summary>
    ClientErrorRate,

    /// <summary>The Flow's endpoint is answering with errors.</summary>
    EndpointErrorRate,

    /// <summary>The Flow's endpoint is answering slowly.</summary>
    EndpointLatency,

    /// <summary>The Flow's endpoint is not answering.</summary>
    EndpointAvailability,
}

/// <summary>Whether an alert has just been raised or has just cleared.</summary>
public enum FlowAlertState
{
    /// <summary>A state this library does not know about yet.</summary>
    Unknown,

    /// <summary>The threshold has been crossed.</summary>
    Activated,

    /// <summary>Things are back below the threshold.</summary>
    Deactivated,
}

/// <summary>
/// A Flow moved between states.
/// </summary>
/// <remarks>
/// Sent on creation as well, in which case there is no previous status and the new one is
/// <see cref="FlowStatus.Draft"/>. Like the template and phone number events this belongs to
/// the account rather than to a number, so <see cref="WhatsAppEvent.PhoneNumberId"/> is empty.
/// </remarks>
public sealed record FlowStatusChanged : WhatsAppEvent
{
    /// <summary>Which Flow.</summary>
    public required string FlowId { get; init; }

    /// <summary>What it was. <see cref="FlowStatus.Unknown"/> when the Flow was just created.</summary>
    public FlowStatus PreviousStatus { get; init; }

    /// <summary>What it is now.</summary>
    public FlowStatus Status { get; init; }

    /// <summary>Meta's own sentence about it, useful in a log line.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Meta's monitoring crossed a threshold on a Flow, or came back under it.
/// </summary>
/// <remarks>
/// Worth listening to: sustained trouble on a Flow's endpoint moves the Flow to
/// <see cref="FlowStatus.Throttled"/> and then to <see cref="FlowStatus.Blocked"/>, which
/// arrives as a <see cref="FlowStatusChanged"/> after the fact.
/// </remarks>
public sealed record FlowAlert : WhatsAppEvent
{
    /// <summary>Which Flow.</summary>
    public required string FlowId { get; init; }

    /// <summary>What is being complained about.</summary>
    public FlowAlertKind Kind { get; init; }

    /// <summary>The alert name exactly as Meta wrote it.</summary>
    public string? RawKind { get; init; }

    /// <summary>Whether it was raised or cleared.</summary>
    public FlowAlertState State { get; init; }

    /// <summary>The threshold that was crossed.</summary>
    public double? Threshold { get; init; }

    /// <summary>Meta's own sentence about it.</summary>
    public string? Message { get; init; }

    /// <summary>How many requests the figure was worked out from.</summary>
    public int? RequestCount { get; init; }

    /// <summary>The error rate over all of them.</summary>
    public double? ErrorRate { get; init; }

    /// <summary>Median endpoint latency, in milliseconds.</summary>
    public int? MedianLatency { get; init; }

    /// <summary>Ninetieth-percentile endpoint latency, in milliseconds.</summary>
    public int? NinetiethPercentileLatency { get; init; }

    /// <summary>Each error that went into the alert.</summary>
    public IReadOnlyList<FlowAlertError> Errors { get; init; } = [];
}

/// <summary>One kind of error counted towards a <see cref="FlowAlert"/>.</summary>
public sealed record FlowAlertError
{
    /// <summary>Meta's name for it.</summary>
    public string? ErrorType { get; init; }

    /// <summary>How many times it happened.</summary>
    public int? Count { get; init; }

    /// <summary>Its share of the requests.</summary>
    public double? Rate { get; init; }
}
