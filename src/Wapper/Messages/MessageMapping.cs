using System.Globalization;
using System.Text.Json;
using Wapper.Internal;
using Wapper.Media;

namespace Wapper.Messages;

/// <summary>Turns the public message models into the shapes the Cloud API expects.</summary>
internal static class MessageMapping
{
    // What Meta accepts in an interactive message. Checked here because the alternative is
    // its answer to an oversized field: a bare 100 that does not say which field it objected
    // to, on a send that looked perfectly reasonable.
    //
    // The two body limits really do differ — a reply-button message takes 1024 characters and
    // a list message 4096 — and both are Meta's own documented numbers.

    private const int MaxButtons = 3;
    private const int MaxButtonIdLength = 256;
    private const int MaxButtonTitleLength = 20;
    private const int MaxButtonBodyLength = 1024;

    private const int MaxSections = 10;
    private const int MaxRows = 10;
    private const int MaxSectionTitleLength = 24;
    private const int MaxRowIdLength = 200;
    private const int MaxRowTitleLength = 24;
    private const int MaxRowDescriptionLength = 72;
    private const int MaxListButtonLength = 20;
    private const int MaxListBodyLength = 4096;

    private const int MaxHeaderLength = 60;
    private const int MaxFooterLength = 60;

    /// <summary>
    /// The parameter each of these limits is thrown against: the message the caller handed in.
    /// </summary>
    private const string MessageParameter = "message";

    public static MediaPayload ToPayload(this MediaSource source, string? caption = null, string? fileName = null)
    {
        if (source.Id is null && source.Link is null)
        {
            throw new ArgumentException(
                "The media source names neither an uploaded id nor a link.",
                nameof(source));
        }

        return new MediaPayload
        {
            Id = source.Id,
            Link = source.Link?.AbsoluteUri,
            Caption = caption,
            FileName = fileName,
        };
    }

    public static LocationPayload ToPayload(this Location location)
    {
        ArgumentNullException.ThrowIfNull(location);

        return new LocationPayload
        {
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Name = location.Name,
            // WhatsApp only shows the address when there is a name to show it under.
            Address = location.Name is null ? null : location.Address,
        };
    }

    public static ContactPayload ToPayload(this Contact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);

        return new ContactPayload
        {
            Name = new ContactNamePayload
            {
                FormattedName = contact.Name.FormattedName,
                FirstName = contact.Name.FirstName,
                LastName = contact.Name.LastName,
                MiddleName = contact.Name.MiddleName,
                Prefix = contact.Name.Prefix,
                Suffix = contact.Name.Suffix,
            },
            Phones = Map(contact.Phones, p => new ContactPhonePayload
            {
                Phone = p.Phone,
                Type = p.Type,
                WaId = p.WhatsAppId,
            }),
            Emails = Map(contact.Emails, e => new ContactEmailPayload { Email = e.Email, Type = e.Type }),
            Urls = Map(contact.Urls, u => new ContactUrlPayload { Url = u.Url, Type = u.Type }),
            Addresses = Map(contact.Addresses, a => new ContactAddressPayload
            {
                Street = a.Street,
                City = a.City,
                State = a.State,
                Zip = a.Zip,
                Country = a.Country,
                CountryCode = a.CountryCode,
                Type = a.Type,
            }),
            Org = contact.Organisation is { } org
                ? new ContactOrgPayload
                {
                    Company = org.Company,
                    Department = org.Department,
                    Title = org.Title,
                }
                : null,
            Birthday = contact.Birthday?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
    }

    public static InteractivePayload ToPayload(this ButtonMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Buttons.Count is 0 or > MaxButtons)
        {
            throw new ArgumentException(
                $"A reply-button message carries one to {MaxButtons} buttons, and this one has " +
                $"{message.Buttons.Count}. Use a list message for more choices.",
                nameof(message));
        }

        Limit(message.Body, MaxButtonBodyLength, "The body of a reply-button message");
        Limit(message.Header?.Text, MaxHeaderLength, "A text header");
        Limit(message.Footer, MaxFooterLength, "A footer");

        for (var i = 0; i < message.Buttons.Count; i++)
        {
            Limit(message.Buttons[i].Id, MaxButtonIdLength, $"The id of button {i + 1}");
            Limit(message.Buttons[i].Title, MaxButtonTitleLength, $"The title of button {i + 1}");
        }

