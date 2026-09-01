using Wapper.Webhooks;

namespace Wapper.Sample;

// Handlers run inside the webhook request, and Meta wants an answer within a few hundred
// milliseconds. Everything here is quick; real work belongs on a queue. Handlers also have to
// be idempotent, because Meta repeats deliveries — and because a handler that throws fails
// the whole delivery, which Meta then repeats too.

/// <summary>Answers what a customer writes, taps or shares.</summary>
internal sealed class Conversation(IWhatsAppClient whatsApp, ILogger<Conversation> logger)
    : IWhatsAppEventHandler<TextMessage>,
      IWhatsAppEventHandler<InteractiveReply>,
      IWhatsAppEventHandler<LocationMessage>
{
    public async Task HandleAsync(TextMessage message, CancellationToken ct)
    {
        // Blue ticks, and a typing indicator while the reply is on its way.
        await whatsApp.Messages.MarkAsReadAsync(message.Id, showTyping: true, ct);

        // Quoting the message keeps the thread readable when several are in flight.
        await whatsApp.Messages.SendTextAsync(
            message.From,
            $"You said: {message.Text}",
            replyToMessageId: message.Id,
            cancellationToken: ct);
    }

    public Task HandleAsync(InteractiveReply reply, CancellationToken ct) =>
        // ReplyId is the id set on the button or the row when the message was sent. It is
        // the only thing that identifies what was pressed.
        whatsApp.Messages.SendTextAsync(reply.From, $"Noted: {reply.ReplyId}", cancellationToken: ct);

    public Task HandleAsync(LocationMessage location, CancellationToken ct)
    {
        logger.LogInformation(
            "{Customer} is at {Latitude}, {Longitude}.",
            location.From,
            location.Location.Latitude,
            location.Location.Longitude);

        return Task.CompletedTask;
    }
}

/// <summary>Fetches the files customers send.</summary>
internal sealed class Attachments(IWhatsAppClient whatsApp, IHostEnvironment environment)
    : IWhatsAppEventHandler<MediaMessage>
{
    public async Task HandleAsync(MediaMessage message, CancellationToken ct)
    {
        // Only the id arrives. The bytes have to be fetched, and promptly: an id from a
        // webhook expires after seven days. The result owns a connection, so dispose it.
        await using var media = await whatsApp.Media.DownloadAsync(message.MediaId, ct);

        var inbox = Path.Combine(environment.ContentRootPath, "inbox");
        Directory.CreateDirectory(inbox);

        await using var target = File.Create(Path.Combine(inbox, message.FileName ?? message.MediaId));
        await media.Content.CopyToAsync(target, ct);
    }
}

/// <summary>Follows what happened to the messages this application sent.</summary>
internal sealed class Deliveries(ILogger<Deliveries> logger) : IWhatsAppEventHandler<MessageStatusChanged>
{
    public Task HandleAsync(MessageStatusChanged status, CancellationToken ct)
    {
        // A send only said Meta accepted the message. This is where delivery, reading and
        // failure arrive — with the callbackData the send attached, so an order number is
        // matched without a table of message ids.
        if (status.Status == MessageDeliveryStatus.Failed)
        {
            logger.LogWarning(
                "Message {MessageId} for order {Order} failed: {Error}",
                status.MessageId,
                status.CallbackData,
                status.Errors.Count > 0 ? status.Errors[0] : null);
        }
        else
        {
            logger.LogInformation(
                "Message {MessageId} for order {Order} is {Status}.",
                status.MessageId,
                status.CallbackData,
                status.Status);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Remembers who asked for no more marketing.</summary>
internal sealed class OptOuts(ILogger<OptOuts> logger) : IWhatsAppEventHandler<MarketingPreferenceChanged>
{
    public Task HandleAsync(MarketingPreferenceChanged change, CancellationToken ct)
    {
        // The one webhook that changes what may be sent. After a Stop, marketing templates to
        // this customer are accepted by the API and then fail with 131050 — so this is the
        // place to write the opt-out down, not the status webhook.
        logger.LogInformation("{Customer} asked to {Preference} marketing messages.", change.WhatsAppId, change.Preference);

        return Task.CompletedTask;
    }
}

/// <summary>Notices what this library has no typed event for yet.</summary>
internal sealed class Audit(ILogger<Audit> logger) : IWhatsAppEventHandler<UnknownEvent>
{
    public Task HandleAsync(UnknownEvent unknown, CancellationToken ct)
    {
        // A capability changing, a security alert, or a known field shaped in a way the
        // library could not read. The body comes with it.
        logger.LogWarning("Unhandled webhook field {Field}: {Json}", unknown.Field, unknown.Json);

        return Task.CompletedTask;
    }
}
