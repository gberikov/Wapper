using Wapper.Messages;
using Wapper.Templates;

namespace Wapper.Tests.Templates;

/// <summary>
/// Meta rejects a template whose values do not fit it on every single message, so the first
/// wave of a broadcast burns whole — two thousand refusals, a quality rating spent and a day
/// lost. Everything needed to catch it is in the template itself.
/// </summary>
public class TemplateValidationTests
{
    [Fact]
    public void Values_that_fit_the_template_raise_nothing()
    {
        var template = Positional("Thank you, {{1}}! Your order is {{2}}.");

        var issues = template.Validate(Message(TemplateComponent.Body(
            TemplateParameter.FromText("Pablo"),
            TemplateParameter.FromText("860198-230332"))));

        Assert.Empty(issues);
    }

    [Fact]
    public void A_numbered_placeholder_counts_by_its_index_and_not_by_how_often_it_appears()
    {
        // "only {{2}}" expects two values: Meta fills them by position, so a template that
        // skips {{1}} still leaves a hole where {{1}} would have gone.
        var template = Positional("only {{2}}");

        var issues = template.Validate(Message(
            TemplateComponent.Body(TemplateParameter.FromText("one"))));

        var issue = Assert.Single(issues);
        Assert.Equal(TemplateProblem.Missing, issue.Problem);
        Assert.Equal("{{2}}", issue.Parameter);
    }

    [Fact]
    public void A_value_the_template_has_no_placeholder_for_is_named()
    {
        var template = Positional("Thank you, {{1}}!");

        var issues = template.Validate(Message(TemplateComponent.Body(
            TemplateParameter.FromText("Pablo"),
            TemplateParameter.FromText("and one too many"))));

        var issue = Assert.Single(issues);
        Assert.Equal(TemplateProblem.Unexpected, issue.Problem);
        Assert.Equal("{{2}}", issue.Parameter);
    }

    [Fact]
    public void A_name_used_twice_in_the_template_is_one_substitution()
    {
        var template = Named("Hello {{first_name}}. See you soon, {{first_name}}!");

        var issues = template.Validate(Message(TemplateComponent.Body(
            TemplateParameter.FromText("Pablo", name: "first_name"))));

        Assert.Empty(issues);
    }

    [Fact]
    public void A_named_template_does_not_take_values_matched_by_order()
    {
        var template = Named("Hello {{first_name}}.");

        var issues = template.Validate(Message(
            TemplateComponent.Body(TemplateParameter.FromText("Pablo"))));

        // Two things are wrong, and both are worth saying: the value has no name, and the
        // placeholder has nothing filling it.
        Assert.Contains(issues, issue => issue.Problem == TemplateProblem.WrongFormat);
        Assert.Contains(
            issues,
            issue => issue.Problem == TemplateProblem.Missing && issue.Parameter == "first_name");
    }

    [Fact]
    public void A_numbered_template_does_not_take_values_matched_by_name()
    {
        var template = Positional("Hello {{1}}.");

        var issues = template.Validate(Message(TemplateComponent.Body(
            TemplateParameter.FromText("Pablo", name: "first_name"))));

        Assert.Equal(TemplateProblem.WrongFormat, Assert.Single(issues).Problem);
    }

    [Fact]
    public void A_name_the_template_never_declared_is_named()
    {
        var template = Named("Hello {{first_name}}.");

        var issues = template.Validate(Message(TemplateComponent.Body(
            TemplateParameter.FromText("Pablo", name: "first_name"),
            TemplateParameter.FromText("860198", name: "order_number"))));

        var issue = Assert.Single(issues);
        Assert.Equal(TemplateProblem.Unexpected, issue.Problem);
        Assert.Equal("order_number", issue.Parameter);
    }

