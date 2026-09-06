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
while it did. Sleeps are capped by the time left in the original `MaxWait`. At the deadline,
the limiter rechecks Redis before refusing: an estimate in a delayed response may already
be out of date. A new penalty cannot extend the sleep deadline, but Redis round trips can
delay completion. Cancellation or exceeding the deadline makes one round trip to return
permits that can safely be reused. A future permit inside the queue stays spent if later
reservations exist, because returning it would give a newcomer an existing waiter's place.
The unused permit refills at the configured rate. There is no polling.

`KeyLifetime` is a minimum: keys survive until their budgets can refill and the maximum
waits of granted calls have elapsed, with a one-minute grace period for delayed callers.
Penalties extend retention too. A short configured lifetime cannot erase a live reservation.

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
keys expire on their own after their retention period.

The test suite exercises this against Redis in cluster mode — one node holding every slot,
which is enough to enforce the rule — and checks both halves: the refusal without a hash tag
names the setting, and with one a grant and a penalty land on the same slot. A cluster of
several nodes behaves the same way for the limiter, since every key of a call is on one of
them; it is not run here.

## What is stored

One hash per budget, under `{KeyPrefix}{Budget}:{Key}` — a pair budget is keyed by a digest
of the customer's number rather than the number itself. The fields are the balance, the
count of permits earned so far, the rate and burst, when they were last brought up to date,
when a hold ends, and how long granted calls need the state retained. Older keys acquire the
additional retention fields on their next grant. All instances need the corrected limiter
before cancellation and retention guarantees apply across the deployment.
