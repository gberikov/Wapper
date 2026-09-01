using Wapper.Webhooks;

namespace Wapper.Tests.Webhooks;

/// <summary>
/// The scan that says which tenant a delivery is for. It runs on a body nobody has verified
/// yet, so what it must never do is throw, hang, or read more than it was asked for.
/// </summary>
public class WebhookRoutingTests
{
    [Fact]
    public void A_delivery_names_its_account_and_its_number()
    {
        const string Body = """
            {"object":"whatsapp_business_account","entry":[{"id":"102290129340398","changes":[
              {"field":"messages",
               "value":{"messaging_product":"whatsapp",
                "metadata":{"display_phone_number":"15550001111","phone_number_id":"106540352242922"},
                "messages":[{"from":"79000000001","id":"wamid.A","timestamp":"1755000000",
                             "type":"text","text":{"body":"hello"}}]}}]}]}
            """;

        var origin = Assert.Single(WhatsAppWebhookParser.ReadOrigins(Encoding.UTF8.GetBytes(Body)));

        Assert.Equal("106540352242922", origin.PhoneNumberId);
        Assert.Equal("102290129340398", origin.BusinessAccountId);
    }

    [Fact]
    public void An_account_level_delivery_names_only_its_account()
    {
        // A template verdict carries no metadata block at all. Without this the account-level
        // fields would have nothing to route by.
        const string Body = """
            {"object":"whatsapp_business_account","entry":[{"id":"102290129340398","changes":[
              {"field":"message_template_status_update",
               "value":{"event":"APPROVED","message_template_id":1}}]}]}
            """;

        var origin = Assert.Single(WhatsAppWebhookParser.ReadOrigins(Encoding.UTF8.GetBytes(Body)));

        Assert.Null(origin.PhoneNumberId);
        Assert.Equal("102290129340398", origin.BusinessAccountId);
    }

    [Fact]
    public void An_account_named_after_its_changes_is_still_read()
    {
        // JSON property order is not guaranteed, and an entry whose id came last used to be
        // the sort of thing that reads as a missing account.
        const string Body = """
            {"entry":[{"changes":[{"field":"messages",
               "value":{"metadata":{"phone_number_id":"106540352242922"}}}],
              "id":"102290129340398"}],"object":"whatsapp_business_account"}
            """;

        var origin = Assert.Single(WhatsAppWebhookParser.ReadOrigins(Encoding.UTF8.GetBytes(Body)));

        Assert.Equal("106540352242922", origin.PhoneNumberId);
        Assert.Equal("102290129340398", origin.BusinessAccountId);
    }

    [Fact]
    public void Every_entry_and_number_in_a_delivery_is_reported()
    {
        // The parser's own documentation says one delivery carries events "for more than one
        // phone number". Missing the second is how a delivery gets verified against the wrong
        // tenant's secret.
        const string Body = """
            {"object":"whatsapp_business_account","entry":[
              {"id":"acme","changes":[
                {"field":"messages","value":{"metadata":{"phone_number_id":"111"}}},
                {"field":"messages","value":{"metadata":{"phone_number_id":"222"}}}]},
              {"id":"globex","changes":[
                {"field":"messages","value":{"metadata":{"phone_number_id":"333"}}}]}]}
            """;

        var origins = WhatsAppWebhookParser.ReadOrigins(Encoding.UTF8.GetBytes(Body));

        Assert.Equal(3, origins.Count);
        Assert.Equal(new WhatsAppWebhookOrigin("111", "acme"), origins[0]);
        Assert.Equal(new WhatsAppWebhookOrigin("222", "acme"), origins[1]);
        Assert.Equal(new WhatsAppWebhookOrigin("333", "globex"), origins[2]);
    }

