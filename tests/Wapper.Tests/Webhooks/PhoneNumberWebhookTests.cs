using Wapper.PhoneNumbers;
using Wapper.Webhooks;

namespace Wapper.Tests.Webhooks;

/// <summary>
/// Phone number events are account-level, like the template ones: they name the number in
/// display form and carry no phone number id at all.
/// </summary>
public class PhoneNumberWebhookTests
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
    public void A_flagged_number_is_parsed()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "phone_number_quality_update",
            """
            {
              "display_phone_number": "15550783881",
              "event": "FLAGGED",
              "current_limit": "TIER_10K"
            }
            """));

        var change = Assert.IsType<PhoneNumberQualityChanged>(Assert.Single(events));

        // The warning before the messaging limit falls.
        Assert.Equal(PhoneNumberQualityEvent.Flagged, change.Event);
        Assert.Equal(MessagingLimitTier.Tier10K, change.CurrentLimit);
        Assert.Equal("15550783881", change.DisplayPhoneNumber);
        // There is no phone number id anywhere in this payload, only the account on the entry.
        Assert.Equal("102290129340398", change.BusinessAccountId);
        Assert.Empty(change.PhoneNumberId);
    }

    [Fact]
    public void The_replacement_limit_field_wins_over_the_retired_one()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "phone_number_quality_update",
            """
            {
              "display_phone_number": "15550783881",
              "event": "UPGRADE",
              "old_limit": "TIER_1K",
              "current_limit": "TIER_10K",
              "max_daily_conversations_per_business": "TIER_100K"
            }
            """));

        var change = Assert.IsType<PhoneNumberQualityChanged>(Assert.Single(events));

        // Meta retired `current_limit` in favour of `max_daily_conversations_per_business`.
        Assert.Equal(MessagingLimitTier.Tier100K, change.CurrentLimit);
        Assert.Equal(MessagingLimitTier.Tier1K, change.PreviousLimit);
    }

    [Fact]
    public void A_throughput_upgrade_carries_no_previous_limit()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "phone_number_quality_update",
            """
            {
              "display_phone_number": "15550783881",
              "event": "THROUGHPUT_UPGRADE",
              "current_limit": "TIER_UNLIMITED"
            }
            """));

        var change = Assert.IsType<PhoneNumberQualityChanged>(Assert.Single(events));

        // The number may now send considerably faster; the daily limit did not move, so no
        // old value is sent.
        Assert.Equal(PhoneNumberQualityEvent.ThroughputUpgrade, change.Event);
        Assert.Equal(MessagingLimitTier.Unlimited, change.CurrentLimit);
        Assert.Equal(MessagingLimitTier.Unknown, change.PreviousLimit);
    }

    [Fact]
    public void An_unknown_event_string_is_kept_rather_than_dropped()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "phone_number_quality_update",
            """{"display_phone_number": "15550783881", "event": "SOMETHING_NEW"}"""));

        var change = Assert.IsType<PhoneNumberQualityChanged>(Assert.Single(events));

        Assert.Equal(PhoneNumberQualityEvent.Unknown, change.Event);
        Assert.Equal("SOMETHING_NEW", change.RawEvent);
    }

    [Fact]
    public void An_approved_display_name_carries_a_null_rejection_reason()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "phone_number_name_update",
            """
            {
              "display_phone_number": "15550783881",
              "decision": "APPROVED",
              "requested_verified_name": "Lucky Shrub",
              "rejection_reason": null
            }
            """));

        var change = Assert.IsType<PhoneNumberNameChanged>(Assert.Single(events));

        Assert.Equal(DisplayNameDecision.Approved, change.Decision);
        Assert.Equal("Lucky Shrub", change.RequestedName);
        // Sent as JSON null on an approval rather than being left out.
        Assert.Equal(DisplayNameRejectionReason.None, change.RejectionReason);
    }

    [Fact]
    public void A_rejected_display_name_says_why()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            "phone_number_name_update",
            """
            {
              "display_phone_number": "15550783881",
              "decision": "REJECTED",
              "requested_verified_name": "Dave from Sales",
              "rejection_reason": "NAME_EMPLOYEE_ISSUE"
            }
            """));

        var change = Assert.IsType<PhoneNumberNameChanged>(Assert.Single(events));

        Assert.Equal(DisplayNameDecision.Rejected, change.Decision);
        Assert.Equal(DisplayNameRejectionReason.PersonalName, change.RejectionReason);
        Assert.Equal("NAME_EMPLOYEE_ISSUE", change.RawRejectionReason);
    }
}
