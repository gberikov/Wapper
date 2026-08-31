namespace Wapper.Analytics;

/// <summary>
/// What the account has been sending, and what it has been charged for.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these needs <see cref="WhatsAppCredentials.WhatsAppBusinessAccountId"/> and
/// spends the account's hourly management allowance, which the client paces for you.
/// </para>
/// <para>
/// Figures are approximate and will not tie out exactly against an invoice. Nothing older than
/// a year can be asked for, and for templates nothing older than 90 days.
/// </para>
/// <para>
/// Cost is not reported at all for an account billed through a Solution Partner. Asking for
/// cost and nothing else makes such an account answer with an explanation instead of a figure.
/// </para>
/// </remarks>
public interface IAnalyticsApi
{
    /// <summary>How many messages went out, and how many arrived.</summary>
    Task<MessagingAnalytics> GetMessagingAsync(
        MessagingAnalyticsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many conversations there were and what they cost, optionally broken down.
    /// </summary>
    /// <remarks>
    /// Without <see cref="ConversationAnalyticsQuery.Dimensions"/> the answer is one number per
    /// time slice; each dimension splits every slice further, and only the fields whose
    /// dimension was asked for are filled in on the data points.
    /// </remarks>
    Task<ConversationAnalytics> GetConversationsAsync(
        ConversationAnalyticsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many messages were delivered and at which rate, optionally broken down.
    /// </summary>
    /// <remarks>
    /// This is where volume tiers are visible: ask for
    /// <see cref="PricingDimension.Tier"/> alongside
    /// <see cref="PricingDimension.PricingCategory"/> and
    /// <see cref="PricingDimension.Country"/>, and the data points carry the tier the rate came
    /// from. No webhook reports tiers.
    /// </remarks>
    Task<PricingAnalytics> GetPricingAsync(
        PricingAnalyticsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How individual templates performed — sent, delivered, read, and buttons pressed.
    /// </summary>
    /// <remarks>
    /// Ten templates at a time, daily granularity only, and at most 90 days back. Button
    /// presses are only counted for templates categorised as marketing or utility.
    /// </remarks>
    Task<TemplateAnalytics> GetTemplatesAsync(
        TemplateAnalyticsQuery query,
        CancellationToken cancellationToken = default);
}
