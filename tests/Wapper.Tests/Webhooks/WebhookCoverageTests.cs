using System.Text.Json;
using Wapper.Webhooks;

namespace Wapper.Tests.Webhooks;

/// <summary>
/// The parts of a delivery that used to be dropped: whole webhook fields with no typed event,
/// a submitted Flow, an order, and the attribution and billing details on a message.
/// </summary>
public class WebhookCoverageTests
{
    [Fact]
    public void A_field_with_no_typed_event_is_reported_rather_than_dropped()
    {
        // Meta has more than twenty webhook fields. Silently dropping one means an account
        // being offboarded, or a customer opting out of marketing, leaves no trace at all.
        const string Body = """
            {"object":"whatsapp_business_account","entry":[{"id":"102290129340398","changes":[
              {"field":"account_update",
               "value":{"event":"DISABLED_UPDATE","ban_info":{"waba_ban_state":"SCHEDULE_FOR_DISABLE"}}}]}]}
            """;

        var unknown = Assert.IsType<UnknownEvent>(Assert.Single(WhatsAppWebhookParser.Parse(Body)));

        Assert.Equal("account_update", unknown.Field);
        Assert.Equal("102290129340398", unknown.BusinessAccountId);

        // The body comes with it, so an application can act on a field this library has not
        // been taught yet without waiting for a release.
        var value = JsonDocument.Parse(unknown.Json).RootElement;
        Assert.Equal("DISABLED_UPDATE", value.GetProperty("event").GetString());
    }

    [Fact]
    public void A_submitted_flow_carries_the_answers_and_the_token_it_was_sent_with()
    {
        const string Body = """
            {"object":"whatsapp_business_account","entry":[{"id":"W","changes":[{"field":"messages",
             "value":{"messaging_product":"whatsapp",
              "metadata":{"display_phone_number":"15550001111","phone_number_id":"106540352242922"},
              "messages":[{"from":"79000000001","id":"wamid.A","timestamp":"1755000000",
                           "type":"interactive",
                           "interactive":{"type":"nfm_reply",
                             "nfm_reply":{"name":"flow","body":"Sent",
                               "response_json":"{\"flow_token\":\"booking-42\",\"seats\":\"4\"}"}}}]}}]}]}
            """;

        var reply = Assert.IsType<FlowReply>(Assert.Single(WhatsAppWebhookParser.Parse(Body)));

        Assert.Equal("flow", reply.Name);
        Assert.Equal("Sent", reply.Body);

        // The shape belongs to the Flow, so it arrives as the document the screens produced.
        var answers = JsonDocument.Parse(reply.ResponseJson).RootElement;
        Assert.Equal("booking-42", answers.GetProperty("flow_token").GetString());
        Assert.Equal("4", answers.GetProperty("seats").GetString());
    }

    [Fact]
    public void An_ad_that_started_the_conversation_is_reported_on_the_first_message()
    {
        // The conversation an ad opens is free of charge, and this is the only place the
        // attribution appears. An application that reports on ad spend reads it here or not
        // at all.
        const string Body = """
            {"object":"whatsapp_business_account","entry":[{"id":"W","changes":[{"field":"messages",
             "value":{"messaging_product":"whatsapp",
              "metadata":{"phone_number_id":"106540352242922"},
              "messages":[{"from":"79000000001","id":"wamid.A","timestamp":"1755000000",
                           "type":"text","text":{"body":"is it in stock?"},
                           "referral":{"source_url":"https://fb.me/ad","source_type":"ad",
                                       "source_id":"12345","headline":"Half price",
                                       "media_type":"image","ctwa_clid":"click-99"}}]}}]}]}
            """;

        var message = Assert.IsType<TextMessage>(Assert.Single(WhatsAppWebhookParser.Parse(Body)));
        var referral = message.Referral!;

        Assert.Equal("ad", referral.SourceType);
        Assert.Equal("12345", referral.SourceId);
        Assert.Equal("Half price", referral.Headline);
        Assert.Equal("click-99", referral.ClickId);
    }

