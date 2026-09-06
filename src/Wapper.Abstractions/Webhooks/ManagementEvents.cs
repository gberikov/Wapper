using Wapper.PhoneNumbers;
using Wapper.Templates;

namespace Wapper.Webhooks;

// Account-level webhook fields, typed against Meta's webhook reference pages as of
// June 2026 (account_alerts updated 21 May 2026; security and
// message_template_components_update 17 Jun 2026; business_capability_update and
// template_category_update as published alongside). Every enum keeps the raw string beside
// it, so a value added after that date is not lost — it is merely Unknown.

/// <summary>What an <see cref="AccountAlert"/> is about.</summary>
public enum AccountAlertEntity
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>The business portfolio. <see cref="AccountAlert.EntityId"/> is its id.</summary>
    Business,

    /// <summary>A business phone number. <see cref="AccountAlert.EntityId"/> is the number's id.</summary>
    PhoneNumber,

    /// <summary>A business phone number's business profile.</summary>
    BusinessProfile,
}

/// <summary>How seriously to take an <see cref="AccountAlert"/>.</summary>
public enum AccountAlertSeverity
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>Nothing to do; Meta is letting you know.</summary>
    Informational,

    /// <summary>Action may be needed. <see cref="AccountAlert.Description"/> says what.</summary>
    Warning,

    /// <summary>A rejection or a denial. <see cref="AccountAlert.Description"/> may say how to resolve it.</summary>
    Critical,
}

/// <summary>Whether an <see cref="AccountAlert"/> is still in force.</summary>
public enum AccountAlertStatus
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>In force.</summary>
    Active,

    /// <summary>Not in force.</summary>
    None,
}

/// <summary>What an <see cref="AccountAlert"/> reports.</summary>
public enum AccountAlertKind
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>
    /// Meta cannot decide on a messaging limit increase yet: not enough messaging to judge
    /// by, an identity verification that was rejected, or quality too low.
    /// </summary>
    IncreasedCapabilitiesEligibilityDeferred,

    /// <summary>The messaging limit cannot be increased because of past messaging activity.</summary>
    IncreasedCapabilitiesEligibilityFailed,

    /// <summary>More is needed before the messaging limit can be increased: verify the identity, or the business.</summary>
    IncreasedCapabilitiesEligibilityNeedMoreInfo,

    /// <summary>Official Business Account status was granted.</summary>
    OfficialBusinessAccountApproved,

    /// <summary>Official Business Account status was denied.</summary>
    OfficialBusinessAccountRejected,

    /// <summary>The number's business profile photo was deleted. Upload another.</summary>
    ProfilePictureLost,
}

/// <summary>
/// A notice about a number's messaging limit, its Official Business Account status, or its
/// business profile.
/// </summary>
/// <remarks>
/// The <c>account_alerts</c> field. Account-level: it names a business portfolio, a phone
/// number or a profile through <see cref="EntityType"/> and <see cref="EntityId"/>, and the
/// account through <see cref="WhatsAppEvent.BusinessAccountId"/>. Meta has sent the alert
/// fields both nested under <c>alert_info</c> and flat on the value; both are read.
/// </remarks>
public sealed record AccountAlert : WhatsAppEvent
{
    /// <summary>What the alert is about.</summary>
    public AccountAlertEntity EntityType { get; init; }

    /// <summary>The raw entity type, in case Meta sent one this library does not know.</summary>
    public string? RawEntityType { get; init; }

    /// <summary>The id of the business portfolio or phone number the alert is about.</summary>
    public string? EntityId { get; init; }

    /// <summary>How seriously to take it.</summary>
    public AccountAlertSeverity Severity { get; init; }

    /// <summary>The raw severity.</summary>
    public string? RawSeverity { get; init; }

    /// <summary>Whether it is still in force.</summary>
    public AccountAlertStatus Status { get; init; }

    /// <summary>The raw status.</summary>
    public string? RawStatus { get; init; }

    /// <summary>What it reports.</summary>
    public AccountAlertKind Kind { get; init; }

    /// <summary>The raw alert type.</summary>
    public string? RawKind { get; init; }

    /// <summary>Meta's own sentence about it, which often says what to do.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// The business's messaging or phone number limits changed.
/// </summary>
/// <remarks>
/// The <c>business_capability_update</c> field. Which phone number limit arrives depends on
/// the messaging limit: Meta sends the per-portfolio limit for a business still on the
/// lowest tier and the per-account limit for everything above it.
/// </remarks>
public sealed record BusinessCapabilityChanged : WhatsAppEvent
{
    /// <summary>How many business-initiated conversations the portfolio may open a day.</summary>
    public MessagingLimitTier MessagingLimit { get; init; }

    /// <summary>
    /// The raw messaging limit. A tier name on current webhooks, a number on older ones.
    /// </summary>
    public string? RawMessagingLimit { get; init; }

    /// <summary>
    /// The per-phone-number messaging limit, as a number of conversations. Sent by webhook
    /// versions before v24.0 and retired in February 2026; <c>-1</c> means unlimited.
    /// </summary>
    public int? MaxDailyConversationsPerPhone { get; init; }

