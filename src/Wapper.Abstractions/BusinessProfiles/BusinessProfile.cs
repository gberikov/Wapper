namespace Wapper.BusinessProfiles;

/// <summary>The category shown under a business's name in the WhatsApp client.</summary>
public enum BusinessVertical
{
    /// <summary>Not set, or a category this library does not know about yet.</summary>
    Unknown,

    /// <summary>Alcoholic beverages.</summary>
    Alcohol,

    /// <summary>Clothing and apparel.</summary>
    Apparel,

    /// <summary>Automotive.</summary>
    Automotive,

    /// <summary>Beauty, spa and salon.</summary>
    Beauty,

    /// <summary>Education.</summary>
    Education,

    /// <summary>Entertainment.</summary>
    Entertainment,

    /// <summary>Event planning and service.</summary>
    EventPlanning,

    /// <summary>Finance and banking.</summary>
    Finance,

    /// <summary>Public service.</summary>
    Government,

    /// <summary>Food and grocery.</summary>
    Grocery,

    /// <summary>Medical and health.</summary>
    Health,

    /// <summary>Hotel and lodging.</summary>
    Hotel,

    /// <summary>Non-profit.</summary>
    NonProfit,

    /// <summary>Online gambling and gaming.</summary>
    OnlineGambling,

    /// <summary>Over-the-counter drugs.</summary>
    OverTheCounterDrugs,

    /// <summary>Anything the other categories do not cover.</summary>
    Other,

    /// <summary>Gambling and gaming that is not online — a betting shop or a casino.</summary>
    PhysicalGambling,

    /// <summary>Professional services.</summary>
    ProfessionalServices,

    /// <summary>Restaurant.</summary>
    Restaurant,

    /// <summary>Shopping and retail.</summary>
    Retail,

    /// <summary>Travel and transportation.</summary>
    Travel,
}

/// <summary>
/// What a WhatsApp user sees when they tap a business's name in a message thread.
/// </summary>
/// <remarks>
/// Also the shape of an update, where every property left <see langword="null"/> is left
/// alone — the Cloud API merges rather than replaces. The one asymmetry is the picture: it
/// is read as <see cref="PictureUrl"/> and written as <see cref="PictureHandle"/>, because
/// Meta takes an uploaded file rather than a URL.
/// </remarks>
public sealed record BusinessProfile
{
    /// <summary>
    /// The About text, shown beneath the profile image, number and contact buttons.
    /// </summary>
    /// <remarks>
    /// Between 1 and 139 characters. Emoji work, but their Unicode values have to be escaped.
    /// A URL is shown as text rather than as a link, and Markdown is not rendered.
    /// </remarks>
    public string? About { get; init; }

    /// <summary>Street address of the business. At most 256 characters.</summary>
    public string? Address { get; init; }

    /// <summary>What the business does. At most 512 characters.</summary>
    public string? Description { get; init; }

    /// <summary>Contact email address. At most 128 characters.</summary>
    public string? Email { get; init; }

    /// <summary>Category of the business.</summary>
    /// <remarks>
    /// On an update, <see cref="BusinessVertical.Unknown"/> clears the category — unless
    /// <see cref="RawVertical"/> is set, which marks a profile read back with a category this
    /// library does not know; that one is left untouched rather than erased.
    /// </remarks>
    public BusinessVertical? Vertical { get; init; }

    /// <summary>
    /// The category exactly as Meta wrote it, for one this library has not been taught yet.
    /// </summary>
    public string? RawVertical { get; init; }

    /// <summary>
    /// URLs of the business — a website, a Facebook page, an Instagram profile.
    /// </summary>
    /// <remarks>
    /// At most two, each at most 256 characters, and each with its <c>http://</c> or
    /// <c>https://</c> still on it.
    /// </remarks>
    public IReadOnlyList<string>? Websites { get; init; }

    /// <summary>Where the current profile picture can be fetched from. Read only.</summary>
    public string? PictureUrl { get; init; }

    /// <summary>
    /// Handle of a picture already uploaded to Meta, to use as the profile picture. Write only.
    /// </summary>
    /// <remarks>
    /// Only worth setting by hand when the same picture is being applied to several numbers.
    /// <c>SetPictureAsync</c> uploads a file and applies it in one call.
    /// </remarks>
    public string? PictureHandle { get; init; }
}
