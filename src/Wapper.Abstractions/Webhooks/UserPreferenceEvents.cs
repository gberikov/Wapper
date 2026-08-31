namespace Wapper.Webhooks;

/// <summary>What a customer decided about marketing messages from this business.</summary>
public enum MarketingPreference
{
    /// <summary>Something the platform returned that this library does not recognise.</summary>
    Unknown,

    /// <summary>They asked for no more. Marketing templates to them are accepted and never delivered.</summary>
    Stop,

    /// <summary>They changed their mind, and marketing templates get through again.</summary>
    Resume,
}

/// <summary>
/// A customer stopped, or resumed, marketing messages from this business.
/// </summary>
/// <remarks>
/// <para>
/// The one webhook that changes what an application is allowed to send. After a
/// <see cref="MarketingPreference.Stop"/> every marketing template to that customer is
/// accepted by the API and then fails on the status webhook with
/// <see cref="WhatsAppErrorCodes.UserOptedOut"/>, which is a slow and expensive way to find
/// out — so record it here and stop sending.
/// </para>
/// <para>
/// Arrives on the business phone number the customer opted out of, so
/// <see cref="WhatsAppEvent.PhoneNumberId"/> is set. It says nothing about the customer's
/// "interested" or "not interested" feedback, which Meta keeps to itself.
/// </para>
/// </remarks>
public sealed record MarketingPreferenceChanged : WhatsAppEvent
{
    /// <summary>The customer, as a WhatsApp id.</summary>
    public string WhatsAppId { get; init; } = string.Empty;

    /// <summary>What they decided.</summary>
    public MarketingPreference Preference { get; init; }

    /// <summary>The raw value, in case Meta sent one this library does not know.</summary>
    public string? RawPreference { get; init; }

    /// <summary>Meta's own sentence about it, for a log line.</summary>
    public string? Detail { get; init; }
}
