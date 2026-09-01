using System.Globalization;
using System.Text.RegularExpressions;
using Wapper.Messages;

namespace Wapper.Templates;

/// <summary>What is wrong with a set of values for a template.</summary>
public enum TemplateProblem
{
    /// <summary>The template declares a placeholder nothing fills.</summary>
    Missing,

    /// <summary>The message sends a value the template has no placeholder for.</summary>
    Unexpected,

    /// <summary>
    /// Named values for a template with numbered placeholders, or the other way round.
    /// </summary>
    WrongFormat,

    /// <summary>
    /// The value or the button is of the wrong kind — text for a media header, a quick reply
    /// for a URL button.
    /// </summary>
    WrongKind,
}

/// <summary>One thing the Cloud API would reject the message for.</summary>
/// <remarks>
/// <see cref="Description"/> is written for whoever has to fix the broadcast file, and is
/// what <see cref="ToString"/> returns. The other properties are for code that sorts or
/// counts them.
/// </remarks>
public sealed record TemplateIssue
{
    /// <summary>Which part of the template it is about.</summary>
    public required TemplateComponentType Component { get; init; }

    /// <summary>What is wrong.</summary>
    public required TemplateProblem Problem { get; init; }

    /// <summary>
    /// The placeholder it is about — <c>{{2}}</c>, or <c>order_number</c> — when one
    /// placeholder is to blame.
    /// </summary>
    public string? Parameter { get; init; }

    /// <summary>What is wrong, in a sentence.</summary>
    public required string Description { get; init; }

    /// <inheritdoc />
    public override string ToString() => Description;
}

/// <summary>
/// Checking a set of values against the template it is meant to fill, before any of it is
/// sent.
/// </summary>
/// <remarks>
/// Meta rejects a mismatch on every single message, so a broadcast that gets this wrong burns
/// its whole first wave, takes the quality rating down with it and delivers nothing. The
/// template says everything needed to catch that on the way out of the file it was read from.
/// </remarks>
public static partial class TemplateValidation
{
    /// <summary>
    /// Everything the Cloud API would reject this message for, or an empty list when the
    /// values fit the template.
    /// </summary>
    /// <param name="template">
    /// The template as Meta holds it, from <c>Templates.GetAsync</c> or
    /// <c>Templates.ListAsync</c>. Read it once and check the whole list against it.
    /// </param>
    /// <param name="message">The values about to be sent.</param>
    /// <remarks>
    /// <para>
    /// Pure and offline: no call is made, nothing is sent, and the same template can be
    /// reused across a list of recipients.
    /// </para>
    /// <para>
    /// The two are assumed to be the same template — the names and languages are not
    /// compared, because a mismatch there is a bug in the caller rather than something Meta
    /// would explain.
    /// </para>
    /// <para>
    /// It answers for what the template declares. Meta also enforces limits that depend on
    /// the values themselves — a filled-in template longer than 1024 characters, a newline
    /// inside a parameter — and those still come back as <c>132005</c> and <c>132007</c>.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<TemplateIssue> Validate(this Template template, TemplateMessage message)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(message);

        var issues = new List<TemplateIssue>();

        ValidateHeader(template, Component(message, TemplateComponentType.Header, issues), issues);
        ValidateBody(template, Component(message, TemplateComponentType.Body, issues), issues);
        ValidateButtons(template, message, issues);

