using System.Security.Cryptography;
using Wapper.Webhooks;

namespace Wapper.Tests.Webhooks;

public class WebhookSignatureTests
{
    private const string AppSecret = "an-app-secret";

    private static readonly byte[] Body = """{"object":"whatsapp_business_account"}"""u8.ToArray();

    [Fact]
    public void A_signature_Meta_would_send_is_accepted()
    {
        Assert.True(WhatsAppWebhookSignature.IsValid(Body, Sign(Body), AppSecret));
    }

    [Fact]
    public void A_body_altered_by_one_byte_is_rejected()
    {
        var signature = Sign(Body);
        var tampered = """{"object":"whatsapp_business_accounT"}"""u8.ToArray();

        Assert.False(WhatsAppWebhookSignature.IsValid(tampered, signature, AppSecret));
    }

    [Fact]
    public void A_signature_made_with_another_secret_is_rejected()
    {
        Assert.False(WhatsAppWebhookSignature.IsValid(Body, Sign(Body, "someone-elses"), AppSecret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("deadbeef")]
    [InlineData("sha1=deadbeef")]
    [InlineData("sha256=")]
    [InlineData("sha256=nothexatall")]
    [InlineData("sha256=00")]
    public void A_malformed_or_missing_header_is_rejected(string? header)
    {
        // The endpoint is public, so anything that is not a well-formed signature over this
        // exact body has to be turned away.
        Assert.False(WhatsAppWebhookSignature.IsValid(Body, header, AppSecret));
    }

    [Fact]
    public void The_signature_is_read_whatever_the_case_of_the_prefix()
    {
        Assert.True(WhatsAppWebhookSignature.IsValid(
            Body,
            Sign(Body).Replace("sha256=", "SHA256=", StringComparison.Ordinal),
            AppSecret));
    }

    [Fact]
    public void Uppercase_hex_is_accepted()
    {
        Assert.True(WhatsAppWebhookSignature.IsValid(Body, Sign(Body).ToUpperInvariant(), AppSecret));
    }

    [Fact]
    public void The_verify_token_of_the_subscription_handshake_is_compared()
    {
        Assert.True(WhatsAppWebhookSignature.IsVerifyTokenValid("expected", "expected"));
        Assert.False(WhatsAppWebhookSignature.IsVerifyTokenValid("wrong", "expected"));
        Assert.False(WhatsAppWebhookSignature.IsVerifyTokenValid(null, "expected"));
        Assert.False(WhatsAppWebhookSignature.IsVerifyTokenValid("", "expected"));
        // A prefix must not pass, which a length-blind comparison would allow.
        Assert.False(WhatsAppWebhookSignature.IsVerifyTokenValid("expect", "expected"));
    }

    private static string Sign(byte[] body, string secret = AppSecret) =>
        "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();
}
