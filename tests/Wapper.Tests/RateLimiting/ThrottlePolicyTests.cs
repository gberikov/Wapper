using Wapper.RateLimiting;

namespace Wapper.Tests.RateLimiting;

public class ThrottlePolicyTests
{
    [Theory]
    [InlineData(WhatsAppErrorCodes.MessageThroughputReached, RateLimitBudget.PhoneNumberThroughput)]
    [InlineData(WhatsAppErrorCodes.PairRateLimitReached, RateLimitBudget.RecipientPair)]
    [InlineData(WhatsAppErrorCodes.BusinessAccountRateLimitReached, RateLimitBudget.BusinessAccountRequests)]
    [InlineData(WhatsAppErrorCodes.ApplicationRequestLimitReached, RateLimitBudget.ApplicationRequests)]
    public void A_rate_limit_error_holds_back_the_budget_it_names(int code, RateLimitBudget expected)
    {
        Assert.True(ThrottlePolicy.ShouldRetry(new WhatsAppError { Code = code }, out var budget));
        Assert.Equal(expected, budget);
    }

    [Fact]
    public void The_pair_limit_does_not_hold_back_the_whole_phone_number()
    {
        // Only one conversation is affected. Holding the number back would stall every other
        // recipient for no reason.
        ThrottlePolicy.ShouldRetry(
            new WhatsAppError { Code = WhatsAppErrorCodes.PairRateLimitReached },
            out var budget);

        Assert.NotEqual(RateLimitBudget.PhoneNumberThroughput, budget);
    }

    [Theory]
    [InlineData(WhatsAppErrorCodes.TemporarilyUnavailable)]
    [InlineData(WhatsAppErrorCodes.ServiceUnavailable)]
    [InlineData(WhatsAppErrorCodes.ServerUnavailable)]
    [InlineData(WhatsAppErrorCodes.MaintenanceMode)]
    public void A_transient_server_failure_is_retried_without_holding_a_budget(int code)
    {
        Assert.True(ThrottlePolicy.ShouldRetry(new WhatsAppError { Code = code }, out var budget));
        Assert.Null(budget);
    }

    [Theory]
    // Retrying these is worse than failing: the spam and marketing limits lift only when the
    // content or the wait changes, an opted-out user never wants the message, and a blocked
    // registration stays blocked for 72 hours whatever the client does.
    [InlineData(WhatsAppErrorCodes.SpamRateLimitReached)]
    [InlineData(WhatsAppErrorCodes.PerUserMarketingLimitReached)]
    [InlineData(WhatsAppErrorCodes.UserOptedOut)]
    [InlineData(WhatsAppErrorCodes.RegistrationLimitReached)]
    public void A_limit_that_retrying_cannot_clear_is_not_retried(int code)
    {
        Assert.False(ThrottlePolicy.ShouldRetry(new WhatsAppError { Code = code }, out var budget));
        Assert.Null(budget);
    }

    [Fact]
    public void An_unknown_code_is_retried_only_when_Meta_calls_it_transient()
    {
        Assert.False(ThrottlePolicy.ShouldRetry(new WhatsAppError { Code = 999999 }, out _));
        Assert.True(ThrottlePolicy.ShouldRetry(
            new WhatsAppError { Code = 999999, IsTransient = true },
            out _));
    }

    [Theory]
    // The only backoff formula Meta publishes: 4^X seconds, X starting at zero.
    [InlineData(0, 1)]
    [InlineData(1, 4)]
    [InlineData(2, 16)]
    [InlineData(3, 64)]
    [InlineData(4, 256)]
    public void Backoff_follows_the_documented_formula(int attempt, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), ThrottlePolicy.Backoff(attempt));
    }

    [Fact]
    public void Backoff_stops_growing_so_it_cannot_run_away()
    {
        Assert.Equal(ThrottlePolicy.Backoff(5), ThrottlePolicy.Backoff(50));
    }
}
