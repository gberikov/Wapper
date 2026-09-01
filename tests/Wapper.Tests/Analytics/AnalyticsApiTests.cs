using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Wapper.Analytics;
using Wapper.Internal;
using Wapper.RateLimiting;
using Wapper.Tests.Fakes;

namespace Wapper.Tests.Analytics;

public class AnalyticsApiTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(1543543200);
    private static readonly DateTimeOffset End = DateTimeOffset.FromUnixTimeSeconds(1544148000);

    private static readonly WhatsAppCredentials Credentials = new()
    {
        AccessToken = "token-abc",
        PhoneNumberId = "106540352242922",
        WhatsAppBusinessAccountId = "102290129340398",
    };

    [Fact]
    public async Task Messaging_analytics_is_a_field_expansion_on_the_account()
    {
        var (analytics, handler) = Create("""{"analytics":{"data_points":[]},"id":"1"}""");

        await analytics.GetMessagingAsync(
            new MessagingAnalyticsQuery
            {
                Start = Start,
                End = End,
                Granularity = AnalyticsGranularity.Day,
            },
            TestContext.Current.CancellationToken);

        // Not an endpoint of its own: the account node, read with one field expanded, and the
        // filters written as arguments on that field.
        Assert.Equal(
            "https://graph.facebook.com/v26.0/102290129340398" +
            "?fields=analytics.start(1543543200).end(1544148000).granularity(DAY)",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task The_two_families_spell_the_same_granularity_differently()
    {
        var (messaging, messagingHandler) = Create("""{"analytics":{}}""");
        var (conversations, conversationHandler) = Create("""{"conversation_analytics":{"data":[]}}""");

        await messaging.GetMessagingAsync(
            new MessagingAnalyticsQuery
            {
                Start = Start,
                End = End,
                Granularity = AnalyticsGranularity.Month,
            },
            TestContext.Current.CancellationToken);

        await conversations.GetConversationsAsync(
            new ConversationAnalyticsQuery
            {
                Start = Start,
                End = End,
                Granularity = AnalyticsGranularity.Month,
            },
            TestContext.Current.CancellationToken);

        // MONTH for messaging, MONTHLY for conversations, and each rejects the other's word
        // for it.
        Assert.Contains(
            "granularity(MONTH)",
            Assert.Single(messagingHandler.Requests).RequestUri!.Query,
            StringComparison.Ordinal);
        Assert.Contains(
            "granularity(MONTHLY)",
            Assert.Single(conversationHandler.Requests).RequestUri!.Query,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Message_kinds_go_up_as_the_numbers_Meta_uses_for_them()
    {
        var (analytics, handler) = Create("""{"analytics":{}}""");

        await analytics.GetMessagingAsync(
            new MessagingAnalyticsQuery
            {
                Start = Start,
                End = End,
                Granularity = AnalyticsGranularity.Day,
                ProductTypes = [MessageProductType.TemplateMessages, MessageProductType.IncomingMessages],
            },
            TestContext.Current.CancellationToken);

        // 0 and 100, not names.
        Assert.Contains(
            "product_types([0,100])",
            Uri.UnescapeDataString(Assert.Single(handler.Requests).RequestUri!.Query),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Messaging_figures_are_read()
    {
        var (analytics, _) = Create("""
            {
              "analytics": {
                "phone_numbers": ["16505550111"],
                "country_codes": ["US", "BR"],
                "granularity": "DAY",
                "data_points": [
                  {"start": 1543543200, "end": 1543629600, "sent": 196093, "delivered": 179715}
                ]
              },
              "id": "102290129340398"
            }
            """);

        var result = await analytics.GetMessagingAsync(
            new MessagingAnalyticsQuery
            {
                Start = Start,
                End = End,
                Granularity = AnalyticsGranularity.Day,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(["US", "BR"], result.CountryCodes);
        var point = Assert.Single(result.DataPoints);
        Assert.Equal(196093, point.Sent);
        Assert.Equal(179715, point.Delivered);
        // Unix seconds, as numbers rather than as the strings the webhooks use.
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1543543200), point.Start);
    }

    [Fact]
    public async Task Conversation_data_points_are_read_out_of_the_extra_wrapper()
    {
        var (analytics, _) = Create("""
            {
              "conversation_analytics": {
                "data": [{
                  "data_points": [
                    {
                      "start": 1685602800,
                      "end": 1688194800,
                      "conversation": 1558,
                      "phone_number": "15550458206",
                      "country": "US",
                      "conversation_type": "REGULAR",
                      "conversation_direction": "UNKNOWN",
                      "conversation_category": "AUTHENTICATION",
                      "cost": 15.58
                    }
                  ]
                }]
              }
            }
            """);

        var result = await analytics.GetConversationsAsync(
            new ConversationAnalyticsQuery
            {
                Start = Start,
                End = End,
                Granularity = AnalyticsGranularity.Month,
            },
            TestContext.Current.CancellationToken);

        // A `data` array of objects that each hold the data points — one level deeper than the
        // messaging field, for no reason anyone has explained.
        var point = Assert.Single(result.DataPoints);
        Assert.Equal(1558, point.Conversations);
        Assert.Equal(15.58m, point.Cost);
        Assert.Equal(ConversationCategory.Authentication, point.Category);
        Assert.Equal(ConversationType.Regular, point.Type);
        // Meta reports UNKNOWN as an answer, not as a gap.
        Assert.Equal(ConversationDirection.Unknown, point.Direction);
    }

    [Fact]
    public async Task Breakdowns_go_up_as_a_bracketed_array()
    {
        var (analytics, handler) = Create("""{"conversation_analytics":{"data":[]}}""");

        await analytics.GetConversationsAsync(
            new ConversationAnalyticsQuery
            {
                Start = Start,
                End = End,
                Granularity = AnalyticsGranularity.Day,
                Dimensions = [ConversationDimension.ConversationCategory, ConversationDimension.Country],
                Categories = [ConversationCategory.Marketing],
            },
            TestContext.Current.CancellationToken);

        var query = Uri.UnescapeDataString(Assert.Single(handler.Requests).RequestUri!.Query);
        Assert.Contains("""dimensions(["CONVERSATION_CATEGORY","COUNTRY"])""", query, StringComparison.Ordinal);
        Assert.Contains("""conversation_categories(["MARKETING"])""", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_filter_that_was_not_set_is_left_off_entirely()
    {
        var (analytics, handler) = Create("""{"conversation_analytics":{"data":[]}}""");

        await analytics.GetConversationsAsync(
            new ConversationAnalyticsQuery
            {
                Start = Start,
                End = End,
                Granularity = AnalyticsGranularity.Day,
            },
            TestContext.Current.CancellationToken);

        // Meta reads a missing filter as "all of them", so there is nothing to send.
        var query = Assert.Single(handler.Requests).RequestUri!.Query;
        Assert.DoesNotContain("dimensions", query, StringComparison.Ordinal);
        Assert.DoesNotContain("metric_types", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pricing_data_points_carry_the_volume_tier()
    {
        var (analytics, _) = Create("""
            {
              "pricing_analytics": {
                "data": [{
                  "data_points": [
                    {
                      "start": 1749106800,
                      "end": 1749193200,
                      "country": "IN",
                      "tier": "0:750000",
                      "pricing_type": "REGULAR",
                      "pricing_category": "AUTHENTICATION_INTERNATIONAL",
                      "volume": 2,
                      "cost": 4.6
                    },
                    {
                      "start": 1749193200,
                      "end": 1749279600,
                      "country": "IN",
                      "pricing_type": "FREE_CUSTOMER_SERVICE",
                      "pricing_category": "SERVICE",
                      "volume": 2,
                      "cost": 0
                    }
                  ]
                }]
              }
            }
            """);

        var result = await analytics.GetPricingAsync(
            new PricingAnalyticsQuery
            {
                Start = Start,
                End = End,
                Granularity = AnalyticsGranularity.Day,
                Dimensions = [PricingDimension.Tier, PricingDimension.PricingCategory, PricingDimension.Country],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.DataPoints.Count);
        // No webhook reports tiers; this is the only place they show up.
        Assert.Equal("0:750000", result.DataPoints[0].Tier);
        Assert.Equal(PricingCategory.AuthenticationInternational, result.DataPoints[0].Category);
        Assert.Equal(4.6m, result.DataPoints[0].Cost);
        // Absent on the points that were not tiered, rather than zero.
        Assert.Null(result.DataPoints[1].Tier);
        Assert.Equal(PricingType.FreeCustomerService, result.DataPoints[1].Type);
    }

    [Fact]
    public async Task A_pricing_category_this_library_does_not_know_is_kept_as_written()
    {
        var (analytics, _) = Create("""
            {"pricing_analytics":{"data":[{"data_points":[{"pricing_category":"SOMETHING_NEW"}]}]}}
            """);

        var result = await analytics.GetPricingAsync(
            new PricingAnalyticsQuery
            {
                Start = Start,
                End = End,
                Granularity = AnalyticsGranularity.Day,
            },
            TestContext.Current.CancellationToken);

        // Meta has added MARKETING_LITE and REFERRAL_CONVERSION since this list was written,
        // and will add more.
        var point = Assert.Single(result.DataPoints);
        Assert.Equal(PricingCategory.Unknown, point.Category);
        Assert.Equal("SOMETHING_NEW", point.RawCategory);
    }

    [Fact]
    public async Task Template_analytics_is_an_edge_with_ordinary_query_parameters()
    {
        var (analytics, handler) = Create("""{"data":[]}""");

        await analytics.GetTemplatesAsync(
            new TemplateAnalyticsQuery
            {
                Start = Start,
                End = End,
                TemplateIds = ["1421988012088524", "2632273056924580"],
                MetricTypes = [TemplateMetric.Sent, TemplateMetric.Clicked],
            },
            TestContext.Current.CancellationToken);

        var uri = Assert.Single(handler.Requests).RequestUri!;
        Assert.StartsWith(
            "https://graph.facebook.com/v26.0/102290129340398/template_analytics",
            uri.AbsoluteUri,
            StringComparison.Ordinal);

        var query = Uri.UnescapeDataString(uri.Query);
        // Bracketed and unquoted for the ids; a bare comma-separated list for the metrics.
        Assert.Contains("template_ids=[1421988012088524,2632273056924580]", query, StringComparison.Ordinal);
        Assert.Contains("metric_types=SENT,CLICKED", query, StringComparison.Ordinal);
        // The only granularity this one accepts.
        Assert.Contains("granularity=DAILY", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clicks_and_cost_are_read_as_the_lists_they_are()
    {
        var (analytics, _) = Create("""
            {
              "data": [{
                "granularity": "DAILY",
                "product_type": "cloud_api",
                "data_points": [{
                  "template_id": "2632273056924580",
                  "start": 1718064000,
                  "end": 1718150400,
                  "sent": 120,
                  "delivered": 118,
                  "read": 90,
                  "clicked": [
                    {"type": "quick_reply_button", "button_content": "Contact Support", "count": 108},
                    {"type": "unique_url_button", "button_content": "Tell me more", "count": 16}
                  ],
                  "cost": [
                    {"type": "amount_spent", "value": 0.03},
                    {"type": "cost_per_delivered", "value": 0.01}
                  ]
                }]
              }]
            }
            """);

        var result = await analytics.GetTemplatesAsync(
            new TemplateAnalyticsQuery
            {
                Start = Start,
                End = End,
                TemplateIds = ["2632273056924580"],
            },
            TestContext.Current.CancellationToken);

        var point = Assert.Single(result.DataPoints);
        Assert.Equal(120, point.Sent);
        // Neither of these is a number: clicks are counted per button, and cost is reported as
        // several different figures at once.
        Assert.Equal(2, point.Clicked.Count);
        Assert.Equal("Contact Support", point.Clicked[0].ButtonContent);
        Assert.Equal(108, point.Clicked[0].Count);
        Assert.Equal(0.03m, point.Cost[0].Value);
        Assert.Equal("amount_spent", point.Cost[0].Type);
    }

    [Fact]
    public async Task Asking_for_the_account_time_zone_says_which_one_it_was()
    {
        var (analytics, handler) = Create("""
            {"data":[{"waba_timezone":"America/Los_Angeles","granularity":"DAILY","data_points":[]}]}
            """);

        var result = await analytics.GetTemplatesAsync(
            new TemplateAnalyticsQuery
            {
                Start = Start,
                End = End,
                TemplateIds = ["1"],
                UseAccountTimeZone = true,
            },
            TestContext.Current.CancellationToken);

        Assert.Contains(
            "use_waba_timezone=true",
            Assert.Single(handler.Requests).RequestUri!.Query,
            StringComparison.Ordinal);
        Assert.Equal("America/Los_Angeles", result.TimeZone);
    }

    [Fact]
    public async Task An_eleventh_template_never_reaches_Meta()
    {
        var (analytics, handler) = Create("""{"data":[]}""");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            analytics.GetTemplatesAsync(
                new TemplateAnalyticsQuery
                {
                    Start = Start,
                    End = End,
                    TemplateIds = [.. Enumerable.Range(0, 11).Select(i => i.ToString(CultureInfo.InvariantCulture))],
                },
                TestContext.Current.CancellationToken));

        Assert.Contains("10", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_backwards_range_never_reaches_Meta()
    {
        var (analytics, handler) = Create("""{"analytics":{}}""");

        // Meta answers a backwards range with an empty result, which reads exactly like a
        // quiet week.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            analytics.GetMessagingAsync(
                new MessagingAnalyticsQuery
                {
                    Start = End,
                    End = Start,
                    Granularity = AnalyticsGranularity.Day,
                },
                TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Analytics_spend_the_account_allowance()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"analytics":{}}""");
        var time = new FakeTimeProvider();
        var limiter = new RecordingRateLimiter(new InMemoryRateLimiter(time));
        var analytics = new AnalyticsApi(
            new GraphApiClient(
                new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
                new StubCredentialsProvider(Credentials),
                limiter,
                new StaticOptionsMonitor<WhatsAppOptions>(new WhatsAppOptions()),
                time),
            WhatsAppTenant.Default);

        await analytics.GetMessagingAsync(
            new MessagingAnalyticsQuery
            {
                Start = Start,
                End = End,
                Granularity = AnalyticsGranularity.Day,
            },
            TestContext.Current.CancellationToken);

        Assert.Contains(
            limiter.Requested,
            r => r.Scope.Budget == RateLimitBudget.BusinessAccountRequests
                 && r.Scope.Key == "102290129340398");
    }

    [Fact]
    public async Task Reading_analytics_without_a_business_account_id_says_which_setting_is_missing()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"analytics":{}}""");
        var analytics = CreateWith(handler, Credentials with { WhatsAppBusinessAccountId = null });

        var exception = await Assert.ThrowsAsync<WhatsAppConfigurationException>(() =>
            analytics.GetMessagingAsync(
                new MessagingAnalyticsQuery
                {
                    Start = Start,
                    End = End,
                    Granularity = AnalyticsGranularity.Day,
                },
                TestContext.Current.CancellationToken));

        Assert.Contains("WhatsAppBusinessAccountId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Template_analytics_follows_the_cursor_until_the_platform_stops_offering_one()
    {
        // An ordinary edge, so it pages like one. Stopping after the first page would
        // quietly understate every figure — the numbers look plausible and nothing says a
        // second page existed.
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.OK, """
                {"data":[{"granularity":"DAILY","product_type":"CLOUD_API",
                  "data_points":[{"template_id":"1","start":1543543200,"end":1543629600,"sent":10}]}],
                 "paging":{"cursors":{"after":"CURSOR"},"next":"https://graph.facebook.com/next"}}
                """),
            (HttpStatusCode.OK, """
                {"data":[{"granularity":"DAILY",
                  "data_points":[{"template_id":"1","start":1543629600,"end":1543716000,"sent":7}]}],
                 "paging":{"cursors":{"after":"CURSOR"}}}
                """));
        var analytics = CreateWith(handler, Credentials);

        var result = await analytics.GetTemplatesAsync(
            new TemplateAnalyticsQuery { Start = Start, End = End, TemplateIds = ["1"] },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.DataPoints.Count);
        Assert.Equal([10, 7], result.DataPoints.Select(p => p.Sent));
        Assert.Equal("DAILY", result.Granularity);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains(
            "after=CURSOR",
            handler.Requests[1].RequestUri!.Query,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_conversation_breakdown_this_library_does_not_know_is_kept_raw()
    {
        var (analytics, _) = Create("""
            {"conversation_analytics":{"data":[{"data_points":[
              {"start":1543543200,"end":1543629600,"conversation":5,
               "conversation_category":"MARKETING_LITE","conversation_type":"SPONSORED",
               "conversation_direction":"UNKNOWN"}]}]}}
            """);

        var result = await analytics.GetConversationsAsync(
            new ConversationAnalyticsQuery
            {
                Start = Start,
                End = End,
                Granularity = AnalyticsGranularity.Day,
            },
            TestContext.Current.CancellationToken);

        var point = Assert.Single(result.DataPoints);
        // Meta keeps adding to these lists, and without the raw string a billing report can
        // only ever write down "Unknown".
        Assert.Equal(ConversationCategory.Unknown, point.Category);
        Assert.Equal("MARKETING_LITE", point.RawCategory);
        Assert.Equal(ConversationType.Unknown, point.Type);
        Assert.Equal("SPONSORED", point.RawType);
        Assert.Equal("UNKNOWN", point.RawDirection);
    }

    private static (IAnalyticsApi Analytics, StubHttpMessageHandler Handler) Create(string response)
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, response);
        return (CreateWith(handler, Credentials), handler);
    }

    private static IAnalyticsApi CreateWith(
        StubHttpMessageHandler handler,
        WhatsAppCredentials credentials)
    {
        var time = new FakeTimeProvider();

        return new AnalyticsApi(
            new GraphApiClient(
                new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
                new StubCredentialsProvider(credentials),
                new InMemoryRateLimiter(time),
                new StaticOptionsMonitor<WhatsAppOptions>(new WhatsAppOptions()),
                time),
            WhatsAppTenant.Default);
    }
}