        return new InteractivePayload
        {
            Type = "button",
            Header = message.Header?.ToPayload(),
            Body = new InteractiveTextPayload { Text = message.Body },
            Footer = Footer(message.Footer),
            Action = new InteractiveActionPayload
            {
                Buttons = [.. message.Buttons.Select(b => new InteractiveButtonPayload
                {
                    Reply = new InteractiveReplyPayload { Id = b.Id, Title = b.Title },
                })],
            },
        };
    }

    public static InteractivePayload ToPayload(this ListMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var rows = message.Sections.Sum(s => s.Rows.Count);
        if (rows is 0 or > MaxRows)
        {
            throw new ArgumentException(
                $"A list message carries one to {MaxRows} rows across all its sections, and this " +
                $"one has {rows}.",
                nameof(message));
        }

        if (message.Sections.Count > MaxSections)
        {
            throw new ArgumentException(
                $"A list message carries at most {MaxSections} sections, and this one has " +
                $"{message.Sections.Count}.",
                nameof(message));
        }

        Limit(message.Body, MaxListBodyLength, "The body of a list message");
        Limit(message.Header, MaxHeaderLength, "A text header");
        Limit(message.Footer, MaxFooterLength, "A footer");
        Limit(message.ButtonText, MaxListButtonLength, "The text on the button that opens a list");

        for (var s = 0; s < message.Sections.Count; s++)
        {
            var section = message.Sections[s];

            Limit(section.Title, MaxSectionTitleLength, $"The title of section {s + 1}");

            for (var r = 0; r < section.Rows.Count; r++)
            {
                // Numbered within the section, because that is how the caller wrote them.
                var where = $"row {r + 1} of section {s + 1}";

                Limit(section.Rows[r].Id, MaxRowIdLength, $"The id of {where}");
                Limit(section.Rows[r].Title, MaxRowTitleLength, $"The title of {where}");
                Limit(section.Rows[r].Description, MaxRowDescriptionLength, $"The description of {where}");
            }
        }

        return new InteractivePayload
        {
            Type = "list",
            // A list message only accepts a text header, whatever the other interactive
            // types allow.
            Header = message.Header is null
                ? null
                : new InteractiveHeaderPayload { Type = "text", Text = message.Header },
            Body = new InteractiveTextPayload { Text = message.Body },
            Footer = Footer(message.Footer),
            Action = new InteractiveActionPayload
            {
                Button = message.ButtonText,
                Sections = [.. message.Sections.Select(s => new InteractiveSectionPayload
                {
                    Title = s.Title,
                    Rows = [.. s.Rows.Select(r => new InteractiveRowPayload
                    {
                        Id = r.Id,
                        Title = r.Title,
                        Description = r.Description,
                    })],
                })],
            },
        };
    }

    public static InteractivePayload ToPayload(this CallToActionMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new InteractivePayload
        {
            Type = "cta_url",
            Header = message.Header?.ToPayload(),
            Body = new InteractiveTextPayload { Text = message.Body },
            Footer = Footer(message.Footer),
            Action = new InteractiveActionPayload
            {
                Name = "cta_url",
                Parameters = new InteractiveParametersPayload
                {
                    DisplayText = message.ButtonText,
                    Url = message.Url.AbsoluteUri,
                },
            },
        };
    }

    public static InteractivePayload ToPayload(this FlowMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.FlowToken);

        if (string.IsNullOrWhiteSpace(message.FlowId) == string.IsNullOrWhiteSpace(message.FlowName))
        {
            throw new ArgumentException(
                "A Flow message names the Flow either by id or by name, and this one names " +
                (string.IsNullOrWhiteSpace(message.FlowId) ? "neither." : "both."),
                nameof(message));
        }

        if (message.Action == FlowAction.Navigate && string.IsNullOrWhiteSpace(message.Screen))
        {
            // Meta answers this with a bare 100 that does not mention the screen.
            throw new ArgumentException(
                "A Flow that navigates opens on a screen, so Screen has to be set. Use " +
                $"{nameof(FlowAction)}.{nameof(FlowAction.DataExchange)} to let the Flow's " +
                "endpoint decide instead.",
                nameof(message));
        }

        if (message.Action == FlowAction.DataExchange
            && (message.Screen is not null || message.DataJson is not null))
        {
            // The first screen comes from the endpoint, so there is nothing to name and
            // nothing to hand it. Meta rejects the payload outright rather than ignoring it.
            throw new ArgumentException(
                "A Flow that asks its endpoint for the first screen takes neither a Screen " +
                "nor DataJson. Leave both unset, or use " +
                $"{nameof(FlowAction)}.{nameof(FlowAction.Navigate)} to open on a screen of " +
                "your own.",
                nameof(message));
        }

        return new InteractivePayload
        {
            Type = "flow",
            Header = message.Header?.ToPayload(),
            Body = new InteractiveTextPayload { Text = message.Body },
            Footer = Footer(message.Footer),
            Action = new InteractiveActionPayload
            {
                Name = "flow",
                Parameters = new InteractiveParametersPayload
                {
                    FlowMessageVersion = "3",
                    FlowToken = message.FlowToken,
                    FlowId = message.FlowId,
                    FlowName = message.FlowName,
                    FlowCallToAction = message.ButtonText,
                    FlowAction = message.Action == FlowAction.DataExchange
                        ? "data_exchange"
                        : "navigate",
                    Mode = message.Draft ? "draft" : null,
                    FlowActionPayload = message.Screen is null && message.DataJson is null
                        ? null
                        : new FlowActionPayload
                        {
                            Screen = message.Screen,
                            Data = ParseData(message.DataJson),
                        },
                },
            },
        };
    }

    /// <remarks>
    /// Parsed rather than passed through as a string: the Flow expects an object, and a
    /// string containing JSON is not one. Parsing here also means a malformed document is a
    /// clear argument failure instead of a bare 100 from Meta.
    /// </remarks>
    private static JsonElement? ParseData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    $"The data handed to a Flow screen is a JSON object, and this is a " +
                    $"{document.RootElement.ValueKind}.",
                    nameof(FlowMessage.DataJson));
            }

            using var properties = document.RootElement.EnumerateObject();

            if (!properties.MoveNext())
            {
                // Meta documents the data as a non-empty object and rejects {} with a bare
                // 100. Leaving the property unset is how a screen is handed nothing.
                throw new ArgumentException(
                    "The data handed to a Flow screen is a non-empty JSON object, and this " +
                    $"one is empty. Leave {nameof(FlowMessage.DataJson)} unset to hand the " +
                    "screen nothing.",
                    nameof(FlowMessage.DataJson));
            }

            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The data handed to a Flow screen is not valid JSON.",
                nameof(FlowMessage.DataJson),
                exception);
        }
    }

    public static TemplatePayload ToPayload(this TemplateMessage template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return new TemplatePayload
        {
            Name = template.Name,
            Language = new TemplateLanguagePayload { Code = template.Language },
            Components = template.Components.Count == 0
                ? null
                : [.. template.Components.Select(ToPayload)],
        };
    }

    private static InteractiveHeaderPayload ToPayload(this InteractiveHeader header) => header.Type switch
    {
        "text" => new InteractiveHeaderPayload { Type = "text", Text = header.Text },
        "image" => new InteractiveHeaderPayload { Type = "image", Image = header.Media!.Value.ToPayload() },
        "video" => new InteractiveHeaderPayload { Type = "video", Video = header.Media!.Value.ToPayload() },
        "document" => new InteractiveHeaderPayload { Type = "document", Document = header.Media!.Value.ToPayload() },
        _ => throw new ArgumentException($"Unknown interactive header type '{header.Type}'.", nameof(header)),
    };

    private static TemplateComponentPayload ToPayload(TemplateComponent component) => new()
    {
        Type = component.Type switch
        {
            TemplateComponentType.Header => "header",
            TemplateComponentType.Body => "body",
            TemplateComponentType.Button => "button",
            _ => throw new ArgumentException(
                $"Unknown template component type '{component.Type}'.",
                nameof(component)),
        },
        SubType = component.SubType,
        // Meta takes the button index as a string, not a number.
        Index = component.Index?.ToString(CultureInfo.InvariantCulture),
        Parameters = component.Parameters.Count == 0
            ? null
            : [.. component.Parameters.Select(ToPayload)],
    };

    private static TemplateParameterPayload ToPayload(TemplateParameter parameter) => new()
    {
        Type = parameter.Type,
        ParameterName = parameter.Name,
        Text = parameter.Text,
        Payload = parameter.PayloadValue,
        Image = parameter.Type == "image" ? parameter.Media!.Value.ToPayload() : null,
        Video = parameter.Type == "video" ? parameter.Media!.Value.ToPayload() : null,
        Document = parameter.Type == "document" ? parameter.Media!.Value.ToPayload() : null,
        CouponCode = parameter.CouponCode,
        Location = parameter.Location is { } point
            ? new TemplateLocationPayload
            {
                // Strings here, unlike the location message, which takes numbers.
                Latitude = point.Latitude.ToString(CultureInfo.InvariantCulture),
                Longitude = point.Longitude.ToString(CultureInfo.InvariantCulture),
                Name = point.Name,
                Address = point.Address,
            }
            : null,
        Currency = parameter.Currency is { } currency
            ? new TemplateCurrencyPayload
            {
                FallbackValue = currency.FallbackValue,
                Code = currency.Code,
                Amount1000 = currency.AmountInThousandths,
            }
            : null,
        DateTime = parameter.DateTimeText is { } moment
            ? new TemplateDateTimePayload { FallbackValue = moment }
            : null,
    };

    private static InteractiveTextPayload? Footer(string? footer) =>
        footer is null ? null : new InteractiveTextPayload { Text = footer };

    /// <summary>
    /// Refuses a field longer than Meta accepts, saying which field and how long it is.
    /// </summary>
    /// <remarks>
    /// Naming the field is the whole point. Meta answers an oversized one with a bare
    /// <c>100</c> that says nothing about which of a dozen strings it objected to, and a list
    /// message has one per row.
    /// </remarks>
    private static void Limit(string? value, int max, string what)
    {
        if (value is not null && value.Length > max)
        {
            throw new ArgumentException(
                $"{what} is at most {max} characters, and this one is {value.Length}.",
                MessageParameter);
        }
    }

    private static List<TTarget>? Map<TSource, TTarget>(
        IReadOnlyList<TSource> source,
        Func<TSource, TTarget> map) =>
        source.Count == 0 ? null : [.. source.Select(map)];
}
