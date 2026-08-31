using System.Globalization;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using Wapper.Internal;

namespace Wapper.Analytics;

/// <summary>What one tenant's WhatsApp Business Account has been sending, and paying.</summary>
/// <remarks>
/// Three of the four are field expansions on the account node rather than endpoints of their
/// own — <c>?fields=analytics.start(…).end(…)</c> — and the fourth, template analytics, is an
/// ordinary edge with ordinary query parameters. Meta is not consistent about this and neither
/// can this class be.
/// </remarks>
internal sealed class AnalyticsApi(GraphApiClient client, string tenant) : IAnalyticsApi
{
    /// <summary>Meta refuses more than this many templates on one read.</summary>
    private const int MaxTemplates = 10;

    public async Task<MessagingAnalytics> GetMessagingAsync(
        MessagingAnalyticsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        GuardRange(query.Start, query.End);

        var filters = new StringBuilder()
            .Append(Range(query.Start, query.End))
            // DAY and MONTH here. The conversation and pricing fields want DAILY and MONTHLY
            // for the same thing and reject these.
            .Append(Filter("granularity", query.Granularity switch
            {
                AnalyticsGranularity.HalfHour => "HALF_HOUR",
                AnalyticsGranularity.Month => "MONTH",
                _ => "DAY",
            }))
            .Append(Strings("phone_numbers", query.PhoneNumbers))
            .Append(Strings("country_codes", query.CountryCodes));

        if (query.ProductTypes is { Count: > 0 } productTypes)
        {
            // Numbers rather than names: 0, 2 and 100.
            filters.Append(Filter(
                "product_types",
                Literal(productTypes.Select(type => type switch
                {
                    MessageProductType.TemplateMessages => "0",
                    MessageProductType.NonTemplateMessages => "2",
                    MessageProductType.IncomingMessages => "100",
                    _ => throw new ArgumentOutOfRangeException(nameof(query), type, null),
                }))));
        }

        var response = await ReadAsync(
                "analytics",
                filters.ToString(),
                WhatsAppJsonContext.Default.MessagingAnalyticsResponse,
                cancellationToken)
            .ConfigureAwait(false);

        var payload = response.Analytics;

        return new MessagingAnalytics
        {
            PhoneNumbers = payload?.PhoneNumbers ?? [],
            CountryCodes = payload?.CountryCodes ?? [],
            Granularity = payload?.Granularity,
            DataPoints = [.. (payload?.DataPoints ?? []).Select(point => new MessagingDataPoint
            {
                Start = FromUnix(point.Start),
                End = FromUnix(point.End),
                Sent = point.Sent,
                Delivered = point.Delivered,
            })],
        };
    }

    public async Task<ConversationAnalytics> GetConversationsAsync(
        ConversationAnalyticsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        GuardRange(query.Start, query.End);

        var filters = new StringBuilder()
            .Append(Range(query.Start, query.End))
            .Append(Filter("granularity", LongGranularity(query.Granularity)))
            .Append(Strings("phone_numbers", query.PhoneNumbers))
            .Append(Strings("metric_types", query.MetricTypes?.Select(m => m.ToString().ToUpperInvariant())))
            .Append(Strings("conversation_categories", query.Categories?.Select(ToWire)))
            .Append(Strings("conversation_types", query.Types?.Select(ToWire)))
            .Append(Strings("conversation_directions", query.Directions?.Select(ToWire)))
            .Append(Strings("dimensions", query.Dimensions?.Select(ToWire)));

        var response = await ReadAsync(
                "conversation_analytics",
                filters.ToString(),
                WhatsAppJsonContext.Default.ConversationAnalyticsResponse,
                cancellationToken)
            .ConfigureAwait(false);

        return new ConversationAnalytics
        {
            DataPoints = [.. Flatten(response.ConversationAnalytics).Select(point => new ConversationDataPoint
            {
                Start = FromUnix(point.Start),
                End = FromUnix(point.End),
                Conversations = point.Conversation,
                Cost = point.Cost,
                PhoneNumber = point.PhoneNumber,
                Country = point.Country,
                Category = point.ConversationCategory?.ToUpperInvariant() switch
                {
                    "AUTHENTICATION" => ConversationCategory.Authentication,
                    "MARKETING" => ConversationCategory.Marketing,
                    "SERVICE" => ConversationCategory.Service,
                    "UTILITY" => ConversationCategory.Utility,
                    _ => ConversationCategory.Unknown,
                },
                Type = point.ConversationType?.ToUpperInvariant() switch
                {
                    "FREE_ENTRY_POINT" => ConversationType.FreeEntryPoint,
                    "FREE_TIER" => ConversationType.FreeTier,
                    "REGULAR" => ConversationType.Regular,
                    _ => ConversationType.Unknown,
                },
                Direction = point.ConversationDirection?.ToUpperInvariant() switch
                {
                    "BUSINESS_INITIATED" => ConversationDirection.BusinessInitiated,
                    "USER_INITIATED" => ConversationDirection.UserInitiated,
                    _ => ConversationDirection.Unknown,
                },
            })],
        };
    }

