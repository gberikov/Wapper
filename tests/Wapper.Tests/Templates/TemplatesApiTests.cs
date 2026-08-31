using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Wapper.Internal;
using Wapper.RateLimiting;
using Wapper.Templates;
using Wapper.Tests.Fakes;

namespace Wapper.Tests.Templates;

public class TemplatesApiTests
{
    private const string Created = """{"id":"1387372356726668","status":"PENDING","category":"UTILITY"}""";
    private const string Ok = """{"success":true}""";

    private static readonly WhatsAppCredentials Credentials = new()
    {
        AccessToken = "token-abc",
        PhoneNumberId = "106540352242922",
        WhatsAppBusinessAccountId = "102290129340398",
    };

    private static Template Draft() => new()
    {
        Name = "order_confirmation",
        Language = "en_US",
        Category = TemplateCategory.Utility,
        Body = new TemplateBody
        {
            Text = "Hi {{1}}! Your order number is {{2}}.",
            Examples = [new TemplateParameterExample("Pablo"), new TemplateParameterExample("860198-230332")],
        },
    };

    [Fact]
    public async Task Creating_a_template_posts_it_to_the_business_account()
    {
        var (templates, handler) = Create(Created);

        var result = await templates.CreateAsync(
            Draft(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("1387372356726668", result.Id);
        Assert.Equal(TemplateStatus.Pending, result.Status);
        Assert.Equal(TemplateCategory.Utility, result.Category);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://graph.facebook.com/v26.0/102290129340398/message_templates",
            request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task A_positional_body_example_is_sent_as_a_list_of_lists()
    {
        var (templates, handler) = Create(Created);

        await templates.CreateAsync(Draft(), cancellationToken: TestContext.Current.CancellationToken);

        var body = Component(handler, "BODY");
        // One inner list per example set, of which Meta only ever reviews the first.
        var examples = body.GetProperty("example").GetProperty("body_text");
        Assert.Equal(1, examples.GetArrayLength());
        Assert.Equal("Pablo", examples[0][0].GetString());
        Assert.Equal("860198-230332", examples[0][1].GetString());
    }

    [Fact]
    public async Task A_named_body_example_is_sent_under_a_different_field_entirely()
    {
        var (templates, handler) = Create(Created);

        await templates.CreateAsync(
            Draft() with
            {
                ParameterFormat = TemplateParameterFormat.Named,
                Body = new TemplateBody
                {
                    Text = "Hi {{first_name}}! Your order number is {{order_number}}.",
                    Examples =
                    [
                        new TemplateParameterExample("Pablo", "first_name"),
                        new TemplateParameterExample("860198-230332", "order_number"),
                    ],
                },
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var payload = Body(handler);
        Assert.Equal("NAMED", payload.GetProperty("parameter_format").GetString());

        var named = Component(handler, "BODY").GetProperty("example").GetProperty("body_text_named_params");
        Assert.Equal("first_name", named[0].GetProperty("param_name").GetString());
        Assert.Equal("Pablo", named[0].GetProperty("example").GetString());
    }

    [Fact]
    public async Task A_media_header_carries_the_upload_handle_rather_than_a_media_id()
    {
        var (templates, handler) = Create(Created);

        await templates.CreateAsync(
            Draft() with { Header = TemplateHeader.FromImage("4::aW1hZ2U=") },
            cancellationToken: TestContext.Current.CancellationToken);

        var header = Component(handler, "HEADER");
        Assert.Equal("IMAGE", header.GetProperty("format").GetString());
        // A handle from the Resumable Upload API, not a media id from the media endpoint.
        Assert.Equal(
            "4::aW1hZ2U=",
            header.GetProperty("example").GetProperty("header_handle")[0].GetString());
    }

    [Fact]
    public async Task A_location_header_carries_nothing_but_its_format()
    {
        var (templates, handler) = Create(Created);

        await templates.CreateAsync(
            Draft() with { Header = TemplateHeader.FromLocation() },
            cancellationToken: TestContext.Current.CancellationToken);

        var header = Component(handler, "HEADER");
        Assert.Equal("LOCATION", header.GetProperty("format").GetString());
        // The point is supplied when the template is sent, not when it is created.
        Assert.False(header.TryGetProperty("example", out _));
    }

    [Fact]
    public async Task Buttons_go_into_a_single_component()
    {
        var (templates, handler) = Create(Created);

        await templates.CreateAsync(
            Draft() with
            {
                Buttons =
                [
                    TemplateButton.QuickReply("Unsubscribe"),
                    TemplateButton.Link("Track order", "https://example.com/orders/{{1}}", "1234"),
                    TemplateButton.Call("Call us", "15550051310"),
                    TemplateButton.CopyCode("250FF"),
                ],
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var buttons = Component(handler, "BUTTONS").GetProperty("buttons");
        Assert.Equal(4, buttons.GetArrayLength());
        Assert.Equal("QUICK_REPLY", buttons[0].GetProperty("type").GetString());
        Assert.Equal("1234", buttons[1].GetProperty("example")[0].GetString());
        Assert.Equal("15550051310", buttons[2].GetProperty("phone_number").GetString());
        // A bare string, not a list, unlike every other button example.
        Assert.Equal("250FF", buttons[3].GetProperty("example").GetString());
    }

    [Fact]
    public async Task Category_change_is_allowed_by_default()
    {
        var (templates, handler) = Create(Created);

        await templates.CreateAsync(Draft(), cancellationToken: TestContext.Current.CancellationToken);

        // Without it, a template Meta considers miscategorised is rejected outright rather
        // than moved to the right category.
        Assert.True(Body(handler).GetProperty("allow_category_change").GetBoolean());
    }

    [Theory]
    [InlineData("Order_Confirmation")]
    [InlineData("order confirmation")]
    [InlineData("order-confirmation")]
    [InlineData("заказ")]
    public async Task A_name_the_platform_would_refuse_is_caught_before_sending(string name)
    {
        var (templates, handler) = Create(Created);

        // Meta answers a bad name with a bare code 100, which says nothing about what was
        // wrong with it.
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await templates.CreateAsync(
                Draft() with { Name = name },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("lowercase", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_name_longer_than_the_platform_allows_is_caught_before_sending()
    {
        var (templates, handler) = Create(Created);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await templates.CreateAsync(
                Draft() with { Name = new string('a', 513) },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Managing_templates_without_an_account_id_says_so_plainly()
    {
        var (templates, _) = Create(
            Created,
            Credentials with { WhatsAppBusinessAccountId = null });

        var exception = await Assert.ThrowsAsync<WhatsAppConfigurationException>(async () =>
            await templates.CreateAsync(Draft(), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("WhatsAppBusinessAccountId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Listing_reads_a_template_back_out_of_the_platform_shape()
    {
        // Trimmed from the example in Meta's template management reference.
        const string Page = """
            {
              "data": [{
                "name": "reservation_confirmation",
                "parameter_format": "NAMED",
                "components": [
                  {"type": "HEADER", "format": "IMAGE",
                   "example": {"header_handle": ["https://scontent.whatsapp.net/v/t61"]}},
                  {"type": "BODY", "text": "Your reservation for {{number_of_guests}} is confirmed.",
                   "example": {"body_text_named_params": [
                     {"param_name": "number_of_guests", "example": "4"}]}},
                  {"type": "FOOTER", "text": "The Luckiest Eatery in Town"},
                  {"type": "BUTTONS", "buttons": [
                    {"type": "URL", "text": "Change reservation", "url": "https://example.com/r"},
                    {"type": "PHONE_NUMBER", "text": "Call us", "phone_number": "+16467043595"},
                    {"type": "QUICK_REPLY", "text": "Cancel reservation"}]}
                ],
                "language": "en_US",
                "status": "APPROVED",
                "category": "UTILITY",
                "id": "1387372356726668"
              }],
              "paging": {"cursors": {"before": "QVFIU", "after": "QVFIU"}}
            }
            """;
        var (templates, _) = Create(Page);

        var template = await Single(templates);

        Assert.Equal("1387372356726668", template.Id);
        Assert.Equal("reservation_confirmation", template.Name);
        Assert.Equal(TemplateCategory.Utility, template.Category);
        Assert.Equal(TemplateStatus.Approved, template.Status);
        Assert.Equal(TemplateParameterFormat.Named, template.ParameterFormat);

        Assert.Equal(TemplateHeaderFormat.Image, template.Header!.Format);
        Assert.Equal("Your reservation for {{number_of_guests}} is confirmed.", template.Body.Text);
        Assert.Equal("number_of_guests", Assert.Single(template.Body.Examples).Name);
        Assert.Equal("The Luckiest Eatery in Town", template.Footer);

        Assert.Equal(3, template.Buttons.Count);
        Assert.Equal(TemplateButtonKind.Url, template.Buttons[0].Kind);
        Assert.Equal("+16467043595", template.Buttons[1].PhoneNumber);
        Assert.Equal(TemplateButtonKind.QuickReply, template.Buttons[2].Kind);
    }

    [Fact]
    public async Task Listing_follows_the_cursor_until_the_platform_stops_offering_one()
    {
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.OK, """
                {"data":[{"name":"one","language":"en","category":"UTILITY","status":"APPROVED",
                          "components":[{"type":"BODY","text":"a"}],"id":"1"}],
                 "paging":{"cursors":{"after":"CURSOR"},"next":"https://graph.facebook.com/next"}}
                """),
            // The last page still carries a cursor; only the missing `next` says to stop.
            (HttpStatusCode.OK, """
                {"data":[{"name":"two","language":"en","category":"UTILITY","status":"APPROVED",
                          "components":[{"type":"BODY","text":"b"}],"id":"2"}],
                 "paging":{"cursors":{"after":"CURSOR"}}}
                """));
        var templates = CreateWith(handler, Credentials);

        var names = new List<string>();
        await foreach (var template in templates.ListAsync(
            cancellationToken: TestContext.Current.CancellationToken))
        {
            names.Add(template.Name);
        }

        Assert.Equal(["one", "two"], names);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("after=CURSOR", handler.Requests[1].RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_query_becomes_query_parameters()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"data":[]}""");
        var templates = CreateWith(handler, Credentials);

        await foreach (var _ in templates.ListAsync(
            new TemplateQuery
            {
                Name = "order_confirmation",
                Status = TemplateStatus.Approved,
                Category = TemplateCategory.Utility,
                Language = "en_US",
                PageSize = 50,
            },
            TestContext.Current.CancellationToken))
        {
            // Draining the sequence is what issues the request.
        }

        var query = Assert.Single(handler.Requests).RequestUri!.Query;
        Assert.Contains("name=order_confirmation", query, StringComparison.Ordinal);
        Assert.Contains("status=APPROVED", query, StringComparison.Ordinal);
        Assert.Contains("category=UTILITY", query, StringComparison.Ordinal);
        Assert.Contains("language=en_US", query, StringComparison.Ordinal);
        Assert.Contains("limit=50", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_button_kind_this_library_does_not_know_survives_reading()
    {
        const string Page = """
            {"data":[{"name":"n","language":"en","category":"MARKETING","status":"APPROVED","id":"1",
              "components":[{"type":"BODY","text":"b"},
                            {"type":"BUTTONS","buttons":[{"type":"HOLOGRAM","text":"Project"}]}]}]}
            """;
        var (templates, _) = Create(Page);

        var button = Assert.Single((await Single(templates)).Buttons);

        // Meta adds button types without warning. Dropping one would silently change what a
        // caller thinks the template looks like.
        Assert.Equal(TemplateButtonKind.Unknown, button.Kind);
        Assert.Equal("HOLOGRAM", button.RawKind);
    }

    [Fact]
    public async Task Editing_sends_only_the_components()
    {
        var (templates, handler) = Create(Ok);

        await templates.UpdateAsync(
            "1387372356726668",
            Draft(),
            TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://graph.facebook.com/v26.0/1387372356726668",
            request.RequestUri!.AbsoluteUri);

        var payload = Body(handler);
        Assert.True(payload.TryGetProperty("components", out _));
        // Name, language and category are not editable this way, and sending them fails the
        // call rather than being ignored.
        Assert.False(payload.TryGetProperty("name", out _));
        Assert.False(payload.TryGetProperty("language", out _));
        Assert.False(payload.TryGetProperty("category", out _));
    }

    [Fact]
    public async Task Recategorising_sends_only_the_category()
    {
        var (templates, handler) = Create(Ok);

        await templates.UpdateCategoryAsync(
            "1387372356726668",
            TemplateCategory.Marketing,
            TestContext.Current.CancellationToken);

        var payload = Body(handler);
        Assert.Equal("MARKETING", payload.GetProperty("category").GetString());
        Assert.False(payload.TryGetProperty("components", out _));
    }

    [Fact]
    public async Task Deleting_by_id_carries_the_name_as_well()
    {
        var (templates, handler) = Create(Ok);

        await templates.DeleteAsync(
            "1387372356726668",
            "order_confirmation",
            TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        // Meta wants both. Sending the name alone would take every language with it.
        Assert.Contains("hsm_id=1387372356726668", request.RequestUri!.Query, StringComparison.Ordinal);
        Assert.Contains("name=order_confirmation", request.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_by_name_takes_every_language()
    {
        var (templates, handler) = Create(Ok);

        await templates.DeleteByNameAsync("order_confirmation", TestContext.Current.CancellationToken);

        var query = Assert.Single(handler.Requests).RequestUri!.Query;
        Assert.Contains("name=order_confirmation", query, StringComparison.Ordinal);
        Assert.DoesNotContain("hsm_id", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_several_sends_them_as_one_bracketed_list()
    {
        var (templates, handler) = Create(Ok);

        await templates.DeleteAsync(
            ["1387372356726668", "1304694804498707"],
            TestContext.Current.CancellationToken);

        var query = Uri.UnescapeDataString(Assert.Single(handler.Requests).RequestUri!.Query);
        Assert.Contains("hsm_ids=[1387372356726668,1304694804498707]", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_more_than_the_platform_accepts_is_refused_before_sending()
    {
        var (templates, handler) = Create(Ok);

        // If any id in the batch is invalid the whole request fails and nothing is deleted,
        // so an oversized batch is worth catching here.
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await templates.DeleteAsync(
                Enumerable.Range(0, 101).Select(i => i.ToString()),
                TestContext.Current.CancellationToken));

        Assert.Contains("100", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Deleting_nothing_calls_nothing()
    {
        var (templates, handler) = Create(Ok);

        await templates.DeleteAsync([], TestContext.Current.CancellationToken);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Template_calls_spend_the_business_account_budget()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, Created);
        var time = new FakeTimeProvider();
        var limiter = new RecordingRateLimiter(new InMemoryRateLimiter(time));
        var templates = new TemplatesApi(
            new GraphApiClient(
                new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
                new StubCredentialsProvider(Credentials),
                limiter,
                new StaticOptionsMonitor<WhatsAppOptions>(new WhatsAppOptions()),
                time),
            WhatsAppTenant.Default);

        await templates.CreateAsync(Draft(), cancellationToken: TestContext.Current.CancellationToken);

        // 200 an hour, or 5000 once the account has a registered number. Not the message
        // throughput budget, which these calls do not touch.
        Assert.Contains(
            limiter.Requested,
            r => r.Scope.Budget == RateLimitBudget.BusinessAccountRequests
                 && r.Scope.Key == "102290129340398");
        Assert.DoesNotContain(
            limiter.Requested,
            r => r.Scope.Budget == RateLimitBudget.PhoneNumberThroughput);
    }

    private static async Task<Template> Single(ITemplatesApi templates)
    {
        var found = new List<Template>();

        await foreach (var template in templates.ListAsync(
            cancellationToken: TestContext.Current.CancellationToken))
        {
            found.Add(template);
        }

        return Assert.Single(found);
    }

    private static JsonElement Body(StubHttpMessageHandler handler) =>
        JsonDocument.Parse(Assert.Single(handler.Bodies)!).RootElement;

    private static JsonElement Component(StubHttpMessageHandler handler, string type)
    {
        foreach (var component in Body(handler).GetProperty("components").EnumerateArray())
        {
            if (component.GetProperty("type").GetString() == type)
            {
                return component;
            }
        }

        Assert.Fail($"No {type} component was sent.");
        return default;
    }

    private static (ITemplatesApi Templates, StubHttpMessageHandler Handler) Create(
        string response,
        WhatsAppCredentials? credentials = null)
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, response);
        return (CreateWith(handler, credentials ?? Credentials), handler);
    }

    private static ITemplatesApi CreateWith(
        StubHttpMessageHandler handler,
        WhatsAppCredentials credentials)
    {
        var time = new FakeTimeProvider();

        return new TemplatesApi(
            new GraphApiClient(
                new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
                new StubCredentialsProvider(credentials),
                new InMemoryRateLimiter(time),
                new StaticOptionsMonitor<WhatsAppOptions>(new WhatsAppOptions()),
                time),
            WhatsAppTenant.Default);
    }
}