        return issues;
    }

    /// <summary>The header or the body component, of which Meta allows exactly one.</summary>
    private static TemplateComponent? Component(
        TemplateMessage message,
        TemplateComponentType type,
        List<TemplateIssue> issues)
    {
        TemplateComponent? found = null;

        foreach (var component in message.Components.Where(component => component.Type == type))
        {
            if (found is null)
            {
                found = component;
                continue;
            }

            issues.Add(new TemplateIssue
            {
                Component = type,
                Problem = TemplateProblem.Unexpected,
                Description = $"The message carries more than one {Name(type)} component. " +
                              "A template has one of each, and Meta reads the first.",
            });
        }

        return found;
    }

    private static void ValidateHeader(
        Template template,
        TemplateComponent? component,
        List<TemplateIssue> issues)
    {
        var parameters = component?.Parameters ?? [];

        if (template.Header is not { } header)
        {
            if (parameters.Count > 0)
            {
                issues.Add(new TemplateIssue
                {
                    Component = TemplateComponentType.Header,
                    Problem = TemplateProblem.Unexpected,
                    Description = "The template has no header, but the message sends " +
                                  $"{parameters.Count} value(s) for one.",
                });
            }

            return;
        }

        if (header.Format == TemplateHeaderFormat.Text)
        {
            ValidateText(
                header.Text ?? string.Empty,
                template.ParameterFormat,
                TemplateComponentType.Header,
                parameters,
                issues);

            return;
        }

        // An image, video, document or location header carries no placeholder: it takes one
        // value, and the value is the media itself. The template only ever held a sample.
        var expected = header.Format switch
        {
            TemplateHeaderFormat.Image => "image",
            TemplateHeaderFormat.Video => "video",
            TemplateHeaderFormat.Document => "document",
            _ => "location",
        };

        if (parameters.Count == 0)
        {
            issues.Add(new TemplateIssue
            {
                Component = TemplateComponentType.Header,
                Problem = TemplateProblem.Missing,
                Description = $"The template's header is a {expected}, and the message sends " +
                              "nothing for it.",
            });

            return;
        }

        if (!string.Equals(parameters[0].Type, expected, StringComparison.Ordinal))
        {
            issues.Add(new TemplateIssue
            {
                Component = TemplateComponentType.Header,
                Problem = TemplateProblem.WrongKind,
                Description = $"The template's header is a {expected}, and the message sends " +
                              $"a {parameters[0].Type} for it.",
            });
        }

        if (parameters.Count > 1)
        {
            issues.Add(new TemplateIssue
            {
                Component = TemplateComponentType.Header,
                Problem = TemplateProblem.Unexpected,
                Description = $"A {expected} header takes one value, and the message sends " +
                              $"{parameters.Count}.",
            });
        }
    }

    private static void ValidateBody(
        Template template,
        TemplateComponent? component,
        List<TemplateIssue> issues)
    {
        var parameters = component?.Parameters ?? [];

        if (template.Category == TemplateCategory.Authentication)
        {
            // Meta writes the body of an authentication template itself, in every language,
            // so there is no text here to count placeholders in. The one value it takes is
            // the passcode.
            if (parameters.Count != 1)
            {
                issues.Add(new TemplateIssue
                {
                    Component = TemplateComponentType.Body,
                    Problem = parameters.Count == 0
                        ? TemplateProblem.Missing
                        : TemplateProblem.Unexpected,
                    Description = "An authentication template takes exactly one body value, " +
                                  $"the passcode, and the message sends {parameters.Count}.",
                });
            }

            return;
        }

        ValidateText(
            template.Body.Text,
            template.ParameterFormat,
            TemplateComponentType.Body,
            parameters,
            issues);
    }

    /// <summary>
    /// The values for one piece of template text, against the placeholders in it.
    /// </summary>
    private static void ValidateText(
        string text,
        TemplateParameterFormat format,
        TemplateComponentType component,
        IReadOnlyList<TemplateParameter> parameters,
        List<TemplateIssue> issues)
    {
        var placeholders = Placeholder().Matches(text)
            .Select(match => match.Groups[1].Value)
            .ToList();

        if (format == TemplateParameterFormat.Named)
        {
            ValidateNamed(placeholders, component, parameters, issues);
        }
        else
        {
            ValidatePositional(placeholders, component, parameters, issues);
        }
    }

    private static void ValidateNamed(
        List<string> placeholders,
        TemplateComponentType component,
        IReadOnlyList<TemplateParameter> parameters,
        List<TemplateIssue> issues)
    {
        // A name repeated in the text is still one substitution: Meta fills every occurrence
        // from the single value.
        var expected = new HashSet<string>(placeholders, StringComparer.Ordinal);
        var given = new HashSet<string>(StringComparer.Ordinal);

        foreach (var parameter in parameters)
        {
            if (string.IsNullOrEmpty(parameter.Name))
            {
                issues.Add(new TemplateIssue
                {
                    Component = component,
                    Problem = TemplateProblem.WrongFormat,
                    Description = $"The template's {Name(component)} placeholders are named, " +
                                  "and the message sends a value with no name. Positional " +
                                  "values are not matched by order in a named template.",
                });

                continue;
            }

            given.Add(parameter.Name);
        }

        foreach (var name in expected.Where(name => !given.Contains(name)))
        {
            issues.Add(new TemplateIssue
            {
                Component = component,
                Problem = TemplateProblem.Missing,
                Parameter = name,
                Description = $"The template's {Name(component)} has {{{{{name}}}}}, and the " +
                              "message sends no value for it.",
            });
        }

        foreach (var name in given.Where(name => !expected.Contains(name)))
        {
            issues.Add(new TemplateIssue
            {
                Component = component,
                Problem = TemplateProblem.Unexpected,
                Parameter = name,
                Description = $"The message sends '{name}' for the {Name(component)}, and the " +
                              "template has no such placeholder.",
            });
        }
    }

    private static void ValidatePositional(
        List<string> placeholders,
        TemplateComponentType component,
        IReadOnlyList<TemplateParameter> parameters,
        List<TemplateIssue> issues)
    {
        // Counted by the highest index, not by how many placeholders appear. A body reading
        // "only {{2}}" expects two values and Meta rejects one, because it fills by position.
        var expected = 0;

        foreach (var placeholder in placeholders)
        {
            if (int.TryParse(placeholder, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                expected = Math.Max(expected, index);
            }
        }

        if (parameters.Any(parameter => !string.IsNullOrEmpty(parameter.Name)))
        {
            issues.Add(new TemplateIssue
            {
                Component = component,
                Problem = TemplateProblem.WrongFormat,
                Description = $"The template's {Name(component)} placeholders are numbered, " +
                              "and the message sends named values. Meta matches them by " +
                              "position and ignores the names.",
            });
        }

        for (var index = parameters.Count + 1; index <= expected; index++)
        {
            issues.Add(new TemplateIssue
            {
                Component = component,
                Problem = TemplateProblem.Missing,
                Parameter = $"{{{{{index}}}}}",
                Description = $"The template's {Name(component)} goes up to {{{{{expected}}}}}, " +
                              $"and the message sends {parameters.Count} value(s), so " +
                              $"{{{{{index}}}}} is unfilled.",
            });
        }

        for (var index = expected + 1; index <= parameters.Count; index++)
        {
            issues.Add(new TemplateIssue
            {
                Component = component,
                Problem = TemplateProblem.Unexpected,
                Parameter = $"{{{{{index}}}}}",
                Description = $"The template's {Name(component)} takes {expected} value(s), " +
                              $"and the message sends a {index}{Ordinal(index)}.",
            });
        }
    }

    private static void ValidateButtons(
        Template template,
        TemplateMessage message,
        List<TemplateIssue> issues)
    {
        var filled = new HashSet<int>();

        foreach (var component in message.Components
                     .Where(component => component.Type == TemplateComponentType.Button))
        {
            if (component.Index is not { } index)
            {
                issues.Add(new TemplateIssue
                {
                    Component = TemplateComponentType.Button,
                    Problem = TemplateProblem.Missing,
                    Description = "A button component carries no index. Meta matches a button " +
                                  "by its position, never by its label.",
                });

                continue;
            }

            if (index < 0 || index >= template.Buttons.Count)
            {
                issues.Add(new TemplateIssue
                {
                    Component = TemplateComponentType.Button,
                    Problem = TemplateProblem.Unexpected,
                    Description = $"The message fills button {index}, and the template has " +
                                  $"{template.Buttons.Count}. The index is the button's " +
                                  "position among all of them, whatever their kinds.",
                });

                continue;
            }

            if (!filled.Add(index))
            {
                issues.Add(new TemplateIssue
                {
                    Component = TemplateComponentType.Button,
                    Problem = TemplateProblem.Unexpected,
                    Description = $"The message fills button {index} more than once.",
                });

                continue;
            }

            ValidateButton(template.Buttons[index], index, component, issues);
        }

        for (var index = 0; index < template.Buttons.Count; index++)
        {
            if (filled.Contains(index) || !IsRequired(template.Buttons[index]))
            {
                continue;
            }

            issues.Add(new TemplateIssue
            {
                Component = TemplateComponentType.Button,
                Problem = TemplateProblem.Missing,
                Description = $"Button {index} is a {Name(template.Buttons[index].Kind)} that " +
                              "takes a value per message, and the message sends none.",
            });
        }
    }

    private static void ValidateButton(
        TemplateButton declared,
        int index,
        TemplateComponent component,
        List<TemplateIssue> issues)
    {
        var subType = string.IsNullOrEmpty(component.SubType) ? "(none)" : component.SubType;

        // Meta's own name for the button at this position. An authentication template's
        // passcode button is sent as "url", and older callers send it as "copy_code", so both
        // are taken for it.
        string[] accepted = declared.Kind switch
        {
            TemplateButtonKind.QuickReply => ["quick_reply"],
            TemplateButtonKind.Url => ["url"],
            TemplateButtonKind.CopyCode => ["copy_code"],
            TemplateButtonKind.OneTimePassword => ["url", "copy_code"],
            _ => [],
        };

        if (accepted.Length == 0)
        {
            issues.Add(new TemplateIssue
            {
                Component = TemplateComponentType.Button,
                Problem = TemplateProblem.Unexpected,
                Description = $"Button {index} is a {Name(declared.Kind)}, which takes no " +
                              "values, and the message sends one.",
            });

            return;
        }

        if (!accepted.Contains(subType, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new TemplateIssue
            {
                Component = TemplateComponentType.Button,
                Problem = TemplateProblem.WrongKind,
                Description = $"Button {index} is a {Name(declared.Kind)} in the template, and " +
                              $"the message declares it as '{subType}'. Every message is " +
                              "rejected with a bare 100.",
            });

            return;
        }

        var expected = ValuesFor(declared);

        if (component.Parameters.Count == expected)
        {
            return;
        }

        issues.Add(new TemplateIssue
        {
            Component = TemplateComponentType.Button,
            Problem = component.Parameters.Count < expected
                ? TemplateProblem.Missing
                : TemplateProblem.Unexpected,
            Description = $"Button {index} takes {expected} value(s), and the message sends " +
                          $"{component.Parameters.Count}.",
        });
    }

    /// <summary>How many values the button takes when the message does fill it.</summary>
    private static int ValuesFor(TemplateButton button) => button.Kind switch
    {
        // The payload a quick reply sends back is the sender's, not the template's: the
        // template carries only the label.
        TemplateButtonKind.QuickReply or TemplateButtonKind.CopyCode
            or TemplateButtonKind.OneTimePassword => 1,

        // Only a URL with a placeholder in it takes a suffix. Sending one for a fixed link is
        // rejected just as firmly as leaving one out.
        TemplateButtonKind.Url => HasPlaceholder(button) ? 1 : 0,
        _ => 0,
    };

    /// <summary>Whether the message has to fill the button at all.</summary>
    /// <remarks>
    /// A quick reply is not on this list. Sending no component for one is allowed — the reply
    /// then comes back carrying the button's own label — so demanding a payload would flag
    /// perfectly good broadcasts.
    /// </remarks>
    private static bool IsRequired(TemplateButton button) => button.Kind switch
    {
        TemplateButtonKind.CopyCode or TemplateButtonKind.OneTimePassword => true,
        TemplateButtonKind.Url => HasPlaceholder(button),
        _ => false,
    };

    private static bool HasPlaceholder(TemplateButton button) =>
        button.Url?.Contains("{{", StringComparison.Ordinal) == true;

    private static string Name(TemplateComponentType component) => component switch
    {
        TemplateComponentType.Header => "header",
        TemplateComponentType.Body => "body",
        _ => "button",
    };

    private static string Name(TemplateButtonKind kind) => kind switch
    {
        TemplateButtonKind.QuickReply => "quick reply",
        TemplateButtonKind.Url => "URL button",
        TemplateButtonKind.PhoneNumber => "phone-number button",
        TemplateButtonKind.CopyCode => "copy-code button",
        TemplateButtonKind.VoiceCall => "voice-call button",
        TemplateButtonKind.OneTimePassword => "passcode button",
        _ => "button of a kind this library does not know",
    };

    private static string Ordinal(int index) => (index % 100 / 10 == 1 ? 0 : index % 10) switch
    {
        1 => "st",
        2 => "nd",
        3 => "rd",
        _ => "th",
    };

    /// <remarks>
    /// Whitespace inside the braces is allowed because Meta allows it, and a template written
    /// as <c>{{ 1 }}</c> means the same thing as <c>{{1}}</c>.
    /// </remarks>
    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
