using System.Globalization;
using System.Text.Json;
using Wapper.Internal;
using Wapper.Messages;

namespace Wapper.Webhooks;

/// <summary>
/// Turns a webhook delivery into typed events.
/// </summary>
/// <remarks>
/// One delivery can carry several events, for more than one phone number, so the result is
/// a list rather than a single item.
/// </remarks>
public static class WhatsAppWebhookParser
{
    /// <summary>Parses a webhook body.</summary>
    /// <param name="json">The raw body, exactly as it arrived.</param>
    /// <returns>The events it carried, in the order they appeared.</returns>
    /// <exception cref="WhatsAppException">The body is not a webhook delivery.</exception>
    public static IReadOnlyList<WhatsAppEvent> Parse(ReadOnlySpan<byte> json)
    {
        WebhookPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize(json, WhatsAppJsonContext.Default.WebhookPayload);
        }
        catch (JsonException exception)
        {
            throw new WhatsAppException("The webhook body is not valid JSON.", exception);
        }

        if (payload?.Entry is null)
        {
            throw new WhatsAppException(
                "The webhook body has no entries, so it is not a Cloud API delivery.");
        }

        var events = new List<WhatsAppEvent>();

        foreach (var entry in payload.Entry)
        {
            foreach (var change in entry.Changes ?? [])
            {
                if (change.Value is { } value)
                {
                    Collect(value, events);
                }
            }
        }

