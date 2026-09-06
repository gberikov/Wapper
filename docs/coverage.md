# What is covered

The Cloud API surface this library types, endpoint by endpoint and webhook field by field,
against Graph API `v26.0` as documented by Meta in June 2026. A row is here because a test
in this repository exercises it against a recorded or documented payload; the count of rows
says nothing about the fraction of the Cloud API covered, and neither does the count of
tests. What is not here is listed at the end, so nobody has to find out by looking for a
method that does not exist.

Anything not in the table can still be called through [`Raw`](raw.md), which paces, retries
and reports errors the same way and reads the response as a type of your own.

## Sending

| Endpoint | Method | Variants | Limits and notes | Test |
|---|---|---|---|---|
| `POST /{phone}/messages` | `Messages.SendTextAsync` | body, preview URL, reply context | 4096 characters | `MessagesApiTests` |
| `POST /{phone}/messages` | `Messages.SendImageAsync`, `SendVideoAsync`, `SendAudioAsync`, `SendDocumentAsync`, `SendStickerAsync` | by media id or link, caption, file name | sizes in `MediaLimits` | `MessagesApiTests`, `MediaKindsTests` |
| `POST /{phone}/messages` | `Messages.SendLocationAsync`, `SendContactsAsync` | | | `MessagesApiTests` |
| `POST /{phone}/messages` | `Messages.SendReactionAsync`, `RemoveReactionAsync` | | | `MessagesApiTests` |
| `POST /{phone}/messages` | `Messages.SendButtonsAsync`, `SendListAsync`, `SendCallToActionAsync`, `SendLocationRequestAsync` | reply buttons, list, CTA URL, location request | button and row counts checked before the send | `MessagesApiTests`, `InteractiveLimitTests` |
| `POST /{phone}/messages` | `Messages.SendTemplateAsync` | text/media/location header, body, buttons: quick reply, URL, copy code, OTP; named and numbered placeholders | `Template.Validate` before the send | `MessagesApiTests`, `TemplateValidationTests` |
| `POST /{phone}/messages` | `Messages.SendFlowAsync` | draft and published Flows, first screen data | | `FlowsApiTests` |
| `POST /{phone}/messages` | `Messages.MarkAsReadAsync` | read, typing indicator | | `MessagesApiTests` |
| Media | `Media.UploadAsync`, `GetAsync`, `DownloadAsync`, `DeleteAsync` | seekable and forward-only streams; download by id or by `MediaInfo` | download hosts are checked before the token is presented | `MediaApiTests`, `SecurityTests` |

## Managing the account

| Endpoint | Method | Variants | Limits and notes | Test |
|---|---|---|---|---|
| `GET/POST/DELETE /{waba}/message_templates`, `POST /{template}` | `Templates.ListAsync`, `GetAsync`, `CreateAsync`, `UpdateAsync`, `UpdateCategoryAsync`, `DeleteAsync`, `DeleteByNameAsync`, `UploadHeaderSampleAsync` | paged listing; text, media and location headers; quick reply, URL, phone, copy code and OTP buttons | components this library does not know are read by type and refuse an update rather than being dropped; carousel and limited-time-offer components are among them | `TemplatesApiTests`, `TemplateInspectionTests`, `ResumableUploadTests` |
| `GET /{waba}/phone_numbers`, `GET /{phone}` | `PhoneNumbers.ListAsync`, `GetAsync` | quality, throughput, messaging limit, name status, platform, account mode | | `PhoneNumbersApiTests` |
| `POST /{phone}/request_code`, `verify_code`, `register`, `deregister`, `settings` | `PhoneNumbers.RequestVerificationCodeAsync`, `VerifyAsync`, `RegisterAsync`, `DeregisterAsync`, `SetTwoStepPinAsync` | data localization region | registration and code requests are never retried | `RegistrationTests` |
| `GET/POST /{phone}/whatsapp_business_encryption` | `PhoneNumbers.GetEncryptionKeyAsync`, `SetEncryptionKeyAsync` | | | `PhoneNumbersApiTests` |
| `GET/POST /{phone}/whatsapp_business_profile` | `BusinessProfile.GetAsync`, `UpdateAsync`, `SetPictureAsync` | | picture goes through the resumable upload | `BusinessProfileApiTests` |
| `GET/POST/DELETE /{waba}/subscribed_apps` | `Account.SubscribeAsync`, `GetSubscribedAppsAsync`, `UnsubscribeAsync` | | | `AccountApiTests` |
| `GET/POST/DELETE /{waba}/flows`, `/{flow}`, `/{flow}/assets`, `/{flow}/publish`, `/{flow}/deprecate` | `Flows.ListAsync`, `GetAsync`, `CreateAsync`, `UpdateAsync`, `UpdateJsonAsync`, `PublishAsync`, `DeprecateAsync`, `DeleteAsync`, `GetPreviewAsync`, `ListAssetsAsync` | health and preview on read; JSON from a string or a stream | | `FlowsApiTests` |
| `GET /{waba}/analytics`, `conversation_analytics`, `pricing_analytics`, `template_analytics` | `Analytics.GetMessagingAsync`, `GetConversationsAsync`, `GetPricingAsync`, `GetTemplatesAsync` | granularity, phone number and country filters | | `AnalyticsApiTests` |

