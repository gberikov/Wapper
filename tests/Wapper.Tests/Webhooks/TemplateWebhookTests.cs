using Wapper.Templates;
using Wapper.Webhooks;

namespace Wapper.Tests.Webhooks;

/// <summary>
/// Template events are the awkward ones: they belong to the account rather than to a phone
/// number, and carry no metadata block at all.
/// </summary>
public class TemplateWebhookTests
{
    private static string Delivery(string field, string value) => $$"""
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "102290129340398",
            "time": 1755000000,
            "changes": [{ "field": "{{field}}", "value": {{value}} }]
          }]
        }
        """;

    [Fact]
    public void An_approval_is_parsed()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "message_template_status_update",
            """
            {
              "event": "APPROVED",
              "message_template_id": 1387372356726668,
              "message_template_name": "order_confirmation",
              "message_template_language": "en_US",
              "reason": "NONE"
            }
            """));

        var change = Assert.IsType<TemplateStatusChanged>(Assert.Single(events));

        Assert.Equal(TemplateStatus.Approved, change.Status);
        // Sent as a number, unlike every other id in the payload.
        Assert.Equal("1387372356726668", change.TemplateId);
        Assert.Equal("order_confirmation", change.TemplateName);
        Assert.Equal("en_US", change.TemplateLanguage);
        Assert.Equal(TemplateStatusChangeReason.None, change.Reason);
    }

    [Fact]
    public void A_template_event_is_attributed_to_the_account_not_to_a_number()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "message_template_status_update",
            """
            {
              "event": "REJECTED",
              "message_template_id": 1,
              "message_template_name": "n",
              "message_template_language": "en",
              "reason": "ABUSIVE_CONTENT"
            }
            """));

        var change = Assert.IsType<TemplateStatusChanged>(Assert.Single(events));

        // There is no phone number anywhere in this payload. Insisting on one, as the message
        // path does, would drop the event entirely.
        Assert.Equal("102290129340398", change.BusinessAccountId);
        Assert.Empty(change.PhoneNumberId);
        Assert.Equal(TemplateStatus.Rejected, change.Status);
        Assert.Equal(TemplateStatusChangeReason.AbusiveContent, change.Reason);
    }

    [Fact]
    public void A_rejection_keeps_whatever_explanation_came_with_it()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "message_template_status_update",
            """
            {
              "event": "REJECTED",
              "message_template_id": 1,
              "message_template_name": "n",
              "message_template_language": "en",
              "reason": "INVALID_FORMAT",
              "other_info": {"title": "Header", "description": "The header exceeds 60 characters."}
            }
            """));

        var change = Assert.IsType<TemplateStatusChanged>(Assert.Single(events));

        Assert.Equal("The header exceeds 60 characters.", change.Details);
    }

    [Fact]
    public void An_unknown_event_string_is_kept_rather_than_dropped()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "message_template_status_update",
            """
            {
              "event": "FLAGGED_BY_A_NEW_SYSTEM",
              "message_template_id": 1,
              "message_template_name": "n",
              "message_template_language": "en"
            }
            """));

        var change = Assert.IsType<TemplateStatusChanged>(Assert.Single(events));

        Assert.Equal(TemplateStatus.Unknown, change.Status);
        Assert.Equal("FLAGGED_BY_A_NEW_SYSTEM", change.RawEvent);
    }

    [Fact]
    public void A_rejection_carries_the_reviewer_s_own_words()
    {
        // `reason` alone is INVALID_FORMAT, which tells an operator nothing about what to
        // change. Meta puts that in `rejection_info`, not in the `other_info` the library
        // used to read.
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "message_template_status_update",
            """
            {
              "event": "REJECTED",
              "message_template_id": 1387372356726668,
              "message_template_name": "abandoned_cart",
              "message_template_language": "en_US",
              "reason": "INVALID_FORMAT",
              "rejection_info": {
                "reason": "Your template has parameters placed next to each other.",
                "recommendation": "Separate parameters with descriptive text."
              }
            }
            """));

        var change = Assert.IsType<TemplateStatusChanged>(Assert.Single(events));

        Assert.Equal(TemplateStatus.Rejected, change.Status);
        Assert.Equal(TemplateStatusChangeReason.InvalidFormat, change.Reason);
        Assert.Equal("Your template has parameters placed next to each other.", change.Details);
        Assert.Equal("Separate parameters with descriptive text.", change.Recommendation);
    }

    [Fact]
    public void Other_info_still_wins_where_meta_sends_it()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "message_template_status_update",
            """
            {
              "event": "REJECTED",
              "message_template_id": 1,
              "message_template_name": "n",
              "message_template_language": "en",
              "reason": "ABUSIVE_CONTENT",
              "other_info": {"title": "Component", "description": "The body asks for a PIN."},
              "rejection_info": {"reason": "ignored", "recommendation": "Remove the request."}
            }
            """));

        var change = Assert.IsType<TemplateStatusChanged>(Assert.Single(events));

        Assert.Equal("The body asks for a PIN.", change.Details);
        // The recommendation comes along either way; it has nowhere else to go.
        Assert.Equal("Remove the request.", change.Recommendation);
    }

    [Fact]
    public void A_quality_drop_is_parsed()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "message_template_quality_update",
            """
            {
              "previous_quality_score": "GREEN",
              "new_quality_score": "RED",
              "message_template_id": 1387372356726668,
              "message_template_name": "order_confirmation",
              "message_template_language": "en_US"
            }
            """));

        var change = Assert.IsType<TemplateQualityChanged>(Assert.Single(events));

        // The warning before Meta pauses the template outright.
        Assert.Equal(TemplateQuality.Green, change.Previous);
        Assert.Equal(TemplateQuality.Red, change.Current);
        Assert.Equal("order_confirmation", change.TemplateName);
    }

    [Fact]
    public void A_message_still_carries_the_account_it_arrived_on()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "messages",
            """
            {
              "messaging_product": "whatsapp",
              "metadata": {"display_phone_number": "15550001111", "phone_number_id": "106540352242922"},
              "messages": [{"from": "79000000001", "id": "wamid.A", "timestamp": "1755000000",
                            "type": "text", "text": {"body": "hi"}}]
            }
            """));

        var message = Assert.IsType<TextMessage>(Assert.Single(events));

        Assert.Equal("102290129340398", message.BusinessAccountId);
        Assert.Equal("106540352242922", message.PhoneNumberId);
    }
}
