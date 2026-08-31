namespace Wapper.Messages;

/// <summary>What the Cloud API said when it accepted a message.</summary>
/// <remarks>
/// Acceptance is not delivery. The message has entered Meta's pipeline; whether it reached
/// the handset arrives later on the <c>messages</c> webhook as a status update, and a
/// message can still fail there with an error the send call never saw.
/// </remarks>
public sealed record SentMessage
{
    /// <summary>
    /// Identifier of the message, in the <c>wamid.</c> form. Quote it to react to the
    /// message, to reply to it, or to match the status updates that follow.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The recipient as WhatsApp knows them, which is not always the number that was dialled:
    /// some countries normalise it, so this is the value to store.
    /// </summary>
    public string? RecipientId { get; init; }

    /// <summary>
    /// Present when Meta chose to say something about the message up front, such as
    /// <c>accepted</c> or <c>held_for_quality_assessment</c>.
    /// </summary>
    public string? Status { get; init; }
}
