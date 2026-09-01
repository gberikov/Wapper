using Wapper.Internal;
using Wapper.Webhooks;

namespace Wapper.Templates;

/// <summary>Between the public template model and the shapes the management API uses.</summary>
internal static class TemplateMapping
{
    public static TemplateDefinitionPayload ToPayload(this Template template, bool? allowCategoryChange)
    {
        ArgumentNullException.ThrowIfNull(template);

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
            Components = ToComponents(template),
        };
    }

    /// <summary>The components alone, which is all an edit is allowed to send.</summary>
    public static List<TemplateComponentDefinitionPayload> ToComponents(Template template)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (template.UnknownComponents.Count > 0)
        {
            // Components are replaced wholesale, not merged. Writing this template back
            // without the components this library cannot model would silently erase them at
            // Meta — a carousel losing its whole card deck over a typo fix in the body.
            throw new ArgumentException(
                "This template carries components this library has no typed form for " +
                $"({string.Join(", ", template.UnknownComponents)}). An edit replaces the " +
                "components wholesale, so writing it back would erase them at Meta. Edit it " +
                "in WhatsApp Manager, or through Raw.",
                nameof(template));
        }

        var components = new List<TemplateComponentDefinitionPayload>(4);

        if (template.Header is { } header)
        {
            components.Add(ToPayload(header, template.ParameterFormat));
        }

        components.Add(new TemplateComponentDefinitionPayload
        {
            Type = "BODY",
            // Left out on an authentication template, which Meta writes itself and refuses
            // to accept text for.
            Text = string.IsNullOrEmpty(template.Body.Text) ? null : template.Body.Text,
            Example = BodyExample(template.Body.Examples, template.ParameterFormat),
            AddSecurityRecommendation = template.Body.AddSecurityRecommendation,
        });

        if (!string.IsNullOrEmpty(template.Footer) || template.CodeExpirationMinutes is not null)
        {
            components.Add(new TemplateComponentDefinitionPayload
            {
                Type = "FOOTER",
                // The expiry replaces the footer text rather than joining it: Meta writes
                // that sentence itself so it comes out translated.
                Text = template.CodeExpirationMinutes is null ? template.Footer : null,
                CodeExpirationMinutes = template.CodeExpirationMinutes,
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

        return components;
    }

    public static Template ToTemplate(this TemplateDefinitionPayload payload)
    {
        var format = string.Equals(payload.ParameterFormat, "NAMED", StringComparison.OrdinalIgnoreCase)
            ? TemplateParameterFormat.Named
            : TemplateParameterFormat.Positional;

        TemplateHeader? header = null;
        TemplateBody? body = null;
        string? footer = null;
        int? codeExpiration = null;
        IReadOnlyList<TemplateButton> buttons = [];
        List<string>? unknown = null;

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
                        AddSecurityRecommendation = component.AddSecurityRecommendation,
                    };
                    break;

                case "FOOTER":
                    footer = component.Text;
                    codeExpiration = component.CodeExpirationMinutes;
                    break;

                case "BUTTONS":
                    buttons = [.. (component.Buttons ?? []).Select(ToButton)];
                    break;

                default:
                    // Meta adds component types without warning. Failing the whole read over
                    // one would take the listing with it, so the component is recorded by its
                    // type instead — which is also what stops an edit erasing it unseen.
                    (unknown ??= []).Add(component.Type ?? "(untyped)");
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
            CodeExpirationMinutes = codeExpiration,
            Buttons = buttons,
            TimeToLive = payload.MessageSendTtlSeconds is { } seconds
                ? TimeSpan.FromSeconds(seconds)
                : null,
            QualityScore = ParseQuality(payload.QualityScore?.Score),
            RawQualityScore = payload.QualityScore?.Score,
            RejectedReason = payload.RejectedReason,
            PreviousCategory = ParseCategory(payload.PreviousCategory),
            UnknownComponents = unknown ?? [],
        };
    }

    /// <remarks>
    /// Meta spells the same four ratings two ways depending on which endpoint you ask, so
    /// both spellings are accepted — as they are for the webhook that reports the same thing.
    /// </remarks>
    internal static TemplateQuality ParseQuality(string? score) => score?.ToUpperInvariant() switch
    {
        "GREEN" or "HIGH" => TemplateQuality.Green,
        "YELLOW" or "MEDIUM" => TemplateQuality.Yellow,
        "RED" or "LOW" => TemplateQuality.Red,
        "UNKNOWN" or "PENDING" => TemplateQuality.Pending,
        _ => TemplateQuality.Unknown,
    };

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

    /// <remarks>
    /// Built through the internal constructor rather than the public factories: those
    /// validate, and Meta does not promise to send a media handle back with a template it
    /// stored months ago. Failing here would take down the whole listing over one component.
    /// </remarks>
    private static TemplateHeader? ToHeader(TemplateComponentDefinitionPayload component) =>
        component.Format?.ToUpperInvariant() switch
        {
            "TEXT" => new TemplateHeader(TemplateHeaderFormat.Text)
            {
                Text = component.Text,
                Examples = ReadHeaderExamples(component.Example),
            },
            "IMAGE" => new TemplateHeader(TemplateHeaderFormat.Image) { MediaHandle = FirstHandle(component) },
            "VIDEO" => new TemplateHeader(TemplateHeaderFormat.Video) { MediaHandle = FirstHandle(component) },
            "DOCUMENT" => new TemplateHeader(TemplateHeaderFormat.Document) { MediaHandle = FirstHandle(component) },
            "LOCATION" => new TemplateHeader(TemplateHeaderFormat.Location),
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
        TemplateButtonKind.OneTimePassword => OtpToPayload(button),
        _ => throw new ArgumentException(
            $"'{button.Kind}' is not a button the Cloud API accepts.",
            nameof(button)),
    };

    private static TemplateButtonDefinitionPayload OtpToPayload(TemplateButton button)
    {
        var otp = button.OneTimePassword ?? throw new ArgumentException(
            "A one-time-passcode button has to say how the code is delivered. Build one with " +
            $"{nameof(TemplateButton)}.{nameof(TemplateButton.CopyOneTimePassword)} or " +
            $"{nameof(TemplateButton.AutofillOneTimePassword)}.",
            nameof(button));

        return new TemplateButtonDefinitionPayload
        {
            Type = "OTP",
            Text = button.Text,
            OtpType = otp.Delivery switch
            {
                OneTimePasswordDelivery.CopyCode => "COPY_CODE",
                OneTimePasswordDelivery.OneTap => "ONE_TAP",
                OneTimePasswordDelivery.ZeroTap => "ZERO_TAP",
                _ => throw new ArgumentException(
                    $"'{otp.Delivery}' is not a passcode delivery the Cloud API accepts.",
                    nameof(button)),
            },
            AutofillText = otp.AutofillText,
            // The newer of the two shapes. Meta reads the older package_name and
            // signature_hash pair as a one-app list, so there is nothing to gain by sending it.
            SupportedApps = otp.SupportedApps.Count == 0
                ? null
                : [.. otp.SupportedApps.Select(app => new TemplateApplicationPayload
                {
                    PackageName = app.PackageName,
                    SignatureHash = app.SignatureHash,
                })],
            ZeroTapTermsAccepted = otp.ZeroTapTermsAccepted,
        };
    }

    private static TemplateOneTimePassword ToOtp(TemplateButtonDefinitionPayload payload)
    {
        var apps = new List<TemplateApplication>();

        foreach (var app in payload.SupportedApps ?? [])
        {
            apps.Add(new TemplateApplication(app.PackageName ?? string.Empty, app.SignatureHash ?? string.Empty));
        }

        // The shape Meta used before supported_apps, still returned for templates created
        // under it. Folded into the list so a caller only has one place to look.
        if (apps.Count == 0 && payload.PackageName is { } package)
        {
            apps.Add(new TemplateApplication(package, payload.SignatureHash ?? string.Empty));
        }

        return new TemplateOneTimePassword
        {
            Delivery = payload.OtpType?.ToUpperInvariant() switch
            {
                "COPY_CODE" => OneTimePasswordDelivery.CopyCode,
                "ONE_TAP" => OneTimePasswordDelivery.OneTap,
                "ZERO_TAP" => OneTimePasswordDelivery.ZeroTap,
                _ => OneTimePasswordDelivery.Unknown,
            },
            RawDelivery = payload.OtpType,
            AutofillText = payload.AutofillText,
            SupportedApps = apps,
            ZeroTapTermsAccepted = payload.ZeroTapTermsAccepted,
        };
    }

    /// <inheritdoc cref="ToHeader" path="/remarks" />
    private static TemplateButton ToButton(TemplateButtonDefinitionPayload payload) =>
        payload.Type?.ToUpperInvariant() switch
        {
            "QUICK_REPLY" => new TemplateButton(TemplateButtonKind.QuickReply) { Text = payload.Text },
            "URL" => new TemplateButton(TemplateButtonKind.Url)
            {
                Text = payload.Text,
                Url = payload.Url,
                UrlExample = payload.Example?.First,
            },
            "PHONE_NUMBER" => new TemplateButton(TemplateButtonKind.PhoneNumber)
            {
                Text = payload.Text,
                PhoneNumber = payload.PhoneNumber,
            },
            "COPY_CODE" => new TemplateButton(TemplateButtonKind.CopyCode)
            {
                Text = payload.Text,
                CopyCodeExample = payload.Example?.First,
            },
            "VOICE_CALL" => new TemplateButton(TemplateButtonKind.VoiceCall) { Text = payload.Text },
            "OTP" => new TemplateButton(TemplateButtonKind.OneTimePassword)
            {
                Text = payload.Text,
                OneTimePassword = ToOtp(payload),
            },
            _ => TemplateButton.FromUnknown(payload.Type, payload.Text),
        };
}