    [Fact]
    public void An_order_carries_its_lines()
    {
        const string Body = """
            {"object":"whatsapp_business_account","entry":[{"id":"W","changes":[{"field":"messages",
             "value":{"messaging_product":"whatsapp",
              "metadata":{"phone_number_id":"106540352242922"},
              "messages":[{"from":"79000000001","id":"wamid.A","timestamp":"1755000000",
                           "type":"order",
                           "order":{"catalog_id":"cat-1","text":"please deliver friday",
                                    "product_items":[{"product_retailer_id":"sku-9","quantity":2,
                                                      "item_price":19.5,"currency":"EUR"}]}}]}}]}]}
            """;

        var order = Assert.IsType<OrderMessage>(Assert.Single(WhatsAppWebhookParser.Parse(Body)));

        Assert.Equal("cat-1", order.CatalogId);
        Assert.Equal("please deliver friday", order.Text);

        var line = Assert.Single(order.Products);
        Assert.Equal("sku-9", line.ProductRetailerId);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(19.5m, line.ItemPrice);
        Assert.Equal("EUR", line.Currency);
    }

    [Fact]
    public void A_quoted_catalogue_item_says_which_product_the_customer_was_looking_at()
    {
        const string Body = """
            {"object":"whatsapp_business_account","entry":[{"id":"W","changes":[{"field":"messages",
             "value":{"messaging_product":"whatsapp",
              "metadata":{"phone_number_id":"106540352242922"},
              "messages":[{"from":"79000000001","id":"wamid.A","timestamp":"1755000000",
                           "type":"text","text":{"body":"does it come in blue?"},
                           "context":{"id":"wamid.B",
                                      "referred_product":{"catalog_id":"cat-1",
                                                          "product_retailer_id":"sku-9"}}}]}}]}]}
            """;

        var message = Assert.IsType<TextMessage>(Assert.Single(WhatsAppWebhookParser.Parse(Body)));

        Assert.Equal("cat-1", message.ReferredProduct!.Value.CatalogId);
        Assert.Equal("sku-9", message.ReferredProduct.Value.ProductRetailerId);
    }