    [Fact]
    public void One_number_repeated_across_changes_is_reported_once()
    {
        const string Body = """
            {"object":"whatsapp_business_account","entry":[{"id":"acme","changes":[
              {"field":"messages","value":{"metadata":{"phone_number_id":"111"}}},
              {"field":"messages","value":{"metadata":{"phone_number_id":"111"}}}]}]}
            """;

        Assert.Single(WhatsAppWebhookParser.ReadOrigins(Encoding.UTF8.GetBytes(Body)));
    }

    [Theory]
    // Not JSON at all.
    [InlineData("not json")]
    // JSON, but truncated halfway through.
    [InlineData("""{"object":"whatsapp_business_account","entry":[{"id":"W","chan""")]
    // No entries to route by.
    [InlineData("""{"object":"whatsapp_business_account"}""")]
    [InlineData("""{"object":"whatsapp_business_account","entry":[]}""")]
    // The entry array is not an array.
    [InlineData("""{"entry":"nonsense"}""")]
    public void A_body_with_nothing_to_route_by_reports_nothing_rather_than_throwing(string body)
    {
        // Unlike Parse, this runs before the signature has been checked, so it runs on
        // anything at all. Refusing is the caller's job; throwing here would turn a crafted
        // body into a 500.
        Assert.Empty(WhatsAppWebhookParser.ReadOrigins(Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public void A_body_nested_past_all_reason_reports_nothing_rather_than_throwing()
    {
        var body = """{"entry":[{"id":"W","changes":""" + new string('[', 200) + new string(']', 200) + "}]}";

        Assert.Empty(WhatsAppWebhookParser.ReadOrigins(Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public void An_identifier_longer_than_any_meta_issues_is_not_read_at_all()
    {
        // This runs before the signature has been checked, so it runs on anything. A routing
        // field is fifteen digits; one a hundred kilobytes long is somebody seeing what the
        // scan will hold on to, and what the log line that names it will print.
        var body = """
            {"object":"whatsapp_business_account","entry":[{"id":"ACCOUNT","changes":[
              {"field":"messages","value":{"metadata":{"phone_number_id":"NUMBER"}}}]}]}
            """
            .Replace("ACCOUNT", new string('9', 100_000), StringComparison.Ordinal)
            .Replace("NUMBER", new string('8', 100_000), StringComparison.Ordinal);

        var origin = Assert.Single(WhatsAppWebhookParser.ReadOrigins(Encoding.UTF8.GetBytes(body)));

        // Read as absent, so the delivery resolves to no tenant and is refused.
        Assert.Null(origin.PhoneNumberId);
        Assert.Empty(origin.BusinessAccountId);
    }

    [Fact]
    public void The_delivery_key_is_the_digest_of_the_body_as_it_arrived()
    {
        const string Body = """{"object":"whatsapp_business_account","entry":[{"id":"W"}]}""";

        var key = WhatsAppWebhookParser.DeliveryKey(Body);

        // 64 hex characters, which is what goes in a unique index.
        Assert.Equal(64, key.Length);
        Assert.Equal(key, WhatsAppWebhookParser.DeliveryKey(Encoding.UTF8.GetBytes(Body)));
        Assert.Matches("^[0-9a-f]{64}$", key);
    }

    [Fact]
    public void Two_deliveries_that_differ_anywhere_get_different_keys()
    {
        const string First = """{"object":"whatsapp_business_account","entry":[{"id":"W"}]}""";
        const string Second = """{"object":"whatsapp_business_account","entry":[{"id":"X"}]}""";

        Assert.NotEqual(
            WhatsAppWebhookParser.DeliveryKey(First),
            WhatsAppWebhookParser.DeliveryKey(Second));

        // Same content, different bytes. The key is over what arrived, so a re-serialized
        // body is a different delivery as far as it is concerned — which is why it has to be
        // taken before anything reformats it.
        Assert.NotEqual(
            WhatsAppWebhookParser.DeliveryKey(First),
            WhatsAppWebhookParser.DeliveryKey(First.Replace(":[", ": [", StringComparison.Ordinal)));
    }
}
