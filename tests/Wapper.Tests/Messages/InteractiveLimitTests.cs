using Wapper.Messages;

namespace Wapper.Tests.Messages;

/// <summary>
/// The structural limits Meta puts on an interactive message. All of them are answered with a
/// bare <c>100</c> that names no field, so the value of checking here is entirely in the
/// message: which field, and how long it actually was.
/// </summary>
public class InteractiveLimitTests
{
    private static ButtonMessage Buttons(
        string body = "Pick one",
        string id = "yes",
        string title = "Yes",
        string? header = null,
        string? footer = null) =>
        new()
        {
            Body = body,
            Header = header is null ? null : InteractiveHeader.FromText(header),
            Footer = footer,
            Buttons = [new ReplyButton { Id = id, Title = title }],
        };

    private static ListMessage List(
        string body = "Pick a slot",
        string buttonText = "See slots",
        string sectionTitle = "Morning",
        string rowId = "9",
        string rowTitle = "09:00",
        string? rowDescription = null,
        int sections = 1) =>
        new()
        {
            Body = body,
            ButtonText = buttonText,
            Sections = [.. Enumerable.Range(0, sections).Select(_ => new ListSection
            {
                Title = sectionTitle,
                Rows = [new ListRow { Id = rowId, Title = rowTitle, Description = rowDescription }],
            })],
        };

    public static TheoryData<string, int, ButtonMessage> OversizedButtonFields() => new()
    {
        { "body", 1024, Buttons(body: new string('b', 1025)) },
        { "id of button 1", 256, Buttons(id: new string('i', 257)) },
        { "title of button 1", 20, Buttons(title: new string('t', 21)) },
        { "text header", 60, Buttons(header: new string('h', 61)) },
        { "footer", 60, Buttons(footer: new string('f', 61)) },
    };

    [Theory]
    [MemberData(nameof(OversizedButtonFields))]
    public void An_oversized_reply_button_field_is_named_and_measured(
        string field,
        int max,
        ButtonMessage message)
    {
        var thrown = Assert.Throws<ArgumentException>(() => message.ToPayload());

        // The field, so the caller knows which of a dozen strings to shorten.
        Assert.Contains(field, thrown.Message, StringComparison.Ordinal);
        // The limit and the actual length, so they know by how much.
        Assert.Contains($"at most {max} characters", thrown.Message, StringComparison.Ordinal);
        Assert.Contains($"is {max + 1}.", thrown.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, int, ListMessage> OversizedListFields() => new()
    {
        // A list message takes four times the body a reply-button message does. Meta's own
        // numbers, and they really do differ.
        { "body", 4096, List(body: new string('b', 4097)) },
        { "button that opens a list", 20, List(buttonText: new string('o', 21)) },
        { "title of section 1", 24, List(sectionTitle: new string('s', 25)) },
        { "id of row 1 of section 1", 200, List(rowId: new string('i', 201)) },
        { "title of row 1 of section 1", 24, List(rowTitle: new string('t', 25)) },
        { "description of row 1 of section 1", 72, List(rowDescription: new string('d', 73)) },
    };

    [Theory]
    [MemberData(nameof(OversizedListFields))]
    public void An_oversized_list_field_is_named_and_measured(string field, int max, ListMessage message)
    {
        var thrown = Assert.Throws<ArgumentException>(() => message.ToPayload());

        Assert.Contains(field, thrown.Message, StringComparison.Ordinal);
        Assert.Contains($"at most {max} characters", thrown.Message, StringComparison.Ordinal);
        Assert.Contains($"is {max + 1}.", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_eleventh_section_is_refused()
    {
        // Eleven sections holding ten rows between them: the row count is legal, so this is
        // the one shape the row check does not already cover. A section built from an empty
        // collection is how it happens.
        var message = new ListMessage
        {
            Body = "Pick a slot",
            ButtonText = "See slots",
            Sections =
            [
                .. Enumerable.Range(0, 10).Select(i => new ListSection
                {
                    Title = $"Section {i}",
                    Rows = [new ListRow { Id = i.ToString(), Title = $"Row {i}" }],
                }),
                new ListSection { Title = "Nothing today", Rows = [] },
            ],
        };

        var thrown = Assert.Throws<ArgumentException>(message.ToPayload);

        Assert.Contains("at most 10 sections", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("has 11", thrown.Message, StringComparison.Ordinal);
    }

    private static CallToActionMessage CallToAction(
        string body = "Tap below",
        string buttonText = "See dates",
        string? header = null,
        string? footer = null) =>
        new()
        {
            Body = body,
            ButtonText = buttonText,
            Url = new Uri("https://example.com/dates"),
            Header = header is null ? null : InteractiveHeader.FromText(header),
            Footer = footer,
        };

    public static TheoryData<string, int, CallToActionMessage> OversizedCallToActionFields() => new()
    {
        { "body", 1024, CallToAction(body: new string('b', 1025)) },
        { "label", 20, CallToAction(buttonText: new string('l', 21)) },
        { "text header", 60, CallToAction(header: new string('h', 61)) },
        { "footer", 60, CallToAction(footer: new string('f', 61)) },
    };

    [Theory]
    [MemberData(nameof(OversizedCallToActionFields))]
    public void An_oversized_call_to_action_field_is_named_and_measured(
        string field,
        int max,
        CallToActionMessage message)
    {
        // The same documented limits as the other interactive types, previously checked for
        // buttons and lists alone — the same bare 100 from Meta either way.
        var thrown = Assert.Throws<ArgumentException>(() => message.ToPayload());

        Assert.Contains(field, thrown.Message, StringComparison.Ordinal);
        Assert.Contains($"at most {max} characters", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_oversized_flow_body_is_named_and_measured()
    {
        var message = new FlowMessage
        {
            FlowId = "1",
            FlowToken = "token",
            ButtonText = "Book",
            Body = new string('b', 1025),
            Screen = "BOOK",
        };

        var thrown = Assert.Throws<ArgumentException>(() => message.ToPayload());

        Assert.Contains("Flow message", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("at most 1024 characters", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_message_at_every_limit_is_accepted()
    {
        // The other half of a limit: one character short of it has to go through, or the
        // check is worse than the bare 100 it replaced.
        var buttons = Buttons(
            body: new string('b', 1024),
            id: new string('i', 256),
            title: new string('t', 20),
            header: new string('h', 60),
            footer: new string('f', 60));

        var list = List(
            body: new string('b', 4096),
            buttonText: new string('o', 20),
            sectionTitle: new string('s', 24),
            rowId: new string('i', 200),
            rowTitle: new string('t', 24),
            rowDescription: new string('d', 72),
            sections: 10);

        Assert.Equal("button", buttons.ToPayload().Type);
        Assert.Equal("list", list.ToPayload().Type);
    }
}
