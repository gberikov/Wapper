namespace Wapper.PhoneNumbers;

/// <summary>How Meta should deliver a verification code to a business phone number.</summary>
public enum VerificationCodeMethod
{
    /// <summary>A text message.</summary>
    Sms,

    /// <summary>A voice call reading the code out.</summary>
    Voice,
}

/// <summary>
/// Reading the business phone numbers of a WhatsApp Business Account, and getting one onto
/// the Cloud API.
/// </summary>
/// <remarks>
/// <para>
/// These calls are billed against the account's management allowance — 200 requests an hour,
/// or 5000 once the account has a registered phone number — which the client paces for you.
/// </para>
/// <para>
/// A number cannot be created or deleted through the API. Numbers are added in WhatsApp
/// Manager, Meta Business Suite or Embedded Signup, and only a business portfolio admin can
/// remove one.
/// </para>
/// </remarks>
public interface IPhoneNumbersApi
{
    /// <summary>
    /// Lists the numbers on the account, newest onboarding first, fetching further pages as
    /// they are read.
    /// </summary>
    /// <remarks>
    /// Needs <see cref="WhatsAppCredentials.WhatsAppBusinessAccountId"/>. An application that
    /// only sends messages never has to configure it; one that inspects its numbers does.
    /// </remarks>
    IAsyncEnumerable<PhoneNumber> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one number.</summary>
    /// <param name="phoneNumberId">
    /// Which number. Defaults to the tenant's own, so a single-number application can call
    /// this with no arguments.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Worth calling at startup: <see cref="PhoneNumber.Status"/> says whether the number can
    /// send at all, and <see cref="PhoneNumber.Throughput"/> says how fast.
    /// </remarks>
    Task<PhoneNumber> GetAsync(
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the two-step verification PIN.
    /// </summary>
    /// <param name="pin">Six digits.</param>
    /// <param name="phoneNumberId">Which number. Defaults to the tenant's own.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The PIN is required to register the number and to delete it, and this is the only way
    /// to set a new one without knowing the old one. Two-step verification cannot be switched
    /// off through the API.
    /// </remarks>
    Task SetTwoStepPinAsync(
        string pin,
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks Meta to send a verification code to the number.
    /// </summary>
    /// <param name="method">Whether to text the code or read it out in a call.</param>
    /// <param name="language">
    /// Locale of the message carrying the code. Meta's reference calls this a two-letter
    /// language code and then gives <c>en_US</c> in every example; both are accepted.
    /// </param>
    /// <param name="phoneNumberId">Which number. Defaults to the tenant's own.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// <para>
    /// This is step one of getting a number onto the Cloud API: request a code, pass it to
    /// <see cref="VerifyAsync"/>, then <see cref="RegisterAsync"/>. The number must already
    /// have been added to the account in WhatsApp Manager.
    /// </para>
    /// <para>
    /// Check <see cref="PhoneNumber.CodeVerification"/> first: asking for a code for a number
    /// that is already verified fails with code <c>136024</c>.
    /// </para>
    /// <para>
    /// The call is never retried automatically, because a retry sends a second code and only
    /// the newest one works — leaving whoever is reading the first message stuck.
    /// </para>
    /// </remarks>
    Task RequestVerificationCodeAsync(
        VerificationCodeMethod method,
        string language = "en_US",
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies ownership of the number with the code that was sent to it.
    /// </summary>
    /// <param name="code">
    /// The code from the message. Meta writes it with a hyphen and expects it without one, so
    /// hyphens and spaces are stripped here.
    /// </param>
    /// <param name="phoneNumberId">Which number. Defaults to the tenant's own.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task VerifyAsync(
        string code,
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers the verified number for use with the Cloud API.
    /// </summary>
    /// <param name="pin">
    /// The number's six-digit two-step verification PIN. If it has none yet, this sets it.
    /// </param>
    /// <param name="dataLocalizationRegion">
    /// Two-letter ISO 3166 country code to keep data at rest in — <c>DE</c>, <c>IN</c>,
    /// <c>BR</c> and the other regions Meta lists. Leave it out for no local storage.
    /// </param>
    /// <param name="phoneNumberId">Which number. Defaults to the tenant's own.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// <para>
    /// A number can only be registered through the API — WhatsApp Manager cannot do it. Also
    /// call this again after a display name change is approved, which the
    /// <c>phone_number_name_update</c> webhook announces.
    /// </para>
    /// <para>
    /// Registration and deregistration share an allowance of ten attempts per number per 72
    /// hours; the eleventh fails with code <c>133016</c> and locks the number out for the rest
    /// of the window. Because of that this call is never retried automatically, and
    /// <c>133016</c> is never retried at all.
    /// </para>
    /// <para>
    /// Local storage cannot be turned off or moved in place: deregister the number and
    /// register it again, with a different region or with none.
    /// </para>
    /// </remarks>
    Task RegisterAsync(
        string pin,
        string? dataLocalizationRegion = null,
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the number off the Cloud API, and disables local storage on it.
    /// </summary>
    /// <param name="phoneNumberId">Which number. Defaults to the tenant's own.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Neither the number nor its message history is deleted; it simply stops working until it
    /// is registered again. Spends the same ten-attempts-per-72-hours allowance as
    /// <see cref="RegisterAsync"/>, so it is not retried automatically either.
    /// </remarks>
    Task DeregisterAsync(
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default);
}
