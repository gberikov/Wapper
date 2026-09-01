using Wapper.Templates;

namespace Wapper.Tests.Templates;

/// <summary>
/// What a template asks for, answered by the one thing that reads <c>{{…}}</c>. A caller
/// needs it to tell an operator what to put in a broadcast file, and writing that parser a
/// second time is how the counting rules get lost.
/// </summary>
public class TemplateInspectionTests
{
    [Fact]
    public void Numbered_placeholders_are_listed_up_to_the_highest_index()
    {
        // "only {{2}}" is two substitutions, not one: Meta fills them by position, so the
        // hole where {{1}} would go still has to be filled.
        Assert.Equal(["{{1}}", "{{2}}"], Positional("only {{2}}").Placeholders());
    }

    [Fact]
    public void Numbered_placeholders_come_back_in_order_however_the_text_orders_them()
    {
        Assert.Equal(
            ["{{1}}", "{{2}}", "{{3}}"],
            Positional("{{3}} then {{1}}, and {{2}} again — {{1}}").Placeholders());
    }

    [Fact]
    public void A_name_used_twice_is_one_substitution()
    {
        Assert.Equal(
            ["first_name", "conference_name"],
            Named("Hello {{first_name}}! {{conference_name}} starts soon, {{first_name}}.")
                .Placeholders());
    }

    [Fact]
    public void A_body_with_nothing_to_fill_in_asks_for_nothing()
    {
        Assert.Empty(Positional("We are closed on Monday.").Placeholders());
        Assert.Empty(Named("We are closed on Monday.").Placeholders());
    }

    [Fact]
    public void The_list_is_the_same_every_time_it_is_asked_for()
    {
        // It goes in front of a person and into somebody's table, so it cannot depend on set
        // ordering.
        var template = Named("{{b}} {{a}} {{b}} {{c}}");

        Assert.Equal(["b", "a", "c"], template.Placeholders());
        Assert.Equal(template.Placeholders(), template.Placeholders());
    }

    [Fact]
    public void An_authentication_template_asks_for_nothing_in_its_body()
    {
        // Meta writes that body itself, in every language. The passcode is still required —
        // Validate says so — but there is no placeholder in the text to name.
        var template = Template.Authentication(
            "verification_code",
            "en_US",
            TemplateButton.CopyOneTimePassword());

        Assert.Empty(template.Placeholders());
    }

    [Fact]
    public void A_quick_reply_is_found_at_its_position_among_all_the_buttons()
    {
        // The URL button occupies index 1. Counting only quick replies would put the second
        // payload on index 1 — the link — and Meta either refuses the message or, worse,
        // records the tap against the wrong button.
        var template = WithButtons(
            TemplateButton.QuickReply("Yes"),
            TemplateButton.Link("Track order", "https://example.com/orders/{{1}}", "1234"),
            TemplateButton.QuickReply("No"));

        Assert.Equal([0, 2], template.QuickReplyIndexes());
    }

    [Fact]
    public void A_template_of_nothing_but_quick_replies_reads_as_it_looks()
    {
        var template = WithButtons(
            TemplateButton.QuickReply("Yes"),
            TemplateButton.QuickReply("No"));

        Assert.Equal([0, 1], template.QuickReplyIndexes());
    }

    [Fact]
    public void A_template_with_no_buttons_has_no_positions_to_give()
    {
        Assert.Empty(Positional("Thank you, {{1}}!").QuickReplyIndexes());
    }

    [Fact]
    public void A_template_whose_buttons_are_all_something_else_has_none_either()
    {
        var template = WithButtons(
            TemplateButton.Link("Track order", "https://example.com/orders/{{1}}", "1234"),
            TemplateButton.Call("Call us", "+15550100"));

        Assert.Empty(template.QuickReplyIndexes());
    }

    private static Template Positional(string body) => new()
    {
        Name = "order_confirmation",
        Language = "en_US",
        Category = TemplateCategory.Utility,
        Body = new TemplateBody { Text = body },
    };

    private static Template Named(string body) => Positional(body) with
    {
        ParameterFormat = TemplateParameterFormat.Named,
    };

    private static Template WithButtons(params TemplateButton[] buttons) =>
        Positional("Thank you, {{1}}!") with { Buttons = buttons };
}
