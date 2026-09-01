namespace Wapper.Analytics;

/// <summary>How wide each data point is.</summary>
/// <remarks>
/// Meta spells these differently depending on which metric is being asked for — <c>DAY</c> and
/// <c>MONTH</c> for messaging, <c>DAILY</c> and <c>MONTHLY</c> for conversations and pricing —
/// and rejects the other spelling. That translation happens here rather than in your code.
/// </remarks>
public enum AnalyticsGranularity
{
    /// <summary>Half an hour.</summary>
    HalfHour,

    /// <summary>A day.</summary>
    Day,

    /// <summary>A month.</summary>
    Month,
}

/// <summary>Which messages to count.</summary>
public enum MessageProductType
{
    /// <summary>Template messages sent to WhatsApp users.</summary>
    TemplateMessages,

    /// <summary>Everything else sent to WhatsApp users.</summary>
    NonTemplateMessages,

    /// <summary>Messages sent by WhatsApp users to the business.</summary>
    IncomingMessages,
}

/// <summary>What to report about conversations.</summary>
public enum ConversationMetric
{
    /// <summary>
    /// Approximate charges, in the account's currency.
    /// </summary>
    /// <remarks>
    /// Not returned at all for an account billed through a Solution Partner. Asking for cost
    /// and nothing else makes such an account answer with an exception rather than a figure.
    /// </remarks>
    Cost,

    /// <summary>How many conversations there were.</summary>
    Conversation,
}

/// <summary>What a conversation was charged as.</summary>
public enum ConversationCategory
{
    /// <summary>A category this library does not know about yet.</summary>
    Unknown,

    /// <summary>One-time passcodes and account verification.</summary>
    Authentication,

    /// <summary>Promotions and offers.</summary>
    Marketing,

    /// <summary>Answering a customer's question.</summary>
    Service,

    /// <summary>Updates about something the customer asked for.</summary>
    Utility,
}

/// <summary>Whether a conversation was billed, and why not when it was not.</summary>
public enum ConversationType
{
    /// <summary>A type this library does not know about yet.</summary>
    Unknown,

    /// <summary>Started from a free entry point, such as an ad that clicks to WhatsApp.</summary>
    FreeEntryPoint,

    /// <summary>Inside the monthly free allowance.</summary>
    FreeTier,

    /// <summary>Everything else, which is to say the billable ones.</summary>
    Regular,
}

/// <summary>Who started the conversation.</summary>
public enum ConversationDirection
{
    /// <summary>Meta could not work it out, which it often cannot.</summary>
    Unknown,

    /// <summary>The business did.</summary>
    BusinessInitiated,

    /// <summary>The customer did.</summary>
    UserInitiated,
}

/// <summary>How to break the conversation figures down.</summary>
/// <remarks>
/// Without any of these the answer is one number per time slice. Each dimension added splits
/// every slice further, and the field it names is only filled in on the data points when it
/// has been asked for.
/// </remarks>
public enum ConversationDimension
{
    /// <summary>Split by what the conversation was charged as.</summary>
    ConversationCategory,

    /// <summary>Split by who started it.</summary>
    ConversationDirection,

    /// <summary>Split by whether it was billable.</summary>
    ConversationType,

    /// <summary>Split by the customer's country.</summary>
    Country,

    /// <summary>Split by business phone number.</summary>
    Phone,
}

/// <summary>What to report about pricing.</summary>
public enum PricingMetric
{
    /// <summary>Approximate charges, in the account's currency.</summary>
    /// <inheritdoc cref="ConversationMetric.Cost" path="/remarks" />
    Cost,

    /// <summary>How many messages were delivered.</summary>
    Volume,
}

/// <summary>Which rate a message was charged at.</summary>
public enum PricingCategory
{
    /// <summary>A category this library does not know about yet.</summary>
    Unknown,

    /// <summary>The authentication rate.</summary>
    Authentication,

    /// <summary>The authentication-international rate.</summary>
    AuthenticationInternational,

    /// <summary>The marketing rate.</summary>
    Marketing,

    /// <summary>The marketing-lite rate.</summary>
    MarketingLite,

    /// <summary>Not charged: non-template messages, and utility inside a service window.</summary>
    Service,

    /// <summary>The utility rate.</summary>
    Utility,

