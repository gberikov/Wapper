using Wapper.RateLimiting;

namespace Wapper;

/// <summary>
/// What a Cloud API error says about the call that caused it.
/// </summary>
/// <remarks>
/// Named in the Cloud API's own terms, not in any application's. Whether a failed recipient
/// should be dropped from a list, a campaign paused or an operator paged is a decision for
/// the caller; which of these five things Meta actually said is not, and it is the part only
/// Meta knows.
/// </remarks>
public enum WhatsAppFailureKind
{
    /// <summary>
    /// Meta named a code this library has no rule for, and did not mark it transient.
    /// </summary>
    /// <remarks>
    /// Meta adds codes without warning. Treat one of these the way an unexpected failure
    /// deserves — log it with <see cref="WhatsAppError.TraceId"/> and look at it — rather
    /// than folding it into any of the outcomes below.
    /// </remarks>
    Unknown,

    /// <summary>Something on Meta's side went wrong. The same call may well succeed.</summary>
    Transient,

    /// <summary>
    /// A budget is exhausted. <see cref="WhatsAppFailure.Budget"/> names which one, when Meta
    /// named it.
    /// </summary>
    /// <remarks>
    /// Not all of them clear by waiting a few seconds: a spam restriction lifts only as
    /// quality recovers, and the per-user marketing limit needs a day.
    /// <see cref="WhatsAppFailure.CanRetry"/> is what separates the two.
    /// </remarks>
    RateLimited,

    /// <summary>
    /// This recipient will not receive this message, however often it is sent. Every other
    /// recipient is unaffected.
    /// </summary>
    /// <remarks>
    /// The number is not on WhatsApp, the handset has been unreachable for too long, or the
    /// customer opted out. Nothing about the account or the message is wrong.
    /// </remarks>
    RecipientUnreachable,

    /// <summary>
    /// The account or its credentials cannot send at all. Every recipient fails the same way
    /// until a human fixes it.
    /// </summary>
    /// <remarks>
    /// An unpaid invoice, a locked account, an expired token, a number that was never
    /// registered. The recipients in flight when this starts are not bad numbers, and marking
    /// them as such is how one billing problem turns into a ruined contact list.
    /// </remarks>
    AccountBlocked,

    /// <summary>
    /// The call will never be accepted as it stands. The message, the template or the
    /// parameters have to change.
    /// </summary>
    /// <remarks>
    /// A template whose values do not fit it, a free-form message outside the 24-hour window,
    /// a malformed parameter. Sending the same thing again to the same recipient produces the
    /// same error; sending something else may not.
    /// </remarks>
    RequestRejected,
}

/// <summary>
/// What Meta said, reduced to the decision a caller has to make.
/// </summary>
/// <param name="Kind">Which kind of failure this is.</param>
/// <param name="CanRetry">
/// Whether sending the same call again may succeed. False whenever repeating it is pointless
/// or actively harmful — Meta counts rejected calls against several of these budgets.
/// </param>
/// <param name="Budget">
/// The budget that was exhausted, when Meta's code names one, and <see langword="null"/>
/// otherwise.
/// </param>
/// <remarks>
/// This is the same table the client's own retry logic runs on, so what a caller sees and
/// what the client did cannot drift apart.
/// </remarks>
public readonly record struct WhatsAppFailure(
    WhatsAppFailureKind Kind,
    bool CanRetry,
    RateLimitBudget? Budget = null);

