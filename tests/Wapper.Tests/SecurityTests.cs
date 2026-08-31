using Wapper.Internal;
using Wapper.RateLimiting;

namespace Wapper.Tests;

/// <summary>
/// The places where getting it wrong hands somebody a working access token, or writes a
/// customer's phone number into a log.
/// </summary>
public class CredentialsPrintingTests
{
    [Fact]
    public void Printing_credentials_does_not_print_the_access_token()
    {
        var credentials = new WhatsAppCredentials
        {
            AccessToken = "EAAJB-super-secret-token",
            PhoneNumberId = "106540352242922",
            WhatsAppBusinessAccountId = "102290129340398",
            AppId = "app-1",
        };

        var printed = credentials.ToString();

        // A record prints every property it has. Logging one — or any object holding one —
        // would otherwise put a token that works into the log.
        Assert.DoesNotContain("EAAJB-super-secret-token", printed, StringComparison.Ordinal);
        Assert.Contains("***", printed, StringComparison.Ordinal);

        // The rest is what makes the line worth logging at all.
        Assert.Contains("106540352242922", printed, StringComparison.Ordinal);
        Assert.Contains("102290129340398", printed, StringComparison.Ordinal);
        Assert.Contains("app-1", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rate_limit_message_does_not_spell_out_the_customer_s_number()
    {
        var exception = new WhatsAppRateLimitedException(
            RateLimitScope.RecipientPair("106540352242922", "79001234567"),
            TimeSpan.FromSeconds(6),
            TimeSpan.FromSeconds(1));

        // The business number identifies which of your own numbers stalled, and is worth
        // keeping. The recipient is personal data that nobody asked to have logged.
        Assert.Contains("106540352242922", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("79001234567", exception.Message, StringComparison.Ordinal);
        Assert.Contains("4567", exception.Message, StringComparison.Ordinal);

        // Still available in full to a caller that deliberately wants it.
        Assert.Equal("106540352242922->79001234567", exception.Scope.Key);
    }

    [Fact]
    public void A_scope_that_is_not_a_pair_is_printed_whole()
    {
        var exception = new WhatsAppRateLimitedException(
            RateLimitScope.PhoneNumberThroughput("106540352242922"),
            TimeSpan.FromSeconds(6),
            TimeSpan.FromSeconds(1));

        Assert.Contains("106540352242922", exception.Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// A media URL is not a Graph API address — Meta returns a host of its own choosing — and the
/// download only works with the bearer token attached. That makes the URL somewhere a token
/// gets sent, so where it points is checked rather than trusted.
/// </summary>
public class MediaDownloadHostTests
{
    [Theory]
    [InlineData("https://lookaside.fbsbx.com/whatsapp_business/attachments/?mid=1")]
    [InlineData("https://mmg.whatsapp.net/v/t62.7118-24/1")]
    [InlineData("https://scontent.xx.fbcdn.net/v/t61/2")]
    [InlineData("https://graph.facebook.com/v26.0/media")]
    public void Meta_s_own_hosts_are_fetched(string url)
    {
        GraphApiClient.GuardFetchUri(new WhatsAppOptions(), new Uri(url));
    }

    [Theory]
    // Somewhere else entirely: a stored or replayed MediaInfo pointing at an attacker would
    // otherwise collect a token good for the whole account.
    [InlineData("https://attacker.example/collect")]
    // A suffix that only looks like one of Meta's. The match has to be on a label boundary.
    [InlineData("https://evilfbcdn.net/x")]
    [InlineData("https://lookaside.fbsbx.com.attacker.example/x")]
    // Meta's own host, but in the clear, which puts the token on the wire for anyone to read.
    [InlineData("http://lookaside.fbsbx.com/x")]
    public void Anywhere_else_is_refused_before_the_token_is_attached(string url)
    {
        var exception = Assert.Throws<WhatsAppException>(() =>
            GraphApiClient.GuardFetchUri(new WhatsAppOptions(), new Uri(url)));

        Assert.Contains("access token", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_base_address_is_always_allowed_so_a_proxy_still_works()
    {
        var options = new WhatsAppOptions { BaseAddress = new Uri("https://graph.internal/proxy/") };

        GraphApiClient.GuardFetchUri(options, new Uri("https://graph.internal/media/1"));
    }

    [Fact]
    public void A_host_Meta_starts_using_can_be_added_without_a_new_package()
    {
        var options = new WhatsAppOptions();
        options.MediaDownloadHosts.Add("newcdn.example");

        GraphApiClient.GuardFetchUri(options, new Uri("https://media.newcdn.example/1"));
    }
}
