using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Wapper.Internal;
using Wapper.RateLimiting;
using Wapper.Templates;
using Wapper.Tests.Fakes;
using Wapper.Webhooks;

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
    public async Task Reading_asks_for_the_fields_Meta_leaves_out_by_default()
    {
        var (templates, handler) = Create("""{"data":[]}""");

        await foreach (var _ in templates.ListAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            // Draining the sequence is what issues the request.
        }

        // Without an explicit list a template comes back without its quality score or the
        // reason it was rejected — the two things it is most often read for once it exists.
        var query = Assert.Single(handler.Requests).RequestUri!.Query;
        Assert.Contains("quality_score", query, StringComparison.Ordinal);
        Assert.Contains("rejected_reason", query, StringComparison.Ordinal);
        Assert.Contains("parameter_format", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_one_template_asks_for_the_same_fields()
    {
        var (templates, handler) = Create("""
            {"name":"n","language":"en","category":"UTILITY","status":"REJECTED","id":"1",
             "rejected_reason":"INVALID_FORMAT","quality_score":{"score":"UNKNOWN"},
             "components":[{"type":"BODY","text":"b"}]}
            """);

        var template = await templates.GetAsync("1387372356726668", TestContext.Current.CancellationToken);

        Assert.StartsWith(
            "https://graph.facebook.com/v26.0/1387372356726668?fields=",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri,
            StringComparison.Ordinal);
        Assert.Equal("INVALID_FORMAT", template.RejectedReason);
        Assert.Equal(TemplateQuality.Pending, template.QualityScore);
    }

    [Fact]
    public async Task A_header_sample_goes_through_the_resumable_upload_and_comes_back_as_a_handle()
    {
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.OK, """{"id":"upload:MTphdHRhY2htZW50"}"""),
            (HttpStatusCode.OK, """{"h":"4:aW1hZ2U="}"""));
        var templates = CreateWith(handler, Credentials with { AppId = "1234567890" });

        var handle = await templates.UploadHeaderSampleAsync(
            new MemoryStream([1, 2, 3]),
            "image/png",
            TestContext.Current.CancellationToken);

        // The handle is what TemplateHeader.FromImage takes. It is not a media id, and the
        // media endpoint would not know what to do with it.
        Assert.Equal("4:aW1hZ2U=", handle);

        Assert.Equal(2, handler.Requests.Count);
        Assert.StartsWith(
            "https://graph.facebook.com/v26.0/1234567890/uploads?",
            handler.Requests[0].RequestUri!.AbsoluteUri,
            StringComparison.Ordinal);
        Assert.Equal("OAuth", handler.Requests[1].Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task A_template_read_back_with_a_category_this_library_does_not_know_can_still_be_edited()
    {
        var (templates, handler) = Create(Ok);

        // An edit never sends the category, so there is no reason for one Meta invented last
        // week to stop the components going up.
        await templates.UpdateAsync(
            "1387372356726668",
            Draft() with { Category = TemplateCategory.Unknown },
            TestContext.Current.CancellationToken);

        Assert.Single(handler.Requests);
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

    [Fact]
    public async Task A_media_header_without_the_sample_handle_still_reads()
    {
        // Meta does not promise to hand the sample handle back with a template it stored
        // months ago. Throwing over it would take down the whole listing, not just the one
        // component, and a listing is how an application finds out what it can send.
        const string Page = """
            {"data":[{"name":"n","language":"en","category":"MARKETING","status":"APPROVED","id":"1",
              "components":[{"type":"HEADER","format":"IMAGE"},{"type":"BODY","text":"b"}]}]}
            """;
        var (templates, _) = Create(Page);

        var template = await Single(templates);

        Assert.Equal(TemplateHeaderFormat.Image, template.Header!.Format);
        Assert.Null(template.Header.MediaHandle);
    }

    [Fact]
    public async Task Buttons_that_arrive_without_their_sample_or_label_still_read()
    {
        const string Page = """
            {"data":[{"name":"n","language":"en","category":"MARKETING","status":"APPROVED","id":"1",
              "components":[{"type":"BODY","text":"b"},
                            {"type":"BUTTONS","buttons":[{"type":"COPY_CODE"},
                                                         {"type":"QUICK_REPLY"},
                                                         {"type":"URL","text":"Track"}]}]}]}
            """;
        var (templates, _) = Create(Page);

        var buttons = (await Single(templates)).Buttons;

        Assert.Equal(3, buttons.Count);
        Assert.Equal(TemplateButtonKind.CopyCode, buttons[0].Kind);
        Assert.Null(buttons[0].CopyCodeExample);
        Assert.Equal(TemplateButtonKind.QuickReply, buttons[1].Kind);
        Assert.Equal(TemplateButtonKind.Url, buttons[2].Kind);
        Assert.Null(buttons[2].Url);
    }

    [Fact]
    public async Task An_authentication_template_carries_no_text_of_its_own()
    {
        var (templates, handler) = Create(Created);

        await templates.CreateAsync(
            Template.Authentication(
                "verification_code",
                "en_US",
                TemplateButton.CopyOneTimePassword(),
                codeExpirationMinutes: 10),
            cancellationToken: TestContext.Current.CancellationToken);

        // Meta writes the body and the footer itself, in every language it supports, and
        // rejects a template that brings its own.
        var body = Component(handler, "BODY");
        Assert.False(body.TryGetProperty("text", out _));
        Assert.True(body.GetProperty("add_security_recommendation").GetBoolean());

        var footer = Component(handler, "FOOTER");
        Assert.Equal(10, footer.GetProperty("code_expiration_minutes").GetInt32());
        Assert.False(footer.TryGetProperty("text", out _));

        var button = Component(handler, "BUTTONS").GetProperty("buttons")[0];
        Assert.Equal("OTP", button.GetProperty("type").GetString());
        Assert.Equal("COPY_CODE", button.GetProperty("otp_type").GetString());
    }

    [Fact]
    public async Task An_autofilled_passcode_names_the_apps_it_may_be_delivered_into()
    {
        var (templates, handler) = Create(Created);

        await templates.CreateAsync(
            Template.Authentication(
                "verification_code",
                "en_US",
                TemplateButton.AutofillOneTimePassword(
                    [new TemplateApplication("com.example.app", "K2h6uSdG3xY")],
                    autofillText: "Autofill",
                    zeroTap: true)),
            cancellationToken: TestContext.Current.CancellationToken);

        var button = Component(handler, "BUTTONS").GetProperty("buttons")[0];

        Assert.Equal("ZERO_TAP", button.GetProperty("otp_type").GetString());
        Assert.Equal("Autofill", button.GetProperty("autofill_text").GetString());
        // Meta will not approve a zero-tap template without this.
        Assert.True(button.GetProperty("zero_tap_terms_accepted").GetBoolean());

        var app = button.GetProperty("supported_apps")[0];
        Assert.Equal("com.example.app", app.GetProperty("package_name").GetString());
        Assert.Equal("K2h6uSdG3xY", app.GetProperty("signature_hash").GetString());
    }

    [Fact]
    public async Task A_passcode_button_reads_back_from_the_shape_Meta_used_before_supported_apps()
    {
        const string Page = """
            {"data":[{"name":"n","language":"en","category":"AUTHENTICATION","status":"APPROVED","id":"1",
              "quality_score":{"score":"GREEN"},"previous_category":"UTILITY",
              "components":[{"type":"BODY","add_security_recommendation":true},
                            {"type":"FOOTER","code_expiration_minutes":5},
                            {"type":"BUTTONS","buttons":[{"type":"OTP","otp_type":"ONE_TAP",
                                                          "text":"Copy code","autofill_text":"Autofill",
                                                          "package_name":"com.example.app",
                                                          "signature_hash":"K2h6uSdG3xY"}]}]}]}
            """;
        var (templates, _) = Create(Page);

        var template = await Single(templates);

        Assert.True(template.Body.AddSecurityRecommendation);
        Assert.Equal(5, template.CodeExpirationMinutes);
        Assert.Equal(TemplateQuality.Green, template.QualityScore);
        Assert.Equal(TemplateCategory.Utility, template.PreviousCategory);

        var otp = Assert.Single(template.Buttons).OneTimePassword!;
        Assert.Equal(OneTimePasswordDelivery.OneTap, otp.Delivery);
        // The older single-app pair is folded into the list, so a caller has one place to look.
        var app = Assert.Single(otp.SupportedApps);
        Assert.Equal("com.example.app", app.PackageName);
        Assert.Equal("K2h6uSdG3xY", app.SignatureHash);
    }

    [Fact]
    public async Task A_component_kind_this_library_does_not_know_survives_reading_and_blocks_an_edit()
    {
        const string Page = """
            {"data":[{"name":"n","language":"en","category":"MARKETING","status":"APPROVED","id":"1",
              "components":[{"type":"BODY","text":"b"},{"type":"CAROUSEL"}]}]}
            """;
        var (templates, _) = Create(Page);

        var template = await Single(templates);

        // The carousel cannot be modelled, but it can be seen.
        Assert.Equal("CAROUSEL", Assert.Single(template.UnknownComponents));

        // Components are replaced wholesale on an edit, so writing this template back would
        // erase the carousel at Meta. A typo fix in the body must not cost the card deck.
        var (editor, handler) = Create(Ok);
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            editor.UpdateAsync("1", template, TestContext.Current.CancellationToken));

        Assert.Contains("CAROUSEL", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_quality_spelling_this_library_does_not_know_is_kept_raw()
    {
        const string Page = """
            {"data":[{"name":"n","language":"en","category":"MARKETING","status":"APPROVED","id":"1",
              "quality_score":{"score":"SUPERB"},
              "components":[{"type":"BODY","text":"b"}]}]}
            """;
        var (templates, _) = Create(Page);

        var template = await Single(templates);

        // A Graph read has no UnknownEvent to fall back on, so the raw string is the only
        // way a caller can see what Meta actually said.
        Assert.Equal(TemplateQuality.Unknown, template.QualityScore);
        Assert.Equal("SUPERB", template.RawQualityScore);
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