/// <summary>Reading a Cloud API error.</summary>
public static class WhatsAppErrorExtensions
{
    /// <summary>
    /// What this error means: whether to retry, whether the recipient is reachable at all,
    /// and whether the problem is the account rather than the message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Works on a <see cref="WhatsAppError"/> rather than on an exception on purpose. A send
    /// that Meta accepts and then fails to deliver reports its code on the webhook, in
    /// <c>MessageStatusChanged.Errors</c>, where there is no exception to catch and the same
    /// decision still has to be made.
    /// </para>
    /// <para>
    /// Pure and offline, so it can be run over a list of failed recipients, or in a test,
    /// without touching the network.
    /// </para>
    /// </remarks>
    public static WhatsAppFailure Classify(this WhatsAppError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Code switch
        {
            // Paced budgets. Each one is held back while the client backs off, and each has
            // its own key: exhausting one must not stall traffic governed by another.
            WhatsAppErrorCodes.MessageThroughputReached =>
                new(WhatsAppFailureKind.RateLimited, true, RateLimitBudget.PhoneNumberThroughput),

            // Only this conversation is affected. Holding back the phone number as well would
            // stall every other recipient for no reason.
            WhatsAppErrorCodes.PairRateLimitReached =>
                new(WhatsAppFailureKind.RateLimited, true, RateLimitBudget.RecipientPair),

            WhatsAppErrorCodes.BusinessAccountRateLimitReached =>
                new(WhatsAppFailureKind.RateLimited, true, RateLimitBudget.BusinessAccountRequests),

            // Meta is explicit that calls made while blocked are still counted and push the
            // recovery further out, so this one has to stop the application, not just slow it.
            WhatsAppErrorCodes.ApplicationRequestLimitReached =>
                new(WhatsAppFailureKind.RateLimited, true, RateLimitBudget.ApplicationRequests),

            // Limits that waiting a few seconds does not clear. A spam restriction lifts as
            // quality recovers, the per-user marketing limit needs a day and burns delivery
            // metrics if hammered, a Flow stays throttled for an hour, and a blocked
            // registration is blocked for 72 hours whatever the client does.
            WhatsAppErrorCodes.SpamRateLimitReached
                or WhatsAppErrorCodes.PerUserMarketingLimitReached
                or WhatsAppErrorCodes.FlowThrottled
                or WhatsAppErrorCodes.RegistrationLimitReached
                or WhatsAppErrorCodes.TooManyPinGuesses
                or WhatsAppErrorCodes.PinGuessedTooFast =>
                new(WhatsAppFailureKind.RateLimited, false),

            WhatsAppErrorCodes.TemporarilyUnavailable
                or WhatsAppErrorCodes.ServiceUnavailable
                or WhatsAppErrorCodes.ServerUnavailable
                // Raised while a phone number is being upgraded to higher throughput, which
                // Meta documents as lasting up to a minute.
                or WhatsAppErrorCodes.MaintenanceMode =>
                new(WhatsAppFailureKind.Transient, true),

            // Nothing about this recipient changes on a retry: the number is not on WhatsApp,
            // the handset has been gone too long, the customer opted out of marketing, or the
            // number is the sender's own.
            WhatsAppErrorCodes.MessageUndeliverable
                or WhatsAppErrorCodes.UserOptedOut
                or WhatsAppErrorCodes.SenderAndRecipientMatch =>
                new(WhatsAppFailureKind.RecipientUnreachable, false),

            // The account, not the message and not the recipient. Billing, a policy lock, a
            // dead token, a number that is not registered — all of them fail every send
            // identically until someone fixes the account, and none of them says anything
            // about the people being written to.
            WhatsAppErrorCodes.BusinessEligibilityPaymentIssue
                or WhatsAppErrorCodes.AccountLocked
                or WhatsAppErrorCodes.InvalidAccessToken
                or WhatsAppErrorCodes.PermissionDenied
                or WhatsAppErrorCodes.PermissionError
                or WhatsAppErrorCodes.RegistrationCertificateMismatch
                or WhatsAppErrorCodes.TwoStepPinMismatch
                or WhatsAppErrorCodes.PhoneNumberNotVerified
                or WhatsAppErrorCodes.PhoneNumberNotRegistered =>
                new(WhatsAppFailureKind.AccountBlocked, false),

            // The call as composed will never be accepted. A template that does not fit its
            // parameters will not fit them a second later; a paused or disabled template needs
            // a human; the 24-hour window does not reopen on a retry, and only a template gets
            // through once it has closed; a Flow stays blocked while its endpoint is unhealthy.
            WhatsAppErrorCodes.InvalidParameter
                or WhatsAppErrorCodes.ReEngagementRequired
                or WhatsAppErrorCodes.UnsupportedMessageType
                or WhatsAppErrorCodes.TemplateParameterCountMismatch
                or WhatsAppErrorCodes.TemplateDoesNotExist
                or WhatsAppErrorCodes.TemplateTextTooLong
                or WhatsAppErrorCodes.TemplateFormatCharacterPolicyViolated
                or WhatsAppErrorCodes.TemplateParameterFormatMismatch
                or WhatsAppErrorCodes.TemplatePaused
                or WhatsAppErrorCodes.TemplateDisabled
                or WhatsAppErrorCodes.FlowBlocked =>
                new(WhatsAppFailureKind.RequestRejected, false),

            // Meta sets is_transient on the failures it considers worth repeating. Trust it
            // for anything not named above, and say so plainly when it does not.
            _ => error.IsTransient
                ? new(WhatsAppFailureKind.Transient, true)
                : new WhatsAppFailure(WhatsAppFailureKind.Unknown, false),
        };
    }
}
