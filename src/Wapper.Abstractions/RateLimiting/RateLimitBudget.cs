namespace Wapper.RateLimiting;

/// <summary>
/// The independent budgets the Cloud API enforces. They have different keys, different
/// windows and different error codes, so they are paced separately: exhausting one must
/// not stall traffic governed by another.
/// </summary>
public enum RateLimitBudget
{
    /// <summary>
    /// Messages per second for one business phone number: 80 by default, 1000 after Meta
    /// upgrades the number automatically. Reported as error
    /// <see cref="WhatsAppErrorCodes.MessageThroughputReached"/>.
    /// </summary>
    PhoneNumberThroughput,

    /// <summary>
    /// Messages from one sender to one recipient: one per six seconds, with a burst
    /// allowance of 45 borrowed from the following minutes. Reported as error
    /// <see cref="WhatsAppErrorCodes.PairRateLimitReached"/>.
    /// </summary>
    /// <remarks>
    /// Keyed per recipient on purpose. Hitting it for one conversation says nothing about
    /// any other, so a queue must never stall on it.
    /// </remarks>
    RecipientPair,

    /// <summary>
    /// Management requests per hour for one WhatsApp Business Account: 200, or 5000 once
    /// the account has a registered phone number. Reported as error
    /// <see cref="WhatsAppErrorCodes.BusinessAccountRateLimitReached"/>.
    /// </summary>
    BusinessAccountRequests,

    /// <summary>
    /// The platform-wide budget of the whole application. Reported as error
    /// <see cref="WhatsAppErrorCodes.ApplicationRequestLimitReached"/>.
    /// </summary>
    /// <remarks>
    /// Meta computes it as <c>200 × daily active users</c> and does not publish the number,
    /// so it cannot be paced ahead of time. It is only ever penalised after the fact, from
    /// the error itself or from the <c>X-App-Usage</c> header. Keyed by Meta app id, so
    /// every tenant of a multi-tenant host backs off together.
    /// </remarks>
    ApplicationRequests,
}