    /// <summary>Received through a free entry point.</summary>
    ReferralConversion,
}

/// <summary>Whether a message was billable.</summary>
public enum PricingType
{
    /// <summary>A type this library does not know about yet.</summary>
    Unknown,

    /// <summary>Free: non-template messages and utility inside a customer service window.</summary>
    FreeCustomerService,

    /// <summary>Free: everything inside a free entry point service window.</summary>
    FreeEntryPoint,

    /// <summary>Billable.</summary>
    Regular,
}

/// <summary>How to break the pricing figures down.</summary>
public enum PricingDimension
{
    /// <summary>Split by the customer's country.</summary>
    Country,

    /// <summary>Split by business phone number.</summary>
    Phone,

    /// <summary>Split by the rate charged.</summary>
    PricingCategory,

    /// <summary>Split by whether it was billable.</summary>
    PricingType,

    /// <summary>
    /// Split by volume tier. Ask for this together with
    /// <see cref="PricingCategory"/> and <see cref="Country"/>, or the tier is not filled in.
    /// </summary>
    Tier,
}

/// <summary>What to report about a template.</summary>
public enum TemplateMetric
{
    /// <summary>Approximate charges.</summary>
    /// <inheritdoc cref="ConversationMetric.Cost" path="/remarks" />
    Cost,

    /// <summary>Button presses. Only for templates categorised as marketing or utility.</summary>
    Clicked,

    /// <summary>Messages that reached a device.</summary>
    Delivered,

    /// <summary>Messages that were opened.</summary>
    Read,

    /// <summary>Messages that went out.</summary>
    Sent,
}

/// <summary>Which product the template was sent through.</summary>
public enum TemplateProductType
{
    /// <summary>The Cloud API. What this library sends through, and the default.</summary>
    CloudApi,

    /// <summary>The Marketing Messages API for WhatsApp.</summary>
    MarketingMessagesApi,
}

/// <summary>A time range and how finely to slice it. Shared by every analytics query.</summary>
public abstract record AnalyticsQuery
{
    /// <summary>Beginning of the range.</summary>
    public required DateTimeOffset Start { get; init; }

    /// <summary>End of the range.</summary>
    public required DateTimeOffset End { get; init; }

    /// <summary>Business phone numbers to include. All of the account's when left unset.</summary>
    public IReadOnlyList<string>? PhoneNumbers { get; init; }
}

/// <summary>What to ask for about messages sent and delivered.</summary>
public sealed record MessagingAnalyticsQuery : AnalyticsQuery
{
    /// <summary>How wide each data point is.</summary>
    public required AnalyticsGranularity Granularity { get; init; }

    /// <summary>Which messages to count. All of them when left unset.</summary>
    public IReadOnlyList<MessageProductType>? ProductTypes { get; init; }

    /// <summary>
    /// Two-letter country codes to include. Every country communicated with when left unset.
    /// </summary>
    public IReadOnlyList<string>? CountryCodes { get; init; }
}

/// <summary>What to ask for about conversations and what they cost.</summary>
public sealed record ConversationAnalyticsQuery : AnalyticsQuery
{
    /// <summary>How wide each data point is.</summary>
    public required AnalyticsGranularity Granularity { get; init; }

    /// <summary>What to report. Everything available when left unset.</summary>
    public IReadOnlyList<ConversationMetric>? MetricTypes { get; init; }

    /// <summary>Which categories to include. All of them when left unset.</summary>
    public IReadOnlyList<ConversationCategory>? Categories { get; init; }

    /// <summary>Which types to include. All of them when left unset.</summary>
    public IReadOnlyList<ConversationType>? Types { get; init; }

    /// <summary>Which directions to include. All of them when left unset.</summary>
    public IReadOnlyList<ConversationDirection>? Directions { get; init; }

    /// <summary>
    /// How to break the figures down. One number per time slice when left unset.
    /// </summary>
    public IReadOnlyList<ConversationDimension>? Dimensions { get; init; }
}

/// <summary>What to ask for about what messages were charged at.</summary>
public sealed record PricingAnalyticsQuery : AnalyticsQuery
{
    /// <summary>How wide each data point is.</summary>
    public required AnalyticsGranularity Granularity { get; init; }

