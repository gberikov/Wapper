using Wapper.Internal;

namespace Wapper.Templates;

/// <summary>Between the public template model and the shapes the management API uses.</summary>
internal static class TemplateMapping
{
    public static TemplateDefinitionPayload ToPayload(this Template template, bool? allowCategoryChange)
    {
        ArgumentNullException.ThrowIfNull(template);

        var components = new List<TemplateComponentDefinitionPayload>(4);

        if (template.Header is { } header)
        {
            components.Add(ToPayload(header, template.ParameterFormat));
        }

        components.Add(new TemplateComponentDefinitionPayload
        {
            Type = "BODY",
            Text = template.Body.Text,
            Example = BodyExample(template.Body.Examples, template.ParameterFormat),
        });

        if (!string.IsNullOrEmpty(template.Footer))
        {
            components.Add(new TemplateComponentDefinitionPayload
            {
                Type = "FOOTER",
                Text = template.Footer,
            });
        }

        if (template.Buttons.Count > 0)
        {
            components.Add(new TemplateComponentDefinitionPayload
            {
                Type = "BUTTONS",
                Buttons = [.. template.Buttons.Select(ToPayload)],
            });
        }

        return new TemplateDefinitionPayload
        {
            Name = template.Name,
            Language = template.Language,
            Category = ToWire(template.Category),
            ParameterFormat = template.ParameterFormat == TemplateParameterFormat.Named
                ? "NAMED"
                : "POSITIONAL",
            AllowCategoryChange = allowCategoryChange,
            MessageSendTtlSeconds = template.TimeToLive is { } ttl ? (int)ttl.TotalSeconds : null,
            Components = components,
        };
    }

    public static Template ToTemplate(this TemplateDefinitionPayload payload)
    {
        var format = string.Equals(payload.ParameterFormat, "NAMED", StringComparison.OrdinalIgnoreCase)
            ? TemplateParameterFormat.Named
            : TemplateParameterFormat.Positional;

        TemplateHeader? header = null;
        TemplateBody? body = null;
        string? footer = null;
        IReadOnlyList<TemplateButton> buttons = [];

        foreach (var component in payload.Components ?? [])
        {
            switch (component.Type?.ToUpperInvariant())
            {
                case "HEADER":
                    header = ToHeader(component);
                    break;

                case "BODY":
                    body = new TemplateBody
                    {
                        Text = component.Text ?? string.Empty,
                        Examples = ReadBodyExamples(component.Example),
                    };
                    break;

                case "FOOTER":
                    footer = component.Text;
                    break;

                case "BUTTONS":
                    buttons = [.. (component.Buttons ?? []).Select(ToButton)];
                    break;

                default:
                    // Meta adds component types without warning. Losing one is better than
                    // failing to read the whole template because of it.
                    break;
            }
        }

        return new Template
        {
            Id = payload.Id,
            Name = payload.Name ?? string.Empty,
            Language = payload.Language ?? string.Empty,
            Category = ParseCategory(payload.Category),
            Status = ParseStatus(payload.Status),
            RawStatus = payload.Status,
            SubCategory = payload.SubCategory,
            ParameterFormat = format,
            Header = header,
            // The body is the one component Meta requires, so its absence means the payload
            // was not a template at all.
            Body = body ?? new TemplateBody { Text = string.Empty },
            Footer = footer,
            Buttons = buttons,
            TimeToLive = payload.MessageSendTtlSeconds is { } seconds
                ? TimeSpan.FromSeconds(seconds)
                : null,
        };
    }

    public static TemplateCategory ParseCategory(string? category) =>
        category?.ToUpperInvariant() switch
        {
            "AUTHENTICATION" => TemplateCategory.Authentication,
            "MARKETING" => TemplateCategory.Marketing,
            "UTILITY" => TemplateCategory.Utility,
            _ => TemplateCategory.Unknown,
        };

