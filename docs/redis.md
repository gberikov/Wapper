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
back too — including calls on the other instances that were already waiting their turn: a
call re-checks the budgets when its wait runs out and sits out any hold recorded in the
meantime, rather than walking into a block the Cloud API has just announced. If Redis
becomes unreachable the limiter logs and falls back to pacing that instance alone — Meta
rejects the overshoot, which the retry path already handles, rather than a Redis blip
becoming a messaging outage. Set `FallBackToLocal = false` to make it fatal instead.

## Round trips

A call that is granted at once costs one round trip: one Lua script prices and spends every
budget of the call atomically. A call that has to wait makes one more when its wait runs
out, to ask whether anything changed while it slept, and one more per penalty that arrived
while it did. A call cancelled while waiting makes one to hand its permits back. There is no
polling.

## Redis Cluster

Every budget of one call is spent by one script, and Redis Cluster only runs a script over
keys that hash to the same slot. The default `KeyPrefix` of `wapper:rl:` does not arrange
that, so on a cluster the first paced call fails with a `WhatsAppConfigurationException`
saying so — not with a warning about Redis being away, because no retry or fallback fixes
a key prefix.

Give the prefix a hash tag:

```csharp
builder.Services.AddWhatsAppRedisRateLimiting(
    "node1:6379,node2:6379,node3:6379",
    options => options.KeyPrefix = "{wapper}:rl:");
```

Everything inside the braces decides the slot, so every key of the limiter lands on one
shard. That is the trade-off: the limiter's whole traffic goes to one node. It is one small
script per message, and a single node handles far more of those a second than the Cloud API
lets any number of phone numbers send, so it is not a bottleneck in practice — but it is not
spread out either. A tag per phone number would spread it and break the application budget,
which is shared by every number on the app; the limiter therefore does not offer one.

Change the prefix on every instance at the same time. An instance on the old prefix and one
on the new pace against different keys, and together they spend the allowance twice. The old
keys expire on their own after `KeyLifetime`.

The test suite exercises this against Redis in cluster mode — one node holding every slot,
which is enough to enforce the rule — and checks both halves: the refusal without a hash tag
names the setting, and with one a grant and a penalty land on the same slot. A cluster of
several nodes behaves the same way for the limiter, since every key of a call is on one of
them; it is not run here.

## What is stored

One hash per budget, under `{KeyPrefix}{Budget}:{Key}` — a pair budget is keyed by a digest
of the customer's number rather than the number itself. The fields are the balance, the
count of permits earned so far, the rate, when they were last brought up to date and when a
hold ends. Version 0.5.0 added the count and the rate; an older instance reads and writes the
same keys without them, and a newer one treats their absence as a fresh budget, so the two
can run side by side through a rolling deployment.
