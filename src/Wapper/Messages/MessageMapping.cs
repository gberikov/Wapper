using System.Globalization;
using Wapper.Internal;
using Wapper.Media;

namespace Wapper.Messages;

/// <summary>Turns the public message models into the shapes the Cloud API expects.</summary>
internal static class MessageMapping
{
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

        if (message.Buttons.Count is 0 or > 3)
        {
            throw new ArgumentException(
                $"A reply-button message carries one to three buttons, and this one has " +
                $"{message.Buttons.Count}. Use a list message for more choices.",
                nameof(message));
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
        if (rows is 0 or > 10)
        {
            throw new ArgumentException(
                $"A list message carries one to ten rows across all its sections, and this one " +
                $"has {rows}.",
                nameof(message));
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
                Parameters = new CallToActionParametersPayload
                {
                    DisplayText = message.ButtonText,
                    Url = message.Url.AbsoluteUri,
                },
            },
        };
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

    private static List<TTarget>? Map<TSource, TTarget>(
        IReadOnlyList<TSource> source,
        Func<TSource, TTarget> map) =>
        source.Count == 0 ? null : [.. source.Select(map)];
}
