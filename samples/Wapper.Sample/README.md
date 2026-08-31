# Wapper.Sample

A small ASP.NET Core backend showing the parts of Wapper most applications reach for:

| Where | What |
|---|---|
| `Program.cs` | Registration, the webhook endpoint, one HTTP endpoint per kind of send (text, template, reply buttons, document upload, location request), and a `Raw` call to an endpoint the library has no typed API for. |
| `Outbound.cs` | Telling the failures apart — branching on `WhatsAppError.Code`, never on the HTTP status. |
| `Handlers.cs` | Answering messages and taps, downloading what customers send, following delivery statuses, recording marketing opt-outs, noticing unknown webhook fields. |

## Running it

1. Put the ids in `appsettings.json` and the secrets in user-secrets, which live outside the
   repository and are loaded automatically in development:

   ```bash
   dotnet user-secrets init
   dotnet user-secrets set "WhatsApp:AccessToken" "EAAJB..."
   dotnet user-secrets set "WhatsApp:AppSecret" "..."
   dotnet user-secrets set "WhatsApp:WebhookVerifyToken" "..."
   ```

   The same keys work as environment variables with `__` for `:`, which is how they are
   supplied anywhere that is not a developer's machine. `WhatsAppBusinessAccountId` is only
   needed for `POST /subscribe` and `GET /account`.
2. `dotnet run`, and expose the app on a public https URL (a tunnel is fine while developing).
3. In the Meta app dashboard, point the WhatsApp webhook at `https://<host>/whatsapp` with the
   verify token from step 1, and subscribe to the webhook fields. `messages` is the only one
   this sample cannot work without; `user_preferences` is what the opt-out handler needs.
   [`docs/webhooks.md`](../../docs/webhooks.md) lists the rest and says which are worth
   switching on.
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

```http
GET /account
```

The template endpoint assumes an approved `order_confirmation` template with named
`{{first_name}}` and `{{order_number}}` placeholders;
[`docs/templates.md`](../../docs/templates.md) shows how to create one. `GET /account` needs
`WhatsAppBusinessAccountId`.