        return events;
    }

    /// <inheritdoc cref="Parse(ReadOnlySpan{byte})" />
    public static IReadOnlyList<WhatsAppEvent> Parse(string json) =>
        Parse(System.Text.Encoding.UTF8.GetBytes(json));

    private static void Collect(WebhookValue value, List<WhatsAppEvent> events)
    {
        // Everything in the payload identifies the account by phone number id, and nothing
        // else does. Without it there is no way to say which tenant an event belongs to.
        var phoneNumberId = value.Metadata?.PhoneNumberId;
        if (string.IsNullOrEmpty(phoneNumberId))
        {
            return;
        }

        var display = value.Metadata?.DisplayPhoneNumber;

        foreach (var message in value.Messages ?? [])
        {
            if (ToEvent(message, value, phoneNumberId, display) is { } converted)
            {
                events.Add(converted);
            }
        }

        foreach (var status in value.Statuses ?? [])
        {
            if (ToEvent(status, phoneNumberId, display) is { } converted)
            {
                events.Add(converted);
            }
        }

        foreach (var error in value.Errors ?? [])
        {
            events.Add(new WebhookError
            {
                PhoneNumberId = phoneNumberId,
                DisplayPhoneNumber = display,
                Error = error.ToError(),
            });
        }
    }

    private static WhatsAppEvent? ToEvent(
        WebhookMessage message,
        WebhookValue value,
        string phoneNumberId,
        string? display)
    {
        if (message.Id is null || message.From is null)
        {
            return null;
        }

        var common = new MessageFields(
            phoneNumberId,
            display,
            ToTimestamp(message.Timestamp),
            message.Id,
            message.From,
            ProfileNameOf(value, message.From),
            message.Context?.Id,
            message.Context?.Forwarded == true || message.Context?.FrequentlyForwarded == true);

        return message.Type switch
        {
            "text" when message.Text?.Body is { } body => New<TextMessage>(common) with { Text = body },
            "image" => Media(common, message.Image, IncomingMediaKind.Image),
            "audio" => Media(common, message.Audio, IncomingMediaKind.Audio),
            "video" => Media(common, message.Video, IncomingMediaKind.Video),
            "document" => Media(common, message.Document, IncomingMediaKind.Document),
            "sticker" => Media(common, message.Sticker, IncomingMediaKind.Sticker),
            "location" when message.Location is { } location => New<LocationMessage>(common) with
            {
                Location = new Location
                {
                    Latitude = location.Latitude,
                    Longitude = location.Longitude,
                    Name = location.Name,
                    Address = location.Address,
                },
            },
            "contacts" when message.Contacts is { Count: > 0 } contacts => New<ContactsMessage>(common) with
            {
                Contacts = [.. contacts.Select(ToContact)],
            },
            "reaction" when message.Reaction?.MessageId is { } reactedTo => New<ReactionMessage>(common) with
            {
                MessageId = reactedTo,
                Emoji = message.Reaction.Emoji,
            },
            "interactive" => Interactive(common, message.Interactive, message.Type),
            // A template quick-reply arrives as its own message type carrying the payload the
            // template attached, not as an interactive reply carrying a button id.
            "button" when message.Button?.Payload is { } payload => New<TemplateButtonReply>(common) with
            {
                Payload = payload,
                Text = message.Button.Text,
            },
            "system" => New<SystemMessage>(common) with
            {
                Body = message.System?.Body,
                Kind = message.System?.Type,
                NewWhatsAppId = message.System?.WaId,
            },
            _ => Unsupported(common, message),
        };
    }

    private static WhatsAppEvent Media(MessageFields common, WebhookMedia? media, IncomingMediaKind kind) =>
        media?.Id is not { } id
            ? new UnsupportedMessage
            {
                PhoneNumberId = common.PhoneNumberId,
                DisplayPhoneNumber = common.Display,
                Timestamp = common.Timestamp,
                Id = common.Id,
                From = common.From,
                ProfileName = common.ProfileName,
                ReplyToMessageId = common.ReplyTo,
                IsForwarded = common.Forwarded,
                Type = kind.ToString().ToLowerInvariant(),
            }
            : New<MediaMessage>(common) with
            {
                Kind = kind,
                MediaId = id,
                MimeType = media.MimeType,
                Sha256 = media.Sha256,
                Caption = media.Caption,
                FileName = media.FileName,
                IsVoice = media.Voice,
                IsAnimated = media.Animated,
            };

    private static WhatsAppEvent Interactive(
        MessageFields common,
        WebhookInteractive? interactive,
        string? type)
    {
        var reply = interactive?.ButtonReply ?? interactive?.ListReply;

        if (reply?.Id is not { } id)
        {
            return Unsupported(common, type);
        }

        return New<InteractiveReply>(common) with
        {
            Kind = interactive!.ButtonReply is not null
                ? InteractiveReplyKind.Button
                : InteractiveReplyKind.List,
            ReplyId = id,
            Title = reply.Title,
            Description = reply.Description,
        };
    }

    private static WhatsAppEvent? ToEvent(WebhookStatus status, string phoneNumberId, string? display)
    {
        if (status.Id is null || status.RecipientId is null)
        {
            return null;
        }

        return new MessageStatusChanged
        {
            PhoneNumberId = phoneNumberId,
            DisplayPhoneNumber = display,
            Timestamp = ToTimestamp(status.Timestamp),
            MessageId = status.Id,
            RecipientId = status.RecipientId,
            Status = status.Status switch
            {
                "sent" => MessageDeliveryStatus.Sent,
                "delivered" => MessageDeliveryStatus.Delivered,
                "read" => MessageDeliveryStatus.Read,
                "failed" => MessageDeliveryStatus.Failed,
                "played" => MessageDeliveryStatus.Played,
                "deleted" => MessageDeliveryStatus.Deleted,
                _ => MessageDeliveryStatus.Unknown,
            },
            RawStatus = status.Status,
            ConversationId = status.Conversation?.Id,
            ConversationCategory = status.Pricing?.Category ?? status.Conversation?.Origin?.Type,
            Billable = status.Pricing?.Billable,
            Errors = status.Errors is { Count: > 0 } errors
                ? [.. errors.Select(e => e.ToError())]
                : [],
        };
    }

    private static UnsupportedMessage Unsupported(MessageFields common, WebhookMessage message) =>
        new()
        {
            PhoneNumberId = common.PhoneNumberId,
            DisplayPhoneNumber = common.Display,
            Timestamp = common.Timestamp,
            Id = common.Id,
            From = common.From,
            ProfileName = common.ProfileName,
            ReplyToMessageId = common.ReplyTo,
            IsForwarded = common.Forwarded,
            Type = message.Type ?? "unknown",
            Error = message.Errors is { Count: > 0 } errors ? errors[0].ToError() : null,
        };

    private static UnsupportedMessage Unsupported(MessageFields common, string? type) =>
        new()
        {
            PhoneNumberId = common.PhoneNumberId,
            DisplayPhoneNumber = common.Display,
            Timestamp = common.Timestamp,
            Id = common.Id,
            From = common.From,
            ProfileName = common.ProfileName,
            ReplyToMessageId = common.ReplyTo,
            IsForwarded = common.Forwarded,
            Type = type ?? "unknown",
        };

    private static TMessage New<TMessage>(MessageFields common)
        where TMessage : IncomingMessage, new() =>
        new()
        {
            PhoneNumberId = common.PhoneNumberId,
            DisplayPhoneNumber = common.Display,
            Timestamp = common.Timestamp,
            Id = common.Id,
            From = common.From,
            ProfileName = common.ProfileName,
            ReplyToMessageId = common.ReplyTo,
            IsForwarded = common.Forwarded,
        };

    private static string? ProfileNameOf(WebhookValue value, string from) =>
        value.Contacts?.FirstOrDefault(c => c.WaId == from)?.Profile?.Name;

    private static DateTimeOffset ToTimestamp(string? timestamp) =>
        long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : default;

    private static Contact ToContact(ContactPayload payload) => new()
    {
        Name = new ContactName
        {
            FormattedName = payload.Name?.FormattedName ?? string.Empty,
            FirstName = payload.Name?.FirstName,
            LastName = payload.Name?.LastName,
            MiddleName = payload.Name?.MiddleName,
            Prefix = payload.Name?.Prefix,
            Suffix = payload.Name?.Suffix,
        },
        Phones = [.. (payload.Phones ?? []).Select(p => new ContactPhone
        {
            Phone = p.Phone ?? string.Empty,
            Type = p.Type,
            WhatsAppId = p.WaId,
        })],
        Emails = [.. (payload.Emails ?? []).Select(e => new ContactEmail
        {
            Email = e.Email ?? string.Empty,
            Type = e.Type,
        })],
        Urls = [.. (payload.Urls ?? []).Select(u => new ContactUrl
        {
            Url = u.Url ?? string.Empty,
            Type = u.Type,
        })],
        Addresses = [.. (payload.Addresses ?? []).Select(a => new ContactAddress
        {
            Street = a.Street,
            City = a.City,
            State = a.State,
            Zip = a.Zip,
            Country = a.Country,
            CountryCode = a.CountryCode,
            Type = a.Type,
        })],
        Organisation = payload.Org is { } org
            ? new ContactOrganisation
            {
                Company = org.Company,
                Department = org.Department,
                Title = org.Title,
            }
            : null,
        Birthday = DateOnly.TryParse(payload.Birthday, CultureInfo.InvariantCulture, out var birthday)
            ? birthday
            : null,
    };

    private readonly record struct MessageFields(
        string PhoneNumberId,
        string? Display,
        DateTimeOffset Timestamp,
        string Id,
        string From,
        string? ProfileName,
        string? ReplyTo,
        bool Forwarded);
}
