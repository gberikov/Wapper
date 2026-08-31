# Analytics

Four metrics, all against the WhatsApp Business Account:

```csharp
var conversations = await whatsApp.Analytics.GetConversationsAsync(
    new ConversationAnalyticsQuery
    {
        Start = DateTimeOffset.UtcNow.AddDays(-30),
        End = DateTimeOffset.UtcNow,
        Granularity = AnalyticsGranularity.Day,
        Dimensions = [ConversationDimension.ConversationCategory, ConversationDimension.Country],
    },
    ct);
```

`GetMessagingAsync` counts messages sent and delivered. `GetConversationsAsync` counts
conversations and what they cost. `GetPricingAsync` counts delivered messages by the rate they
were charged at — and it is the only place volume tiers are visible, since no webhook reports
them. `GetTemplatesAsync` is per template: sent, delivered, read, and buttons pressed.

Things worth knowing:

- **Meta spells the same granularity two ways.** `DAY` and `MONTH` for messaging, `DAILY` and
  `MONTHLY` for conversations and pricing, and each rejects the other's word for it. One
  `AnalyticsGranularity` here, translated per metric.
- **A filter left unset means "all of them".** That is Meta's own default, so nothing is sent.
- **`Dimensions` decides what comes back.** Without them the answer is one number per time
  slice; the breakdown fields on the data points are only filled in for dimensions that were
  asked for.
- **Cost is not reported for an account billed through a Solution Partner**, and asking for
  cost and nothing else makes such an account answer with an explanation instead of a figure.
- **Template clicks and cost are not numbers.** Clicks are counted per button, and cost arrives
  as several figures at once — amount spent, cost per delivery, cost per click.
- **Lookback is a year**, and 90 days for templates. Ten templates per read.

A backwards range is refused here rather than sent: Meta answers one with an empty result,
which reads exactly like a quiet week.