    [Fact]
    public void A_status_carries_the_billing_details_and_whatever_the_send_attached()
    {
        const string Body = """
            {"object":"whatsapp_business_account","entry":[{"id":"W","changes":[{"field":"messages",
             "value":{"messaging_product":"whatsapp",
              "metadata":{"phone_number_id":"106540352242922"},
              "statuses":[{"id":"wamid.A","status":"sent","timestamp":"1755000000",
                           "recipient_id":"79000000001",
                           "biz_opaque_callback_data":"order-4711",
                           "conversation":{"id":"c-1","expiration_timestamp":"1755086400",
                                           "origin":{"type":"marketing"}},
                           "pricing":{"billable":true,"pricing_model":"PMP",
                                      "category":"marketing","type":"regular"}}]}}]}]}
            """;

        var status = Assert.IsType<MessageStatusChanged>(Assert.Single(WhatsAppWebhookParser.Parse(Body)));

        // The whole point: matching a status to your own records without keeping a table of
        // message ids.
        Assert.Equal("order-4711", status.CallbackData);

        Assert.Equal("PMP", status.PricingModel);
        Assert.Equal("regular", status.PricingType);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1755086400),
            status.ConversationExpiresAt);
    }

    [Fact]
    public void A_first_time_customer_asking_for_a_welcome_is_its_own_event()
    {
        const string Body = """
            {"object":"whatsapp_business_account","entry":[{"id":"W","changes":[{"field":"messages",
             "value":{"messaging_product":"whatsapp",
              "metadata":{"phone_number_id":"106540352242922"},
              "messages":[{"from":"79000000001","id":"wamid.A","timestamp":"1755000000",
                           "type":"request_welcome"}]}}]}]}
            """;

        var welcome = Assert.IsType<WelcomeRequest>(Assert.Single(WhatsAppWebhookParser.Parse(Body)));

        Assert.Equal("79000000001", welcome.From);
    }

    [Fact]
    public void A_known_field_shaped_in_an_unknown_way_does_not_fail_the_whole_delivery()
    {
        // The value of a "messages" change arriving as something other than an object. One
        // malformed change must not cost the delivery the changes around it.
        const string Body = """
            {"object":"whatsapp_business_account","entry":[{"id":"W","changes":[
              {"field":"messages","value":"nonsense"},
              {"field":"messages",
               "value":{"messaging_product":"whatsapp",
                "metadata":{"phone_number_id":"106540352242922"},
                "messages":[{"from":"79000000001","id":"wamid.A","timestamp":"1755000000",
                             "type":"text","text":{"body":"hello"}}]}}]}]}
            """;

        var events = WhatsAppWebhookParser.Parse(Body);

        Assert.Equal(2, events.Count);

        // The unreadable change is reported rather than dropped, under the field it came on
        // and with the body it came with. A handler for UnknownEvent is the one place to
        // learn that something is being discarded.
        var unknown = Assert.IsType<UnknownEvent>(events[0]);
        Assert.Equal("messages", unknown.Field);
        Assert.Equal("\"nonsense\"", unknown.Json);

        Assert.Equal("hello", Assert.IsType<TextMessage>(events[1]).Text);
    }

    [Fact]
    public void A_customer_stopping_marketing_messages_is_its_own_event()
    {
        // Taken from Meta's user_preferences reference. The one webhook that changes what an
        // application may send: after this, marketing templates to the customer are accepted
        // and never delivered.
        const string Body = """
            {"object":"whatsapp_business_account","entry":[{"id":"102290129340398","changes":[
              {"field":"user_preferences",
               "value":{"messaging_product":"whatsapp",
                "metadata":{"display_phone_number":"15550783881","phone_number_id":"106540352242922"},
                "contacts":[{"wa_id":"16505551234"}],
                "user_preferences":[{"wa_id":"16505551234",
                                     "detail":"User requested to stop marketing messages",
                                     "category":"marketing_messages",
                                     "value":"stop",
                                     "timestamp":1731705721}]}}]}]}
            """;

        var change = Assert.IsType<MarketingPreferenceChanged>(Assert.Single(WhatsAppWebhookParser.Parse(Body)));

        Assert.Equal("16505551234", change.WhatsAppId);
        Assert.Equal(MarketingPreference.Stop, change.Preference);
        Assert.Equal("stop", change.RawPreference);
        Assert.Equal("106540352242922", change.PhoneNumberId);
        Assert.Equal("102290129340398", change.BusinessAccountId);
        // A number here, unlike every timestamp on a message.
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1731705721), change.Timestamp);
    }

    [Fact]
    public void A_conversation_expiry_that_cannot_be_read_is_absent_rather_than_year_one()
    {
        const string Body = """
            {"object":"whatsapp_business_account","entry":[{"id":"W","changes":[{"field":"messages",
             "value":{"messaging_product":"whatsapp",
              "metadata":{"phone_number_id":"106540352242922"},
              "statuses":[{"id":"wamid.A","status":"sent","timestamp":"1755000000",
                           "recipient_id":"79000000001",
                           "conversation":{"id":"c-1","expiration_timestamp":""}}]}}]}]}
            """;

        var status = Assert.IsType<MessageStatusChanged>(Assert.Single(WhatsAppWebhookParser.Parse(Body)));

        // A caller comparing this against now to decide whether a free-form reply is still
        // allowed would take DateTimeOffset.MinValue for a window that closed long ago.
        Assert.Null(status.ConversationExpiresAt);
    }
}
