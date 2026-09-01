using System.Globalization;
using System.Text.RegularExpressions;

namespace Wapper.Templates;

/// <summary>
/// Reading what a template asks for, without sending anything.
/// </summary>
/// <remarks>
/// Reading <c>{{…}}</c> is the library's job, not its caller's: the rules for counting
/// placeholders are Meta's and they are not the obvious ones.
/// </remarks>
public static partial class TemplateInspection
{
    /// <summary>
    /// The substitutions the template's body expects, in a fixed order.
    /// </summary>
    /// <returns>
    /// For a template with named placeholders, the names — <c>first_name</c> — each once, in
    /// the order they first appear in the text. For a numbered one, <c>{{1}}</c> through
    /// <c>{{n}}</c>, where <c>n</c> is the highest index the body mentions.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The list is what to show a person: the count is the number of values a send has to
    /// carry, and the entries name them. It is also stable — the same template gives the same
    /// list every time, so it can be stored or printed beside a template name.
    /// </para>
    /// <para>
    /// Numbered placeholders are counted by the <i>highest index</i>, not by how many appear.
    /// A body reading <c>only {{2}}</c> expects two values, because Meta fills them by
    /// position and rejects a message that brings one. A name repeated in a named template is
    /// one substitution, filled from a single value.
    /// </para>
    /// <para>
    /// The body, and only the body. A template's other placeholders are numbered separately
    /// from it and from each other — a text header carries at most one, and so does each URL
    /// button — so putting them in one list would say that <c>{{1}}</c> of the header and
    /// <c>{{1}}</c> of the body were the same value, which they are not. Read those from
    /// <see cref="Template.Header"/> and <see cref="Template.Buttons"/>;
    /// <see cref="TemplateValidation.Validate"/> checks all of them together.
    /// </para>
    /// <para>
    /// An authentication template's body has no placeholders of its own — Meta writes that
    /// text itself, in every language — and still takes exactly one value, the passcode. This
    /// returns an empty list for one.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Placeholders(this Template template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return Placeholders(template.Body.Text, template.ParameterFormat);
    }

    /// <summary>
    /// Where each quick-reply button sits among the template's buttons, in order.
    /// </summary>
    /// <returns>
    /// The indexes to pass to <c>TemplateComponent.QuickReplyButton</c>, or an empty list for
    /// a template with no quick replies.
    /// </returns>
    /// <remarks>
    /// Counted across every button the template has, which is the whole reason this is here.
    /// Meta routes a payload by position, whatever the kinds of the buttons around it — so a
    /// template of nothing but quick replies makes the position and the payload's own ordinal
    /// agree, and code counting only quick replies keeps working right up to the day a URL
    /// button is added between them. Then the positions shift by one in silence: either every
    /// message of the wave is refused with a bare <c>100</c>, or, worse, the payload lands on
    /// the neighbouring button and a customer who tapped one thing is recorded as having
    /// tapped another.
    /// </remarks>
    public static IReadOnlyList<int> QuickReplyIndexes(this Template template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var indexes = new List<int>(template.Buttons.Count);

        for (var index = 0; index < template.Buttons.Count; index++)
        {
            if (template.Buttons[index].Kind == TemplateButtonKind.QuickReply)
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    /// <summary>
    /// The substitutions one piece of template text expects, the way Meta counts them.
    /// </summary>
    /// <remarks>
    /// The one place <c>{{…}}</c> is read. Both the public list and the check against a
    /// message run on it, so what a caller is shown and what is enforced cannot disagree.
    /// </remarks>
    internal static List<string> Placeholders(string text, TemplateParameterFormat format)
    {
        var tokens = Placeholder().Matches(text).Select(match => match.Groups[1].Value);

        if (format == TemplateParameterFormat.Named)
        {
            // A name repeated in the text is one substitution: Meta fills every occurrence
            // from the single value. First appearance wins the position, so the list reads in
            // the order someone filling the template in would meet them.
            var seen = new HashSet<string>(StringComparer.Ordinal);

            return [.. tokens.Where(seen.Add)];
        }

        // Counted by the highest index, not by how many placeholders appear. A body reading
        // "only {{2}}" expects two values, and Meta rejects one, because it fills by position.
        var highest = 0;

        foreach (var token in tokens)
        {
            if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                highest = Math.Max(highest, index);
            }
        }

        return [.. Enumerable.Range(1, highest).Select(index => $"{{{{{index}}}}}")];
    }

    /// <remarks>
    /// Whitespace inside the braces is allowed because Meta allows it, and a template written
    /// as <c>{{ 1 }}</c> means the same thing as <c>{{1}}</c>.
    /// </remarks>
    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
