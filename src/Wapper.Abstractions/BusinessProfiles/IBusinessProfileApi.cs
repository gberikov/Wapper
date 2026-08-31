namespace Wapper.BusinessProfiles;

/// <summary>
/// Reading and editing the business profile shown behind a business phone number.
/// </summary>
/// <remarks>
/// There is no way to delete a profile: it exists for as long as the phone number does.
/// </remarks>
public interface IBusinessProfileApi
{
    /// <summary>Reads the profile.</summary>
    /// <param name="phoneNumberId">
    /// Which number's profile. Defaults to the tenant's own, so a single-number application
    /// can call this with no arguments.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Every field is asked for by name, because Graph returns almost nothing without an
    /// explicit list.
    /// </remarks>
    Task<BusinessProfile> GetAsync(
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the fields that are set on <paramref name="profile"/>, leaving the rest alone.
    /// </summary>
    /// <param name="profile">
    /// The fields to change. A <see langword="null"/> property is not sent, so it keeps its
    /// current value; to clear a field, set it to an empty string.
    /// </param>
    /// <param name="phoneNumberId">Which number's profile. Defaults to the tenant's own.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException">
    /// A field is longer than Meta accepts, the email address is not one, or there are more
    /// than two websites. Every one of these comes back from Meta as a bare code <c>100</c>
    /// that does not say which field was wrong, so they are checked here instead.
    /// </exception>
    Task UpdateAsync(
        BusinessProfile profile,
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a picture and makes it the profile picture.
    /// </summary>
    /// <param name="picture">
    /// The image. Square, at least 192×192 pixels; anything else is cropped or rejected.
    /// </param>
    /// <param name="mimeType">
    /// <c>image/jpeg</c> or <c>image/png</c>. Meta's uploader takes no other image type.
    /// </param>
    /// <param name="phoneNumberId">Which number's profile. Defaults to the tenant's own.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// <para>
    /// Three calls: a resumable upload session is opened against the Meta app, the bytes go
    /// up, and the handle that comes back is written to the profile.
    /// </para>
    /// <para>
    /// The upload is addressed to the app rather than to anything WhatsApp-shaped, so it needs
    /// <see cref="WhatsAppCredentials.AppId"/> — which nothing else in this library does.
    /// </para>
    /// <para>
    /// The picture is buffered in memory so that the upload can be retried, so this is not the
    /// call to hand a very large file to. A profile picture has no business being one.
    /// </para>
    /// </remarks>
    Task SetPictureAsync(
        Stream picture,
        string mimeType,
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default);
}
