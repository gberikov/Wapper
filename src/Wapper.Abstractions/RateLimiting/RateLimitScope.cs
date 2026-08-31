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
    /// <remarks>
    /// Keyed by tenant, because the application id is not part of the credentials and
    /// cannot be derived from a token. For the usual arrangement — one app, one set of
    /// tenants — that is the same thing.
    /// </remarks>
    public static RateLimitScope ApplicationRequests(string tenant) =>
        new(RateLimitBudget.ApplicationRequests, tenant);

    /// <inheritdoc />
    public override string ToString() => $"{Budget}({Key})";
}
