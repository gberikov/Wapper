# Running in more than one instance

The four budgets and how the client paces itself against them are in the
[README](../README.md#why-the-rate-limiting-matters). This is what changes once the
application runs as more than one process.

Meta counts per phone number on its side. Three replicas each pacing themselves against the
full allowance send three times the rate and have two thirds of it rejected, so the counters
have to be shared:

```csharp
builder.Services.AddWhatsApp();
builder.Services.AddWhatsAppRedisRateLimiting("localhost:6379");
```

The budgets then live in Redis, and a penalty recorded by one instance holds the others
back too. If Redis becomes unreachable the limiter logs and falls back to pacing that
instance alone — Meta rejects the overshoot, which the retry path already handles, rather
than a Redis blip becoming a messaging outage. Set `FallBackToLocal = false` to make it
fatal instead.