    /// <summary>How many phone numbers the business portfolio may have, when Meta sent it.</summary>
    public int? MaxPhoneNumbersPerBusiness { get; init; }

    /// <summary>How many phone numbers the account may have, when Meta sent it.</summary>
    public int? MaxPhoneNumbersPerAccount { get; init; }
}

/// <summary>What changed in a number's security settings.</summary>
public enum PhoneNumberSecurityEvent
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>The two-step verification PIN was set or changed in WhatsApp Manager.</summary>
    PinChanged,

    /// <summary>Someone asked, in WhatsApp Manager, to turn two-step verification off.</summary>
    PinResetRequest,

    /// <summary>Two-step verification was turned off through the reset email.</summary>
    PinResetSucceeded,
}

/// <summary>
/// A number's two-step verification settings changed.
/// </summary>
/// <remarks>
/// The <c>security</c> field. Worth alerting on: a PIN reset nobody on your side asked for
/// is somebody else trying to take the number. Account-level, identified by the number in
/// display form.
/// </remarks>
public sealed record PhoneNumberSecurityChanged : WhatsAppEvent
{
    /// <summary>What happened.</summary>
    public PhoneNumberSecurityEvent Event { get; init; }

    /// <summary>The raw event string, in case Meta sent one this library does not know.</summary>
    public string? RawEvent { get; init; }

    /// <summary>
    /// The Meta Business Suite user who asked for the reset. Only sent on a reset request.
    /// </summary>
    public string? RequesterId { get; init; }
}

/// <summary>
/// A template's category changed, or is about to.
/// </summary>
/// <remarks>
/// The <c>template_category_update</c> field. Two notices: one saying the category
/// <em>will</em> change in 24 hours — <see cref="IsPending"/>, with the target in
/// <see cref="CorrectCategory"/> and the moment in <see cref="ChangesAt"/> — and one saying
/// it has, with <see cref="PreviousCategory"/> filled in. The category decides the price of
/// every message sent with the template, which is why it is worth a handler.
/// </remarks>
public sealed record TemplateCategoryChanged : WhatsAppEvent
{
    /// <summary>Identifier of the template.</summary>
    public string TemplateId { get; init; } = string.Empty;

    /// <summary>Its name.</summary>
    public string TemplateName { get; init; } = string.Empty;

    /// <summary>Its locale.</summary>
    public string TemplateLanguage { get; init; } = string.Empty;

    /// <summary>
    /// The category now. On a pending change this is the current, soon to be wrong,
    /// category; on a completed one it is the new category.
    /// </summary>
    public TemplateCategory Category { get; init; }

    /// <summary>The raw <c>new_category</c>.</summary>
    public string? RawCategory { get; init; }

    /// <summary>What it was, on a completed change.</summary>
    public TemplateCategory? PreviousCategory { get; init; }

    /// <summary>The raw <c>previous_category</c>.</summary>
    public string? RawPreviousCategory { get; init; }

    /// <summary>What it will become, on a pending change.</summary>
    public TemplateCategory? CorrectCategory { get; init; }

    /// <summary>The raw <c>correct_category</c>.</summary>
    public string? RawCorrectCategory { get; init; }

    /// <summary>When a pending change takes effect.</summary>
    public DateTimeOffset? ChangesAt { get; init; }

    /// <summary>Whether this is the advance notice rather than the change itself.</summary>
    public bool IsPending => CorrectCategory is not null;
}

/// <summary>One button of a template, as the components webhook describes it.</summary>
public sealed record TemplateComponentButton
{
    /// <summary>
    /// The button type as Meta names it: <c>URL</c>, <c>PHONE_NUMBER</c>, <c>QUICK_REPLY</c>,
    /// <c>COPY_CODE</c>, <c>FLOW</c> and the rest. Left as text: this webhook names more
    /// kinds than the template API lets you create.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>The label.</summary>
    public string? Text { get; init; }

    /// <summary>Where a URL button goes.</summary>
    public string? Url { get; init; }

    /// <summary>What a phone number button dials.</summary>
    public string? PhoneNumber { get; init; }
}

/// <summary>
/// A template was edited.
/// </summary>
/// <remarks>
/// The <c>message_template_components_update</c> field, which carries the text of the
/// template as it now reads. A template edited in WhatsApp Manager by somebody else is
/// otherwise invisible until a send fails validation.
/// </remarks>
public sealed record TemplateComponentsChanged : WhatsAppEvent
{
    /// <summary>Identifier of the template.</summary>
    public string TemplateId { get; init; } = string.Empty;

    /// <summary>Its name.</summary>
    public string TemplateName { get; init; } = string.Empty;

    /// <summary>Its locale.</summary>
    public string TemplateLanguage { get; init; } = string.Empty;

    /// <summary>The header text, when the template has a text header.</summary>
    public string? Header { get; init; }

    /// <summary>The body text.</summary>
    public string? Body { get; init; }

    /// <summary>The footer text, when the template has one.</summary>
    public string? Footer { get; init; }

    /// <summary>The buttons, when the template has any.</summary>
    public IReadOnlyList<TemplateComponentButton> Buttons { get; init; } = [];
}
