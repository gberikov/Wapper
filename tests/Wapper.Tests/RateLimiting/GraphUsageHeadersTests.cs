using Wapper.Internal;

namespace Wapper.Tests.RateLimiting;

public class GraphUsageHeadersTests
{
    [Fact]
    public void App_usage_header_is_read()
    {
        // The sample from Meta's rate limiting reference.
        using var response = WithHeader(
            GraphUsageHeaders.AppUsageHeader,
            """{"call_count":28,"total_time":25,"total_cputime":25}""");

        var reading = GraphUsageHeaders.ReadAppUsage(response);

        Assert.Equal(28, reading.HighestPercent);
        Assert.Equal(TimeSpan.Zero, reading.TimeToRegainAccess);
        Assert.False(reading.IsOverThreshold(100));
    }

    [Fact]
    public void Business_use_case_header_is_read_and_the_worst_entry_wins()
    {
        // Keyed by business object id, each value a list. Meta documents up to 32 objects in
        // one header and does not say which one is ours.
        using var response = WithHeader(
            GraphUsageHeaders.BusinessUseCaseUsageHeader,
            """
            {
              "66782684": [
                {"type":"ads_management","call_count":95,"total_cputime":20,"total_time":20,
                 "estimated_time_to_regain_access":0}
              ],
              "10153848260347724": [
                {"type":"pages","call_count":100,"total_cputime":23,"total_time":23,
                 "estimated_time_to_regain_access":19}
              ]
            }
            """);

        var reading = GraphUsageHeaders.ReadBusinessUseCaseUsage(response);

        Assert.Equal(100, reading.HighestPercent);
        Assert.Equal(TimeSpan.FromMinutes(19), reading.TimeToRegainAccess);
        Assert.True(reading.IsOverThreshold(100));
    }

    [Fact]
    public void An_unrecognised_limit_type_is_still_read()
    {
        // Meta's documented type values omit WhatsApp entirely, even though the business
        // management API is governed by these limits. Matching against an enum would drop
        // exactly the entries this library cares about.
        using var response = WithHeader(
            GraphUsageHeaders.BusinessUseCaseUsageHeader,
            """{"1":[{"type":"whatsapp_business_account","call_count":97,"total_time":0,"total_cputime":0}]}""");

        Assert.Equal(97, GraphUsageHeaders.ReadBusinessUseCaseUsage(response).HighestPercent);
    }

    [Fact]
    public void A_missing_header_reads_as_nothing()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        // Meta only sends these once enough calls have been made, so absence is normal.
        Assert.Equal(UsageReading.None, GraphUsageHeaders.ReadAppUsage(response));
        Assert.Equal(UsageReading.None, GraphUsageHeaders.ReadBusinessUseCaseUsage(response));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"call_count\":")]
    [InlineData("[]")]
    public void A_malformed_header_is_ignored_rather_than_thrown(string raw)
    {
        // These headers are diagnostics. One arriving mangled must not fail a call that
        // otherwise succeeded.
        using var response = WithHeader(GraphUsageHeaders.AppUsageHeader, raw);

        Assert.Equal(UsageReading.None, GraphUsageHeaders.ReadAppUsage(response));
    }

    [Fact]
    public void A_time_to_regain_access_counts_as_over_the_threshold_whatever_the_percentages_say()
    {
        var reading = new UsageReading(0, TimeSpan.FromMinutes(19));

        Assert.True(reading.IsOverThreshold(100));
    }

    private static HttpResponseMessage WithHeader(string name, string value)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }
}