    public static TemplateStatus ParseStatus(string? status) =>
        status?.ToUpperInvariant() switch
        {
            "PENDING" or "IN_REVIEW" => TemplateStatus.Pending,
            "APPROVED" or "ACTIVE" => TemplateStatus.Approved,
            "REJECTED" => TemplateStatus.Rejected,
            "PAUSED" => TemplateStatus.Paused,
            "DISABLED" => TemplateStatus.Disabled,
            "IN_APPEAL" or "PENDING_APPEAL" or "APPEAL_REQUESTED" => TemplateStatus.InAppeal,
            "PENDING_DELETION" => TemplateStatus.PendingDeletion,
            "DELETED" => TemplateStatus.Deleted,
            "ARCHIVED" => TemplateStatus.Archived,
            "LIMIT_EXCEEDED" => TemplateStatus.LimitExceeded,
            _ => TemplateStatus.Unknown,
        };

    public static string ToWire(TemplateCategory category) => category switch
    {
        TemplateCategory.Authentication => "AUTHENTICATION",
        TemplateCategory.Marketing => "MARKETING",
        TemplateCategory.Utility => "UTILITY",
        _ => throw new ArgumentException(
            $"'{category}' is not a category the Cloud API accepts.",
            nameof(category)),
    };

    public static string ToWire(TemplateStatus status) => status switch
    {
        TemplateStatus.Pending => "PENDING",
        TemplateStatus.Approved => "APPROVED",
        TemplateStatus.Rejected => "REJECTED",
        TemplateStatus.Paused => "PAUSED",
        TemplateStatus.Disabled => "DISABLED",
        TemplateStatus.InAppeal => "IN_APPEAL",
        TemplateStatus.PendingDeletion => "PENDING_DELETION",
        TemplateStatus.Deleted => "DELETED",
        TemplateStatus.Archived => "ARCHIVED",
        TemplateStatus.LimitExceeded => "LIMIT_EXCEEDED",
        _ => throw new ArgumentException(
            $"'{status}' is not a status the Cloud API filters on.",
            nameof(status)),
    };

    private static TemplateComponentDefinitionPayload ToPayload(
        TemplateHeader header,
        TemplateParameterFormat format) =>
        header.Format switch
        {
            TemplateHeaderFormat.Text => new TemplateComponentDefinitionPayload
            {
                Type = "HEADER",
                Format = "TEXT",
                Text = header.Text,
                Example = HeaderExample(header.Examples, format),
            },
            TemplateHeaderFormat.Location => new TemplateComponentDefinitionPayload
            {
                Type = "HEADER",
                Format = "LOCATION",
            },
            _ => new TemplateComponentDefinitionPayload
            {
                Type = "HEADER",
                Format = header.Format.ToString().ToUpperInvariant(),
                Example = new TemplateExamplePayload
                {
                    HeaderHandle = header.MediaHandle is { } handle ? [handle] : null,
                },
            },
        };

    private static TemplateExamplePayload? HeaderExample(
        IReadOnlyList<TemplateParameterExample> examples,
        TemplateParameterFormat format)
    {
        if (examples.Count == 0)
        {
            return null;
        }

        return format == TemplateParameterFormat.Named
            ? new TemplateExamplePayload { HeaderTextNamedParams = [.. examples.Select(ToNamed)] }
            : new TemplateExamplePayload { HeaderText = [.. examples.Select(e => e.Value)] };
    }

    private static TemplateExamplePayload? BodyExample(
        IReadOnlyList<TemplateParameterExample> examples,
        TemplateParameterFormat format)
    {
        if (examples.Count == 0)
        {
            return null;
        }

        // The positional body example is a list of lists: one inner list per example set,
        // and Meta only ever reviews the first.
        return format == TemplateParameterFormat.Named
            ? new TemplateExamplePayload { BodyTextNamedParams = [.. examples.Select(ToNamed)] }
            : new TemplateExamplePayload { BodyText = [[.. examples.Select(e => e.Value)]] };
    }

    private static TemplateNamedExamplePayload ToNamed(TemplateParameterExample example) => new()
    {
        ParamName = example.Name,
        Example = example.Value,
    };

