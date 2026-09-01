using System.Text.Json;
using Wapper.PhoneNumbers;
using Wapper.Webhooks;

namespace Wapper.Tests.Webhooks;

/// <summary>
/// The account webhook carries policy violations, restrictions, offboarding and deletion —
/// the things an application has to act on and cannot learn any other way. It used to arrive
/// as <see cref="UnknownEvent"/>.
/// </summary>
public class AccountWebhookTests
{
    private static string Delivery(string value) => $$"""
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "102290129340398",
            "time": 1755000000,
            "changes": [{ "field": "account_update", "value": {{value}} }]
          }]
        }
        """;

    [Fact]
    public void A_number_sent_as_a_bare_string_is_read()
    {
        // Meta's own documented example, and what its test delivery sends.
        var events = WhatsAppWebhookParser.Parse(Delivery(
            """{"event":"VERIFIED_ACCOUNT","phone_number":"16505551111"}"""));

        var update = Assert.IsType<AccountUpdated>(Assert.Single(events));

        Assert.Equal(AccountUpdateEvent.VerifiedAccount, update.Event);
        Assert.Equal("16505551111", update.PhoneNumber);
        // Account-level: nothing in the payload names a phone number id.
        Assert.Equal("102290129340398", update.BusinessAccountId);
        Assert.Empty(update.PhoneNumberId);
    }

    [Fact]
    public void A_number_sent_as_an_object_is_read()
    {
        // The same field, the other shape. Both are live, and reading only one of them throws
        // on the other or loses it.
        var events = WhatsAppWebhookParser.Parse(Delivery(
            """
            {"event":"PHONE_NUMBER_QUALITY_UPDATE","current_limit":"TIER_1K",
             "phone_number":{"display_phone_number":"77000231088","quality_rating":"RED"}}
            """));

        var update = Assert.IsType<AccountUpdated>(Assert.Single(events));

        Assert.Equal(AccountUpdateEvent.PhoneNumberQualityUpdate, update.Event);
        Assert.Equal("77000231088", update.PhoneNumber);
        Assert.Equal(PhoneNumberQuality.Red, update.QualityRating);
        Assert.Equal(MessagingLimitTier.Tier1K, update.CurrentLimit);
    }

    [Fact]
    public void A_disablement_carries_the_ban_state()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            """
            {"event":"DISABLED_UPDATE",
             "ban_info":{"waba_ban_state":"SCHEDULE_FOR_DISABLE","waba_ban_date":"2026-09-14"}}
            """));

        var update = Assert.IsType<AccountUpdated>(Assert.Single(events));

        Assert.Equal(AccountUpdateEvent.DisabledUpdate, update.Event);
        Assert.Equal("SCHEDULE_FOR_DISABLE", update.BanState);
        Assert.Equal("2026-09-14", update.BanDate);
    }

    [Fact]
    public void A_restriction_says_what_was_taken_away_and_until_when()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            """
            {"event":"ACCOUNT_RESTRICTION",
             "restriction_info":[{"restriction_type":"RESTRICTED_BIZ_INITIATED_MESSAGING",
                                  "expiration":1641330498},
                                 {"restriction_type":"RESTRICTED_ADD_PHONE_NUMBER_ACTION",
                                  "expiration":1641330498}]}
            """));

        var update = Assert.IsType<AccountUpdated>(Assert.Single(events));

        Assert.Equal(AccountUpdateEvent.AccountRestriction, update.Event);
        Assert.Equal(2, update.Restrictions.Count);
        Assert.Equal("RESTRICTED_BIZ_INITIATED_MESSAGING", update.Restrictions[0].Type);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1641330498),
            update.Restrictions[0].ExpiresAt);
    }

    [Fact]
    public void A_violation_carries_the_policy_that_was_broken()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery(
            """{"event":"ACCOUNT_VIOLATION","violation_info":{"violation_type":"ADULT"}}"""));

        var update = Assert.IsType<AccountUpdated>(Assert.Single(events));

        Assert.Equal(AccountUpdateEvent.AccountViolation, update.Event);
        Assert.Equal("ADULT", update.ViolationType);
    }

    [Fact]
    public void An_event_this_library_does_not_know_keeps_its_body()
    {
        // Half this field is only meaningful to a Solution Partner, and Meta adds to it
        // faster than it documents it. Those arrive typed but unparsed rather than dropped.
        var events = WhatsAppWebhookParser.Parse(Delivery(
            """
            {"event":"VOLUME_BASED_PRICING_TIER_UPDATE",
             "volume_tier_info":{"tier":"TIER_2","pricing_category":"MARKETING"}}
            """));

        var update = Assert.IsType<AccountUpdated>(Assert.Single(events));

        Assert.Equal(AccountUpdateEvent.Unknown, update.Event);
        Assert.Equal("VOLUME_BASED_PRICING_TIER_UPDATE", update.RawEvent);

        var value = JsonDocument.Parse(update.Json).RootElement;
        Assert.Equal("TIER_2", value.GetProperty("volume_tier_info").GetProperty("tier").GetString());
    }
}
