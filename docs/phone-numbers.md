# Phone numbers

## Checking the phone number

```csharp
var number = await whatsApp.PhoneNumbers.GetAsync(cancellationToken: ct);

if (number.Status is not PhoneNumberStatus.Connected)
{
    logger.LogError("{Number} is {Status} and cannot send.", number.DisplayPhoneNumber, number.Status);
}
```

Worth doing at startup. A number that is `Flagged`, `RateLimited` or `Restricted` fails every
send with an error that reads like a transient one, and no amount of retrying will help.

Graph returns only a handful of fields by default and leaves out the ones worth reading a
number for, so Wapper always asks for the full set — `status` and `throughput` included.

`number.Throughput` is the messages-per-second ceiling: `Standard` is 80, `High` is 1000. Meta
raises it as volume grows, and announces it on the webhook. `RateLimits.MessagesPerSecond`
defaults to the conservative 80; raise it once the number reports `High`, or a high-throughput
number will be paced twelve times slower than it is allowed to send.

```csharp
builder.Services.AddWhatsAppWebhookHandler<NumberWatcher, PhoneNumberQualityChanged>();
```

`PhoneNumberQualityEvent.Flagged` is the one to act on: it means quality has dropped and the
daily messaging limit will fall if nothing changes. Display name decisions arrive as
`PhoneNumberNameChanged`.

Numbers cannot be created or deleted through the API — that is WhatsApp Manager, Meta Business
Suite or Embedded Signup. `SetTwoStepPinAsync` is the exception, and the only way to set a new
PIN without knowing the old one.

## Getting a number onto the Cloud API

Adding a number to the account is WhatsApp Manager's job. Everything after that is the API's,
and registering is *only* the API's — WhatsApp Manager cannot do it:

```csharp
await whatsApp.PhoneNumbers.RequestVerificationCodeAsync(
    VerificationCodeMethod.Sms,
    cancellationToken: ct);

// The message spells the code "123-830"; the hyphen is stripped for you.
await whatsApp.PhoneNumbers.VerifyAsync(code, cancellationToken: ct);

// Sets the two-step PIN if the number has none yet.
await whatsApp.PhoneNumbers.RegisterAsync("150954", cancellationToken: ct);
```

Registering and deregistering share an allowance of **ten attempts per number per 72 hours**,
and Meta counts the failed ones. The eleventh returns `133016` and locks the number out for the
rest of the window, so Wapper never retries any of these three calls automatically — a retry
would spend an attempt, or in the case of `RequestVerificationCodeAsync` send a second code and
silently invalidate the first.

Pass a two-letter country code to keep data at rest in one region:

```csharp
await whatsApp.PhoneNumbers.RegisterAsync("150954", "DE", cancellationToken: ct);
```

Local storage cannot be moved or switched off in place: deregister, then register again.

Register a second time after a display name change is approved — `PhoneNumberNameChanged` with
`DisplayNameDecision.Approved` is the signal. Re-registering before approval does nothing.

## The business profile

What a recipient sees when they tap the business's name in a thread:

```csharp
await whatsApp.BusinessProfile.UpdateAsync(
    new BusinessProfile
    {
        About = "Butterflies, and the things butterflies need.",
        Email = "hello@butterflies.example",
        Vertical = BusinessVertical.Retail,
        Websites = ["https://www.butterflies.example"],
    },
    cancellationToken: ct);
```

The update merges: a property left `null` keeps its current value, and an empty string clears
it. Every length limit — 139 characters of About, 512 of description, two websites — is checked
before the call, because Meta rejects all of them with the same bare `100` that never says which
field it objected to.

The picture is the odd one out. It is set by uploading a file to Meta and writing back the
handle, so it goes through the Resumable Upload API — which is addressed to the Meta app rather
than to the phone number, wants the token under the `OAuth` scheme instead of `Bearer`, and is
the only thing in this library that needs `WhatsApp:AppId`:

```csharp
await using var picture = File.OpenRead("logo.png");
await whatsApp.BusinessProfile.SetPictureAsync(picture, "image/png", cancellationToken: ct);
```

Reading is the usual story: Graph answers a bare read with the messaging product and nothing
else, so Wapper always names the fields. The profile comes back wrapped in a one-element array
even though a number has exactly one, and an empty array means nobody has filled it in.