    public async Task<PricingAnalytics> GetPricingAsync(
        PricingAnalyticsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        GuardRange(query.Start, query.End);

        var filters = new StringBuilder()
            .Append(Range(query.Start, query.End))
            .Append(Filter("granularity", LongGranularity(query.Granularity)))
            .Append(Strings("phone_numbers", query.PhoneNumbers))
            .Append(Strings("country_codes", query.CountryCodes))
            .Append(Strings("metric_types", query.MetricTypes?.Select(m => m.ToString().ToUpperInvariant())))
            .Append(Strings("pricing_categories", query.Categories?.Select(ToWire)))
            .Append(Strings("pricing_types", query.Types?.Select(ToWire)))
            .Append(Strings("dimensions", query.Dimensions?.Select(ToWire)));

        var response = await ReadAsync(
                "pricing_analytics",
                filters.ToString(),
                WhatsAppJsonContext.Default.PricingAnalyticsResponse,
                cancellationToken)
            .ConfigureAwait(false);

        return new PricingAnalytics
        {
            DataPoints = [.. Flatten(response.PricingAnalytics).Select(point => new PricingDataPoint
            {
                Start = FromUnix(point.Start),
                End = FromUnix(point.End),
                Volume = point.Volume,
                Cost = point.Cost,
                PhoneNumber = point.PhoneNumber,
                Country = point.Country,
                Tier = point.Tier,
                Category = point.PricingCategory?.ToUpperInvariant() switch
                {
                    "AUTHENTICATION" => PricingCategory.Authentication,
                    "AUTHENTICATION_INTERNATIONAL" => PricingCategory.AuthenticationInternational,
                    "MARKETING" => PricingCategory.Marketing,
                    "MARKETING_LITE" => PricingCategory.MarketingLite,
                    "SERVICE" => PricingCategory.Service,
                    "UTILITY" => PricingCategory.Utility,
                    "REFERRAL_CONVERSION" => PricingCategory.ReferralConversion,
                    _ => PricingCategory.Unknown,
                },
                RawCategory = point.PricingCategory,
                Type = point.PricingType?.ToUpperInvariant() switch
                {
                    "FREE_CUSTOMER_SERVICE" => PricingType.FreeCustomerService,
                    "FREE_ENTRY_POINT" => PricingType.FreeEntryPoint,
                    "REGULAR" => PricingType.Regular,
                    _ => PricingType.Unknown,
                },
            })],
        };
    }

    public async Task<TemplateAnalytics> GetTemplatesAsync(
        TemplateAnalyticsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        GuardRange(query.Start, query.End);

        if (query.TemplateIds is not { Count: > 0 })
        {
            throw new ArgumentException(
                "Template analytics are read per template, so at least one id is needed.",
                nameof(query));
        }

        if (query.TemplateIds.Count > MaxTemplates)
        {
            throw new ArgumentException(
                $"The Cloud API reads at most {MaxTemplates} templates at a time, and this call " +
                $"passed {query.TemplateIds.Count}. Read them in batches.",
                nameof(query));
        }

        var parameters = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"start={query.Start.ToUnixTimeSeconds()}")
            .Append(CultureInfo.InvariantCulture, $"&end={query.End.ToUnixTimeSeconds()}")
            // The only value this one accepts.
            .Append("&granularity=DAILY")
            .Append("&template_ids=")
            .Append(Literal(query.TemplateIds));

