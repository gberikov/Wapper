using System.Net.Http.Json;
using Wapper.Internal;
using Wapper.Media;

namespace Wapper.Messages;

/// <summary>Sending messages for one tenant.</summary>
internal sealed class MessagesApi(GraphApiClient client, string tenant) : IMessagesApi
{
    public Task<SentMessage> SendTextAsync(
        string to,
        string text,
        bool previewUrl = false,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        return SendAsync(
            to,
            replyToMessageId,
            payload =>
            {
                payload.Type = "text";
                payload.Text = new TextPayload
                {
                    Body = text,
                    // Only written when asked for: a preview is fetched from the link while
                    // the message is being sent, which delays delivery.
                    PreviewUrl = previewUrl ? true : null,
                };
            },
            cancellationToken);
    }

    public Task<SentMessage> SendImageAsync(
        string to,
        MediaSource media,
        string? caption = null,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            to,
            replyToMessageId,
            payload =>
            {
                payload.Type = "image";
                payload.Image = media.ToPayload(caption);
            },
            cancellationToken);

    public Task<SentMessage> SendVideoAsync(
        string to,
        MediaSource media,
        string? caption = null,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            to,
            replyToMessageId,
            payload =>
            {
                payload.Type = "video";
                payload.Video = media.ToPayload(caption);
            },
            cancellationToken);

    public Task<SentMessage> SendAudioAsync(
        string to,
        MediaSource media,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            to,
            replyToMessageId,
            payload =>
            {
                payload.Type = "audio";
                payload.Audio = media.ToPayload();
            },
            cancellationToken);

    public Task<SentMessage> SendDocumentAsync(
        string to,
        MediaSource media,
        string? caption = null,
        string? fileName = null,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            to,
            replyToMessageId,
            payload =>
            {
                payload.Type = "document";
                payload.Document = media.ToPayload(caption, fileName);
            },
            cancellationToken);

    public Task<SentMessage> SendStickerAsync(
        string to,
        MediaSource media,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            to,
            replyToMessageId,
            payload =>
            {
                payload.Type = "sticker";
                payload.Sticker = media.ToPayload();
            },
            cancellationToken);

    public Task<SentMessage> SendLocationAsync(
        string to,
        Location location,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            to,
            replyToMessageId,
            payload =>
            {
                payload.Type = "location";
                payload.Location = location.ToPayload();
            },
            cancellationToken);

    public Task<SentMessage> SendContactsAsync(
        string to,
        IEnumerable<Contact> contacts,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contacts);

        var cards = contacts.Select(c => c.ToPayload()).ToList();
        if (cards.Count == 0)
        {
            throw new ArgumentException("A contacts message needs at least one card.", nameof(contacts));
        }

        return SendAsync(
            to,
            replyToMessageId,
            payload =>
            {
                payload.Type = "contacts";
                payload.Contacts = cards;
            },
            cancellationToken);
    }

    public Task<SentMessage> SendReactionAsync(
        string to,
        string messageId,
        string emoji,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(emoji);

        return ReactAsync(to, messageId, emoji, cancellationToken);
    }

    public Task<SentMessage> RemoveReactionAsync(
        string to,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        // An empty emoji is how a reaction is taken back. There is no separate endpoint.
        return ReactAsync(to, messageId, string.Empty, cancellationToken);
    }

    public Task<SentMessage> SendButtonsAsync(
        string to,
        ButtonMessage message,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var interactive = message.ToPayload();

        return SendAsync(
            to,
            replyToMessageId,
            payload =>
            {
                payload.Type = "interactive";
                payload.Interactive = interactive;
            },
            cancellationToken);
    }

    public Task<SentMessage> SendListAsync(
        string to,
        ListMessage message,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var interactive = message.ToPayload();

        return SendAsync(
            to,
            replyToMessageId,
            payload =>
            {
                payload.Type = "interactive";
                payload.Interactive = interactive;
            },
            cancellationToken);
    }

    public Task<SentMessage> SendCallToActionAsync(
        string to,
        CallToActionMessage message,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var interactive = message.ToPayload();

        return SendAsync(
            to,
            replyToMessageId,
            payload =>
            {
                payload.Type = "interactive";
                payload.Interactive = interactive;
            },
            cancellationToken);
    }

    public Task<SentMessage> SendTemplateAsync(
        string to,
        TemplateMessage template,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var payloadTemplate = template.ToPayload();

        return SendAsync(
            to,
            replyToMessageId,
            payload =>
            {
                payload.Type = "template";
                payload.Template = payloadTemplate;
            },
            cancellationToken);
    }

    public async Task MarkAsReadAsync(
        string messageId,
        bool showTyping = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        var payload = new SendMessagePayload
        {
            // A read receipt goes to the same endpoint as a message, distinguished by
            // carrying a status instead of a recipient and a type.
            RecipientType = null,
            Status = "read",
            MessageId = messageId,
            TypingIndicator = showTyping ? new TypingIndicatorPayload() : null,
        };

        await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    Path = $"{credentials.PhoneNumberId}/messages",
                    Kind = GraphCallKind.Message,
                    Content = () => JsonContent.Create(
                        payload,
                        WhatsAppJsonContext.Default.SendMessagePayload),
                },
                WhatsAppJsonContext.Default.SendMessageResponse,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<SentMessage> ReactAsync(
        string to,
        string messageId,
        string emoji,
        CancellationToken cancellationToken) =>
        SendAsync(
            to,
            replyToMessageId: null,
            payload =>
            {
                payload.Type = "reaction";
                payload.Reaction = new ReactionPayload { MessageId = messageId, Emoji = emoji };
            },
            cancellationToken);

    private async Task<SentMessage> SendAsync(
        string to,
        string? replyToMessageId,
        Action<SendMessagePayload> build,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        var payload = new SendMessagePayload
        {
            To = to,
            Context = replyToMessageId is null
                ? null
                : new MessageContextPayload { MessageId = replyToMessageId },
        };
        build(payload);

        var response = await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    Path = $"{credentials.PhoneNumberId}/messages",
                    Kind = GraphCallKind.Message,
                    // Named so the pair allowance is counted per conversation. Without it the
                    // client would pace the phone number and walk straight into 131056.
                    Recipient = to,
                    Content = () => JsonContent.Create(
                        payload,
                        WhatsAppJsonContext.Default.SendMessagePayload),
                },
                WhatsAppJsonContext.Default.SendMessageResponse,
                cancellationToken)
            .ConfigureAwait(false);

        var message = response.Messages?.FirstOrDefault();

        if (message?.Id is null)
        {
            throw new WhatsAppException(
                "The Cloud API accepted the message but returned no message id, so there is " +
                "nothing to match its delivery status against.");
        }

        return new SentMessage
        {
            Id = message.Id,
            // The recipient as WhatsApp knows them, which is not always the number dialled.
            RecipientId = response.Contacts?.FirstOrDefault()?.WaId,
            Status = message.MessageStatus,
        };
    }
}