    /// <summary>What to report. Everything available when left unset.</summary>
    public IReadOnlyList<PricingMetric>? MetricTypes { get; init; }

    /// <summary>
    /// Two-letter country codes to include. Every country communicated with when left unset.
    /// </summary>
    public IReadOnlyList<string>? CountryCodes { get; init; }

    /// <summary>Which rates to include. All of them when left unset.</summary>
    public IReadOnlyList<PricingCategory>? Categories { get; init; }

    /// <summary>Which billing types to include. All of them when left unset.</summary>
    public IReadOnlyList<PricingType>? Types { get; init; }

    /// <summary>How to break the figures down. No breakdown when left unset.</summary>
    public IReadOnlyList<PricingDimension>? Dimensions { get; init; }
}

/// <summary>What to ask for about templates.</summary>
/// <remarks>
/// Always daily, and never further back than 90 days — unlike the other three, whose window is
/// a year.
/// </remarks>
public sealed record TemplateAnalyticsQuery
{
    /// <summary>Beginning of the range.</summary>
    public required DateTimeOffset Start { get; init; }

    /// <summary>End of the range.</summary>
    public required DateTimeOffset End { get; init; }

    /// <summary>Which templates. At least one, at most ten.</summary>
    public required IReadOnlyList<string> TemplateIds { get; init; }

    /// <summary>What to report. Everything available when left unset.</summary>
    public IReadOnlyList<TemplateMetric>? MetricTypes { get; init; }

    /// <summary>Which product to count. The Cloud API when left unset.</summary>
    public TemplateProductType? ProductType { get; init; }

    /// <summary>
    /// Whether to slice the days by the account's own time zone rather than by UTC.
    /// </summary>
    public bool UseAccountTimeZone { get; init; }
}

/// <summary>Messages sent and delivered in one time slice.</summary>
public sealed record MessagingDataPoint
{
    /// <summary>Beginning of the slice.</summary>
    public DateTimeOffset Start { get; init; }

    /// <summary>End of the slice.</summary>
    public DateTimeOffset End { get; init; }

    /// <summary>How many went out.</summary>
    public int Sent { get; init; }

    /// <summary>How many arrived.</summary>
    public int Delivered { get; init; }
}

/// <summary>Messages sent and delivered over a range.</summary>
public sealed record MessagingAnalytics
{
    /// <summary>The numbers the figures cover.</summary>
    public IReadOnlyList<string> PhoneNumbers { get; init; } = [];

    /// <summary>The countries the figures cover.</summary>
    public IReadOnlyList<string> CountryCodes { get; init; } = [];

    /// <summary>How wide each slice is, as Meta reports it back.</summary>
    public string? Granularity { get; init; }

    /// <summary>One entry per time slice.</summary>
    public IReadOnlyList<MessagingDataPoint> DataPoints { get; init; } = [];
}

/// <summary>Conversations in one slice of one breakdown.</summary>
/// <remarks>
/// The breakdown fields are only filled in when the matching dimension was asked for.
/// </remarks>
public sealed record ConversationDataPoint
{
    /// <summary>Beginning of the slice.</summary>
    public DateTimeOffset Start { get; init; }

    /// <summary>End of the slice.</summary>
    public DateTimeOffset End { get; init; }

    /// <summary>How many conversations.</summary>
    public int? Conversations { get; init; }

    /// <summary>What they cost, in the account's currency.</summary>
    public decimal? Cost { get; init; }

    /// <summary>Which business phone number, when broken down by phone.</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>Which country, when broken down by country.</summary>
    public string? Country { get; init; }

    /// <summary>What it was charged as, when broken down by category.</summary>
    public ConversationCategory Category { get; init; }

    /// <summary>The category exactly as Meta wrote it. Meta keeps adding to this list.</summary>
    public string? RawCategory { get; init; }

    /// <summary>Whether it was billable, when broken down by type.</summary>
    public ConversationType Type { get; init; }

    /// <summary>The type exactly as Meta wrote it.</summary>
    public string? RawType { get; init; }

    /// <summary>Who started it, when broken down by direction.</summary>
    public ConversationDirection Direction { get; init; }

    /// <summary>The direction exactly as Meta wrote it.</summary>
    public string? RawDirection { get; init; }
}

