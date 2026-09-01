using Wapper.RateLimiting;

namespace Wapper.Tests;

/// <summary>
/// What a code means is Meta's knowledge, not the caller's. The incident this came out of:
/// <c>131042</c> — an unpaid invoice — was swept up by a "4xx is hopeless" rule, and one
/// billing problem marked 434 live contacts as unreachable in nine minutes.
/// </summary>
public class ErrorClassificationTests
{
    [Theory]
    [InlineData(WhatsAppErrorCodes.ServiceUnavailable, WhatsAppFailureKind.Transient)]
    [InlineData(WhatsAppErrorCodes.MessageThroughputReached, WhatsAppFailureKind.RateLimited)]
    [InlineData(WhatsAppErrorCodes.MessageUndeliverable, WhatsAppFailureKind.RecipientUnreachable)]
    [InlineData(WhatsAppErrorCodes.BusinessEligibilityPaymentIssue, WhatsAppFailureKind.AccountBlocked)]
    [InlineData(WhatsAppErrorCodes.TemplateParameterCountMismatch, WhatsAppFailureKind.RequestRejected)]
    public void Each_outcome_has_a_code_that_means_it(int code, WhatsAppFailureKind expected) =>
        Assert.Equal(expected, new WhatsAppError { Code = code }.Classify().Kind);

    [Fact]
    public void An_unpaid_invoice_is_the_account_and_not_the_recipient()
    {
        // The whole point of the classification. Every recipient in flight fails identically
        // while the invoice is unpaid, and none of them is a bad number.
        var failure = new WhatsAppError
        {
            Code = WhatsAppErrorCodes.BusinessEligibilityPaymentIssue,
        }.Classify();

        Assert.Equal(WhatsAppFailureKind.AccountBlocked, failure.Kind);
        Assert.NotEqual(WhatsAppFailureKind.RecipientUnreachable, failure.Kind);
        Assert.False(failure.CanRetry);
    }

    [Fact]
    public void An_opted_out_customer_is_an_unreachable_recipient()
    {
        // Declared in WhatsAppErrorCodes since the first release and used in no decision at
        // all until now: a customer who opted out of marketing is exactly the hopeless
        // recipient this is for.
        var failure = new WhatsAppError { Code = WhatsAppErrorCodes.UserOptedOut }.Classify();

        Assert.Equal(WhatsAppFailureKind.RecipientUnreachable, failure.Kind);
        Assert.False(failure.CanRetry);
    }

    [Fact]
    public void A_code_nobody_here_knows_is_not_guessed_at()
    {
        var failure = new WhatsAppError { Code = 999999 }.Classify();

        Assert.Equal(WhatsAppFailureKind.Unknown, failure.Kind);
        Assert.False(failure.CanRetry);
        Assert.Null(failure.Budget);
    }

    [Fact]
    public void A_code_nobody_here_knows_is_retried_when_Meta_calls_it_transient()
    {
        // is_transient is Meta's own word for "worth repeating", and it is all there is to go
        // on for a code invented last week.
        var failure = new WhatsAppError { Code = 999999, IsTransient = true }.Classify();

        Assert.Equal(WhatsAppFailureKind.Transient, failure.Kind);
        Assert.True(failure.CanRetry);
    }

    [Theory]
    [InlineData(WhatsAppErrorCodes.MessageThroughputReached, RateLimitBudget.PhoneNumberThroughput)]
    [InlineData(WhatsAppErrorCodes.PairRateLimitReached, RateLimitBudget.RecipientPair)]
    [InlineData(WhatsAppErrorCodes.BusinessAccountRateLimitReached, RateLimitBudget.BusinessAccountRequests)]
    [InlineData(WhatsAppErrorCodes.ApplicationRequestLimitReached, RateLimitBudget.ApplicationRequests)]
    public void A_paced_limit_names_its_budget_and_is_worth_retrying(int code, RateLimitBudget expected)
    {
        var failure = new WhatsAppError { Code = code }.Classify();

        Assert.Equal(WhatsAppFailureKind.RateLimited, failure.Kind);
        Assert.Equal(expected, failure.Budget);
        Assert.True(failure.CanRetry);
    }

    [Theory]
    // A limit is not always something to wait out: the spam restriction lifts as quality
    // recovers, and the per-user marketing limit needs a day.
    [InlineData(WhatsAppErrorCodes.SpamRateLimitReached)]
    [InlineData(WhatsAppErrorCodes.PerUserMarketingLimitReached)]
    public void A_limit_that_waiting_a_moment_cannot_clear_says_so(int code)
    {
        var failure = new WhatsAppError { Code = code }.Classify();

        Assert.Equal(WhatsAppFailureKind.RateLimited, failure.Kind);
        Assert.False(failure.CanRetry);
    }

    [Theory]
    [InlineData(WhatsAppErrorCodes.MessageThroughputReached)]
    [InlineData(WhatsAppErrorCodes.PerUserMarketingLimitReached)]
    [InlineData(WhatsAppErrorCodes.BusinessEligibilityPaymentIssue)]
    [InlineData(WhatsAppErrorCodes.ServiceUnavailable)]
    [InlineData(999999)]
    public void The_client_retries_on_the_same_table_the_caller_reads(int code)
    {
        // One table, not two. If these ever disagree, a caller reading the classification and
        // the client deciding to retry have started telling different stories about the same
        // error.
        var error = new WhatsAppError { Code = code };
        var failure = error.Classify();

        Assert.Equal(failure.CanRetry, ThrottlePolicy.ShouldRetry(error, out var budget));
        Assert.Equal(failure.Budget, budget);
    }
}
