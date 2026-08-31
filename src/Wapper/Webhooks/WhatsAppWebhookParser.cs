using System.Globalization;
using System.Text.Json;
using Wapper.Flows;
using Wapper.Internal;
using Wapper.Messages;
using Wapper.PhoneNumbers;
using Wapper.Templates;

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
                Collect(change.Field, change.Value, entry.Id ?? string.Empty, events);
            }
        }

        return events;
    }

    /// <inheritdoc cref="Parse(ReadOnlySpan{byte})" />
    public static IReadOnlyList<WhatsAppEvent> Parse(string json) =>
        Parse(System.Text.Encoding.UTF8.GetBytes(json));

    private static void Collect(
        string? field,
        JsonElement value,
        string businessAccountId,
        List<WhatsAppEvent> events)
    {
        var before = events.Count;

        // Template, phone number, account and Flow events belong to the account rather than
        // to a number and carry no metadata at all, so they are read before a phone number is
        // insisted on.
        switch (field)
        {
            case "message_template_status_update" when Bind(value) is { } status:
                events.Add(ToStatusChange(status, businessAccountId));
                break;

            case "message_template_quality_update" when Bind(value) is { } quality:
                events.Add(ToQualityChange(quality, businessAccountId));
                break;

            case "phone_number_quality_update" when Bind(value) is { } numberQuality:
                events.Add(ToPhoneNumberQualityChange(numberQuality, businessAccountId));
                break;

            case "phone_number_name_update" when Bind(value) is { } name:
                events.Add(ToPhoneNumberNameChange(name, businessAccountId));
                break;

            case "account_update" when Bind(value) is { } account:
                events.Add(ToAccountUpdate(account, value, businessAccountId));
                break;

            // One field carries both the status changes and the monitoring alerts, told apart
            // by `event`.
            case "flows" when Bind(value) is { } flow:
                events.Add(flow.Event == "FLOW_STATUS_CHANGE"
                    ? ToFlowStatusChange(flow, businessAccountId)
                    : ToFlowAlert(flow, businessAccountId));
                break;

            case "messages" when Bind(value) is { } messages:
                CollectMessages(messages, value, businessAccountId, events);
                break;

            case "user_preferences" when Bind(value) is { } preferences:
                CollectPreferences(preferences, value, businessAccountId, events);
                break;

            default:
                // Meta has more than twenty webhook fields and keeps adding to them. Dropping
                // one leaves no trace of a security alert or a capability being withdrawn, so
                // it is reported with the body it arrived in instead. A field this library
                // does know but could not read lands here too, for the same reason: the
                // alternative is silence.
                events.Add(Unreadable(field, value, businessAccountId));
                break;
        }

        if (events.Count == before)
        {
            // The field bound cleanly and yielded nothing: a shape this library could read
            // but found no event in. Silence here is the worst of the failure modes, because
            // there is nowhere left to notice it, so it is reported like any other change
            // that could not be turned into an event.
            events.Add(Unreadable(field, value, businessAccountId));
        }
    }

    /// <summary>
    /// Reports a change this library could not turn into an event, with the body it arrived
    /// in.
    /// </summary>
    private static UnknownEvent Unreadable(string? field, JsonElement value, string businessAccountId) => new()
    {
        BusinessAccountId = businessAccountId,
        Field = field ?? string.Empty,
        Json = value.ValueKind == JsonValueKind.Undefined ? string.Empty : value.GetRawText(),
    };

    /// <summary>
    /// Binds the <c>value</c> object of a change this library has an event for.
    /// </summary>
    /// <remarks>
    /// Left as raw JSON until here so that a delivery on a field nobody handles costs one
    /// string rather than a walk over every property the messages webhook can carry.
    /// </remarks>
    private static WebhookValue? Bind(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            return value.Deserialize(WhatsAppJsonContext.Default.WebhookValue);
        }
        catch (JsonException)
        {
            // A field this library knows, shaped in a way it does not. Failing the whole
            // delivery over it would cost the changes around it; the caller falls through to
            // UnknownEvent, which keeps the body so nothing is lost without trace.
            return null;
        }
    }

    private static void CollectPreferences(
        WebhookValue value,
        JsonElement raw,
        string businessAccountId,
        List<WhatsAppEvent> events)
    {
        var phoneNumberId = value.Metadata?.PhoneNumberId ?? string.Empty;
        var display = value.Metadata?.DisplayPhoneNumber;

        // Meta sends this change two ways: an array of preferences, and — with no array at
        // all — one preference laid flat on `value` itself. Reading only the array loses an
        // opt-out silently, and the price of that is marketing messages to someone who asked
        // for none, which is a spam complaint and a quality rating.
        List<WebhookUserPreference> preferences = value.UserPreferences is { Count: > 0 } array
            ? array
            : [new WebhookUserPreference
            {
                WaId = value.WaId,
                Detail = value.Detail,
                Category = value.Category,
                Value = value.PreferenceValue,
                Timestamp = value.Timestamp,
            }];

        foreach (var preference in preferences)
        {
            if (preference.WaId is not { } customer)
            {
                // Neither shape carried a customer. Reported rather than skipped: an opt-out
                // that goes missing costs sends to somebody who asked for none.
                events.Add(Unreadable("user_preferences", raw, businessAccountId));
                continue;
            }

            events.Add(new MarketingPreferenceChanged
            {
                PhoneNumberId = phoneNumberId,
                BusinessAccountId = businessAccountId,
                DisplayPhoneNumber = display,
                Timestamp = preference.Timestamp is { } seconds
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                    : default,
                WhatsAppId = customer,
                Preference = preference.Value?.ToUpperInvariant() switch
                {
                    "STOP" => MarketingPreference.Stop,
                    "RESUME" => MarketingPreference.Resume,
                    _ => MarketingPreference.Unknown,
                },
                RawPreference = preference.Value,
                Category = preference.Category,
                Detail = preference.Detail,
            });
        }
    }

    private static void CollectMessages(
        WebhookValue value,
        JsonElement raw,
        string businessAccountId,
        List<WhatsAppEvent> events)
    {
        // The phone number is the only identifier this payload carries, and without it there
        // is no saying which number an event belongs to. Meta always sends it, so this is a
        // shape nobody has seen — which is exactly why it is reported rather than dropped:
        // an incoming message vanishing is not something an application can find out about
        // any other way.
        var phoneNumberId = value.Metadata?.PhoneNumberId;
        if (string.IsNullOrEmpty(phoneNumberId))
        {
            events.Add(Unreadable("messages", raw, businessAccountId));
            return;
        }

        var display = value.Metadata?.DisplayPhoneNumber;

        // A message with no id or no sender, or a status with no id or no recipient, is
        // reported rather than skipped. The body of the whole change comes along, since one
        // item of it has no raw form of its own by this point.
        foreach (var message in value.Messages ?? [])
        {
            events.Add(
                ToEvent(message, value, phoneNumberId, display, businessAccountId)
                ?? Unreadable("messages", raw, businessAccountId));
        }

        foreach (var status in value.Statuses ?? [])
        {
            events.Add(
                ToEvent(status, phoneNumberId, display, businessAccountId)
                ?? Unreadable("messages", raw, businessAccountId));
        }

        foreach (var error in value.Errors ?? [])
        {
            events.Add(new WebhookError
            {
                PhoneNumberId = phoneNumberId,
                BusinessAccountId = businessAccountId,
                DisplayPhoneNumber = display,
                Error = error.ToError(),
            });
        }
    }

    private static TemplateStatusChanged ToStatusChange(WebhookValue value, string businessAccountId) => new()
    {
        BusinessAccountId = businessAccountId,
        TemplateId = value.MessageTemplateId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        TemplateName = value.MessageTemplateName ?? string.Empty,
        TemplateLanguage = value.MessageTemplateLanguage ?? string.Empty,
        Status = TemplateMapping.ParseStatus(value.Event),
        RawEvent = value.Event,
        Reason = ParseReason(value.Reason),
        RawReason = value.Reason,
        // A rejection puts the part a human needs in `rejection_info`, not in `other_info`.
        // Reading only the latter leaves an operator with a bare INVALID_FORMAT and no idea
        // what to change.
        Details = value.OtherInfo?.Description
            ?? value.OtherInfo?.Title
            ?? value.RejectionInfo?.Reason,
        Recommendation = value.RejectionInfo?.Recommendation,
    };

    private static AccountUpdated ToAccountUpdate(
        WebhookValue value,
        JsonElement raw,
        string businessAccountId)
    {
        // Sent as an object on some events and as a bare string on others; both forms are
        // live, and the string one is what Meta's own test delivery sends.
        var number = value.PhoneNumber;
        var nested = number.ValueKind == JsonValueKind.Object;

        return new AccountUpdated
        {
            BusinessAccountId = businessAccountId,
            Event = ParseAccountEvent(value.Event),
            RawEvent = value.Event,
            PhoneNumber = nested ? Property(number, "display_phone_number") : Text(number),
            QualityRating = PhoneNumberMapping.ParseQuality(
                nested ? Property(number, "quality_rating") : null),
            CurrentLimit = PhoneNumberMapping.ParseTier(value.CurrentLimit),
            BanState = value.BanInfo?.WabaBanState,
            BanDate = value.BanInfo?.WabaBanDate,
            ViolationType = value.ViolationInfo?.ViolationType,
            Restrictions = [.. (value.RestrictionInfo ?? []).Select(restriction => new AccountRestriction
            {
                Type = restriction.RestrictionType,
                ExpiresAt = restriction.Expiration is { } seconds
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                    : null,
                Remediation = restriction.Remediation,
            })],
            Json = raw.GetRawText(),
        };
    }

    /// <summary>Reads a string property, or nothing when it is absent or not a string.</summary>
    private static string? Property(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) ? Text(property) : null;

    /// <summary>Reads an element as a string, or nothing when it is not one.</summary>
    private static string? Text(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static AccountUpdateEvent ParseAccountEvent(string? name) => name?.ToUpperInvariant() switch
    {
        "VERIFIED_ACCOUNT" => AccountUpdateEvent.VerifiedAccount,
        "ACCOUNT_VIOLATION" => AccountUpdateEvent.AccountViolation,
        "ACCOUNT_RESTRICTION" => AccountUpdateEvent.AccountRestriction,
        "DISABLED_UPDATE" => AccountUpdateEvent.DisabledUpdate,
        "ACCOUNT_DELETED" => AccountUpdateEvent.AccountDeleted,
        "ACCOUNT_OFFBOARDED" => AccountUpdateEvent.AccountOffboarded,
        "ACCOUNT_RECONNECTED" => AccountUpdateEvent.AccountReconnected,
        "PHONE_NUMBER_QUALITY_UPDATE" => AccountUpdateEvent.PhoneNumberQualityUpdate,
        "PARTNER_ADDED" => AccountUpdateEvent.PartnerAdded,
        "PARTNER_REMOVED" => AccountUpdateEvent.PartnerRemoved,
        _ => AccountUpdateEvent.Unknown,
    };

    private static TemplateQualityChanged ToQualityChange(WebhookValue value, string businessAccountId) => new()
    {
        BusinessAccountId = businessAccountId,
        TemplateId = value.MessageTemplateId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        TemplateName = value.MessageTemplateName ?? string.Empty,
        TemplateLanguage = value.MessageTemplateLanguage ?? string.Empty,
        Previous = TemplateMapping.ParseQuality(value.PreviousQualityScore),
        Current = TemplateMapping.ParseQuality(value.NewQualityScore),
    };

    private static PhoneNumberQualityChanged ToPhoneNumberQualityChange(
        WebhookValue value,
        string businessAccountId) => new()
    {
        BusinessAccountId = businessAccountId,
        DisplayPhoneNumber = value.DisplayPhoneNumber,
        Event = ParsePhoneNumberEvent(value.Event),
        RawEvent = value.Event,
        PreviousLimit = PhoneNumberMapping.ParseTier(value.OldLimit),
        // `max_daily_conversations_per_business` replaced `current_limit`, which Meta retired
        // in February 2026. Older deliveries and some intermediaries still send the old one.
        CurrentLimit = PhoneNumberMapping.ParseTier(
            value.MaxDailyConversationsPerBusiness ?? value.CurrentLimit),
    };

    private static PhoneNumberNameChanged ToPhoneNumberNameChange(
        WebhookValue value,
        string businessAccountId) => new()
    {
        BusinessAccountId = businessAccountId,
        DisplayPhoneNumber = value.DisplayPhoneNumber,
        Decision = ParseDecision(value.Decision),
        RawDecision = value.Decision,
        RequestedName = value.RequestedVerifiedName,
        RejectionReason = ParseRejection(value.RejectionReason),
        RawRejectionReason = value.RejectionReason,
    };

    private static FlowStatusChanged ToFlowStatusChange(
        WebhookValue value,
        string businessAccountId) => new()
    {
        BusinessAccountId = businessAccountId,
        FlowId = value.FlowId ?? string.Empty,
        // Absent when the Flow has just been created, which is the one case where there is no
        // previous state to report.
        PreviousStatus = FlowMapping.ParseStatus(value.OldStatus),
        Status = FlowMapping.ParseStatus(value.NewStatus),
        Message = value.Message,
    };

    private static FlowAlert ToFlowAlert(WebhookValue value, string businessAccountId) => new()
    {
        BusinessAccountId = businessAccountId,
        FlowId = value.FlowId ?? string.Empty,
        Kind = ParseAlertKind(value.Event),
        RawKind = value.Event,
        State = value.AlertState?.ToUpperInvariant() switch
        {
            "ACTIVATED" => FlowAlertState.Activated,
            "DEACTIVATED" => FlowAlertState.Deactivated,
            _ => FlowAlertState.Unknown,
        },
        Threshold = value.Threshold,
        Message = value.Message,
        RequestCount = value.RequestsCount,
        ErrorRate = value.ErrorRate,
        MedianLatency = value.P50Latency,
        NinetiethPercentileLatency = value.P90Latency,
        Errors = [.. (value.Errors ?? []).Select(error => new FlowAlertError
        {
            ErrorType = error.ErrorType,
            Count = error.ErrorCount,
            Rate = error.ErrorRate,
        })],
    };

    private static FlowAlertKind ParseAlertKind(string? name) => name?.ToUpperInvariant() switch
    {
        "CLIENT_ERROR_RATE" => FlowAlertKind.ClientErrorRate,
        "ENDPOINT_ERROR_RATE" => FlowAlertKind.EndpointErrorRate,
        "ENDPOINT_LATENCY" => FlowAlertKind.EndpointLatency,
        "ENDPOINT_AVAILABILITY" => FlowAlertKind.EndpointAvailability,
        _ => FlowAlertKind.Unknown,
    };

    private static PhoneNumberQualityEvent ParsePhoneNumberEvent(string? name) => name?.ToUpperInvariant() switch
    {
        "ONBOARDING" => PhoneNumberQualityEvent.Onboarding,
        "FLAGGED" => PhoneNumberQualityEvent.Flagged,
        "UNFLAGGED" => PhoneNumberQualityEvent.Unflagged,
        "UPGRADE" => PhoneNumberQualityEvent.Upgrade,
        "DOWNGRADE" => PhoneNumberQualityEvent.Downgrade,
        "THROUGHPUT_UPGRADE" => PhoneNumberQualityEvent.ThroughputUpgrade,
        _ => PhoneNumberQualityEvent.Unknown,
    };

    private static DisplayNameDecision ParseDecision(string? decision) => decision?.ToUpperInvariant() switch
    {
        "APPROVED" => DisplayNameDecision.Approved,
        "DEFERRED" => DisplayNameDecision.Deferred,
        "PENDING" => DisplayNameDecision.Pending,
        "REJECTED" => DisplayNameDecision.Rejected,
        _ => DisplayNameDecision.Unknown,
    };

    private static DisplayNameRejectionReason ParseRejection(string? reason) => reason?.ToUpperInvariant() switch
    {
        // Absent, or the literal string "NONE", both meaning the name was accepted.
        null or "NONE" => DisplayNameRejectionReason.None,
        // Meta documents these two identically: the name named a person.
        "NAME_EMPLOYEE_ISSUE" or "NAME_INDIVIDUAL_ISSUE" => DisplayNameRejectionReason.PersonalName,
        "NAME_ENDCLIENT_NOTRELATED" => DisplayNameRejectionReason.UnrelatedBusiness,
        "NAME_FORMAT_UNACCEPTABLE" => DisplayNameRejectionReason.UnacceptableFormat,
        "NAME_NOT_CONSISTENT" => DisplayNameRejectionReason.InconsistentWithBranding,
        _ => DisplayNameRejectionReason.Unknown,
    };

    private static TemplateStatusChangeReason ParseReason(string? reason) => reason?.ToUpperInvariant() switch
    {
        "NONE" => TemplateStatusChangeReason.None,
        "ABUSIVE_CONTENT" => TemplateStatusChangeReason.AbusiveContent,
        "INVALID_FORMAT" => TemplateStatusChangeReason.InvalidFormat,
        "SCAM" or "LOW_QUALITY" => TemplateStatusChangeReason.ScamOrLowQuality,
        _ => TemplateStatusChangeReason.Unknown,
    };

    private static WhatsAppEvent? ToEvent(
        WebhookMessage message,
        WebhookValue value,
        string phoneNumberId,
        string? display,
        string businessAccountId)
    {
        if (message.Id is null || message.From is null)
        {
            return null;
        }

        var common = new MessageFields(
            phoneNumberId,
            businessAccountId,
            display,
            ToTimestamp(message.Timestamp),
            message.Id,
            message.From,
            ProfileNameOf(value, message.From),
            message.Context?.Id,
            message.Context?.Forwarded == true || message.Context?.FrequentlyForwarded == true,
            ToReferral(message.Referral),
            ToReferredProduct(message.Context?.ReferredProduct));

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
            "order" when message.Order is { } order => New<OrderMessage>(common) with
            {
                CatalogId = order.CatalogId,
                Text = order.Text,
                Products = [.. (order.ProductItems ?? []).Select(item => new OrderProduct
                {
                    ProductRetailerId = item.ProductRetailerId,
                    Quantity = item.Quantity,
                    ItemPrice = item.ItemPrice,
                    Currency = item.Currency,
                })],
            },
            "request_welcome" => New<WelcomeRequest>(common),
            "system" => New<SystemMessage>(common) with
            {
                Body = message.System?.Body,
                Kind = message.System?.Type,
                NewWhatsAppId = message.System?.WaId,
            },
            _ => Unsupported(common, message.Type, message.Errors),
        };
    }

    private static WhatsAppEvent Media(MessageFields common, WebhookMedia? media, IncomingMediaKind kind) =>
        media?.Id is not { } id
            ? Unsupported(common, kind.ToString().ToLowerInvariant(), errors: null)
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
        // A submitted Flow arrives here rather than as a message type of its own, and carries
        // the whole form rather than one tapped control.
        if (interactive?.FlowReply is { } flow)
        {
            return New<FlowReply>(common) with
            {
                Name = flow.Name,
                Body = flow.Body,
                ResponseJson = flow.ResponseJson ?? string.Empty,
            };
        }

        var reply = interactive?.ButtonReply ?? interactive?.ListReply;

        if (reply?.Id is not { } id)
        {
            return Unsupported(common, type, errors: null);
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

    private static WhatsAppEvent? ToEvent(
        WebhookStatus status,
        string phoneNumberId,
        string? display,
        string businessAccountId)
    {
        if (status.Id is null || status.RecipientId is null)
        {
            return null;
        }

        return new MessageStatusChanged
        {
            PhoneNumberId = phoneNumberId,
            BusinessAccountId = businessAccountId,
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
            PricingType = status.Pricing?.Type,
            PricingModel = status.Pricing?.PricingModel,
            // Null when absent or unreadable, never the year-one default: a caller comparing
            // it against now to decide whether a free-form reply is still allowed would take
            // that for a window that closed long ago.
            ConversationExpiresAt = TryTimestamp(status.Conversation?.ExpirationTimestamp),
            CallbackData = status.CallbackData,
            Errors = status.Errors is { Count: > 0 } errors
                ? [.. errors.Select(e => e.ToError())]
                : [],
        };
    }

    private static UnsupportedMessage Unsupported(
        MessageFields common,
        string? type,
        List<GraphError>? errors) =>
        New<UnsupportedMessage>(common) with
        {
            Type = type ?? "unknown",
            Error = errors is { Count: > 0 } reported ? reported[0].ToError() : null,
        };

    private static TMessage New<TMessage>(MessageFields common)
        where TMessage : IncomingMessage, new() =>
        new()
        {
            PhoneNumberId = common.PhoneNumberId,
            BusinessAccountId = common.BusinessAccountId,
            DisplayPhoneNumber = common.Display,
            Timestamp = common.Timestamp,
            Id = common.Id,
            From = common.From,
            ProfileName = common.ProfileName,
            ReplyToMessageId = common.ReplyTo,
            IsForwarded = common.Forwarded,
            Referral = common.Referral,
            ReferredProduct = common.ReferredProduct,
        };

    private static MessageReferral? ToReferral(WebhookReferral? referral) =>
        referral is null
            ? null
            : new MessageReferral
            {
                SourceUrl = referral.SourceUrl,
                SourceType = referral.SourceType,
                SourceId = referral.SourceId,
                Headline = referral.Headline,
                Body = referral.Body,
                MediaType = referral.MediaType,
                ImageUrl = referral.ImageUrl,
                VideoUrl = referral.VideoUrl,
                ThumbnailUrl = referral.ThumbnailUrl,
                ClickId = referral.ClickId,
            };

    private static ReferredProduct? ToReferredProduct(WebhookReferredProduct? product) =>
        product is null ? null : new ReferredProduct(product.CatalogId, product.ProductRetailerId);

    private static string? ProfileNameOf(WebhookValue value, string from)
    {
        foreach (var contact in value.Contacts ?? [])
        {
            if (string.Equals(contact.WaId, from, StringComparison.Ordinal))
            {
                return contact.Profile?.Name;
            }
        }

        return null;
    }

    private static DateTimeOffset ToTimestamp(string? timestamp) =>
        TryTimestamp(timestamp) ?? default;

    private static DateTimeOffset? TryTimestamp(string? timestamp) =>
        long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

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
        string BusinessAccountId,
        string? Display,
        DateTimeOffset Timestamp,
        string Id,
        string From,
        string? ProfileName,
        string? ReplyTo,
        bool Forwarded,
        MessageReferral? Referral,
        ReferredProduct? ReferredProduct);
}
