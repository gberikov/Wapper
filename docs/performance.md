# Performance

What the three things this library does on a hot path cost, measured with the benchmarks in
`benchmarks/Wapper.Benchmarks`. The numbers are a baseline for the machine below and nothing
else: no real network is involved anywhere, and none of this says how many messages a second
the Cloud API will take from you — that is Meta's number, in [the README](../README.md#why-the-rate-limiting-matters).

```
dotnet run -c Release --project benchmarks/Wapper.Benchmarks -- --filter '*'
```

Taken on 6 September 2026, BenchmarkDotNet 0.15.8, ShortRun job (3 warm-ups, 3 iterations):
Windows 11 25H2, Intel Core i9-13980HX, .NET 8.0.30 x64 RyuJIT, SDK 10.0.400. Mean of
3 iterations; the error column is wide, so read the ratios and the allocations rather than
the third digit.

## Parsing a webhook delivery

One delivery with *N* messages in it, parsed into events. `Typed` is text messages;
`UnknownTypes` a type this library has no event for, kept whole as `UnknownMessage`;
`MixedWithUnreadable` alternates text with an item the parser cannot bind, reported as an
`UnknownEvent` beside the others.

| Method              | Items | Mean       | Allocated |
|---------------------|------:|-----------:|----------:|
| Typed               |     1 |    4.6 μs  |   5.1 KB  |
| UnknownTypes        |     1 |    3.8 μs  |   5.0 KB  |
| Typed               |    10 |   14.9 μs  |  23.2 KB  |
| MixedWithUnreadable |    10 |   91.3 μs  |  34.6 KB  |
| UnknownTypes        |    10 |   17.9 μs  |  22.0 KB  |
| Typed               |   100 |  127.2 μs  | 203.0 KB  |
| MixedWithUnreadable |   100 |  893.7 μs  | 316.9 KB  |
| UnknownTypes        |   100 |  162.3 μs  | 191.2 KB  |

Linear in the number of items, about 1.3 μs and 2 KB per message. An unknown type costs the
same as a known one plus its raw JSON. An item that cannot be read costs about 15 μs more: the
binding fails with an exception, which is caught per item. That is the failure path and the
price of not losing the item's neighbours; a delivery full of unreadable items is still under
a millisecond.

## The in-memory rate limiter

One send spends three budgets: the application, the phone number and the recipient pair.
`Scopes` is how many distinct recipients the calls rotate over. Per call, over 100 calls; the
clock is a fake one wound by the benchmark, so no real waiting is included.

| Method               | Scopes | Mean     | Allocated |
|----------------------|-------:|---------:|----------:|
| GrantedAtOnce        |      1 |  2.3 μs  |    120 B  |
| QueuedBehindAPenalty |      1 | 10.8 μs  |    449 B  |
| QueuedThenCancelled  |      1 | 47.4 μs  |  2 386 B  |
| GrantedAtOnce        |   1000 |  3.1 μs  |    384 B  |
| QueuedBehindAPenalty |   1000 | 11.6 μs  |    713 B  |
| QueuedThenCancelled  |   1000 | 41.0 μs  |  2 650 B  |

A call that is granted at once costs about 2–3 μs and a hundred-odd bytes — the three
reservations. One that queues behind a penalty pays for the wake-up and the re-pricing of
every reservation, about 11 μs. A cancelled wait is the expensive one, at about 45 μs, most
of it the `OperationCanceledException`; it is also the rare one.

The queue in a bucket is scanned for a reservation's position on every re-pricing, which is
linear in the number of waiters on that bucket. A thousand callers queued on one phone
number is the ceiling this was designed to; past that, a ranked structure would be the next
step.

## Uploading through the resumable upload

A file handed to `UploadHeaderSampleAsync` or `SetPictureAsync`, against an in-process stand-in
for the upload endpoint that reads the body to its end.

| Method      | Bytes     | Mean       | Allocated    |
|-------------|----------:|-----------:|-------------:|
| Seekable    |    65 536 |    8.3 μs  |     8.6 KB   |
| ForwardOnly |    65 536 |   49.0 μs  |   153.4 KB   |
| Seekable    | 4 194 304 |  131.8 μs  |     8.6 KB   |
| ForwardOnly | 4 194 304 |    1.9 ms  | 10 256.7 KB  |

A seekable stream — a file, a `MemoryStream` — costs the same few kilobytes whatever its
size: it is sent from where it stands and never copied. A stream that cannot be rewound is
read into memory once so the upload can declare its length and be retried; the allocation is
about 2.5 times the file, because the buffer grows by doubling as it reads, and it is capped
at 100 MB before it starts. Hand in a seekable stream when the file is large.

## The Redis limiter

Not benchmarked here: the cost is round trips, and those are counted rather than timed.

| Call | Round trips |
|---|---|
| Granted at once | 1 — one script prices and spends every budget atomically |
| Granted with a wait | 2 — plus one when the wait runs out, to ask whether a penalty arrived meanwhile; one more per penalty that did |
| Cancelled while waiting | 2 — the grant, and one to hand the permits back |
| Penalty | 1 |

Nothing polls. Every budget of a call is spent by one script, which is also why Redis
Cluster needs a hash tag in the key prefix — see [Running in more than one instance](redis.md).
