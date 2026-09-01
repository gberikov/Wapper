# Sending messages

## Sending a template message

The most common send, because it is the only one allowed once the 24-hour customer service
window has closed. The values go in by component, matched to the placeholders the template
declares:

```csharp
await whatsApp.Messages.SendTemplateAsync(
    customer,
    new TemplateMessage
    {
        Name = "order_confirmation",
        Language = "en_US",
        Components =
        [
            TemplateComponent.Body(
                TemplateParameter.FromText("Pablo", name: "first_name"),
                TemplateParameter.FromText("860198-230332", name: "order_number")),
            TemplateComponent.UrlButton(0, "860198-230332"),
        ],
    },
    callbackData: orderId,
    cancellationToken: ct);
```

Leave `name:` out for a template with numbered placeholders, and keep the parameters in the
order the placeholders appear. A media header takes the file the customer will actually see,
per message — `TemplateParameter.FromImage(MediaSource.FromId(mediaId))` — where the template
itself only ever held a sample. `FromMoney`, `FromDateTime`, `FromLocation` and
`CopyCodeButton` cover the other placeholder kinds.

A template that has not been approved, or whose parameters do not match, is rejected with
one of the `132xxx` codes, and none of them is retried: see [Errors](errors.md).

## Interactive messages

Buttons are above. Three is the most WhatsApp allows; a list carries up to ten choices:

```csharp
await whatsApp.Messages.SendListAsync(customer, new ListMessage
{
    Body = "When suits you?",
    ButtonText = "Pick a slot",
    Sections =
    [
        new ListSection
        {
            Title = "Tomorrow",
            Rows =
            [
                new ListRow { Id = "slot:0900", Title = "09:00" },
                new ListRow { Id = "slot:1400", Title = "14:00", Description = "Afternoon" },
            ],
        },
    ],
}, cancellationToken: ct);
```

What the customer taps comes back as an `InteractiveReply` carrying the `Id` you set. A
`CallToActionMessage` renders a link as a button, and `SendLocationRequestAsync` asks the
customer to share where they are — the answer arrives as a `LocationMessage`, exactly as an
unprompted one would.

Meta's limits on these are tight, and it answers every one of them with a bare `100` that does
not say which field it objected to. They are checked before the send instead, with a message
naming the field and its actual length:

| | Reply buttons | List |
|---|---|---|
| Body | 1024 | 4096 |
| Header (text) | 60 | 60 |
| Footer | 60 | 60 |
| Buttons / rows | 3 buttons | 10 rows across at most 10 sections |
| Button or row title | 20 | 24 |
| Button or row id | 256 | 200 |
| Row description | — | 72 |
| Section title | — | 24 |
| Text on the list button | — | 20 |

The two body limits really do differ; both are Meta's own numbers. Note that a title is what
the customer reads and is short: plan for `Id` to carry the meaning and the title to carry the
label.

## Media

Upload first, then send by id. A link works too, but Meta fetches it at send time, so a slow
host fails the send and the result is cached for ten minutes:

```csharp
await using var file = File.OpenRead("invoice.pdf");
var mediaId = await whatsApp.Media.UploadAsync(file, "application/pdf", "invoice.pdf", ct);

await whatsApp.Messages.SendDocumentAsync(
    customer,
    MediaSource.FromId(mediaId),
    caption: "Your invoice",
    fileName: "invoice-2026-08.pdf",
    cancellationToken: ct);
```

The size limits Meta publishes — 5 MB for an image, 16 MB for audio and video, 100 MB for a
document — are checked before a byte goes up. An uploaded id lives for 30 days.

What a customer sends arrives as a `MediaMessage` carrying an id and nothing else. Fetch the
bytes promptly — an id from a webhook expires after seven days — and dispose the result, which
owns the connection:

```csharp
public sealed class Attachments(IWhatsAppClient whatsApp) : IWhatsAppEventHandler<MediaMessage>
{
    public async Task HandleAsync(MediaMessage message, CancellationToken ct)
    {
        await using var media = await whatsApp.Media.DownloadAsync(message.MediaId, ct);
        await using var target = File.Create(Path.Combine("inbox", message.MediaId));
        await media.Content.CopyToAsync(target, ct);
    }
}
```

A media download is the one call that leaves the Graph API host, so where it goes is checked
before the token is attached: see [what the client refuses to
do](../README.md#a-few-things-the-client-refuses-to-do).

**Nothing caps how much a download reads.** An upload is measured against Meta's limits and
refused before it is sent; a download is a stream, and a stream that is capped is not one — so
the ceiling is yours. `MediaContent.FileSize` and the `Content-Length` behind it are what the
server said, not a promise about what will arrive, so size a buffer with them and count bytes
anyway:

```csharp
await using var media = await whatsApp.Media.DownloadAsync(message.MediaId, ct);
await using var target = File.Create(path);

var buffer = new byte[81920];
long total = 0;

for (int read; (read = await media.Content.ReadAsync(buffer, ct)) > 0;)
{
    if ((total += read) > MaxAttachmentBytes)
    {
        throw new InvalidOperationException("This attachment is larger than we accept.");
    }

    await target.WriteAsync(buffer.AsMemory(0, read), ct);
}
```

Where the ceiling should be is your call and not the library's: an inbox that keeps
attachments wants a different one from a bot that reads a QR code and throws the image away.

