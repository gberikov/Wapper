using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Wapper.Internal;
using Wapper.Media;
using Wapper.Messages;
using Wapper.RateLimiting;
using Wapper.Tests.Fakes;

namespace Wapper.Tests.Messages;

public class MessagesApiTests
{
    private const string Accepted = """
        {
          "messaging_product": "whatsapp",
          "contacts": [{"input": "79000000001", "wa_id": "79000000001"}],
          "messages": [{"id": "wamid.HBgL", "message_status": "accepted"}]
        }
        """;

    private static readonly WhatsAppCredentials Credentials = new()
    {
        AccessToken = "token-abc",
        PhoneNumberId = "106540352242922",
    };

    [Fact]
    public async Task A_send_returns_what_the_api_said_about_it()
    {
        var (messages, handler) = Create();

        var sent = await messages.SendTextAsync(
            "79000000001",
            "hello",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("wamid.HBgL", sent.Id);
        // Not always the number that was dialled: some countries normalise it, and this is
        // the value worth storing.
        Assert.Equal("79000000001", sent.RecipientId);
        Assert.Equal("accepted", sent.Status);

        Assert.Equal(
            "https://graph.facebook.com/v26.0/106540352242922/messages",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task A_text_message_carries_the_expected_envelope()
    {
        var (messages, handler) = Create();

        await messages.SendTextAsync("79000000001", "hello", cancellationToken: TestContext.Current.CancellationToken);

        var body = Body(handler);
        Assert.Equal("whatsapp", body.GetProperty("messaging_product").GetString());
        Assert.Equal("individual", body.GetProperty("recipient_type").GetString());
        Assert.Equal("79000000001", body.GetProperty("to").GetString());
        Assert.Equal("text", body.GetProperty("type").GetString());
        Assert.Equal("hello", body.GetProperty("text").GetProperty("body").GetString());
    }

    [Fact]
    public async Task Link_previews_are_off_unless_asked_for()
    {
        var (messages, handler) = Create();

        await messages.SendTextAsync("79000000001", "see https://example.com", cancellationToken: TestContext.Current.CancellationToken);

        // A preview is fetched from the link while the message is being sent, so it is not
        // something to turn on by accident.
        Assert.False(Body(handler).GetProperty("text").TryGetProperty("preview_url", out _));
    }

    [Fact]
    public async Task Link_previews_are_requested_when_asked_for()
    {
        var (messages, handler) = Create();

        await messages.SendTextAsync(
            "79000000001",
            "see https://example.com",
            previewUrl: true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(Body(handler).GetProperty("text").GetProperty("preview_url").GetBoolean());
    }

    [Fact]
    public async Task A_reply_quotes_the_message_it_answers()
    {
        var (messages, handler) = Create();

        await messages.SendTextAsync(
            "79000000001",
            "hello",
            replyToMessageId: "wamid.INCOMING",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            "wamid.INCOMING",
            Body(handler).GetProperty("context").GetProperty("message_id").GetString());
    }

    [Fact]
    public async Task Media_can_be_sent_by_id_or_by_link()
    {
        var (byId, idHandler) = Create();
        await byId.SendImageAsync(
            "79000000001",
            MediaSource.FromId("media-1"),
            "a caption",
            cancellationToken: TestContext.Current.CancellationToken);

        var image = Body(idHandler).GetProperty("image");
        Assert.Equal("media-1", image.GetProperty("id").GetString());
        Assert.Equal("a caption", image.GetProperty("caption").GetString());
        Assert.False(image.TryGetProperty("link", out _));

        var (byLink, linkHandler) = Create();
        await byLink.SendImageAsync(
            "79000000001",
            MediaSource.FromLink("https://example.com/cat.jpg"),
            cancellationToken: TestContext.Current.CancellationToken);

        var linked = Body(linkHandler).GetProperty("image");
        Assert.Equal("https://example.com/cat.jpg", linked.GetProperty("link").GetString());
        Assert.False(linked.TryGetProperty("id", out _));
    }

    [Fact]
    public async Task A_document_carries_the_name_the_recipient_will_save_it_under()
    {
        var (messages, handler) = Create();

        await messages.SendDocumentAsync(
            "79000000001",
            MediaSource.FromId("media-1"),
            "the invoice",
            "invoice-2026-08.pdf",
            cancellationToken: TestContext.Current.CancellationToken);

        var document = Body(handler).GetProperty("document");
        Assert.Equal("invoice-2026-08.pdf", document.GetProperty("filename").GetString());
        Assert.Equal("the invoice", document.GetProperty("caption").GetString());
    }

    [Fact]
    public async Task A_location_without_a_name_still_sends_its_address()
    {
        var (messages, handler) = Create();

        // WhatsApp shows the address only underneath a name, but that is its display rule to
        // apply. Dropping the field here would lose it from a location merely forwarded on —
        // the template-parameter location two methods over never dropped it.
        await messages.SendLocationAsync(
            "79000000001",
            new Location { Latitude = 51.5, Longitude = -0.12, Address = "nowhere" },
            cancellationToken: TestContext.Current.CancellationToken);

        var location = Body(handler).GetProperty("location");
        Assert.Equal(51.5, location.GetProperty("latitude").GetDouble());
        Assert.Equal("nowhere", location.GetProperty("address").GetString());
    }

    [Fact]
    public async Task An_oversized_location_request_body_is_caught_before_sending()
    {
        var (messages, handler) = Create();

        // The same interactive body limit as the button and list messages, and Meta's same
        // bare 100 when it is passed.
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            messages.SendLocationRequestAsync(
                "79000000001",
                new string('b', 1025),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("location request", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_reaction_is_taken_back_with_an_empty_emoji()
    {
        var (messages, handler) = Create();

        await messages.RemoveReactionAsync(
            "79000000001",
            "wamid.HBgL",
            cancellationToken: TestContext.Current.CancellationToken);

        var reaction = Body(handler).GetProperty("reaction");
        Assert.Equal("wamid.HBgL", reaction.GetProperty("message_id").GetString());
        // The empty string is the whole mechanism; there is no separate endpoint, and
        // omitting the field would not remove anything.
        Assert.Equal(string.Empty, reaction.GetProperty("emoji").GetString());
    }

    [Fact]
    public async Task A_contact_card_is_sent_in_the_shape_the_api_expects()
    {
        var (messages, handler) = Create();

        await messages.SendContactsAsync(
            "79000000001",
            [
                new Contact
                {
                    Name = new ContactName { FormattedName = "Ada Lovelace", FirstName = "Ada" },
                    Phones = [new ContactPhone { Phone = "+44 20 7946 0000", Type = "WORK" }],
                    Organisation = new ContactOrganisation { Company = "Analytical Engines" },
                    Birthday = new DateOnly(1815, 12, 10),
                },
            ],
            cancellationToken: TestContext.Current.CancellationToken);

        var contact = Body(handler).GetProperty("contacts")[0];
        Assert.Equal("Ada Lovelace", contact.GetProperty("name").GetProperty("formatted_name").GetString());
        Assert.Equal("Analytical Engines", contact.GetProperty("org").GetProperty("company").GetString());
        Assert.Equal("1815-12-10", contact.GetProperty("birthday").GetString());
        Assert.Equal("WORK", contact.GetProperty("phones")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Reply_buttons_are_sent_as_an_interactive_message()
    {
        var (messages, handler) = Create();

        await messages.SendButtonsAsync(
            "79000000001",
            new ButtonMessage
            {
                Body = "Pick one",
                Header = InteractiveHeader.FromText("Choices"),
                Footer = "or ignore this",
                Buttons =
                [
                    new ReplyButton { Id = "yes", Title = "Yes" },
                    new ReplyButton { Id = "no", Title = "No" },
                ],
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var interactive = Body(handler).GetProperty("interactive");
        Assert.Equal("button", interactive.GetProperty("type").GetString());
        Assert.Equal("Choices", interactive.GetProperty("header").GetProperty("text").GetString());
        Assert.Equal("or ignore this", interactive.GetProperty("footer").GetProperty("text").GetString());

        var buttons = interactive.GetProperty("action").GetProperty("buttons");
        Assert.Equal(2, buttons.GetArrayLength());
        Assert.Equal("reply", buttons[0].GetProperty("type").GetString());
        Assert.Equal("yes", buttons[0].GetProperty("reply").GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_fourth_reply_button_is_refused_before_it_is_sent()
    {
        var (messages, handler) = Create();

        // WhatsApp rejects the message rather than dropping the extra button, and the error
        // it returns does not say which limit was passed.
        await Assert.ThrowsAsync<ArgumentException>(async () => await messages.SendButtonsAsync(
            "79000000001",
            new ButtonMessage
            {
                Body = "Pick one",
                Buttons =
                [
                    new ReplyButton { Id = "1", Title = "One" },
                    new ReplyButton { Id = "2", Title = "Two" },
                    new ReplyButton { Id = "3", Title = "Three" },
                    new ReplyButton { Id = "4", Title = "Four" },
                ],
            },
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_list_message_carries_its_sections()
    {
        var (messages, handler) = Create();

        await messages.SendListAsync(
            "79000000001",
            new ListMessage
            {
                Body = "Pick a slot",
                ButtonText = "See slots",
                Header = "Tomorrow",
                Sections =
                [
                    new ListSection
                    {
                        Title = "Morning",
                        Rows = [new ListRow { Id = "9", Title = "09:00", Description = "One hour" }],
                    },
                ],
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var interactive = Body(handler).GetProperty("interactive");
        Assert.Equal("list", interactive.GetProperty("type").GetString());
        // A list message only accepts a text header, whatever the other interactive types
        // allow.
        Assert.Equal("text", interactive.GetProperty("header").GetProperty("type").GetString());
        Assert.Equal("See slots", interactive.GetProperty("action").GetProperty("button").GetString());

        var row = interactive.GetProperty("action").GetProperty("sections")[0].GetProperty("rows")[0];
        Assert.Equal("09:00", row.GetProperty("title").GetString());
    }

    [Fact]
    public async Task An_eleventh_list_row_is_refused_before_it_is_sent()
    {
        var (messages, handler) = Create();

        await Assert.ThrowsAsync<ArgumentException>(async () => await messages.SendListAsync(
            "79000000001",
            new ListMessage
            {
                Body = "Pick a slot",
                ButtonText = "See slots",
                Sections =
                [
                    new ListSection
                    {
                        Rows = [.. Enumerable.Range(0, 11).Select(i =>
                            new ListRow { Id = i.ToString(), Title = $"Row {i}" })],
                    },
                ],
            },
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_call_to_action_carries_its_link()
    {
        var (messages, handler) = Create();

        await messages.SendCallToActionAsync(
            "79000000001",
            new CallToActionMessage
            {
                Body = "Your order is ready",
                ButtonText = "Track it",
                Url = new Uri("https://example.com/orders/1"),
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var action = Body(handler).GetProperty("interactive").GetProperty("action");
        Assert.Equal("cta_url", action.GetProperty("name").GetString());
        Assert.Equal("Track it", action.GetProperty("parameters").GetProperty("display_text").GetString());
        Assert.Equal("https://example.com/orders/1", action.GetProperty("parameters").GetProperty("url").GetString());
    }

    [Fact]
    public async Task A_location_request_is_an_interactive_message_with_the_one_action_it_takes()
    {
        var (messages, handler) = Create();

        await messages.SendLocationRequestAsync(
            "79000000001",
            "Where should we deliver to?",
            cancellationToken: TestContext.Current.CancellationToken);

        var interactive = Body(handler).GetProperty("interactive");
        Assert.Equal("location_request_message", interactive.GetProperty("type").GetString());
        Assert.Equal("Where should we deliver to?", interactive.GetProperty("body").GetProperty("text").GetString());
        Assert.Equal("send_location", interactive.GetProperty("action").GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_template_carries_its_language_and_values()
    {
        var (messages, handler) = Create();

        await messages.SendTemplateAsync(
            "79000000001",
            new TemplateMessage
            {
                Name = "order_update",
                Language = "en_US",
                Components =
                [
                    TemplateComponent.Header(TemplateParameter.FromImage(MediaSource.FromId("media-1"))),
                    TemplateComponent.Body(
                        TemplateParameter.FromText("Ada"),
                        TemplateParameter.FromMoney(TemplateCurrency.FromDecimal(12.34m, "USD", "$12.34"))),
                    TemplateComponent.UrlButton(0, "orders/1"),
                ],
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var template = Body(handler).GetProperty("template");
        Assert.Equal("order_update", template.GetProperty("name").GetString());
        Assert.Equal("en_US", template.GetProperty("language").GetProperty("code").GetString());

        var components = template.GetProperty("components");
        Assert.Equal("header", components[0].GetProperty("type").GetString());
        Assert.Equal("media-1", components[0].GetProperty("parameters")[0].GetProperty("image").GetProperty("id").GetString());

        var money = components[1].GetProperty("parameters")[1].GetProperty("currency");
        // Meta takes the amount as an integer number of thousandths, to keep rounding out of it.
        Assert.Equal(12340, money.GetProperty("amount_1000").GetInt64());
        Assert.Equal("USD", money.GetProperty("code").GetString());

        // The button index goes on the wire as a string, not a number.
        Assert.Equal("0", components[2].GetProperty("index").GetString());
        Assert.Equal("url", components[2].GetProperty("sub_type").GetString());
    }

    [Fact]
    public async Task Marking_a_message_read_sends_a_status_rather_than_a_message()
    {
        var (messages, handler) = Create();

        await messages.MarkAsReadAsync("wamid.INCOMING", cancellationToken: TestContext.Current.CancellationToken);

        var body = Body(handler);
        Assert.Equal("read", body.GetProperty("status").GetString());
        Assert.Equal("wamid.INCOMING", body.GetProperty("message_id").GetString());
        // The same endpoint as a send, but the recipient fields have to be absent or the
        // Cloud API rejects it.
        Assert.False(body.TryGetProperty("to", out _));
        Assert.False(body.TryGetProperty("recipient_type", out _));
        Assert.False(body.TryGetProperty("typing_indicator", out _));
    }

    [Fact]
    public async Task A_typing_indicator_rides_along_with_the_read_receipt()
    {
        var (messages, handler) = Create();

        await messages.MarkAsReadAsync(
            "wamid.INCOMING",
            showTyping: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "text",
            Body(handler).GetProperty("typing_indicator").GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_send_that_comes_back_without_a_message_id_is_reported()
    {
        var (messages, _) = Create("""{"messaging_product":"whatsapp","messages":[]}""");

        var exception = await Assert.ThrowsAsync<WhatsAppException>(async () =>
            await messages.SendTextAsync("79000000001", "hello", cancellationToken: TestContext.Current.CancellationToken));

        // Without an id there is nothing to match the delivery status webhook against.
        Assert.Contains("no message id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_send_names_the_recipient_so_the_pair_limit_is_counted_per_conversation()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, Accepted);
        var time = new FakeTimeProvider();
        var limiter = new RecordingRateLimiter(new InMemoryRateLimiter(time));
        var messages = new MessagesApi(
            new GraphApiClient(
                new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
                new StubCredentialsProvider(Credentials),
                limiter,
                new StaticOptionsMonitor<WhatsAppOptions>(new WhatsAppOptions()),
                time),
            WhatsAppTenant.Default);

        await messages.SendTextAsync("79000000001", "hello", cancellationToken: TestContext.Current.CancellationToken);

        // Without the recipient the client would pace the phone number only and walk
        // straight into the pair limit.
        Assert.Contains(
            limiter.Requested,
            r => r.Scope.Budget == RateLimitBudget.RecipientPair
                 && r.Scope.Key.Contains("79000000001", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_template_can_fill_in_a_copy_code_button_and_a_map_header()
    {
        // Both were declarable on a template and unsendable: there was no parameter for the
        // code, and none for the point a location header shows.
        var (messages, handler) = Create();

        await messages.SendTemplateAsync(
            "79000000001",
            new TemplateMessage
            {
                Name = "seasonal_offer",
                Language = "en_US",
                Components =
                [
                    TemplateComponent.Header(TemplateParameter.FromLocation(new Location
                    {
                        Latitude = 37.483307,
                        Longitude = -122.148981,
                        Name = "Our shop",
                        Address = "1 Hacker Way",
                    })),
                    TemplateComponent.CopyCodeButton(0, "SUMMER25"),
                ],
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var components = Body(handler).GetProperty("template").GetProperty("components");

        var location = components[0].GetProperty("parameters")[0].GetProperty("location");
        // Strings here, unlike a location message, which takes numbers.
        Assert.Equal("37.483307", location.GetProperty("latitude").GetString());
        Assert.Equal("-122.148981", location.GetProperty("longitude").GetString());
        Assert.Equal("Our shop", location.GetProperty("name").GetString());

        var button = components[1];
        Assert.Equal("copy_code", button.GetProperty("sub_type").GetString());
        Assert.Equal("0", button.GetProperty("index").GetString());

        var coupon = button.GetProperty("parameters")[0];
        Assert.Equal("coupon_code", coupon.GetProperty("type").GetString());
        Assert.Equal("SUMMER25", coupon.GetProperty("coupon_code").GetString());
    }

    [Fact]
    public async Task A_flow_message_carries_the_token_the_reply_comes_back_with()
    {
        var (messages, handler) = Create();

        await messages.SendFlowAsync(
            "79000000001",
            new FlowMessage
            {
                FlowId = "1122334455",
                FlowToken = "booking-42",
                ButtonText = "Book a table",
                Body = "Pick a time that suits you.",
                Screen = "BOOK",
                DataJson = """{"seats":4}""",
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var interactive = Body(handler).GetProperty("interactive");
        Assert.Equal("flow", interactive.GetProperty("type").GetString());

        var parameters = interactive.GetProperty("action").GetProperty("parameters");
        Assert.Equal("3", parameters.GetProperty("flow_message_version").GetString());
        Assert.Equal("booking-42", parameters.GetProperty("flow_token").GetString());
        Assert.Equal("1122334455", parameters.GetProperty("flow_id").GetString());
        Assert.Equal("Book a table", parameters.GetProperty("flow_cta").GetString());
        Assert.Equal("navigate", parameters.GetProperty("flow_action").GetString());
        // Published unless asked otherwise; the draft warning is not something to ship.
        Assert.False(parameters.TryGetProperty("mode", out _));

        var payload = parameters.GetProperty("flow_action_payload");
        Assert.Equal("BOOK", payload.GetProperty("screen").GetString());
        // A JSON object, not a string holding one: the Flow's screens expect the real thing.
        Assert.Equal(4, payload.GetProperty("data").GetProperty("seats").GetInt32());
    }

    [Fact]
    public async Task A_flow_that_asks_its_endpoint_needs_no_screen()
    {
        var (messages, handler) = Create();

        await messages.SendFlowAsync(
            "79000000001",
            new FlowMessage
            {
                FlowName = "booking",
                FlowToken = "booking-42",
                ButtonText = "Book",
                Body = "Pick a time.",
                Action = FlowAction.DataExchange,
                Draft = true,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var parameters = Body(handler)
            .GetProperty("interactive")
            .GetProperty("action")
            .GetProperty("parameters");

        Assert.Equal("data_exchange", parameters.GetProperty("flow_action").GetString());
        Assert.Equal("booking", parameters.GetProperty("flow_name").GetString());
        Assert.Equal("draft", parameters.GetProperty("mode").GetString());
    }

    [Theory]
    [MemberData(nameof(RefusedFlowMessages))]
    public async Task A_flow_message_that_could_only_fail_is_refused_before_the_call(
        FlowMessage message,
        string expected)
    {
        // Meta answers every one of these with a bare 100 that never says which field it
        // objected to.
        var (messages, handler) = Create();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            messages.SendFlowAsync(
                "79000000001",
                message,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    public static TheoryData<FlowMessage, string> RefusedFlowMessages() => new()
    {
        {
            new FlowMessage { FlowToken = "t", ButtonText = "b", Body = "x", Screen = "S" },
            "names neither"
        },
        {
            new FlowMessage
            {
                FlowId = "1", FlowName = "one", FlowToken = "t", ButtonText = "b",
                Body = "x", Screen = "S",
            },
            "names both"
        },
        {
            new FlowMessage { FlowId = "1", FlowToken = "t", ButtonText = "b", Body = "x" },
            "Screen"
        },
        {
            new FlowMessage
            {
                FlowId = "1", FlowToken = "t", ButtonText = "b", Body = "x",
                Screen = "S", DataJson = "{not json",
            },
            "not valid JSON"
        },
        {
            // Meta documents the data as a non-empty object and rejects {} with a bare 100.
            new FlowMessage
            {
                FlowId = "1", FlowToken = "t", ButtonText = "b", Body = "x",
                Screen = "S", DataJson = "{}",
            },
            "empty"
        },
        {
            // The endpoint decides the first screen, so a payload naming one is refused
            // outright rather than ignored.
            new FlowMessage
            {
                FlowId = "1", FlowToken = "t", ButtonText = "b", Body = "x",
                Action = FlowAction.DataExchange, DataJson = """{"seats":4}""",
            },
            "DataJson"
        },
    };

    [Fact]
    public async Task Callback_data_is_sent_so_the_status_can_be_matched_to_your_own_records()
    {
        var (messages, handler) = Create();

        await messages.SendTextAsync(
            "79000000001",
            "your order is on its way",
            callbackData: "order-4711",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            "order-4711",
            Body(handler).GetProperty("biz_opaque_callback_data").GetString());
    }

    [Fact]
    public async Task Callback_data_longer_than_Meta_accepts_is_refused_before_the_call()
    {
        var (messages, handler) = Create();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            messages.SendTextAsync(
                "79000000001",
                "hello",
                callbackData: new string('x', 513),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_send_without_callback_data_does_not_carry_the_field()
    {
        var (messages, handler) = Create();

        await messages.SendTextAsync(
            "79000000001",
            "hello",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(Body(handler).TryGetProperty("biz_opaque_callback_data", out _));
    }

    [Theory]
    [InlineData("+77000000001")]
    [InlineData("+7 700 000 00 01")]
    [InlineData("+7 (700) 000-00-01")]
    [InlineData("77000000001")]
    public async Task A_number_written_the_way_people_store_it_is_sent_the_way_Meta_wants_it(string to)
    {
        // Numbers live in E.164, with the plus. Meta hands them back on the webhook without
        // one, and nobody wants to discover which form it takes with a wave of two thousand
        // messages, so the punctuation comes off here.
        var (messages, handler) = Create();

        await messages.SendTextAsync(to, "hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("77000000001", Body(handler).GetProperty("to").GetString());
    }

    [Fact]
    public async Task Something_that_is_not_a_phone_number_is_refused_before_it_is_sent()
    {
        var (messages, _) = Create();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            messages.SendTextAsync(
                "pablo@example.com",
                "hello",
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("pablo@example.com", exception.Message, StringComparison.Ordinal);
    }

    private static JsonElement Body(StubHttpMessageHandler handler) =>
        JsonDocument.Parse(Assert.Single(handler.Bodies)!).RootElement;

    private static (IMessagesApi Messages, StubHttpMessageHandler Handler) Create(string response = Accepted)
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, response);
        var time = new FakeTimeProvider();

        var api = new MessagesApi(
            new GraphApiClient(
                new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
                new StubCredentialsProvider(Credentials),
                new InMemoryRateLimiter(time),
                new StaticOptionsMonitor<WhatsAppOptions>(new WhatsAppOptions()),
                time),
            WhatsAppTenant.Default);

        return (api, handler);
    }
}