    [Fact]
    public void A_button_index_is_a_position_among_all_the_buttons()
    {
        // The template's second button is a link, not a quick reply. Declaring it as one is
        // a bare 100 on every message, and the index is what Meta matches on.
        var template = WithButtons(
            TemplateButton.QuickReply("Yes"),
            TemplateButton.Link("Track order", "https://example.com/orders/{{1}}", "1234"),
            TemplateButton.QuickReply("No"));

        var issues = template.Validate(Message(
            TemplateComponent.Body(TemplateParameter.FromText("Pablo")),
            TemplateComponent.QuickReplyButton(1, "yes")));

        var issue = Assert.Single(issues);
        Assert.Equal(TemplateProblem.WrongKind, issue.Problem);
        Assert.Contains("URL button", issue.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void The_button_that_really_is_a_quick_reply_passes()
    {
        var template = WithButtons(
            TemplateButton.QuickReply("Yes"),
            TemplateButton.Link("Track order", "https://example.com/orders/{{1}}", "1234"));

        var issues = template.Validate(Message(
            TemplateComponent.Body(TemplateParameter.FromText("Pablo")),
            TemplateComponent.QuickReplyButton(0, "yes"),
            TemplateComponent.UrlButton(1, "860198")));

        Assert.Empty(issues);
    }

    [Fact]
    public void A_link_with_a_placeholder_has_to_be_filled_in()
    {
        var template = WithButtons(
            TemplateButton.Link("Track order", "https://example.com/orders/{{1}}", "1234"));

        var issues = template.Validate(Message(
            TemplateComponent.Body(TemplateParameter.FromText("Pablo"))));

        Assert.Equal(TemplateProblem.Missing, Assert.Single(issues).Problem);
    }

    [Fact]
    public void A_quick_reply_left_unfilled_is_not_an_error()
    {
        // Sending no component for a quick reply is allowed: the tap comes back carrying the
        // button's own label. Flagging it would fail perfectly good broadcasts.
        var template = WithButtons(TemplateButton.QuickReply("Yes"));

        var issues = template.Validate(Message(
            TemplateComponent.Body(TemplateParameter.FromText("Pablo"))));

        Assert.Empty(issues);
    }

    [Fact]
    public void A_button_the_template_does_not_have_is_named()
    {
        var template = WithButtons(TemplateButton.QuickReply("Yes"));

        var issues = template.Validate(Message(
            TemplateComponent.Body(TemplateParameter.FromText("Pablo")),
            TemplateComponent.QuickReplyButton(3, "yes")));

        Assert.Equal(TemplateProblem.Unexpected, Assert.Single(issues).Problem);
    }

    [Fact]
    public void A_media_header_takes_the_media_and_not_a_line_of_text()
    {
        var template = new Template
        {
            Name = "seasonal_offer",
            Language = "en_US",
            Category = TemplateCategory.Marketing,
            Header = TemplateHeader.FromImage("4::aW1hZ2UvcG5n"),
            Body = new TemplateBody { Text = "Our summer range is in." },
        };

        var issues = template.Validate(Message(
            TemplateComponent.Header(TemplateParameter.FromText("hero.png"))));

        Assert.Equal(TemplateProblem.WrongKind, Assert.Single(issues).Problem);
    }

    [Fact]
    public void An_authentication_template_takes_the_passcode_though_it_has_no_body_text()
    {
        // Meta writes the body itself, in every language, so there is no text to count
        // placeholders in — and the one value it takes is still required.
        var template = Template.Authentication(
            "verification_code",
            "en_US",
            TemplateButton.CopyOneTimePassword());

        Assert.Empty(template.Validate(Message(
            TemplateComponent.Body(TemplateParameter.FromText("J$FpnYnP")),
            TemplateComponent.CopyCodeButton(0, "J$FpnYnP"))));

        Assert.NotEmpty(template.Validate(Message(
            TemplateComponent.CopyCodeButton(0, "J$FpnYnP"))));
    }

    private static TemplateMessage Message(params TemplateComponent[] components) => new()
    {
        Name = "order_confirmation",
        Language = "en_US",
        Components = components,
    };

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
