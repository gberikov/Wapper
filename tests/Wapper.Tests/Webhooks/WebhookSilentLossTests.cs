using Wapper.Webhooks;

namespace Wapper.Tests.Webhooks;

/// <summary>
/// The contract the README and the docs state: anything the library could not read arrives as
/// <see cref="UnknownEvent"/>, so nothing is lost without trace. These are the places that
/// used to break it — quietly, because Meta always sends the fields they turn on.
/// </summary>
public class WebhookSilentLossTests
{
    private static string Delivery(string field, string value) => $$"""
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "102290129340398",
            "changes": [{ "field": "{{field}}", "value": {{value}} }]
          }]
        }
        """;

    [Fact]
    public void A_status_without_a_recipient_is_reported_rather_than_dropped()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "messages",
            """
            {"messaging_product":"whatsapp",
             "metadata":{"phone_number_id":"106540352242922"},
             "statuses":[{"id":"wamid.OURS","status":"failed","timestamp":"1755000100"}]}
            """));

        var unknown = Assert.IsType<UnknownEvent>(Assert.Single(events));

        Assert.Equal("messages", unknown.Field);
        Assert.Contains("wamid.OURS", unknown.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void One_unreadable_status_does_not_cost_the_ones_beside_it()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "messages",
            """
            {"messaging_product":"whatsapp",
             "metadata":{"phone_number_id":"106540352242922"},
             "statuses":[{"id":"wamid.A","status":"sent","timestamp":"1755000100"},
                         {"id":"wamid.B","status":"read","timestamp":"1755000101",
                          "recipient_id":"79000000001"}]}
            """));

        Assert.Equal(2, events.Count);
        Assert.IsType<UnknownEvent>(events[0]);
        Assert.Equal("wamid.B", Assert.IsType<MessageStatusChanged>(events[1]).MessageId);
    }

    [Fact]
    public void A_field_that_bound_but_yielded_nothing_is_reported()
    {
        // The worst of the failure modes: the change read cleanly, produced no event, and so
        // did not even reach the UnknownEvent the docs promise. There was nowhere left to
        // notice it.
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "messages",
            """
            {"messaging_product":"whatsapp",
             "metadata":{"phone_number_id":"106540352242922"}}
            """));

        var unknown = Assert.IsType<UnknownEvent>(Assert.Single(events));

        Assert.Equal("messages", unknown.Field);
        Assert.Contains("106540352242922", unknown.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_preference_change_with_neither_shape_is_reported()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "user_preferences",
            """
            {"messaging_product":"whatsapp",
             "metadata":{"phone_number_id":"106540352242922"},
             "contacts":[{"wa_id":"16505551234"}]}
            """));

        var unknown = Assert.IsType<UnknownEvent>(Assert.Single(events));

        Assert.Equal("user_preferences", unknown.Field);
    }
}
