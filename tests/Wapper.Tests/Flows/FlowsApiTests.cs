using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Wapper.Flows;
using Wapper.Internal;
using Wapper.RateLimiting;
using Wapper.Tests.Fakes;

namespace Wapper.Tests.Flows;

public class FlowsApiTests
{
    private const string Ok = """{"success":true}""";

    /// <summary>What a create answers with when the JSON is wrong — on a 200.</summary>
    private const string CreatedWithErrors = """
        {
          "id": "1122334455",
          "success": true,
          "validation_errors": [{
            "error": "INVALID_PROPERTY_VALUE",
            "error_type": "FLOW_JSON_ERROR",
            "message": "Invalid value found for property 'type'.",
            "line_start": 10,
            "line_end": 10,
            "column_start": 21,
            "column_end": 34,
            "pointers": [{
              "line_start": 10,
              "path": "screens[0].layout.children[0].type"
            }]
          }]
        }
        """;

    private static readonly WhatsAppCredentials Credentials = new()
    {
        AccessToken = "token-abc",
        PhoneNumberId = "106540352242922",
        WhatsAppBusinessAccountId = "102290129340398",
    };

    [Fact]
    public async Task Creating_a_flow_posts_it_to_the_account()
    {
        var (flows, handler) = Create("""{"id":"1122334455","success":true}""");

        var result = await flows.CreateAsync(
            new FlowDefinition
            {
                Name = "Book a table",
                Categories = [FlowCategory.AppointmentBooking, FlowCategory.Other],
                Json = """{"version":"7.0","screens":[]}""",
                Publish = true,
                EndpointUri = new Uri("https://flows.example/hook"),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal("1122334455", result.Id);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://graph.facebook.com/v26.0/102290129340398/flows",
            request.RequestUri!.AbsoluteUri);

        var body = JsonDocument.Parse(Assert.Single(handler.Bodies)!).RootElement;
        Assert.Equal("Book a table", body.GetProperty("name").GetString());
        Assert.Equal(
            ["APPOINTMENT_BOOKING", "OTHER"],
            body.GetProperty("categories").EnumerateArray().Select(c => c.GetString()));
        // A string holding JSON, not nested JSON: Meta reads it back out itself.
        Assert.Equal(
            """{"version":"7.0","screens":[]}""",
            body.GetProperty("flow_json").GetString());
        Assert.True(body.GetProperty("publish").GetBoolean());
        Assert.Equal("https://flows.example/hook", body.GetProperty("endpoint_uri").GetString());
    }

    [Fact]
    public async Task A_broken_flow_is_created_anyway_and_says_what_is_wrong()
    {
        var (flows, _) = Create(CreatedWithErrors);

        var result = await flows.CreateAsync(
            new FlowDefinition { Name = "Broken", Categories = [FlowCategory.Other] },
            TestContext.Current.CancellationToken);

        // 200, an id, "success": true — and a Flow that will never publish. A caller that only
        // watches for exceptions learns nothing.
        Assert.Equal("1122334455", result.Id);
        var error = Assert.Single(result.ValidationErrors);
        Assert.Equal("INVALID_PROPERTY_VALUE", error.Error);
        Assert.Equal("FLOW_JSON_ERROR", error.ErrorType);
        Assert.Equal(10, error.LineStart);
        Assert.Equal(21, error.ColumnStart);
        Assert.Equal("screens[0].layout.children[0].type", Assert.Single(error.Paths));
    }

    [Fact]
    public async Task Asking_to_publish_without_the_json_is_refused_before_the_call()
    {
        var (flows, handler) = Create(Ok);

        // Meta ignores the flag rather than refusing, so the Flow would quietly be a draft.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            flows.CreateAsync(
                new FlowDefinition
                {
                    Name = "No JSON",
                    Categories = [FlowCategory.Other],
                    Publish = true,
                },
                TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_flow_without_a_category_is_refused_before_the_call()
    {
        var (flows, handler) = Create(Ok);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            flows.CreateAsync(
                new FlowDefinition { Name = "Uncategorised", Categories = [] },
                TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Reading_a_flow_asks_for_the_fields_that_are_not_returned_by_default()
    {
        var (flows, handler) = Create("""{"id":"1","status":"DRAFT"}""");

        await flows.GetAsync("1", cancellationToken: TestContext.Current.CancellationToken);

        var query = Assert.Single(handler.Requests).RequestUri!.Query;
        // Only id, name, status, categories and validation_errors come back on their own.
        Assert.Contains("endpoint_uri", query, StringComparison.Ordinal);
        Assert.Contains("health_status", query, StringComparison.Ordinal);
        // invalidate(false) returns the link that exists rather than minting a new one and
        // breaking whatever was already shared.
        Assert.Contains("preview.invalidate(false)", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_health_check_can_be_narrowed_to_one_phone_number()
    {
        var (flows, handler) = Create("""{"id":"1"}""");

        await flows.GetAsync("1", "106540352242922", TestContext.Current.CancellationToken);

        Assert.Contains(
            "health_status.phone_number(106540352242922)",
            Assert.Single(handler.Requests).RequestUri!.Query,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_health_status_says_which_thing_is_blocking_the_send()
    {
        var (flows, _) = Create("""
            {
              "id": "1",
              "status": "DRAFT",
              "health_status": {
                "can_send_message": "BLOCKED",
                "entities": [
                  {
                    "entity_type": "FLOW",
                    "id": "1",
                    "can_send_message": "BLOCKED",
                    "errors": [{
                      "error_code": 131000,
                      "error_description": "endpoint_uri: You need to set the endpoint URI.",
                      "possible_solution": "https://developers.facebook.com/docs"
                    }]
                  },
                  {
                    "entity_type": "APP",
                    "id": "9",
                    "can_send_message": "LIMITED",
                    "additional_info": ["Your app is not subscribed to the message webhook."]
                  }
                ]
              }
            }
            """);

        var flow = await flows.GetAsync("1", cancellationToken: TestContext.Current.CancellationToken);

        // Four things have to be healthy to send a Flow, and validation_errors covers none of
        // them.
        Assert.Equal(MessagingAvailability.Blocked, flow.Health!.CanSendMessage);
        Assert.Equal(2, flow.Health.Entities.Count);
        Assert.Equal(131000, Assert.Single(flow.Health.Entities[0].Errors).Code);
        Assert.Equal(MessagingAvailability.Limited, flow.Health.Entities[1].CanSendMessage);
        Assert.Single(flow.Health.Entities[1].AdditionalInfo);
    }

    [Fact]
    public async Task Listing_reads_every_page_and_follows_next_rather_than_the_cursor()
    {
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.OK, """
                {"data":[{"id":"1","status":"DRAFT","categories":["SURVEY"]}],
                 "paging":{"cursors":{"after":"CURSOR"},"next":"https://graph.facebook.com/next"}}
                """),
            (HttpStatusCode.OK, """
                {"data":[{"id":"2","status":"PUBLISHED"}],"paging":{"cursors":{"after":"CURSOR"}}}
                """));

        var found = new List<Flow>();

        await foreach (var flow in CreateWith(handler, Credentials)
            .ListAsync(TestContext.Current.CancellationToken))
        {
            found.Add(flow);
        }

        Assert.Equal(["1", "2"], found.Select(f => f.Id));
        Assert.Equal(FlowCategory.Survey, Assert.Single(found[0].Categories));
        Assert.Equal(FlowStatus.Published, found[1].Status);
        // The last page still carries a cursor.
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_listing_does_not_ask_for_the_per_flow_work()
    {
        var (flows, handler) = Create("""{"data":[]}""");

        await foreach (var _ in flows.ListAsync(TestContext.Current.CancellationToken))
        {
            // Drains the one empty page.
        }

        // A preview link and a health check for every Flow of the account is not what a
        // listing is for.
        Assert.Equal(
            "https://graph.facebook.com/v26.0/102290129340398/flows",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Uploading_the_flow_json_sends_form_data_with_the_names_Meta_insists_on()
    {
        var (flows, handler) = Create(Ok);

        await flows.UpdateJsonAsync(
            "1122334455",
            """{"version":"7.0","screens":[]}""",
            TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://graph.facebook.com/v26.0/1122334455/assets",
            request.RequestUri!.AbsoluteUri);

        // Multipart, not a JSON body — the one endpoint here that works that way.
        var body = Assert.Single(handler.Bodies)!;
        Assert.StartsWith("multipart/form-data", request.Content!.Headers.ContentType!.MediaType!, StringComparison.Ordinal);
        Assert.Contains("asset_type", body, StringComparison.Ordinal);
        Assert.Contains("FLOW_JSON", body, StringComparison.Ordinal);
        Assert.Contains("flow.json", body, StringComparison.Ordinal);
        Assert.Contains(""""{"version":"7.0","screens":[]}"""", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_uploaded_document_that_is_wrong_is_still_accepted()
    {
        var (flows, _) = Create(CreatedWithErrors);

        var errors = await flows.UpdateJsonAsync(
            "1122334455",
            new MemoryStream(Encoding.UTF8.GetBytes("{}")),
            TestContext.Current.CancellationToken);

        // Same trap as the create: a 200 that carries the reason it will never publish.
        Assert.Equal(
            "Invalid value found for property 'type'.",
            Assert.Single(errors).Message);
    }

    [Fact]
    public async Task An_upload_is_never_retried()
    {
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.InternalServerError,
             """{"error":{"code":131000,"message":"Something went wrong","is_transient":true}}"""),
            (HttpStatusCode.OK, Ok));

        await Assert.ThrowsAsync<WhatsAppApiException>(() =>
            CreateWith(handler, Credentials).UpdateJsonAsync(
                "1122334455",
                new MemoryStream(Encoding.UTF8.GetBytes("{}")),
                TestContext.Current.CancellationToken));

        // The stream has already been read; a second attempt would upload nothing.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Publishing_and_deprecating_post_to_their_own_edges()
    {
        var (flows, handler) = Create(Ok);

        await flows.PublishAsync("1122334455", TestContext.Current.CancellationToken);
        await flows.DeprecateAsync("1122334455", TestContext.Current.CancellationToken);

        Assert.Equal(
            "https://graph.facebook.com/v26.0/1122334455/publish",
            handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal(
            "https://graph.facebook.com/v26.0/1122334455/deprecate",
            handler.Requests[1].RequestUri!.AbsoluteUri);
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Post, r.Method));
    }

    [Fact]
    public async Task Deleting_a_flow_is_a_delete_on_the_flow_itself()
    {
        var (flows, handler) = Create(Ok);

        await flows.DeleteAsync("1122334455", TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal(
            "https://graph.facebook.com/v26.0/1122334455",
            request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Asking_for_a_new_preview_link_invalidates_the_old_one()
    {
        var (flows, handler) = Create("""
            {
              "id": "1122334455",
              "preview": {
                "preview_url": "https://business.facebook.com/wa/manage/flows/55/preview/?token=b9",
                "expires_at": "2026-09-21T11:18:09+0000"
              }
            }
            """);

        var preview = await flows.GetPreviewAsync(
            "1122334455",
            invalidate: true,
            TestContext.Current.CancellationToken);

        Assert.Contains(
            "preview.invalidate(true)",
            Assert.Single(handler.Requests).RequestUri!.Query,
            StringComparison.Ordinal);
        // ISO 8601 with a colonless offset, as everywhere else outside the webhooks.
        Assert.Equal(
            new DateTimeOffset(2026, 9, 21, 11, 18, 9, TimeSpan.Zero),
            preview.ExpiresAt);
    }

    [Fact]
    public async Task Updating_the_metadata_leaves_out_what_was_not_set()
    {
        var (flows, handler) = Create(Ok);

        await flows.UpdateAsync(
            "1122334455",
            new FlowUpdate { Name = "Renamed" },
            TestContext.Current.CancellationToken);

        var body = JsonDocument.Parse(Assert.Single(handler.Bodies)!).RootElement;
        Assert.Equal("Renamed", body.GetProperty("name").GetString());
        // Missing categories keep the ones the Flow already has.
        Assert.False(body.TryGetProperty("categories", out _));
    }

    [Fact]
    public async Task Clearing_the_categories_is_refused_rather_than_sent()
    {
        var (flows, handler) = Create(Ok);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            flows.UpdateAsync(
                "1122334455",
                new FlowUpdate { Categories = [] },
                TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Listing_the_assets_says_where_the_flow_json_can_be_read()
    {
        var (flows, _) = Create("""
            {"data":[{"name":"flow.json","asset_type":"FLOW_JSON","download_url":"https://cdn.example/f"}]}
            """);

        var assets = await flows.ListAssetsAsync("1122334455", TestContext.Current.CancellationToken);

        var asset = Assert.Single(assets);
        Assert.Equal("flow.json", asset.Name);
        Assert.Equal("https://cdn.example/f", asset.DownloadUrl);
    }

    [Fact]
    public async Task Flows_spend_the_account_allowance()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, Ok);
        var time = new FakeTimeProvider();
        var limiter = new RecordingRateLimiter(new InMemoryRateLimiter(time));
        var flows = new FlowsApi(
            new GraphApiClient(
                new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
                new StubCredentialsProvider(Credentials),
                limiter,
                new StaticOptionsMonitor<WhatsAppOptions>(new WhatsAppOptions()),
                time),
            WhatsAppTenant.Default);

        await flows.PublishAsync("1122334455", TestContext.Current.CancellationToken);

        Assert.Contains(
            limiter.Requested,
            r => r.Scope.Budget == RateLimitBudget.BusinessAccountRequests
                 && r.Scope.Key == "102290129340398");
    }

    [Fact]
    public async Task Creating_without_a_business_account_id_says_which_setting_is_missing()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, Ok);
        var flows = CreateWith(handler, Credentials with { WhatsAppBusinessAccountId = null });

        var exception = await Assert.ThrowsAsync<WhatsAppConfigurationException>(() =>
            flows.CreateAsync(
                new FlowDefinition { Name = "n", Categories = [FlowCategory.Other] },
                TestContext.Current.CancellationToken));

        Assert.Contains("WhatsAppBusinessAccountId", exception.Message, StringComparison.Ordinal);
    }

    private static (IFlowsApi Flows, StubHttpMessageHandler Handler) Create(string response)
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, response);
        return (CreateWith(handler, Credentials), handler);
    }

    private static IFlowsApi CreateWith(
        StubHttpMessageHandler handler,
        WhatsAppCredentials credentials)
    {
        var time = new FakeTimeProvider();

        return new FlowsApi(
            new GraphApiClient(
                new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
                new StubCredentialsProvider(credentials),
                new InMemoryRateLimiter(time),
                new StaticOptionsMonitor<WhatsAppOptions>(new WhatsAppOptions()),
                time),
            WhatsAppTenant.Default);
    }
}
