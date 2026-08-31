namespace Wapper.PhoneNumbers;

/// <summary>Reading the business phone numbers of a WhatsApp Business Account.</summary>
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
}
