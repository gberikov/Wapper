using Wapper.Webhooks;

namespace Wapper.Tests.Webhooks;

public class WebhookParserTests
{
    private static string Delivery(string value) => $$"""
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "WABA_ID",
            "changes": [{
              "field": "messages",
              "value": {
                "messaging_product": "whatsapp",
                "metadata": {
                  "display_phone_number": "15550001111",
                  "phone_number_id": "106540352242922"
                },
                {{value}}
              }
            }]
          }]
        }
        """;

    [Fact]
    public void A_text_message_is_parsed()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            "contacts": [{"profile": {"name": "Ada"}, "wa_id": "79000000001"}],
            "messages": [{
              "from": "79000000001",
              "id": "wamid.HBgL",
              "timestamp": "1755000000",
              "type": "text",
              "text": {"body": "hello there"}
            }]
            """));

        var message = Assert.IsType<TextMessage>(Assert.Single(events));

        Assert.Equal("hello there", message.Text);
        Assert.Equal("79000000001", message.From);
        Assert.Equal("wamid.HBgL", message.Id);
        Assert.Equal("Ada", message.ProfileName);
        Assert.Equal("106540352242922", message.PhoneNumberId);
        Assert.Equal("15550001111", message.DisplayPhoneNumber);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1755000000), message.Timestamp);
    }

    [Fact]
    public void A_quoted_reply_carries_the_message_it_answers()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            "messages": [{
              "from": "79000000001",
              "id": "wamid.HBgL",
              "timestamp": "1755000000",
              "type": "text",
              "context": {"from": "15550001111", "id": "wamid.OURS"},
              "text": {"body": "yes"}
            }]
            """));

        Assert.Equal("wamid.OURS", Assert.IsType<TextMessage>(Assert.Single(events)).ReplyToMessageId);
    }

    [Theory]
    [InlineData("image", IncomingMediaKind.Image)]
    [InlineData("audio", IncomingMediaKind.Audio)]
    [InlineData("video", IncomingMediaKind.Video)]
    [InlineData("document", IncomingMediaKind.Document)]
    [InlineData("sticker", IncomingMediaKind.Sticker)]
    public void Media_messages_are_parsed(string type, IncomingMediaKind expected)
    {
        var events = WhatsAppWebhookParser.Parse(Delivery($$"""
            "messages": [{
              "from": "79000000001",
              "id": "wamid.HBgL",
              "timestamp": "1755000000",
              "type": "{{type}}",
              "{{type}}": {"id": "media-1", "mime_type": "application/octet-stream", "sha256": "abc"}
            }]
            """));

        var message = Assert.IsType<MediaMessage>(Assert.Single(events));

        Assert.Equal(expected, message.Kind);
        // Only the id arrives, never the bytes, and it expires after seven days.
        Assert.Equal("media-1", message.MediaId);
        Assert.Equal("abc", message.Sha256);
    }

    [Fact]
    public void A_voice_note_is_told_apart_from_an_audio_file()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            "messages": [{
              "from": "79000000001",
              "id": "wamid.HBgL",
              "timestamp": "1755000000",
              "type": "audio",
              "audio": {"id": "media-1", "mime_type": "audio/ogg; codecs=opus", "voice": true}
            }]
            """));

        Assert.True(Assert.IsType<MediaMessage>(Assert.Single(events)).IsVoice);
    }

    [Fact]
    public void A_reply_button_is_parsed()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            "messages": [{
              "from": "79000000001",
              "id": "wamid.HBgL",
              "timestamp": "1755000000",
              "type": "interactive",
              "interactive": {
                "type": "button_reply",
                "button_reply": {"id": "ship:1", "title": "Send it"}
              }
            }]
            """));

        var reply = Assert.IsType<InteractiveReply>(Assert.Single(events));

        Assert.Equal(InteractiveReplyKind.Button, reply.Kind);
        Assert.Equal("ship:1", reply.ReplyId);
        Assert.Equal("Send it", reply.Title);
    }

    [Fact]
    public void A_list_choice_is_parsed()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            "messages": [{
              "from": "79000000001",
              "id": "wamid.HBgL",
              "timestamp": "1755000000",
              "type": "interactive",
              "interactive": {
                "type": "list_reply",
                "list_reply": {"id": "9", "title": "09:00", "description": "One hour"}
              }
            }]
            """));

        var reply = Assert.IsType<InteractiveReply>(Assert.Single(events));

        Assert.Equal(InteractiveReplyKind.List, reply.Kind);
        Assert.Equal("One hour", reply.Description);
    }

    [Fact]
    public void A_template_quick_reply_is_not_an_interactive_reply()
    {
        // It arrives as its own message type carrying the payload the template attached,
        // rather than a button id.
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            "messages": [{
              "from": "79000000001",
              "id": "wamid.HBgL",
              "timestamp": "1755000000",
              "type": "button",
              "button": {"payload": "STOP", "text": "Unsubscribe"}
            }]
            """));

        var reply = Assert.IsType<TemplateButtonReply>(Assert.Single(events));

        Assert.Equal("STOP", reply.Payload);
        Assert.Equal("Unsubscribe", reply.Text);
    }

    [Fact]
    public void A_reaction_and_its_removal_are_told_apart()
    {
        var added = WhatsAppWebhookParser.Parse(Delivery("""
            "messages": [{
              "from": "79000000001", "id": "wamid.A", "timestamp": "1755000000",
              "type": "reaction", "reaction": {"message_id": "wamid.OURS", "emoji": "👍"}
            }]
            """));
        var removed = WhatsAppWebhookParser.Parse(Delivery("""
            "messages": [{
              "from": "79000000001", "id": "wamid.B", "timestamp": "1755000000",
              "type": "reaction", "reaction": {"message_id": "wamid.OURS", "emoji": ""}
            }]
            """));

        Assert.False(Assert.IsType<ReactionMessage>(Assert.Single(added)).IsRemoved);
        Assert.True(Assert.IsType<ReactionMessage>(Assert.Single(removed)).IsRemoved);
    }

    [Fact]
    public void A_location_is_parsed()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            "messages": [{
              "from": "79000000001", "id": "wamid.A", "timestamp": "1755000000",
              "type": "location",
              "location": {"latitude": 51.5007, "longitude": -0.1246, "name": "Big Ben", "address": "London"}
            }]
            """));

        var location = Assert.IsType<LocationMessage>(Assert.Single(events)).Location;

        Assert.Equal(51.5007, location.Latitude);
        Assert.Equal("Big Ben", location.Name);
    }

    [Fact]
    public void A_contact_card_is_parsed()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            "messages": [{
              "from": "79000000001", "id": "wamid.A", "timestamp": "1755000000",
              "type": "contacts",
              "contacts": [{
                "name": {"formatted_name": "Ada Lovelace", "first_name": "Ada"},
                "phones": [{"phone": "+44 20 7946 0000", "type": "WORK", "wa_id": "442079460000"}],
                "birthday": "1815-12-10"
              }]
            }]
            """));

        var contact = Assert.Single(Assert.IsType<ContactsMessage>(Assert.Single(events)).Contacts);

        Assert.Equal("Ada Lovelace", contact.Name.FormattedName);
        Assert.Equal("442079460000", Assert.Single(contact.Phones).WhatsAppId);
        Assert.Equal(new DateOnly(1815, 12, 10), contact.Birthday);
    }

    [Fact]
    public void A_delivery_status_is_parsed()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            "statuses": [{
              "id": "wamid.OURS",
              "status": "delivered",
              "timestamp": "1755000100",
              "recipient_id": "79000000001",
              "conversation": {"id": "conv-1", "origin": {"type": "utility"}},
              "pricing": {"billable": true, "category": "utility"}
            }]
            """));

        var status = Assert.IsType<MessageStatusChanged>(Assert.Single(events));

        Assert.Equal(MessageDeliveryStatus.Delivered, status.Status);
        Assert.Equal("wamid.OURS", status.MessageId);
        Assert.Equal("conv-1", status.ConversationId);
        Assert.Equal("utility", status.ConversationCategory);
        Assert.True(status.Billable);
    }

    [Fact]
    public void A_failed_status_carries_the_reason()
    {
        // A send call only reports that Meta accepted the message. This is the only place a
        // later failure is ever reported.
        var events = WhatsAppWebhookParser.Parse(Delivery($$"""
            "statuses": [{
              "id": "wamid.OURS",
              "status": "failed",
              "timestamp": "1755000100",
              "recipient_id": "79000000001",
              "errors": [{
                "code": {{WhatsAppErrorCodes.UserOptedOut}},
                "title": "Marketing message not delivered",
                "error_data": {"details": "This message was not delivered."}
              }]
            }]
            """));

        var status = Assert.IsType<MessageStatusChanged>(Assert.Single(events));

        Assert.Equal(MessageDeliveryStatus.Failed, status.Status);
        Assert.Equal(WhatsAppErrorCodes.UserOptedOut, Assert.Single(status.Errors).Code);
    }

    [Fact]
    public void An_unknown_status_is_kept_rather_than_dropped()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            "statuses": [{
              "id": "wamid.OURS", "status": "warp_speed", "timestamp": "1755000100",
              "recipient_id": "79000000001"
            }]
            """));

        var status = Assert.IsType<MessageStatusChanged>(Assert.Single(events));

        Assert.Equal(MessageDeliveryStatus.Unknown, status.Status);
        // Meta adds statuses without warning, so the raw value survives for the application
        // to look at.
        Assert.Equal("warp_speed", status.RawStatus);
    }

    [Fact]
    public void An_unknown_message_type_is_surfaced_rather_than_dropped()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            "messages": [{
              "from": "79000000001", "id": "wamid.A", "timestamp": "1755000000",
              "type": "hologram",
              "hologram": {"whatever": true}
            }]
            """));

        Assert.Equal("hologram", Assert.IsType<UnsupportedMessage>(Assert.Single(events)).Type);
    }

    [Fact]
    public void An_out_of_band_error_is_parsed()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery($$"""
            "errors": [{"code": {{WhatsAppErrorCodes.MessageThroughputReached}}, "message": "Rate limit hit"}]
            """));

        Assert.Equal(
            WhatsAppErrorCodes.MessageThroughputReached,
            Assert.IsType<WebhookError>(Assert.Single(events)).Error.Code);
    }

    [Fact]
    public void One_delivery_can_carry_several_events()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            "messages": [
              {"from": "79000000001", "id": "wamid.A", "timestamp": "1755000000",
               "type": "text", "text": {"body": "one"}},
              {"from": "79000000002", "id": "wamid.B", "timestamp": "1755000001",
               "type": "text", "text": {"body": "two"}}
            ],
            "statuses": [{"id": "wamid.OURS", "status": "read", "timestamp": "1755000002",
                          "recipient_id": "79000000003"}]
            """));

        Assert.Equal(3, events.Count);
        Assert.Equal(2, events.OfType<TextMessage>().Count());
        Assert.Single(events.OfType<MessageStatusChanged>());
    }

    [Fact]
    public void A_change_without_a_phone_number_is_ignored()
    {
        // Nothing else in the payload identifies the account, so an event without it cannot
        // be attributed to a tenant and must not be handed to a handler as though it could.
        var events = WhatsAppWebhookParser.Parse("""
            {"object":"whatsapp_business_account","entry":[{"id":"W","changes":[{"field":"messages",
             "value":{"messaging_product":"whatsapp",
              "messages":[{"from":"79000000001","id":"wamid.A","timestamp":"1","type":"text",
                           "text":{"body":"hi"}}]}}]}]}
            """);

        Assert.Empty(events);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void A_body_that_is_not_a_delivery_is_reported(string body)
    {
        Assert.Throws<WhatsAppException>(() => WhatsAppWebhookParser.Parse(body));
    }
}
