# Wapper.Sample

A small ASP.NET Core backend showing the parts of Wapper most applications reach for:

| Where | What |
|---|---|
| `Program.cs` | Registration, the webhook endpoint, and one HTTP endpoint per kind of send: text, template, reply buttons, document upload, location request. |
| `Outbound.cs` | Telling the failures apart — branching on `WhatsAppError.Code`, never on the HTTP status. |
| `Handlers.cs` | Answering messages and taps, downloading what customers send, following delivery statuses, recording marketing opt-outs, noticing unknown webhook fields. |

## Running it

1. Fill in `appsettings.json`, or set `WhatsApp__AccessToken`, `WhatsApp__PhoneNumberId`,
   `WhatsApp__AppSecret` and `WhatsApp__WebhookVerifyToken` in the environment.
   `WhatsAppBusinessAccountId` is only needed for `POST /subscribe`.
2. `dotnet run`, and expose the app on a public https URL (a tunnel is fine while developing).
3. In the Meta app dashboard, point the WhatsApp webhook at `https://<host>/whatsapp` with the
   verify token from step 1, and subscribe to the `messages` and `user_preferences` fields.
4. `POST /subscribe` once, so the account's webhooks reach this app at all.

Then:

```http
POST /send/text
{"to": "15550001111", "text": "hello"}

POST /send/buttons
{"to": "15550001111", "question": "Ready to ship?", "choices": ["Yes", "Not yet"]}

POST /send/template
{"to": "15550001111", "firstName": "Pablo", "orderNumber": "860198-230332"}
```

The template endpoint assumes an approved `order_confirmation` template with named
`{{first_name}}` and `{{order_number}}` placeholders; the README at the repository root shows
how to create one.
