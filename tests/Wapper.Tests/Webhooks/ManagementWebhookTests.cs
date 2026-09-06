using Wapper.PhoneNumbers;
using Wapper.Templates;
using Wapper.Webhooks;

namespace Wapper.Tests.Webhooks;

/// <summary>
/// The account-level fields typed in this release, each against the example on Meta's
/// webhook reference page for the field.
/// </summary>
public class ManagementWebhookTests
{
    private static string Delivery(string field, string value) => $$"""
        {"object":"whatsapp_business_account","entry":[{"id":"102290129340398","time":1745612159,
         "changes":[{"field":"{{field}}","value":{{value}}}]}]}
        """;

    [Fact]
    public void An_account_alert_is_parsed_from_the_reference_example()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("account_alerts", """
            {"entity_type":"BUSINESS","entity_id":"506914307656634",
             "alert_info":{"alert_severity":"WARNING","alert_status":"ACTIVE",
                           "alert_type":"INCREASED_CAPABILITIES_ELIGIBILITY_DEFERRED",
                           "alert_description":"Limits cannot be increased for your business."}}
            """));

        var alert = Assert.IsType<AccountAlert>(Assert.Single(events));

        Assert.Equal(AccountAlertEntity.Business, alert.EntityType);
        Assert.Equal("506914307656634", alert.EntityId);
        Assert.Equal(AccountAlertSeverity.Warning, alert.Severity);
        Assert.Equal(AccountAlertStatus.Active, alert.Status);
        Assert.Equal(AccountAlertKind.IncreasedCapabilitiesEligibilityDeferred, alert.Kind);
        Assert.Equal("Limits cannot be increased for your business.", alert.Description);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1745612159), alert.Timestamp);
    }

    [Fact]
    public void A_capability_change_is_parsed_with_either_spelling_of_the_limit()
    {
        // The reference example: the tier as a number, and the per-account phone limit.
        var numeric = Assert.IsType<BusinessCapabilityChanged>(Assert.Single(WhatsAppWebhookParser.Parse(
            Delivery("business_capability_update", """
                {"max_daily_conversations_per_business":2000,"max_phone_numbers_per_waba":25}
                """))));

        Assert.Equal(MessagingLimitTier.Tier2K, numeric.MessagingLimit);
        Assert.Equal("TIER_2K", numeric.RawMessagingLimit);
        Assert.Equal(25, numeric.MaxPhoneNumbersPerAccount);
        Assert.Null(numeric.MaxPhoneNumbersPerBusiness);

        // The documented tier name, with the retired per-phone number alongside.
        var named = Assert.IsType<BusinessCapabilityChanged>(Assert.Single(WhatsAppWebhookParser.Parse(
            Delivery("business_capability_update", """
                {"max_daily_conversation_per_phone":250,
                 "max_daily_conversations_per_business":"TIER_250",
                 "max_phone_numbers_per_business":2}
                """))));

        Assert.Equal(MessagingLimitTier.Tier250, named.MessagingLimit);
        Assert.Equal(250, named.MaxDailyConversationsPerPhone);
        Assert.Equal(2, named.MaxPhoneNumbersPerBusiness);

        // Unlimited is -1 on the old spelling.
        var unlimited = Assert.IsType<BusinessCapabilityChanged>(Assert.Single(WhatsAppWebhookParser.Parse(
            Delivery("business_capability_update", """{"max_daily_conversation_per_phone":-1}"""))));

        Assert.Equal(MessagingLimitTier.Unlimited, unlimited.MessagingLimit);
    }

    [Fact]
    public void A_security_change_names_the_event_and_who_asked()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("security", """
            {"display_phone_number":"15550783881","event":"PIN_RESET_REQUEST","requester":"61555822107539"}
            """));

        var security = Assert.IsType<PhoneNumberSecurityChanged>(Assert.Single(events));

        Assert.Equal("15550783881", security.DisplayPhoneNumber);
        Assert.Equal(PhoneNumberSecurityEvent.PinResetRequest, security.Event);
        Assert.Equal("PIN_RESET_REQUEST", security.RawEvent);
        Assert.Equal("61555822107539", security.RequesterId);
    }

    [Fact]
    public void A_pending_category_change_says_what_it_will_become_and_when()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("template_category_update", """
            {"message_template_id":278077987957091,"message_template_name":"welcome_template",
             "message_template_language":"en-US","new_category":"UTILITY",
             "correct_category":"MARKETING","category_update_timestamp":1746169200}
            """));

        var change = Assert.IsType<TemplateCategoryChanged>(Assert.Single(events));

        Assert.True(change.IsPending);
        Assert.Equal("278077987957091", change.TemplateId);
        Assert.Equal("welcome_template", change.TemplateName);
        Assert.Equal("en-US", change.TemplateLanguage);
        Assert.Equal(TemplateCategory.Utility, change.Category);
        Assert.Equal(TemplateCategory.Marketing, change.CorrectCategory);
        Assert.Null(change.PreviousCategory);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1746169200), change.ChangesAt);
    }

    [Fact]
    public void A_completed_category_change_says_what_it_was()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("template_category_update", """
            {"message_template_id":278077987957091,"message_template_name":"welcome_template",
             "message_template_language":"en-US","previous_category":"UTILITY","new_category":"MARKETING"}
            """));

        var change = Assert.IsType<TemplateCategoryChanged>(Assert.Single(events));

        Assert.False(change.IsPending);
        Assert.Equal(TemplateCategory.Utility, change.PreviousCategory);
        Assert.Equal(TemplateCategory.Marketing, change.Category);
        Assert.Null(change.CorrectCategory);
        Assert.Null(change.ChangesAt);
    }

    [Fact]
    public void A_components_change_carries_the_template_as_it_now_reads()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("message_template_components_update", """
            {"message_template_id":1315502779341834,"message_template_name":"order_confirmation",
             "message_template_language":"en_US",
             "message_template_title":"Your order is confirmed!",
             "message_template_element":"Thank you for your order, {{1}}!",
             "message_template_footer":"Lucky Shrub",
             "message_template_buttons":[
               {"message_template_button_type":"PHONE_NUMBER","message_template_button_text":"Phone support",
                "message_template_button_phone_number":"+15550783881"},
               {"message_template_button_type":"URL","message_template_button_text":"Email support",
                "message_template_button_url":"https://www.luckyshrub.com/support"}]}
            """));

        var change = Assert.IsType<TemplateComponentsChanged>(Assert.Single(events));

        Assert.Equal("1315502779341834", change.TemplateId);
        Assert.Equal("Your order is confirmed!", change.Header);
        Assert.Equal("Thank you for your order, {{1}}!", change.Body);
        Assert.Equal("Lucky Shrub", change.Footer);
        Assert.Equal(2, change.Buttons.Count);
        Assert.Equal("PHONE_NUMBER", change.Buttons[0].Type);
        Assert.Equal("+15550783881", change.Buttons[0].PhoneNumber);
        Assert.Equal("URL", change.Buttons[1].Type);
        Assert.Equal("https://www.luckyshrub.com/support", change.Buttons[1].Url);
    }

    [Fact]
    public void A_flow_status_this_library_does_not_know_keeps_its_name()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("flows", """
            {"event":"FLOW_STATUS_CHANGE","flow_id":"1","old_status":"PUBLISHED","new_status":"ARCHIVED"}
            """));

        var change = Assert.IsType<FlowStatusChanged>(Assert.Single(events));

        Assert.Equal(Wapper.Flows.FlowStatus.Unknown, change.Status);
        Assert.Equal("ARCHIVED", change.RawStatus);
        Assert.Equal("PUBLISHED", change.RawPreviousStatus);
    }
}
