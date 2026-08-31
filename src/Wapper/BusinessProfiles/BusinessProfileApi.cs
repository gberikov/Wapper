using System.Net.Mail;
using Wapper.Internal;

namespace Wapper.BusinessProfiles;

/// <summary>The business profile behind one tenant's phone number.</summary>
internal sealed class BusinessProfileApi(GraphApiClient client, string tenant) : IBusinessProfileApi
{
    /// <summary>
    /// The fields to ask for, because Graph answers a bare read with the messaging product
    /// and nothing else.
    /// </summary>
    private const string Fields =
        "about,address,description,email,profile_picture_url,websites,vertical";

    private const int MaxAbout = 139;
    private const int MaxAddress = 256;
    private const int MaxDescription = 512;
    private const int MaxEmail = 128;
    private const int MaxWebsites = 2;
    private const int MaxWebsiteLength = 256;

    public async Task<BusinessProfile> GetAsync(
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default)
    {
        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        var response = await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Get,
                    Path = $"{Target(phoneNumberId, credentials)}/whatsapp_business_profile" +
                           $"?fields={Fields}",
                    Kind = GraphCallKind.Management,
                    Operation = "business_profile.get",
                },
                WhatsAppJsonContext.Default.BusinessProfileResponse,
                cancellationToken)
            .ConfigureAwait(false);

        // A collection of one. A number has a single profile, and an empty array means it has
        // not been filled in rather than that something went wrong.
        var payload = response.Data is [var first, ..] ? first : new BusinessProfilePayload();

        return new BusinessProfile
        {
            About = payload.About,
            Address = payload.Address,
            Description = payload.Description,
            Email = payload.Email,
            Vertical = payload.Vertical is null ? null : ParseVertical(payload.Vertical),
            RawVertical = payload.Vertical,
            Websites = payload.Websites,
            PictureUrl = payload.ProfilePictureUrl,
        };
    }

    public async Task UpdateAsync(
        BusinessProfile profile,
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var payload = ToPayload(profile);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    Path = $"{Target(phoneNumberId, credentials)}/whatsapp_business_profile",
                    Kind = GraphCallKind.Management,
                    Operation = "business_profile.update",
                    Content = GraphContent.Json(
                        payload,
                        WhatsAppJsonContext.Default.BusinessProfilePayload),
                },
                WhatsAppJsonContext.Default.SuccessResponse,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetPictureAsync(
        Stream picture,
        string mimeType,
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        var handle = await ResumableUpload
            .UploadAsync(
                client,
                tenant,
                credentials,
                picture,
                mimeType,
                "profile",
                "business_profile.upload_picture",
                cancellationToken)
            .ConfigureAwait(false);

        await UpdateAsync(
                new BusinessProfile { PictureHandle = handle },
                phoneNumberId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static BusinessProfilePayload ToPayload(BusinessProfile profile)
    {
        Guard(profile.About, MaxAbout, nameof(profile.About));
        Guard(profile.Address, MaxAddress, nameof(profile.Address));
        Guard(profile.Description, MaxDescription, nameof(profile.Description));
        Guard(profile.Email, MaxEmail, nameof(profile.Email));

        if (!string.IsNullOrEmpty(profile.Email) && !MailAddress.TryCreate(profile.Email, out _))
        {
            throw new ArgumentException(
                $"'{profile.Email}' is not an email address.",
                nameof(profile));
        }

        if (profile.Websites is { } websites)
        {
            if (websites.Count > MaxWebsites)
            {
                throw new ArgumentException(
                    $"A business profile holds at most {MaxWebsites} websites, and this one has " +
                    $"{websites.Count}.",
                    nameof(profile));
            }

            foreach (var website in websites)
            {
                Guard(website, MaxWebsiteLength, nameof(profile.Websites));

                // Meta stores what it is given, and a WhatsApp client shows an address with no
                // scheme as unclickable text.
                if (!website.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !website.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"A website has to keep its http:// or https://, and '{website}' has not.",
                        nameof(profile));
                }
            }
        }

        return new BusinessProfilePayload
        {
            MessagingProduct = "whatsapp",
            About = profile.About,
            Address = profile.Address,
            Description = profile.Description,
            Email = profile.Email,
            // An empty string is how the category is cleared, so it is not the same as leaving
            // the property unset.
            Vertical = profile.Vertical is { } vertical ? ToWire(vertical) : null,
            Websites = profile.Websites?.ToList(),
            ProfilePictureHandle = profile.PictureHandle,
        };
    }

    private static void Guard(string? value, int maximum, string field)
    {
        // Meta answers any of these with a bare code 100 that does not say which field it
        // objected to.
        if (value is not null && value.Length > maximum)
        {
            throw new ArgumentException(
                $"{field} is at most {maximum} characters, and this one is {value.Length}.",
                "profile");
        }
    }

    internal static BusinessVertical ParseVertical(string? vertical) => vertical?.ToUpperInvariant() switch
    {
        "ALCOHOL" => BusinessVertical.Alcohol,
        "APPAREL" => BusinessVertical.Apparel,
        "AUTO" => BusinessVertical.Automotive,
        "BEAUTY" => BusinessVertical.Beauty,
        "EDU" => BusinessVertical.Education,
        "ENTERTAIN" => BusinessVertical.Entertainment,
        "EVENT_PLAN" => BusinessVertical.EventPlanning,
        "FINANCE" => BusinessVertical.Finance,
        "GOVT" => BusinessVertical.Government,
        "GROCERY" => BusinessVertical.Grocery,
        "HEALTH" => BusinessVertical.Health,
        "HOTEL" => BusinessVertical.Hotel,
        "NONPROFIT" => BusinessVertical.NonProfit,
        "ONLINE_GAMBLING" => BusinessVertical.OnlineGambling,
        "OTC_DRUGS" => BusinessVertical.OverTheCounterDrugs,
        "OTHER" => BusinessVertical.Other,
        "PHYSICAL_GAMBLING" => BusinessVertical.PhysicalGambling,
        "PROF_SERVICES" => BusinessVertical.ProfessionalServices,
        "RESTAURANT" => BusinessVertical.Restaurant,
        "RETAIL" => BusinessVertical.Retail,
        "TRAVEL" => BusinessVertical.Travel,
        // Includes UNDEFINED, which is what a profile that has never had a category reports.
        _ => BusinessVertical.Unknown,
    };

    internal static string ToWire(BusinessVertical vertical) => vertical switch
    {
        BusinessVertical.Alcohol => "ALCOHOL",
        BusinessVertical.Apparel => "APPAREL",
        BusinessVertical.Automotive => "AUTO",
        BusinessVertical.Beauty => "BEAUTY",
        BusinessVertical.Education => "EDU",
        BusinessVertical.Entertainment => "ENTERTAIN",
        BusinessVertical.EventPlanning => "EVENT_PLAN",
        BusinessVertical.Finance => "FINANCE",
        BusinessVertical.Government => "GOVT",
        BusinessVertical.Grocery => "GROCERY",
        BusinessVertical.Health => "HEALTH",
        BusinessVertical.Hotel => "HOTEL",
        BusinessVertical.NonProfit => "NONPROFIT",
        BusinessVertical.OnlineGambling => "ONLINE_GAMBLING",
        BusinessVertical.OverTheCounterDrugs => "OTC_DRUGS",
        BusinessVertical.Other => "OTHER",
        BusinessVertical.PhysicalGambling => "PHYSICAL_GAMBLING",
        BusinessVertical.ProfessionalServices => "PROF_SERVICES",
        BusinessVertical.Restaurant => "RESTAURANT",
        BusinessVertical.Retail => "RETAIL",
        BusinessVertical.Travel => "TRAVEL",
        // The documented way to clear the category, and the only thing Unknown can honestly
        // mean on the way up.
        BusinessVertical.Unknown => string.Empty,
        _ => throw new ArgumentOutOfRangeException(nameof(vertical), vertical, null),
    };

    private static string Target(string? phoneNumberId, WhatsAppCredentials credentials) =>
        string.IsNullOrWhiteSpace(phoneNumberId)
            ? credentials.PhoneNumberId
            : GraphApiClient.PathSegment(phoneNumberId);
}