/// <summary>Conversations over a range.</summary>
public sealed record ConversationAnalytics
{
    /// <summary>One entry per slice per breakdown.</summary>
    public IReadOnlyList<ConversationDataPoint> DataPoints { get; init; } = [];
}

/// <summary>Delivered messages in one slice of one breakdown.</summary>
public sealed record PricingDataPoint
{
    /// <summary>Beginning of the slice.</summary>
    public DateTimeOffset Start { get; init; }

    /// <summary>End of the slice.</summary>
    public DateTimeOffset End { get; init; }

    /// <summary>How many messages were delivered.</summary>
    public int? Volume { get; init; }

    /// <summary>What they cost, in the account's currency.</summary>
    public decimal? Cost { get; init; }

    /// <summary>Which business phone number, when broken down by phone.</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>Which country, when broken down by country.</summary>
    public string? Country { get; init; }

    /// <summary>
    /// The volume tier the rate came from, written <c>lower:upper</c> — for example
    /// <c>0:750000</c>, or <c>0:MAX</c> for a category that is not tiered.
    /// </summary>
    public string? Tier { get; init; }

    /// <summary>The rate charged.</summary>
    public PricingCategory Category { get; init; }

    /// <summary>The rate exactly as Meta wrote it. Meta keeps adding to this list.</summary>
    public string? RawCategory { get; init; }

    /// <summary>Whether the messages were billable.</summary>
    public PricingType Type { get; init; }

    /// <summary>The type exactly as Meta wrote it.</summary>
    public string? RawType { get; init; }
}

/// <summary>Delivered messages and what they were charged at, over a range.</summary>
public sealed record PricingAnalytics
{
    /// <summary>One entry per slice per breakdown.</summary>
    public IReadOnlyList<PricingDataPoint> DataPoints { get; init; } = [];
}

/// <summary>Presses of one button of one template.</summary>
public sealed record TemplateButtonClicks
{
    /// <summary>The kind of button, for example <c>quick_reply_button</c>.</summary>
    public string? Type { get; init; }

    /// <summary>The label on it, which is how one button is told from another.</summary>
    public string? ButtonContent { get; init; }

    /// <summary>How many presses.</summary>
    public int Count { get; init; }
}

/// <summary>One of the several figures Meta reports under the heading of cost.</summary>
public sealed record TemplateCost
{
    /// <summary>
    /// Which figure: <c>amount_spent</c>, <c>cost_per_delivered</c> or
    /// <c>cost_per_url_button_click</c>.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>Its value, in the account's currency.</summary>
    public decimal Value { get; init; }
}

/// <summary>One template on one day.</summary>
public sealed record TemplateDataPoint
{
    /// <summary>Which template.</summary>
    public string? TemplateId { get; init; }

    /// <summary>Beginning of the day.</summary>
    public DateTimeOffset Start { get; init; }

    /// <summary>End of the day.</summary>
    public DateTimeOffset End { get; init; }

    /// <summary>How many went out.</summary>
    public int? Sent { get; init; }

    /// <summary>How many arrived.</summary>
    public int? Delivered { get; init; }

    /// <summary>How many were opened.</summary>
    public int? Read { get; init; }

    /// <summary>
    /// Presses, one entry per button.
    /// </summary>
    /// <remarks>
    /// Not a number: Meta reports it per button, and only for templates categorised as
    /// marketing or utility.
    /// </remarks>
    public IReadOnlyList<TemplateButtonClicks> Clicked { get; init; } = [];

    /// <summary>
    /// Cost, one entry per figure.
    /// </summary>
    /// <remarks>Not a number either — amount spent, cost per delivery, cost per click.</remarks>
    public IReadOnlyList<TemplateCost> Cost { get; init; } = [];
}

/// <summary>How templates performed over a range.</summary>
public sealed record TemplateAnalytics
{
    /// <summary>How wide each slice is, as Meta reports it back. Always daily.</summary>
    public string? Granularity { get; init; }

    /// <summary>Which product the figures are for.</summary>
    public string? ProductType { get; init; }

    /// <summary>The account's time zone, when the days were sliced by it.</summary>
    public string? TimeZone { get; init; }

    /// <summary>One entry per template per day.</summary>
    public IReadOnlyList<TemplateDataPoint> DataPoints { get; init; } = [];
}