        if (query.MetricTypes is { Count: > 0 } metrics)
        {
            // A bare comma-separated list here, not the bracketed array the field expansions
            // take.
            parameters.Append("&metric_types=").Append(Uri.EscapeDataString(
                string.Join(',', metrics.Select(m => m.ToString().ToUpperInvariant()))));
        }

        if (query.ProductType is { } productType)
        {
            parameters.Append("&product_type=").Append(productType switch
            {
                TemplateProductType.MarketingMessagesApi => "MARKETING_MESSAGES_API_FOR_WHATSAPP",
                _ => "CLOUD_API",
            });
        }

        if (query.UseAccountTimeZone)
        {
            parameters.Append("&use_waba_timezone=true");
        }

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);
        var accountId = GraphApiClient.RequireBusinessAccount(credentials);

        var response = await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Get,
                    Path = $"{accountId}/template_analytics?{parameters}",
                    Kind = GraphCallKind.Management,
                },
                WhatsAppJsonContext.Default.TemplateAnalyticsResponse,
                cancellationToken)
            .ConfigureAwait(false);

        var payload = response.Data is [var first, ..] ? first : null;

        return new TemplateAnalytics
        {
            Granularity = payload?.Granularity,
            ProductType = payload?.ProductType,
            TimeZone = payload?.WabaTimezone,
            DataPoints = [.. (payload?.DataPoints ?? []).Select(point => new TemplateDataPoint
            {
                TemplateId = point.TemplateId,
                Start = FromUnix(point.Start),
                End = FromUnix(point.End),
                Sent = point.Sent,
                Delivered = point.Delivered,
                Read = point.Read,
                Clicked = [.. (point.Clicked ?? []).Select(click => new TemplateButtonClicks
                {
                    Type = click.Type,
                    ButtonContent = click.ButtonContent,
                    Count = click.Count,
                })],
                Cost = [.. (point.Cost ?? []).Select(cost => new TemplateCost
                {
                    Type = cost.Type,
                    Value = cost.Value,
                })],
            })],
        };
    }

    private async Task<TResponse> ReadAsync<TResponse>(
        string field,
        string filters,
        JsonTypeInfo<TResponse> typeInfo,
        CancellationToken cancellationToken)
    {
        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);
        var accountId = GraphApiClient.RequireBusinessAccount(credentials);

        return await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Get,
                    // Not an endpoint: the account node, read with one field expanded.
                    Path = $"{accountId}?fields={field}{filters}",
                    Kind = GraphCallKind.Management,
                },
                typeInfo,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Pulls the data points out of the extra layer the conversation and pricing fields wrap
    /// them in.
    /// </summary>
    private static IEnumerable<TDataPoint> Flatten<TDataPoint>(AnalyticsDataWrapper<TDataPoint>? wrapper) =>
        (wrapper?.Data ?? []).SelectMany(set => set.DataPoints ?? []);

    private static string Range(DateTimeOffset start, DateTimeOffset end) =>
        Filter("start", start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)) +
        Filter("end", end.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

    private static string Filter(string name, string value) => $".{name}({value})";

    /// <summary>An array of quoted strings, escaped so the brackets survive the query.</summary>
    private static string Strings(string name, IEnumerable<string>? values)
    {
        var list = values?.ToList();

        return list is not { Count: > 0 }
            ? string.Empty
            : Filter(name, Uri.EscapeDataString(
                $"[{string.Join(',', list.Select(value => $"\"{value}\""))}]"));
    }

    /// <summary>An array of bare values — ids and numbers, which Meta does not want quoted.</summary>
    private static string Literal(IEnumerable<string> values) =>
        Uri.EscapeDataString($"[{string.Join(',', values)}]");

    /// <summary>The spelling the conversation and pricing fields use.</summary>
    private static string LongGranularity(AnalyticsGranularity granularity) => granularity switch
    {
        AnalyticsGranularity.HalfHour => "HALF_HOUR",
        AnalyticsGranularity.Month => "MONTHLY",
        _ => "DAILY",
    };

    private static string ToWire(ConversationCategory category) => category switch
    {
        ConversationCategory.Authentication => "AUTHENTICATION",
        ConversationCategory.Marketing => "MARKETING",
        ConversationCategory.Service => "SERVICE",
        ConversationCategory.Utility => "UTILITY",
        _ => throw new ArgumentException(
            $"{category} is not a conversation category the Cloud API accepts.",
            nameof(category)),
    };

    private static string ToWire(ConversationType type) => type switch
    {
        ConversationType.FreeEntryPoint => "FREE_ENTRY_POINT",
        ConversationType.FreeTier => "FREE_TIER",
        ConversationType.Regular => "REGULAR",
        _ => throw new ArgumentException(
            $"{type} is not a conversation type the Cloud API accepts.",
            nameof(type)),
    };

    private static string ToWire(ConversationDirection direction) => direction switch
    {
        ConversationDirection.BusinessInitiated => "BUSINESS_INITIATED",
        ConversationDirection.UserInitiated => "USER_INITIATED",
        // Unlike everywhere else, UNKNOWN is a value Meta itself reports and accepts: it is
        // what a conversation whose origin could not be worked out is filed under.
        _ => "UNKNOWN",
    };

    private static string ToWire(ConversationDimension dimension) => dimension switch
    {
        ConversationDimension.ConversationCategory => "CONVERSATION_CATEGORY",
        ConversationDimension.ConversationDirection => "CONVERSATION_DIRECTION",
        ConversationDimension.ConversationType => "CONVERSATION_TYPE",
        ConversationDimension.Country => "COUNTRY",
        ConversationDimension.Phone => "PHONE",
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, null),
    };

    private static string ToWire(PricingCategory category) => category switch
    {
        PricingCategory.Authentication => "AUTHENTICATION",
        PricingCategory.AuthenticationInternational => "AUTHENTICATION_INTERNATIONAL",
        PricingCategory.Marketing => "MARKETING",
        PricingCategory.MarketingLite => "MARKETING_LITE",
        PricingCategory.Service => "SERVICE",
        PricingCategory.Utility => "UTILITY",
        PricingCategory.ReferralConversion => "REFERRAL_CONVERSION",
        _ => throw new ArgumentException(
            $"{category} is not a pricing category the Cloud API accepts.",
            nameof(category)),
    };

    private static string ToWire(PricingType type) => type switch
    {
        PricingType.FreeCustomerService => "FREE_CUSTOMER_SERVICE",
        PricingType.FreeEntryPoint => "FREE_ENTRY_POINT",
        PricingType.Regular => "REGULAR",
        _ => throw new ArgumentException(
            $"{type} is not a pricing type the Cloud API accepts.",
            nameof(type)),
    };

    private static string ToWire(PricingDimension dimension) => dimension switch
    {
        PricingDimension.Country => "COUNTRY",
        PricingDimension.Phone => "PHONE",
        PricingDimension.PricingCategory => "PRICING_CATEGORY",
        PricingDimension.PricingType => "PRICING_TYPE",
        PricingDimension.Tier => "TIER",
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, null),
    };

    private static DateTimeOffset FromUnix(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds);

    private static void GuardRange(DateTimeOffset start, DateTimeOffset end)
    {
        // Meta answers a backwards range with an empty result rather than an error, which
        // reads exactly like a quiet week.
        if (end <= start)
        {
            throw new ArgumentException(
                $"The analytics range ends at {end:O}, which is not after it starts at {start:O}.",
                "query");
        }
    }
}