    private static TemplateHeader? ToHeader(TemplateComponentDefinitionPayload component) =>
        component.Format?.ToUpperInvariant() switch
        {
            "TEXT" => TemplateHeader.FromText(
                component.Text ?? " ",
                [.. ReadHeaderExamples(component.Example)]),
            "IMAGE" => TemplateHeader.FromImage(FirstHandle(component) ?? " "),
            "VIDEO" => TemplateHeader.FromVideo(FirstHandle(component) ?? " "),
            "DOCUMENT" => TemplateHeader.FromDocument(FirstHandle(component) ?? " "),
            "LOCATION" => TemplateHeader.FromLocation(),
            _ => null,
        };

    private static string? FirstHandle(TemplateComponentDefinitionPayload component) =>
        component.Example?.HeaderHandle is { Count: > 0 } handles ? handles[0] : null;

    private static IReadOnlyList<TemplateParameterExample> ReadHeaderExamples(
        TemplateExamplePayload? example)
    {
        if (example?.HeaderTextNamedParams is { Count: > 0 } named)
        {
            return [.. named.Select(n => new TemplateParameterExample(n.Example ?? string.Empty, n.ParamName))];
        }

        return example?.HeaderText is { Count: > 0 } positional
            ? [.. positional.Select(value => new TemplateParameterExample(value))]
            : [];
    }

    private static IReadOnlyList<TemplateParameterExample> ReadBodyExamples(
        TemplateExamplePayload? example)
    {
        if (example?.BodyTextNamedParams is { Count: > 0 } named)
        {
            return [.. named.Select(n => new TemplateParameterExample(n.Example ?? string.Empty, n.ParamName))];
        }

        return example?.BodyText is { Count: > 0 } positional
            ? [.. positional[0].Select(value => new TemplateParameterExample(value))]
            : [];
    }

    private static TemplateButtonDefinitionPayload ToPayload(TemplateButton button) => button.Kind switch
    {
        TemplateButtonKind.QuickReply => new TemplateButtonDefinitionPayload
        {
            Type = "QUICK_REPLY",
            Text = button.Text,
        },
        TemplateButtonKind.Url => new TemplateButtonDefinitionPayload
        {
            Type = "URL",
            Text = button.Text,
            Url = button.Url,
            Example = button.UrlExample is { } example
                ? new TemplateButtonExamplePayload { Values = [example] }
                : null,
        },
        TemplateButtonKind.PhoneNumber => new TemplateButtonDefinitionPayload
        {
            Type = "PHONE_NUMBER",
            Text = button.Text,
            PhoneNumber = button.PhoneNumber,
        },
        TemplateButtonKind.CopyCode => new TemplateButtonDefinitionPayload
        {
            Type = "COPY_CODE",
            // A bare string here, not a list, unlike every other button's example.
            Example = new TemplateButtonExamplePayload { Value = button.CopyCodeExample },
        },
        TemplateButtonKind.VoiceCall => new TemplateButtonDefinitionPayload
        {
            Type = "VOICE_CALL",
            Text = button.Text,
        },
        _ => throw new ArgumentException(
            $"'{button.Kind}' is not a button the Cloud API accepts.",
            nameof(button)),
    };

    private static TemplateButton ToButton(TemplateButtonDefinitionPayload payload) =>
        payload.Type?.ToUpperInvariant() switch
        {
            "QUICK_REPLY" => TemplateButton.QuickReply(payload.Text ?? " "),
            "URL" => TemplateButton.Link(payload.Text ?? " ", payload.Url ?? " ", payload.Example?.First),
            "PHONE_NUMBER" => TemplateButton.Call(payload.Text ?? " ", payload.PhoneNumber ?? " "),
            "COPY_CODE" => TemplateButton.CopyCode(payload.Example?.First ?? " "),
            "VOICE_CALL" => TemplateButton.VoiceCall(payload.Text ?? " "),
            _ => TemplateButton.FromUnknown(payload.Type, payload.Text),
        };
}
