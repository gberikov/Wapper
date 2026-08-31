using Wapper.Flows;
using Wapper.Webhooks;

namespace Wapper.Tests.Webhooks;

/// <summary>
/// One webhook field, <c>flows</c>, carries both the status changes and the monitoring
/// alerts. Like the template and phone number events they belong to the account and name no
/// phone number at all.
/// </summary>
public class FlowWebhookTests
{
    private static string Delivery(string value) => $$"""
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "644600416743275",
            "time": 1684969340,
            "changes": [{ "field": "flows", "value": {{value}} }]
          }]
        }
        """;

    [Fact]
    public void A_published_flow_is_parsed()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            {
              "event": "FLOW_STATUS_CHANGE",
              "message": "Flow Webhook 3 changed status from DRAFT to PUBLISHED",
              "flow_id": "6627390910605886",
              "old_status": "DRAFT",
              "new_status": "PUBLISHED"
            }
            """));

        var change = Assert.IsType<FlowStatusChanged>(Assert.Single(events));

        Assert.Equal("6627390910605886", change.FlowId);
        Assert.Equal(FlowStatus.Draft, change.PreviousStatus);
        Assert.Equal(FlowStatus.Published, change.Status);
        Assert.Equal("644600416743275", change.BusinessAccountId);
        // No phone number anywhere in the payload.
        Assert.Empty(change.PhoneNumberId);
    }

    [Fact]
    public void A_newly_created_flow_has_no_previous_status()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            {
              "event": "FLOW_STATUS_CHANGE",
              "message": "Flow Webhook 3 has been created with DRAFT status",
              "flow_id": "6627390910605886",
              "new_status": "DRAFT"
            }
            """));

        var change = Assert.IsType<FlowStatusChanged>(Assert.Single(events));

        // The creation notification leaves old_status out entirely.
        Assert.Equal(FlowStatus.Unknown, change.PreviousStatus);
        Assert.Equal(FlowStatus.Draft, change.Status);
    }

    [Fact]
    public void A_blocked_flow_is_parsed()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            {
              "event": "FLOW_STATUS_CHANGE",
              "flow_id": "1",
              "old_status": "THROTTLED",
              "new_status": "BLOCKED"
            }
            """));

        var change = Assert.IsType<FlowStatusChanged>(Assert.Single(events));

        // Monitoring throttles first and blocks second, so this one is already the second
        // warning.
        Assert.Equal(FlowStatus.Throttled, change.PreviousStatus);
        Assert.Equal(FlowStatus.Blocked, change.Status);
    }

    [Fact]
    public void An_endpoint_alert_is_parsed()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            {
              "event": "ENDPOINT_ERROR_RATE",
              "message": "Endpoint error rate crossed the threshold",
              "flow_id": "6627390910605886",
              "threshold": 10,
              "alert_state": "ACTIVATED",
              "requests_count": 200,
              "error_rate": 16,
              "errors": [
                { "error_type": "TIMEOUT", "error_count": 29, "error_rate": 14.5 }
              ]
            }
            """));

        var alert = Assert.IsType<FlowAlert>(Assert.Single(events));

        // The same field as the status change, told apart only by `event`.
        Assert.Equal(FlowAlertKind.EndpointErrorRate, alert.Kind);
        Assert.Equal(FlowAlertState.Activated, alert.State);
        Assert.Equal(10, alert.Threshold);
        Assert.Equal(200, alert.RequestCount);
        Assert.Equal(16, alert.ErrorRate);

        // The alert's `errors` reuses the field name the message webhook uses for something
        // else entirely: no code, no message, a count and a rate instead.
        var error = Assert.Single(alert.Errors);
        Assert.Equal("TIMEOUT", error.ErrorType);
        Assert.Equal(29, error.Count);
        Assert.Equal(14.5, error.Rate);
    }

    [Fact]
    public void A_latency_alert_carries_its_percentiles()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            {
              "event": "ENDPOINT_LATENCY",
              "flow_id": "1",
              "alert_state": "DEACTIVATED",
              "p50_latency": 320,
              "p90_latency": 1800
            }
            """));

        var alert = Assert.IsType<FlowAlert>(Assert.Single(events));

        Assert.Equal(FlowAlertKind.EndpointLatency, alert.Kind);
        Assert.Equal(FlowAlertState.Deactivated, alert.State);
        Assert.Equal(320, alert.MedianLatency);
        Assert.Equal(1800, alert.NinetiethPercentileLatency);
    }

    [Fact]
    public void An_alert_this_library_does_not_know_is_kept_rather_than_dropped()
    {
        var events = WhatsAppWebhookParser.Parse(Delivery("""
            {"event": "SOMETHING_NEW", "flow_id": "1"}
            """));

        var alert = Assert.IsType<FlowAlert>(Assert.Single(events));

        Assert.Equal(FlowAlertKind.Unknown, alert.Kind);
        Assert.Equal("SOMETHING_NEW", alert.RawKind);
    }
}