## Webhook fields

| Field | Event | Variants read | Test |
|---|---|---|---|
| `messages` | `TextMessage`, `MediaMessage`, `LocationMessage`, `ContactsMessage`, `ReactionMessage`, `InteractiveReply`, `FlowReply`, `TemplateButtonReply`, `OrderMessage`, `WelcomeRequest`, `SystemMessage`, `UnsupportedMessage`, `UnknownMessage`, `MessageStatusChanged`, `WebhookError` | context, forwarding, referral, referred product, identity change; statuses with pricing and conversation; one unreadable item costs that item alone | `WebhookParserTests`, `WebhookCoverageTests`, `WebhookSilentLossTests` |
| `user_preferences` | `MarketingPreferenceChanged` | array and flat forms | `WebhookCoverageTests` |
| `message_template_status_update` | `TemplateStatusChanged` | reason, rejection info, other info | `TemplateWebhookTests` |
| `message_template_quality_update` | `TemplateQualityChanged` | | `TemplateWebhookTests` |
| `template_category_update` | `TemplateCategoryChanged` | pending (with `correct_category` and the moment) and completed | `ManagementWebhookTests` |
| `message_template_components_update` | `TemplateComponentsChanged` | header, body, footer, URL and phone buttons; other button types by name | `ManagementWebhookTests` |
| `phone_number_quality_update` | `PhoneNumberQualityChanged` | `current_limit` and its replacement | `PhoneNumberWebhookTests` |
| `phone_number_name_update` | `PhoneNumberNameChanged` | | `PhoneNumberWebhookTests` |
| `account_update` | `AccountUpdated` | ten `event` values typed, ban, violation and restriction info; the rest as `Unknown` with the body | `AccountWebhookTests` |
| `account_alerts` | `AccountAlert` | nested `alert_info` and the flat form; six alert types | `ManagementWebhookTests`, `WebhookCoverageTests` |
| `business_capability_update` | `BusinessCapabilityChanged` | tier name and the retired number | `ManagementWebhookTests` |
| `security` | `PhoneNumberSecurityChanged` | three events, requester | `ManagementWebhookTests` |
| `flows` | `FlowStatusChanged`, `FlowAlert` | four alert kinds | `FlowWebhookTests` |
| anything else, or a known field this library could not read | `UnknownEvent` with the `value` | | `WebhookCoverageTests` |

Every enum on every event keeps the raw string beside it, so a value Meta adds is `Unknown`
and still readable.

## Not covered

Confirmed by reading this repository, not by reading Meta's roadmap. Priority is a suggested
order for the next release, not a promise.

| Priority | Area | What is missing |
|---|---|---|
| 1 | Product messages | Single-product, multi-product and catalog messages: typed sending with catalog and product ids. Managing the catalog itself is a Marketing API concern and not needed to send. |
| 1 | Carousel and limited-time-offer templates | Creating, reading and sending them, and keeping their components intact through read and update. |
| 2 | QR codes and short links | `GET/POST/DELETE /{phone}/message_qrdls`. |
| 2 | Conversational components | Welcome message, commands and ice-breakers on `/{phone}/conversational_automation`. |
| 2 | Blocking users | `/{phone}/block_users`. Recent and still moving. |
| 2 | Reading the WhatsApp Business Account | `GET /{waba}`, two dozen fields, most about billing. |
| 2 | Template library, archiving and groups | Contracts to be checked against the current reference before anything is typed. |
| 3 | Calling | Its own permissions, signalling, error codes and the `calls` webhook field. A package of its own. |
| 3 | Partner and coexistence surfaces | Embedded Signup, credit lines, `partner_solutions`, `history`, `smb_app_state_sync`, `smb_message_echoes`. Need advanced access granted to an approved Solution Partner. |
| 3 | Webhook fields not typed | `account_review_update`, `automatic_events`, `calls`, `payment_configuration_update` and the partner fields above arrive as `UnknownEvent`. |
| Audit | Groups | Meta's Groups API is not confirmed here at all; check the current reference before planning it. |
