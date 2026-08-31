namespace Wapper.RateLimiting;

/// <summary>
/// One budget applied to one subject: a phone number, a conversation, a business account.
/// </summary>
/// <param name="Budget">Which budget is being spent.</param>
/// <param name="Key">
/// What it is being spent on. A phone number id, a sender and recipient pair, a business
/// account id, or a tenant name — whatever identifies the thing Meta counts against.
/// </param>
public readonly record struct RateLimitScope(RateLimitBudget Budget, string Key)
{
    /// <summary>Messages per second for one business phone number.</summary>
    public static RateLimitScope PhoneNumberThroughput(string phoneNumberId) =>
        new(RateLimitBudget.PhoneNumberThroughput, phoneNumberId);

    /// <summary>Messages from one business phone number to one recipient.</summary>
    public static RateLimitScope RecipientPair(string phoneNumberId, string recipient) =>
        new(RateLimitBudget.RecipientPair, $"{phoneNumberId}->{recipient}");

    /// <summary>Management requests against one WhatsApp Business Account.</summary>
    public static RateLimitScope BusinessAccountRequests(string businessAccountId) =>
        new(RateLimitBudget.BusinessAccountRequests, businessAccountId);

    /// <summary>The platform-wide budget of the application.</summary>
    /// <param name="application">
    /// The Meta app id, when the credentials carry one, and the tenant name otherwise. Meta
    /// counts this budget per app, so several tenants sharing one app have to share one
    /// scope — holding back only the tenant that discovered the block would leave the others
    /// hammering it, and Meta counts rejected calls too.
    /// </param>
    public static RateLimitScope ApplicationRequests(string application) =>
        new(RateLimitBudget.ApplicationRequests, application);

    /// <inheritdoc />
    public override string ToString() => $"{Budget}({Key})";

    /// <summary>
    /// The key with a recipient's number reduced to its last four digits.
    /// </summary>
    /// <remarks>
    /// Used in exception messages, which end up in logs. A pair scope is keyed by the
    /// customer's phone number, and that is personal data nobody asked to have logged.
    /// </remarks>
    internal string RedactedKey
    {
        get
        {
            var separator = Key.IndexOf("->", StringComparison.Ordinal);

            if (separator < 0)
            {
                return Key;
            }

            var recipient = Key.AsSpan(separator + 2);

            return recipient.Length <= 4
                ? Key
                : string.Concat(Key.AsSpan(0, separator + 2), "…", recipient[^4..]);
        }
    }
}
